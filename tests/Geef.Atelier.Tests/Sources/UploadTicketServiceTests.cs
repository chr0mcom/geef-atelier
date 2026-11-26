using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Sources;

/// <summary>
/// <see cref="UploadTicketService"/> against real Postgres: tenant separation (6.1), one successful upload per ticket also under
/// parallel claims (6.2), expiry with a settable clock (6.3), the token never in plain text in the database or a log (6.4), the
/// ticket limits (6.7), size checks, the resolution for an order and the cleanup.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class UploadTicketServiceTests(PostgresFixture fixture)
{
    private static readonly Func<Guid, string> PageLink = id => $"https://atelier.example/u/{id}";

    [Fact]
    public async Task A_prepared_ticket_hands_out_the_token_once_and_stores_only_its_hash()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();

        var prepared = await PrepareAsync(host, owner, "../../etc/Offer\0 2026.pdf", 4_096);

        Assert.True(UploadTicketService.LooksLikeToken(prepared.Token));
        Assert.Equal(host.Clock.Now + TimeSpan.FromMinutes(15), prepared.TokenExpiresAt);
        Assert.Equal(host.Clock.Now + TimeSpan.FromHours(24), prepared.PageExpiresAt);
        Assert.Equal(4_096, prepared.MaxBytes);

        var ticket = await TicketAsync(prepared.UploadId);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(prepared.Token))), ticket.TokenHash);
        Assert.Matches("^[0-9a-f]{64}$", ticket.TokenHash);
        Assert.Equal((owner, "Offer 2026.pdf", 4_096L, UploadTicketState.Open, 0), (ticket.OwnerUsername, ticket.FileName, ticket.DeclaredBytes, ticket.State, ticket.Attempts));
        Assert.DoesNotContain(prepared.Token, await RowJsonAsync(prepared.UploadId), StringComparison.Ordinal);
        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains(prepared.Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_tickets_never_share_a_token_and_without_a_size_the_file_limit_applies()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();

        var first = await PrepareAsync(host, owner, "a.txt");
        var second = await PrepareAsync(host, owner, "a.txt", 0);

        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.UploadId, second.UploadId);
        Assert.Equal(26_214_400, first.MaxBytes);
        Assert.Null((await TicketAsync(second.UploadId)).DeclaredBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_ticket_without_a_name_is_refused(string name)
    {
        await using var host = UploadTestHost.Start(fixture);

        var ex = await Assert.ThrowsAsync<UploadTicketException>(() => PrepareAsync(host, NewUser(), name));

        Assert.Equal(UploadTicketLines.FileNameInvalid, ex.Message);
    }

    [Fact]
    public async Task Too_long_names_and_too_large_files_are_refused_before_anything_is_stored()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();

        var longName = await Assert.ThrowsAsync<UploadTicketException>(() => PrepareAsync(host, owner, new string('a', 252) + ".pdf"));
        var tooLarge = await Assert.ThrowsAsync<UploadTicketException>(() => PrepareAsync(host, owner, "big.pdf", 26_214_401));

        Assert.Equal(UploadTicketLines.FileNameInvalid, longName.Message);
        Assert.Equal("The file is larger than 25 MB. Ask the person for a smaller file or a shorter version.", tooLarge.Message);
        await using var db = fixture.NewContext();
        Assert.False(await db.UploadTickets.AnyAsync(t => t.OwnerUsername == owner));
    }

    [Fact]
    public async Task The_eleventh_open_ticket_is_refused_but_other_people_and_used_tickets_do_not_count()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var tickets = new List<PreparedUpload>();
        for (var i = 0; i < 10; i++)
            tickets.Add(await PrepareAsync(host, owner, $"{i}.txt"));

        var eleventh = await Assert.ThrowsAsync<UploadTicketException>(() => PrepareAsync(host, owner, "11.txt"));
        Assert.Equal(UploadTicketLines.TooManyTickets, eleventh.Message);
        await PrepareAsync(host, NewUser(), "other.txt");

        var used = await ReceiveAsync(host, tickets[0], Encoding.UTF8.GetBytes("Used file " + Guid.NewGuid()));
        Assert.Equal(201, used.StatusCode);
        await PrepareAsync(host, owner, "11.txt");
    }

    [Fact]
    public async Task At_most_the_daily_number_of_tickets_is_prepared_and_the_next_day_works_again()
    {
        await using var host = UploadTestHost.Start(fixture, uploads: o =>
        {
            o.MaxTicketsPerDay = 3;
            o.PageLifetime = TimeSpan.FromHours(1);
        });
        var owner = NewUser();
        for (var i = 0; i < 3; i++)
            await PrepareAsync(host, owner, $"{i}.txt");

        await Assert.ThrowsAsync<UploadTicketException>(() => PrepareAsync(host, owner, "4.txt"));
        host.Clock.Now += TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1);
        await PrepareAsync(host, owner, "4.txt");
    }

    [Fact]
    public async Task Parallel_preparations_never_pass_the_open_limit_together()
    {
        await using var host = UploadTestHost.Start(fixture, uploads: o => o.MaxOpenTicketsPerUser = 5);
        var owner = NewUser();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            try
            {
                await PrepareAsync(host, owner, $"{i}.txt");
                return true;
            }
            catch (UploadTicketException)
            {
                return false;
            }
        }));

        Assert.Equal(5, results.Count(r => r));
        await using var db = fixture.NewContext();
        Assert.Equal(5, await db.UploadTickets.CountAsync(t => t.OwnerUsername == owner));
    }

    [Fact]
    public async Task A_file_sent_with_the_token_arrives_byte_for_byte_and_the_ticket_is_used_once()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var bytes = SyntheticDocuments.Pdf(3, p => string.Concat(Enumerable.Repeat($"Page {p} of the offer. ", 15)));
        var prepared = await PrepareAsync(host, owner, "offer.pdf", bytes.Length);

        var outcome = await ReceiveAsync(host, prepared, bytes);

        Assert.Equal(201, outcome.StatusCode);
        Assert.True(outcome.Receipt!.Usable, outcome.Receipt.Reason);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), outcome.Sha256);
        Assert.Null(outcome.Reason);
        var ticket = await TicketAsync(prepared.UploadId);
        Assert.Equal((UploadTicketState.Used, outcome.Receipt.SourceId, 1), (ticket.State, ticket.SourceId, ticket.Attempts));
        Assert.Equal(host.Clock.Now, ticket.UsedAt);
        await using (var db = fixture.NewContext())
        {
            var source = await db.Sources.AsNoTracking().SingleAsync(s => s.Id == ticket.SourceId);
            Assert.Equal((owner, outcome.Sha256, SourceOrigin.McpAttachment, "offer.pdf"), (source.OwnerUsername, source.Sha256, source.Origin, source.FileName));
        }

        var again = await ReceiveAsync(host, prepared, bytes);
        Assert.Equal((404, UploadTicketLines.LinkNotValid), (again.StatusCode, again.Reason));
        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains(prepared.Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_wrong_foreign_and_malformed_tokens_all_get_the_same_answer_and_use_no_attempt()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var mine = await PrepareAsync(host, owner, "mine.txt");
        var foreign = await PrepareAsync(host, NewUser(), "foreign.txt");
        var bytes = Encoding.UTF8.GetBytes("Somebody else's file " + Guid.NewGuid());

        var outcomes = new[]
        {
            await ReceiveAsync(host, mine.UploadId, foreign.Token, bytes),
            await ReceiveAsync(host, foreign.UploadId, mine.Token, bytes),
            await ReceiveAsync(host, Guid.NewGuid(), mine.Token, bytes),
            await ReceiveAsync(host, mine.UploadId, UploadTicketService.NewToken(), bytes),
            await ReceiveAsync(host, mine.UploadId, mine.Token + "x", bytes),
            await ReceiveAsync(host, mine.UploadId, string.Empty, bytes),
            await ReceiveAsync(host, mine.UploadId, (await TicketAsync(mine.UploadId)).TokenHash, bytes),
        };

        Assert.All(outcomes, o => Assert.Equal((404, UploadTicketLines.LinkNotValid, (SourceReceipt?)null), (o.StatusCode, o.Reason, o.Receipt)));
        Assert.Equal((UploadTicketState.Open, 0), ((await TicketAsync(mine.UploadId)).State, (await TicketAsync(mine.UploadId)).Attempts));
        Assert.Equal((UploadTicketState.Open, 0), ((await TicketAsync(foreign.UploadId)).State, (await TicketAsync(foreign.UploadId)).Attempts));
        await using var db = fixture.NewContext();
        Assert.False(await db.Sources.AnyAsync(s => s.OwnerUsername == owner));
    }

    [Fact]
    public async Task Ten_parallel_uploads_with_one_token_store_exactly_one_file()
    {
        await using var host = UploadTestHost.Start(fixture, sources: o => o.MaxConcurrentUploadsPerUser = 10);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "race.txt");
        var bytes = Encoding.UTF8.GetBytes("Raced file " + Guid.NewGuid());

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ReceiveAsync(host, prepared, bytes)));

        Assert.Single(outcomes, o => o.StatusCode == 201);
        Assert.All(outcomes.Where(o => o.StatusCode != 201), o => Assert.Contains(o.StatusCode, new[] { 404, 409 }));
        var ticket = await TicketAsync(prepared.UploadId);
        Assert.Equal((UploadTicketState.Used, 1), (ticket.State, ticket.Attempts));
        await using var db = fixture.NewContext();
        Assert.Equal(1, await db.Sources.CountAsync(s => s.OwnerUsername == owner));
    }

    [Fact]
    public async Task While_an_upload_runs_the_token_holder_hears_409_and_the_owner_sees_receiving()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "slow.txt");
        var held = new HeldUploadStream(Encoding.UTF8.GetBytes("Slow file " + Guid.NewGuid()));

        var running = host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ReceiveByTokenAsync(prepared.UploadId, prepared.Token, held));
        await held.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var second = await ReceiveAsync(host, prepared, Encoding.UTF8.GetBytes("Other"));
        var page = await host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ReceiveFromPageAsync(prepared.UploadId, owner, "x.txt", new MemoryStream([1])));
        var probe = await ProbeAsync(host, prepared.UploadId, owner);
        held.Release.SetResult();

        Assert.Equal((409, UploadTicketLines.AlreadyRunning), (second.StatusCode, second.Reason));
        Assert.Equal(409, page.StatusCode);
        Assert.Equal(UploadClaimProbe.Receiving, probe);
        Assert.Equal(201, (await running).StatusCode);
        Assert.Equal(UploadClaimProbe.Used, await ProbeAsync(host, prepared.UploadId, owner));
    }

    [Fact]
    public async Task The_token_expires_after_fifteen_minutes_while_the_page_still_works_for_the_owner()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "late.md");
        var bytes = Encoding.UTF8.GetBytes("# Late file " + Guid.NewGuid());

        host.Clock.Now += TimeSpan.FromMinutes(15);
        var expired = await ReceiveAsync(host, prepared, bytes);
        Assert.Equal((404, UploadTicketLines.LinkNotValid), (expired.StatusCode, expired.Reason));
        Assert.Equal(UploadClaimProbe.Open, await ProbeAsync(host, prepared.UploadId, owner));

        var page = await ReceiveFromPageAsync(host, prepared.UploadId, owner, "late.md", bytes);
        Assert.Equal(201, page.StatusCode);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), page.Sha256);
        await using var db = fixture.NewContext();
        Assert.Equal(SourceOrigin.WebUpload, (await db.Sources.AsNoTracking().SingleAsync(s => s.Id == page.Receipt!.SourceId)).Origin);
    }

    [Fact]
    public async Task One_second_before_the_token_expires_it_still_works()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "edge.txt");

        host.Clock.Now += TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1);

        Assert.Equal(201, (await ReceiveAsync(host, prepared, Encoding.UTF8.GetBytes("Edge " + Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task After_twenty_four_hours_the_page_has_expired_too()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "old.txt");

        host.Clock.Now += TimeSpan.FromHours(24);

        Assert.Equal(UploadClaimProbe.Expired, await ProbeAsync(host, prepared.UploadId, owner));
        var page = await ReceiveFromPageAsync(host, prepared.UploadId, owner, "old.txt", Encoding.UTF8.GetBytes("Old " + Guid.NewGuid()));
        Assert.Equal(404, page.StatusCode);
        Assert.Equal((UploadTicketState.Open, 0), ((await TicketAsync(prepared.UploadId)).State, (await TicketAsync(prepared.UploadId)).Attempts));
    }

    [Fact]
    public async Task Another_signed_in_person_sees_not_found_and_cannot_upload_on_the_page()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var intruder = NewUser();
        var prepared = await PrepareAsync(host, owner, "private.txt");

        Assert.Equal(UploadClaimProbe.NotFound, await ProbeAsync(host, prepared.UploadId, intruder));
        Assert.Equal(UploadClaimProbe.NotFound, await ProbeAsync(host, Guid.NewGuid(), owner));
        var outcome = await ReceiveFromPageAsync(host, prepared.UploadId, intruder, "evil.txt", Encoding.UTF8.GetBytes("Intruder " + Guid.NewGuid()));

        Assert.Equal((404, (SourceReceipt?)null), (outcome.StatusCode, outcome.Receipt));
        Assert.Equal((UploadTicketState.Open, 0), ((await TicketAsync(prepared.UploadId)).State, (await TicketAsync(prepared.UploadId)).Attempts));
        await using var db = fixture.NewContext();
        Assert.False(await db.Sources.AnyAsync(s => s.OwnerUsername == intruder || s.OwnerUsername == owner));
    }

    [Fact]
    public async Task An_unusable_file_is_refused_with_its_sentence_counted_and_the_ticket_stays_open_until_the_last_attempt()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "scan.pdf");
        var scan = SyntheticDocuments.Pdf(2, _ => string.Empty);

        var first = await ReceiveAsync(host, prepared, scan);
        Assert.Equal((422, PdfSourceExtractor.NoTextReason), (first.StatusCode, first.Reason));
        Assert.False(first.Receipt!.Usable);
        Assert.Null(first.Sha256);
        Assert.Equal((UploadTicketState.Open, 1), ((await TicketAsync(prepared.UploadId)).State, (await TicketAsync(prepared.UploadId)).Attempts));

        Assert.Equal(422, (await ReceiveFromPageAsync(host, prepared.UploadId, owner, "scan.pdf", scan)).StatusCode);
        Assert.Equal(422, (await ReceiveAsync(host, prepared, scan)).StatusCode);
        Assert.Equal((UploadTicketState.Failed, 3), ((await TicketAsync(prepared.UploadId)).State, (await TicketAsync(prepared.UploadId)).Attempts));
        Assert.Equal(SourceRefusalCodes.PdfNoText, (await TicketAsync(prepared.UploadId)).LastRefusalCode);
        Assert.Equal(UploadClaimProbe.Failed, await ProbeAsync(host, prepared.UploadId, owner));
        Assert.Equal(404, (await ReceiveAsync(host, prepared, Encoding.UTF8.GetBytes("Good text " + Guid.NewGuid()))).StatusCode);

        await using var db = fixture.NewContext();
        var rows = await db.SourceRefusals.AsNoTracking().Where(r => r.OwnerUsername == owner).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(SourceRefusalCodes.PdfNoText, r.Code));
        Assert.Equal(2, rows.Count(r => r.Path == RefusalPath.McpTicket));
        Assert.Equal(1, rows.Count(r => r.Path == RefusalPath.UploadPage));
        Assert.False(await db.Sources.AnyAsync(s => s.OwnerUsername == owner));
    }

    [Fact]
    public async Task A_smaller_file_than_announced_is_refused_with_both_sizes_and_nothing_is_stored()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "short.txt", 100);

        var outcome = await ReceiveAsync(host, prepared, Encoding.UTF8.GetBytes(new string('s', 50)));

        Assert.Equal((422, "The file is 50 bytes, not the 100 bytes announced. Nothing was stored."), (outcome.StatusCode, outcome.Reason));
        await AssertSizeMismatchCountedAsync(owner, 50);
    }

    [Fact]
    public async Task A_larger_file_than_announced_is_cut_off_and_refused_without_claiming_its_size()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "long.txt", 100);

        var outcome = await ReceiveAsync(host, prepared, Encoding.UTF8.GetBytes(new string('l', 5_000)));

        Assert.Equal((422, "The file is larger than the 100 bytes announced. Nothing was stored."), (outcome.StatusCode, outcome.Reason));
        await AssertSizeMismatchCountedAsync(owner, null);
    }

    [Fact]
    public async Task A_size_mismatch_never_deletes_the_earlier_copy_of_the_same_file()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var bytes = Encoding.UTF8.GetBytes("Kept earlier copy " + Guid.NewGuid());
        var earlier = await ReceiveAsync(host, await PrepareAsync(host, owner, "a.txt"), bytes);
        Assert.Equal(201, earlier.StatusCode);

        var mismatch = await ReceiveAsync(host, await PrepareAsync(host, owner, "a.txt", bytes.Length + 1), bytes);
        var exact = await ReceiveAsync(host, await PrepareAsync(host, owner, "a.txt", bytes.Length), bytes);

        Assert.Equal(422, mismatch.StatusCode);
        Assert.Equal(201, exact.StatusCode);
        Assert.True(exact.Receipt!.Duplicate);
        Assert.Equal(earlier.Receipt!.SourceId, exact.Receipt.SourceId);
        Assert.Equal(earlier.Sha256, exact.Sha256);
        await using var db = fixture.NewContext();
        Assert.True(await db.Sources.AnyAsync(s => s.Id == earlier.Receipt.SourceId));
    }

    [Fact]
    public async Task A_file_above_the_limit_without_an_announced_size_is_too_large()
    {
        await using var host = UploadTestHost.Start(fixture, sources: o => o.MaxFileBytes = 1_000);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "big.txt");

        var outcome = await ReceiveAsync(host, prepared, new byte[1_001]);

        Assert.Equal((413, 1_000L), (outcome.StatusCode, prepared.MaxBytes));
        Assert.Equal((UploadTicketState.Open, 1), ((await TicketAsync(prepared.UploadId)).State, (await TicketAsync(prepared.UploadId)).Attempts));
    }

    [Fact]
    public async Task An_upload_that_breaks_off_releases_the_ticket()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "broken.txt");

        var outcome = await host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>()
            .ReceiveByTokenAsync(prepared.UploadId, prepared.Token, new BreakingUploadStream(Encoding.UTF8.GetBytes("Half " + Guid.NewGuid()))));

        Assert.Equal(400, outcome.StatusCode);
        Assert.Equal((UploadTicketState.Open, 0, (string?)null), ((await TicketAsync(prepared.UploadId)).State, (await TicketAsync(prepared.UploadId)).Attempts, (await TicketAsync(prepared.UploadId)).LastRefusalCode));
    }

    [Fact]
    public async Task A_cancelled_upload_releases_the_ticket_and_rethrows()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "cancelled.txt");
        var held = new HeldUploadStream(Encoding.UTF8.GetBytes("Cancelled " + Guid.NewGuid()));
        using var cts = new CancellationTokenSource();

        var running = host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ReceiveByTokenAsync(prepared.UploadId, prepared.Token, held, cts.Token));
        await held.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal((UploadTicketState.Open, 0), ((await TicketAsync(prepared.UploadId)).State, (await TicketAsync(prepared.UploadId)).Attempts));
    }

    [Fact]
    public async Task An_order_gets_the_receipts_of_arrived_files_in_the_given_order()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var first = await PrepareAsync(host, owner, "first.txt");
        var second = await PrepareAsync(host, owner, "second.md");
        await ReceiveAsync(host, second, Encoding.UTF8.GetBytes("# Second " + Guid.NewGuid()));
        await ReceiveAsync(host, first, Encoding.UTF8.GetBytes("First " + Guid.NewGuid()));

        var receipts = await ResolveAsync(host, [first.UploadId, second.UploadId, first.UploadId], owner);

        Assert.Equal(new[] { "first.txt", "second.md" }, receipts.Select(r => r.FileName));
        Assert.All(receipts, r => Assert.True(r.Usable));
    }

    [Fact]
    public async Task An_order_of_another_person_hears_not_found_and_gets_nothing()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "mine.txt");
        await ReceiveAsync(host, prepared, Encoding.UTF8.GetBytes("Mine " + Guid.NewGuid()));

        var foreign = await Assert.ThrowsAsync<UploadTicketException>(() => ResolveAsync(host, [prepared.UploadId], NewUser()));
        var unknown = Guid.NewGuid();
        var mixed = await Assert.ThrowsAsync<UploadTicketException>(() => ResolveAsync(host, [prepared.UploadId, unknown], owner));

        Assert.Equal($"Upload {prepared.UploadId} was not found. Nothing was started.", foreign.Message);
        Assert.Equal($"Upload {unknown} was not found. Nothing was started.", mixed.Message);
    }

    [Fact]
    public async Task An_order_with_a_file_that_has_not_arrived_names_the_page_and_starts_nothing()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var arrived = await PrepareAsync(host, owner, "arrived.txt");
        await ReceiveAsync(host, arrived, Encoding.UTF8.GetBytes("Arrived " + Guid.NewGuid()));
        var waiting = await PrepareAsync(host, owner, "waiting.txt");

        var ex = await Assert.ThrowsAsync<UploadTicketException>(() => ResolveAsync(host, [arrived.UploadId, waiting.UploadId], owner));

        Assert.Equal(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.NotArrivedFormat, waiting.UploadId, PageLink(waiting.UploadId)), ex.Message);
    }

    [Fact]
    public async Task An_order_with_a_file_that_arrived_and_was_deleted_says_it_is_no_longer_stored()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var deleted = await PrepareAsync(host, owner, "deleted.txt");
        var outcome = await ReceiveAsync(host, deleted, Encoding.UTF8.GetBytes("Deleted later " + Guid.NewGuid()));
        Assert.Equal(SourceDeleteResult.Deleted,
            await host.InScopeAsync(sp => sp.GetRequiredService<ISourceIntakeService>().DeleteAsync(outcome.Receipt!.SourceId!.Value, owner)));

        var gone = await Assert.ThrowsAsync<UploadTicketException>(() => ResolveAsync(host, [deleted.UploadId], owner));

        Assert.Null((await TicketAsync(deleted.UploadId)).SourceId);
        Assert.Equal(UploadTicketState.Used, (await TicketAsync(deleted.UploadId)).State);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.NoLongerStoredFormat, deleted.UploadId), gone.Message);
    }

    [Fact]
    public async Task An_order_with_a_file_that_never_arrived_usable_never_says_it_was_stored_and_gives_the_last_reason()
    {
        await using var host = UploadTestHost.Start(fixture, uploads: o => o.MaxAttempts = 2);
        var owner = NewUser();
        var failed = await PrepareAsync(host, owner, "Brief Vermieter.pdf");
        var refusedOnce = await PrepareAsync(host, owner, "Brief Vermieter 2.pdf");
        var expired = await PrepareAsync(host, owner, "Notiz Vermieter.txt");
        var scan = SyntheticDocuments.Pdf(2, _ => string.Empty);
        Assert.Equal(422, (await ReceiveAsync(host, failed, scan)).StatusCode);
        Assert.Equal(422, (await ReceiveFromPageAsync(host, failed.UploadId, owner, "Brief Vermieter.pdf", scan)).StatusCode);
        Assert.Equal(422, (await ReceiveAsync(host, refusedOnce, scan)).StatusCode);

        var outOfAttempts = await Assert.ThrowsAsync<UploadTicketException>(() => ResolveAsync(host, [failed.UploadId], owner));
        var stillOpen = await Assert.ThrowsAsync<UploadTicketException>(() => ResolveAsync(host, [refusedOnce.UploadId], owner));
        host.Clock.Now += TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1);
        var timedOut = await Assert.ThrowsAsync<UploadTicketException>(() => ResolveAsync(host, [expired.UploadId], owner));
        var expiredAfterRefusal = await Assert.ThrowsAsync<UploadTicketException>(() => ResolveAsync(host, [refusedOnce.UploadId], owner));

        Assert.Equal(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.NoUsableFileRefusedFormat, failed.UploadId, PdfSourceExtractor.NoTextReason), outOfAttempts.Message);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.RefusedSoFarFormat, refusedOnce.UploadId, PdfSourceExtractor.NoTextReason, PageLink(refusedOnce.UploadId)), stillOpen.Message);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.NoUsableFileFormat, expired.UploadId), timedOut.Message);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.NoUsableFileRefusedFormat, refusedOnce.UploadId, PdfSourceExtractor.NoTextReason), expiredAfterRefusal.Message);
        foreach (var message in new[] { outOfAttempts.Message, stillOpen.Message, timedOut.Message, expiredAfterRefusal.Message })
        {
            Assert.DoesNotContain("no longer stored", message, StringComparison.Ordinal);
            Assert.EndsWith("Nothing was started.", message, StringComparison.Ordinal);
            Assert.DoesNotContain("Vermieter", message, StringComparison.Ordinal);
        }
        Assert.StartsWith($"No usable file arrived for upload {failed.UploadId}.", outOfAttempts.Message, StringComparison.Ordinal);
        Assert.StartsWith($"No usable file arrived for upload {expired.UploadId}", timedOut.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_stored_refusal_code_has_a_fixed_sentence_without_any_part_of_the_file()
    {
        await using var host = UploadTestHost.Start(fixture);
        await using var scope = host.Services.CreateAsyncScope();
        var service = (UploadTicketService)scope.ServiceProvider.GetRequiredService<IUploadTicketService>();
        var codes = typeof(SourceRefusalCodes).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!)
            .Where(SourceRefusalCodes.CountsAsAttempt).ToList();

        Assert.Contains(SourceRefusalCodes.TooLongForTicket, codes);
        foreach (var code in codes.Where(c => c is not (SourceRefusalCodes.Other or SourceRefusalCodes.McpJson or SourceRefusalCodes.McpPlaceholder
                     or SourceRefusalCodes.McpBase64Invalid or SourceRefusalCodes.McpTypeMismatch or SourceRefusalCodes.McpUnsupportedType or SourceRefusalCodes.McpEmpty)))
            Assert.NotEqual("This file could not be used.", service.ReasonOf(code));
        Assert.Equal("This file could not be used.", service.ReasonOf("something new"));
    }

    [Fact]
    public async Task Switched_off_the_service_offers_nothing()
    {
        await using var on = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(on, owner, "before.txt");

        await using var off = UploadTestHost.Start(fixture, ticketsOn: false);
        await using var scope = off.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IUploadTicketService>();

        Assert.False(service.Enabled);
        Assert.Equal(UploadTicketLines.NotAvailable, (await Assert.ThrowsAsync<UploadTicketException>(() => service.PrepareAsync(owner, "a.txt", null))).Message);
        Assert.Equal(404, (await service.ReceiveByTokenAsync(prepared.UploadId, prepared.Token, new MemoryStream([1, 2]))).StatusCode);
        Assert.Equal(404, (await service.ReceiveFromPageAsync(prepared.UploadId, owner, "a.txt", new MemoryStream([1, 2]))).StatusCode);
        Assert.Equal(UploadClaimProbe.NotFound, await service.ProbeAsync(prepared.UploadId, owner));
        Assert.Equal(UploadTicketLines.NotAvailable, (await Assert.ThrowsAsync<UploadTicketException>(() => service.ResolveAsync([prepared.UploadId], owner, PageLink))).Message);
        Assert.Equal((UploadTicketState.Open, 0), ((await TicketAsync(prepared.UploadId)).State, (await TicketAsync(prepared.UploadId)).Attempts));
    }

    [Fact]
    public async Task The_switch_is_off_unless_configured_and_both_services_are_always_registered()
    {
        await using var host = UploadTestHost.Start(fixture, ticketsOn: false);
        await using var scope = host.Services.CreateAsyncScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<IUploadTicketService>().Enabled);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISourceRefusalLog>());
        Assert.Equal(UploadTicketsMode.Off, new UploadOptions().Tickets);
    }

    [Fact]
    public async Task Cleanup_releases_hanging_uploads_after_the_upload_timeout()
    {
        await using var host = UploadTestHost.Start(fixture, uploads: o => o.MaxAttempts = 2);
        var owner = NewUser();
        var open = await PrepareAsync(host, owner, "a.txt");
        var last = await PrepareAsync(host, owner, "b.txt");
        await using (var db = fixture.NewContext())
        {
            await db.UploadTickets.Where(t => t.Id == open.UploadId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Receiving).SetProperty(t => t.Attempts, 1).SetProperty(t => t.ReceivingSince, host.Clock.Now));
            await db.UploadTickets.Where(t => t.Id == last.UploadId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Receiving).SetProperty(t => t.Attempts, 2).SetProperty(t => t.ReceivingSince, host.Clock.Now));
        }

        var cleanup = Cleanup(host);
        host.Clock.Now += TimeSpan.FromMinutes(5);
        await cleanup.CleanUploadsAsync(CancellationToken.None);
        Assert.Equal(UploadTicketState.Receiving, (await TicketAsync(open.UploadId)).State);

        host.Clock.Now += TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1);
        var (reset, _, _) = await cleanup.CleanUploadsAsync(CancellationToken.None);

        Assert.True(reset >= 2);
        var reopened = await TicketAsync(open.UploadId);
        Assert.Equal((UploadTicketState.Open, (DateTimeOffset?)null), (reopened.State, reopened.ReceivingSince));
        Assert.Equal(UploadTicketState.Failed, (await TicketAsync(last.UploadId)).State);
    }

    [Fact]
    public async Task Cleanup_deletes_tickets_a_week_after_their_page_expired_and_refusals_after_half_a_year()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var ticket = await PrepareAsync(host, owner, "a.txt");
        await host.Services.GetRequiredService<ISourceRefusalLog>().RecordAsync(new SourceRefusalRecord(owner, RefusalPath.Web, SourceRefusalCodes.Empty, null, "txt", null, 0));
        var cleanup = Cleanup(host);

        host.Clock.Now += TimeSpan.FromHours(24) + TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1);
        await cleanup.CleanUploadsAsync(CancellationToken.None);
        Assert.True(await TicketExistsAsync(ticket.UploadId));

        host.Clock.Now += TimeSpan.FromMinutes(2);
        await cleanup.CleanUploadsAsync(CancellationToken.None);
        Assert.False(await TicketExistsAsync(ticket.UploadId));
        Assert.Equal(1, await RefusalCountAsync(owner));

        host.Clock.Now += TimeSpan.FromDays(180) - TimeSpan.FromDays(9);
        await cleanup.CleanUploadsAsync(CancellationToken.None);
        Assert.Equal(1, await RefusalCountAsync(owner));

        host.Clock.Now += TimeSpan.FromDays(1);
        await cleanup.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, await RefusalCountAsync(owner));
    }

    [Fact]
    public async Task Cleanup_of_an_expired_source_leaves_the_used_ticket_without_its_source()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();
        var prepared = await PrepareAsync(host, owner, "a.txt");
        Assert.Equal(201, (await ReceiveAsync(host, prepared, Encoding.UTF8.GetBytes("Forgotten " + Guid.NewGuid()))).StatusCode);

        host.Clock.Now += TimeSpan.FromHours(25);
        await Cleanup(host).RunOnceAsync(CancellationToken.None);

        var ticket = await TicketAsync(prepared.UploadId);
        Assert.Equal((UploadTicketState.Used, (Guid?)null), (ticket.State, ticket.SourceId));
        var ex = await Assert.ThrowsAsync<UploadTicketException>(() => ResolveAsync(host, [prepared.UploadId], owner));
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.NoLongerStoredFormat, prepared.UploadId), ex.Message);
    }

    [Fact]
    public async Task Uploads_refused_for_overload_in_parallel_use_no_attempt_and_the_file_arrives_later()
    {
        await using var host = UploadTestHost.Start(fixture, uploads: o => o.MaxAttempts = 3, sources: o => o.MaxConcurrentUploadsPerUser = 2);
        var owner = NewUser();
        var first = await PrepareAsync(host, owner, "first.txt");
        var second = await PrepareAsync(host, owner, "second.txt");
        var waiting = new List<PreparedUpload>();
        for (var i = 0; i < 4; i++)
            waiting.Add(await PrepareAsync(host, owner, $"waiting-{i}.txt"));
        var heldFirst = new HeldUploadStream(Encoding.UTF8.GetBytes("First " + Guid.NewGuid()));
        var heldSecond = new HeldUploadStream(Encoding.UTF8.GetBytes("Second " + Guid.NewGuid()));
        var running = new[]
        {
            host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ReceiveByTokenAsync(first.UploadId, first.Token, heldFirst)),
            host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ReceiveFromPageAsync(second.UploadId, owner, "second.txt", heldSecond)),
        };
        await Task.WhenAll(heldFirst.Reading.Task, heldSecond.Reading.Task).WaitAsync(TimeSpan.FromSeconds(10));

        for (var round = 0; round < 4; round++)
        {
            var refused = await Task.WhenAll(waiting.Select((w, i) => i % 2 == 0
                ? ReceiveAsync(host, w, Encoding.UTF8.GetBytes("Waiting " + w.UploadId))
                : ReceiveFromPageAsync(host, w.UploadId, owner, "w.txt", Encoding.UTF8.GetBytes("Waiting " + w.UploadId))));
            Assert.All(refused, r => Assert.Equal(429, r.StatusCode));
        }

        foreach (var w in waiting)
        {
            var ticket = await TicketAsync(w.UploadId);
            Assert.Equal((UploadTicketState.Open, 0, (string?)null), (ticket.State, ticket.Attempts, ticket.LastRefusalCode));
            Assert.Equal(UploadClaimProbe.Open, await ProbeAsync(host, w.UploadId, owner));
        }

        heldFirst.Release.SetResult();
        heldSecond.Release.SetResult();
        Assert.All(await Task.WhenAll(running), r => Assert.Equal(201, r.StatusCode));
        foreach (var w in waiting)
            Assert.Equal(201, (await ReceiveAsync(host, w, Encoding.UTF8.GetBytes("Waiting " + w.UploadId))).StatusCode);
        await using var db = fixture.NewContext();
        Assert.Equal(16, await db.SourceRefusals.CountAsync(r => r.OwnerUsername == owner && r.Code == SourceRefusalCodes.UserBusy));
    }

    [Fact]
    public async Task A_busy_service_uses_no_attempt_while_checked_files_still_use_all_three()
    {
        await using var host = UploadTestHost.Start(fixture, uploads: o => o.MaxAttempts = 1,
            sources: o => { o.MaxConcurrentIntakes = 1; o.ExtractionQueueTimeout = TimeSpan.FromMilliseconds(100); });
        var owner = NewUser();
        var holder = await PrepareAsync(host, NewUser(), "holder.txt");
        var prepared = await PrepareAsync(host, owner, "later.txt");
        var held = new HeldUploadStream(Encoding.UTF8.GetBytes("Holder " + Guid.NewGuid()));
        var running = host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ReceiveByTokenAsync(holder.UploadId, holder.Token, held));
        await held.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10));

        for (var i = 0; i < 3; i++)
            Assert.Equal(503, (await ReceiveAsync(host, prepared, Encoding.UTF8.GetBytes("Later " + prepared.UploadId))).StatusCode);
        Assert.Equal((UploadTicketState.Open, 0), ((await TicketAsync(prepared.UploadId)).State, (await TicketAsync(prepared.UploadId)).Attempts));

        held.Release.SetResult();
        Assert.Equal(201, (await running).StatusCode);
        Assert.Equal(422, (await ReceiveAsync(host, prepared, [])).StatusCode);
        Assert.Equal((UploadTicketState.Failed, 1, SourceRefusalCodes.Empty),
            ((await TicketAsync(prepared.UploadId)).State, (await TicketAsync(prepared.UploadId)).Attempts, (await TicketAsync(prepared.UploadId)).LastRefusalCode));
    }

    [Theory]
    [InlineData(SourceRefusalCodes.UserBusy, false)]
    [InlineData(SourceRefusalCodes.Busy, false)]
    [InlineData(SourceRefusalCodes.Interrupted, false)]
    [InlineData(SourceRefusalCodes.SaveFailed, false)]
    [InlineData(SourceRefusalCodes.Unavailable, false)]
    [InlineData(SourceRefusalCodes.Quota, false)]
    [InlineData(null, false)]
    [InlineData(SourceRefusalCodes.PdfNoText, true)]
    [InlineData(SourceRefusalCodes.Empty, true)]
    [InlineData(SourceRefusalCodes.TooLarge, true)]
    [InlineData(SourceRefusalCodes.SizeMismatch, true)]
    [InlineData(SourceRefusalCodes.Timeout, true)]
    [InlineData(SourceRefusalCodes.TooLongForTicket, true)]
    [InlineData(SourceRefusalCodes.Other, true)]
    public void Only_a_file_that_was_checked_uses_an_attempt(string? code, bool counts) =>
        Assert.Equal(counts, SourceRefusalCodes.CountsAsAttempt(code));

    [Fact]
    public async Task A_pdf_too_long_for_the_ticket_is_refused_with_a_way_out_counted_and_not_kept()
    {
        await using var host = UploadTestHost.Start(fixture, sources: o => { o.FullTextMaxChars = 1_000; o.MapMaxChars = 2_000; });
        var owner = NewUser();
        var pdf = SyntheticDocuments.Pdf(12, p => $"Page {p} of a long synthetic report. " + string.Concat(Enumerable.Repeat("Plain words fill this page. ", 12)));
        var token = await PrepareAsync(host, owner, "report.pdf");
        var page = await PrepareAsync(host, owner, "report.pdf");

        var byToken = await ReceiveAsync(host, token, pdf);
        var byPage = await ReceiveFromPageAsync(host, page.UploadId, owner, "report.pdf", pdf);

        foreach (var outcome in new[] { byToken, byPage })
        {
            Assert.Equal(422, outcome.StatusCode);
            Assert.False(outcome.Receipt!.Usable);
            Assert.Null(outcome.Receipt.SourceId);
            Assert.Null(outcome.Sha256);
            Assert.Equal(string.Empty, outcome.Receipt.Preview);
            Assert.Empty(outcome.Receipt.Warnings);
            Assert.NotEqual(SourceBudgetClass.TooLarge, outcome.Receipt.BudgetClass);
            Assert.Matches(@"^This file is too long to use completely \([0-9,]+ characters, the limit is 2,000\), and pages cannot be chosen when it is added this way\. Use a shorter file, or start the order in the Atelier web app and choose the pages there\. Nothing was stored\.$", outcome.Reason);
            Assert.DoesNotContain("Enter the pages", outcome.Reason, StringComparison.Ordinal);
        }
        Assert.Equal((UploadTicketState.Open, 1, SourceRefusalCodes.TooLongForTicket),
            ((await TicketAsync(token.UploadId)).State, (await TicketAsync(token.UploadId)).Attempts, (await TicketAsync(token.UploadId)).LastRefusalCode));
        var order = await Assert.ThrowsAsync<UploadTicketException>(() => ResolveAsync(host, [token.UploadId], owner));
        Assert.Contains("start the order in the Atelier web app and choose the pages there", order.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("has not arrived yet", order.Message, StringComparison.Ordinal);

        await using var db = fixture.NewContext();
        Assert.False(await db.Sources.AnyAsync(s => s.OwnerUsername == owner));
        var rows = await db.SourceRefusals.AsNoTracking().Where(r => r.OwnerUsername == owner).OrderBy(r => r.Path).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal((SourceRefusalCodes.TooLongForTicket, (SourceKind?)SourceKind.Pdf, (string?)"pdf", (long?)pdf.Length), (r.Code, r.Kind, r.Extension, r.SizeBytes)));
        Assert.Equal(new[] { RefusalPath.McpTicket, RefusalPath.UploadPage }, rows.Select(r => r.Path).Order());
    }

    [Fact]
    public async Task A_text_too_long_for_the_ticket_asks_for_a_shorter_version_and_keeps_an_earlier_copy()
    {
        await using var host = UploadTestHost.Start(fixture, sources: o => { o.FullTextMaxChars = 100; o.MapMaxChars = 200; });
        var owner = NewUser();
        var text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Long synthetic notes. ", 30)) + Guid.NewGuid());
        var earlier = await host.InScopeAsync(sp => sp.GetRequiredService<ISourceIntakeService>()
            .IntakeAsync(new SourceUpload("notes.txt", "text/plain", text.Length, new MemoryStream(text)), owner, SourceOrigin.WebUpload));
        Assert.True(earlier.Usable);

        var outcome = await ReceiveAsync(host, await PrepareAsync(host, owner, "notes.txt"), text);

        Assert.Equal(422, outcome.StatusCode);
        Assert.Matches(@"^This file is too long to use completely \([0-9,]+ characters, the limit is 200\)\. Use a shorter version\. Nothing was stored\.$", outcome.Reason);
        await using var db = fixture.NewContext();
        Assert.True(await db.Sources.AnyAsync(s => s.Id == earlier.SourceId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_file_too_long_for_the_ticket_that_cannot_be_removed_is_not_called_not_stored(bool throws)
    {
        await using var host = UploadTestHost.Start(fixture, sources: o => { o.FullTextMaxChars = 100; o.MapMaxChars = 200; }, replace: services =>
        {
            services.AddScoped<SourceIntakeService>();
            services.Replace(ServiceDescriptor.Scoped<ISourceIntakeService>(sp => new UndeletableIntake(sp.GetRequiredService<SourceIntakeService>(), throws)));
        });
        var owner = NewUser();
        var text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Long synthetic notes. ", 30)) + Guid.NewGuid());

        var outcome = await ReceiveAsync(host, await PrepareAsync(host, owner, "notes.txt"), text);

        Assert.Equal(422, outcome.StatusCode);
        Assert.Null(outcome.Receipt!.SourceId);
        Assert.Matches(@"^This file is too long to use completely \([0-9,]+ characters, the limit is 200\)\. Use a shorter version\. The file could not be removed right away, so it may still be stored for a while\. It is not added to any order this way\.$", outcome.Reason);
        Assert.DoesNotContain(UploadTicketLines.NothingStored, outcome.Reason, StringComparison.Ordinal);
        await using var db = fixture.NewContext();
        Assert.True(await db.Sources.AnyAsync(s => s.OwnerUsername == owner));
        Assert.Contains(host.Logs.Lines, l => l.Contains("could not be removed again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_ticket_refused_for_its_announced_size_is_counted_without_its_name()
    {
        await using var host = UploadTestHost.Start(fixture);
        var owner = NewUser();

        await Assert.ThrowsAsync<UploadTicketException>(() => PrepareAsync(host, owner, "Geheimer Plan.pdf", 26_214_401));

        await using var db = fixture.NewContext();
        var row = Assert.Single(await db.SourceRefusals.AsNoTracking().Where(r => r.OwnerUsername == owner).ToListAsync());
        Assert.Equal((RefusalPath.McpTicket, SourceRefusalCodes.TooLarge, (string?)"pdf", (long?)26_214_401, (SourceKind?)null, (string?)null),
            (row.Path, row.Code, row.Extension, row.SizeBytes, row.Kind, row.DeclaredType));
        Assert.False(await db.UploadTickets.AnyAsync(t => t.OwnerUsername == owner));
    }

    [Fact]
    public async Task A_ticket_left_receiving_by_a_crash_is_released_when_it_is_used_again_with_the_cleanup_deadline()
    {
        await using var host = UploadTestHost.Start(fixture, uploads: o => o.MaxAttempts = 2);
        var owner = NewUser();
        var stale = await PrepareAsync(host, owner, "stale.txt");
        var recent = await PrepareAsync(host, owner, "recent.txt");
        var last = await PrepareAsync(host, owner, "last.txt");
        var deadline = TimeSpan.FromMinutes(5) + TimeSpan.FromMinutes(1);
        await using (var db = fixture.NewContext())
        {
            await db.UploadTickets.Where(t => t.Id == stale.UploadId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Receiving).SetProperty(t => t.Attempts, 1).SetProperty(t => t.ReceivingSince, host.Clock.Now - deadline - TimeSpan.FromSeconds(1)));
            await db.UploadTickets.Where(t => t.Id == recent.UploadId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Receiving).SetProperty(t => t.Attempts, 1).SetProperty(t => t.ReceivingSince, host.Clock.Now - deadline + TimeSpan.FromSeconds(1)));
            await db.UploadTickets.Where(t => t.Id == last.UploadId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Receiving).SetProperty(t => t.Attempts, 2).SetProperty(t => t.ReceivingSince, host.Clock.Now - deadline - TimeSpan.FromSeconds(1)));
        }

        Assert.Equal(UploadClaimProbe.Open, await ProbeAsync(host, stale.UploadId, owner));
        Assert.Equal(UploadClaimProbe.Receiving, await ProbeAsync(host, recent.UploadId, owner));
        Assert.Equal(UploadClaimProbe.Failed, await ProbeAsync(host, last.UploadId, owner));
        Assert.Equal(409, (await ReceiveAsync(host, recent, Encoding.UTF8.GetBytes("Recent " + Guid.NewGuid()))).StatusCode);

        await using (var db = fixture.NewContext())
            await db.UploadTickets.Where(t => t.Id == stale.UploadId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Receiving).SetProperty(t => t.ReceivingSince, host.Clock.Now - deadline - TimeSpan.FromSeconds(1)));
        var byToken = await ReceiveAsync(host, stale, Encoding.UTF8.GetBytes("Stale " + Guid.NewGuid()));

        Assert.Equal(201, byToken.StatusCode);
        Assert.Equal((UploadTicketState.Used, 2), ((await TicketAsync(stale.UploadId)).State, (await TicketAsync(stale.UploadId)).Attempts));
    }

    private async Task AssertSizeMismatchCountedAsync(string owner, long? size)
    {
        await using var db = fixture.NewContext();
        Assert.False(await db.Sources.AnyAsync(s => s.OwnerUsername == owner));
        var row = Assert.Single(await db.SourceRefusals.AsNoTracking().Where(r => r.OwnerUsername == owner).ToListAsync());
        Assert.Equal((SourceRefusalCodes.SizeMismatch, RefusalPath.McpTicket, "txt", size), (row.Code, row.Path, row.Extension, row.SizeBytes));
    }

    private static SourceCleanupBackgroundService Cleanup(UploadTestHost host) =>
        host.Services.GetServices<IHostedService>().OfType<SourceCleanupBackgroundService>().Single();

    private static Task<PreparedUpload> PrepareAsync(UploadTestHost host, string owner, string name, long? size = null) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().PrepareAsync(owner, name, size));

    private static Task<UploadOutcome> ReceiveAsync(UploadTestHost host, PreparedUpload prepared, byte[] bytes) =>
        ReceiveAsync(host, prepared.UploadId, prepared.Token, bytes);

    private static Task<UploadOutcome> ReceiveAsync(UploadTestHost host, Guid id, string token, byte[] bytes) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ReceiveByTokenAsync(id, token, new MemoryStream(bytes)));

    private static Task<UploadOutcome> ReceiveFromPageAsync(UploadTestHost host, Guid id, string owner, string name, byte[] bytes) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ReceiveFromPageAsync(id, owner, name, new MemoryStream(bytes)));

    private static Task<UploadClaimProbe> ProbeAsync(UploadTestHost host, Guid id, string owner) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ProbeAsync(id, owner));

    private static Task<IReadOnlyList<SourceReceipt>> ResolveAsync(UploadTestHost host, IReadOnlyList<Guid> ids, string owner) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ResolveAsync(ids, owner, PageLink));

    private async Task<UploadTicketEntity> TicketAsync(Guid id)
    {
        await using var db = fixture.NewContext();
        return await db.UploadTickets.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task<bool> TicketExistsAsync(Guid id)
    {
        await using var db = fixture.NewContext();
        return await db.UploadTickets.AnyAsync(t => t.Id == id);
    }

    private async Task<int> RefusalCountAsync(string owner)
    {
        await using var db = fixture.NewContext();
        return await db.SourceRefusals.CountAsync(r => r.OwnerUsername == owner);
    }

    private async Task<string> RowJsonAsync(Guid id)
    {
        await using var db = fixture.NewContext();
        return await db.Database.SqlQuery<string>($"""SELECT row_to_json(t)::text AS "Value" FROM "UploadTickets" t WHERE t."Id" = {id}""").SingleAsync();
    }

    private sealed class UndeletableIntake(ISourceIntakeService inner, bool throws) : ISourceIntakeService
    {
        public Task<SourceReceipt> IntakeAsync(SourceUpload upload, string ownerUsername, SourceOrigin origin, CancellationToken ct = default, RefusalPath path = RefusalPath.Web) =>
            inner.IntakeAsync(upload, ownerUsername, origin, ct, path);

        public Task<SourceReceipt> IntakeTextAsync(string title, string text, string ownerUsername, SourceOrigin origin, CancellationToken ct = default) =>
            inner.IntakeTextAsync(title, text, ownerUsername, origin, ct);

        public Task<SourceReceipt?> GetReceiptAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default) =>
            inner.GetReceiptAsync(sourceId, requestingUsername, ct);

        public Task<SourceDeleteResult> DeleteAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default) =>
            throws ? throw new IOException("The storage is not reachable.") : Task.FromResult(SourceDeleteResult.InUse);
    }
}
