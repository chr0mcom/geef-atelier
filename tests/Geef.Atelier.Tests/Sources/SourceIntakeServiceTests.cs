using System.Text;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Sources;

/// <summary><see cref="SourceIntakeService"/> and the cleanup against real Postgres and a temporary storage root.</summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class SourceIntakeServiceTests(PostgresFixture fixture) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"geef-sources-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task StoresAPdfWithReceiptFileAndExpiry()
    {
        await using var provider = Build();
        var user = NewUser();
        var pdf = SyntheticDocuments.Pdf(3, p => string.Concat(Enumerable.Repeat($"Page {p} explains the offer in detail. ", 12)));

        var receipt = await IntakeAsync(provider, user, "../../etc/Offer 2026.pdf", pdf, "application/x-msdownload");

        Assert.True(receipt.Usable, receipt.Reason);
        Assert.False(receipt.Duplicate);
        Assert.Equal("Offer 2026.pdf", receipt.FileName);
        Assert.Equal(SourceKind.Pdf, receipt.Kind);
        Assert.Equal(3, receipt.PageCount);
        Assert.Equal(pdf.Length, receipt.SizeBytes);
        Assert.Equal(SourceBudgetClass.FullText, receipt.BudgetClass);
        Assert.StartsWith("Page 1 explains", receipt.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain("[[page", receipt.Preview, StringComparison.Ordinal);
        Assert.True(receipt.EstimatedTokens > receipt.CharCount / 2);

        await using var db = fixture.NewContext();
        var stored = await db.Sources.AsNoTracking().SingleAsync(s => s.Id == receipt.SourceId);
        Assert.Equal(user, stored.OwnerUsername);
        Assert.Equal("application/pdf", stored.ContentType);
        Assert.Equal(SourceOrigin.WebUpload, stored.Origin);
        Assert.NotNull(stored.ExpiresAt);
        Assert.Equal($"{SourceFileStore.OwnerFolder(user)}/{stored.Id:N}.pdf", stored.StoragePath);
        Assert.Equal(pdf, await File.ReadAllBytesAsync(Path.Combine(_root, stored.StoragePath!)));
        Assert.Contains("[[page 3]]", stored.ExtractedText, StringComparison.Ordinal);
        Assert.Equal(PageMarkers.StripMarkers(stored.ExtractedText!).Length, stored.CharCount);
    }

    [Fact]
    public async Task ReusesTheSameFileOfTheSameOwnerButNeverAcrossOwners()
    {
        await using var provider = Build();
        var owner = NewUser();
        var other = NewUser();
        var bytes = Encoding.UTF8.GetBytes("Meeting notes for the spring campaign. " + Guid.NewGuid());

        var first = await IntakeAsync(provider, owner, "notes.md", bytes);
        var again = await IntakeAsync(provider, owner, "renamed.md", bytes);
        var foreign = await IntakeAsync(provider, other, "notes.md", bytes);

        Assert.Equal(first.SourceId, again.SourceId);
        Assert.True(again.Duplicate);
        Assert.Contains(again.Warnings, w => w.Code == SourceWarningCodes.Duplicate);
        Assert.NotEqual(first.SourceId, foreign.SourceId);
        Assert.False(foreign.Duplicate);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, SourceFileStore.OwnerFolder(owner))));
    }

    [Fact]
    public async Task OwnershipIsCheckedForReceiptsAndDeletion()
    {
        await using var provider = Build();
        var owner = NewUser();
        var receipt = await IntakeAsync(provider, owner, "a.txt", Encoding.UTF8.GetBytes("Text of " + Guid.NewGuid()));
        var id = receipt.SourceId!.Value;

        await using var scope = provider.CreateAsyncScope();
        var intake = scope.ServiceProvider.GetRequiredService<ISourceIntakeService>();
        Assert.Null(await intake.GetReceiptAsync(id, NewUser()));
        Assert.NotNull(await intake.GetReceiptAsync(id, owner));
        Assert.NotNull(await intake.GetReceiptAsync(id, null));
        Assert.Equal(SourceDeleteResult.NotFound, await intake.DeleteAsync(id, NewUser()));

        var stored = (await scope.ServiceProvider.GetRequiredService<ISourceRepository>().GetAsync(id, false))!;
        Assert.Equal(SourceDeleteResult.Deleted, await intake.DeleteAsync(id, owner));
        Assert.False(File.Exists(Path.Combine(_root, stored.StoragePath!)));
        Assert.Equal(SourceDeleteResult.NotFound, await intake.DeleteAsync(id, owner));
    }

    [Fact]
    public async Task AttachedSourcesCannotBeDeleted()
    {
        await using var provider = Build();
        var owner = NewUser();
        var receipt = await IntakeAsync(provider, owner, "a.txt", Encoding.UTF8.GetBytes("Attached " + Guid.NewGuid()));
        var order = Order(owner);
        await using (var db = fixture.NewContext())
        {
            db.Orders.Add(order);
            db.OrderSources.Add(Link(order.Id, receipt.SourceId!.Value));
            await db.SaveChangesAsync();
        }

        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(SourceDeleteResult.InUse, await scope.ServiceProvider.GetRequiredService<ISourceIntakeService>().DeleteAsync(receipt.SourceId!.Value, owner));
    }

    [Fact]
    public async Task RefusesWrongTypesTooLargeAndEmptyFilesWithoutStoringThem()
    {
        await using var provider = Build(o => o.MaxFileBytes = 1_000);
        var user = NewUser();

        var exe = await IntakeAsync(provider, user, "invoice.pdf", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00]);
        Assert.False(exe.Usable);
        Assert.Null(exe.SourceId);
        Assert.Equal(SourceKindDetector.UnsupportedReason, exe.Reason);

        var large = await IntakeAsync(provider, user, "big.txt", new byte[1_001]);
        Assert.False(large.Usable);
        Assert.Equal("This file is larger than 1 KB.", large.Reason);

        var empty = await IntakeAsync(provider, user, "empty.txt", []);
        Assert.Equal("This file is empty.", empty.Reason);

        await using var roomy = Build();
        var scan = await IntakeAsync(roomy, user, "scan.pdf", SyntheticDocuments.Pdf(2, _ => string.Empty));
        Assert.False(scan.Usable);
        Assert.Equal(PdfSourceExtractor.NoTextReason, scan.Reason);

        await using var db = fixture.NewContext();
        Assert.False(await db.Sources.AnyAsync(s => s.OwnerUsername == user));
        Assert.False(Directory.Exists(Path.Combine(_root, SourceFileStore.OwnerFolder(user))));
    }

    [Fact]
    public async Task LongFilesSayThatPartsWillBeSearched()
    {
        await using var provider = Build(o =>
        {
            o.FullTextMaxChars = 100;
            o.MapMaxChars = 300;
        });
        var user = NewUser();

        var withMap = await IntakeAsync(provider, user, "long.txt", Encoding.UTF8.GetBytes(new string('a', 150) + " " + Guid.NewGuid()));
        Assert.Equal(SourceBudgetClass.WithMap, withMap.BudgetClass);
        Assert.Equal("Large file: parts will be searched, not read in full.", Assert.Single(withMap.Warnings, w => w.Code == SourceWarningCodes.UsesDocumentMap).Message);

        var tooLarge = await IntakeAsync(provider, user, "huge.txt", Encoding.UTF8.GetBytes(new string('b', 400) + " " + Guid.NewGuid()));
        Assert.True(tooLarge.Usable);
        Assert.Equal(SourceBudgetClass.TooLarge, tooLarge.BudgetClass);
        var overBudget = Assert.Single(tooLarge.Warnings, w => w.Code == SourceWarningCodes.OverBudget).Message;
        Assert.EndsWith("Please use a shorter version.", overBudget, StringComparison.Ordinal);
        Assert.DoesNotContain("pages", overBudget, StringComparison.OrdinalIgnoreCase);

        var longPdf = await IntakeAsync(provider, user, "long.pdf", SyntheticDocuments.Pdf(3, page => $"Page {page} {Guid.NewGuid()} " + string.Join(' ', Enumerable.Repeat("word", 40))));
        Assert.True(longPdf.Usable);
        Assert.Equal(SourceBudgetClass.TooLarge, longPdf.BudgetClass);
        Assert.EndsWith("Choose the pages to use.", Assert.Single(longPdf.Warnings, w => w.Code == SourceWarningCodes.OverBudget).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PastedTextAndWordFilesAreAccepted()
    {
        await using var provider = Build();
        var user = NewUser();
        await using var scope = provider.CreateAsyncScope();
        var intake = scope.ServiceProvider.GetRequiredService<ISourceIntakeService>();

        var pasted = await intake.IntakeTextAsync("Brief", "We need a friendly reminder. " + Guid.NewGuid(), user, SourceOrigin.Pasted);
        Assert.True(pasted.Usable);
        Assert.Equal("Brief.txt", pasted.FileName);
        Assert.Equal(SourceKind.Text, pasted.Kind);

        var docx = await IntakeAsync(provider, user, "offer.docx", SyntheticDocuments.Docx(["# Offer", "Text " + Guid.NewGuid()]));
        Assert.True(docx.Usable, docx.Reason);
        Assert.Equal(SourceKind.Docx, docx.Kind);
        Assert.StartsWith("# Offer", docx.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanupDeletesExpiredUnattachedSourcesWithTheirFiles()
    {
        var clock = new MutableTime(DateTimeOffset.UtcNow);
        await using var provider = Build(time: clock);
        var user = NewUser();
        var receipt = await IntakeAsync(provider, user, "old.txt", Encoding.UTF8.GetBytes("Forgotten upload " + Guid.NewGuid()));
        string path;
        await using (var db = fixture.NewContext())
            path = (await db.Sources.AsNoTracking().SingleAsync(s => s.Id == receipt.SourceId)).StoragePath!;

        var cleanup = provider.GetServices<IHostedService>().OfType<SourceCleanupBackgroundService>().Single();
        clock.Now = clock.Now.AddHours(25);
        var (deleted, _) = await cleanup.RunOnceAsync(CancellationToken.None);

        Assert.True(deleted >= 1);
        await using var check = fixture.NewContext();
        Assert.False(await check.Sources.AnyAsync(s => s.Id == receipt.SourceId));
        Assert.False(File.Exists(Path.Combine(_root, path)));
    }

    [Fact]
    public async Task CleanupFreesOriginalsOfLongFinishedOrdersButKeepsTheText()
    {
        var clock = new MutableTime(DateTimeOffset.UtcNow);
        await using var provider = Build(time: clock);
        var user = NewUser();
        var receipt = await IntakeAsync(provider, user, "kept.txt", Encoding.UTF8.GetBytes("Kept text " + Guid.NewGuid()));
        var old = DateTimeOffset.UtcNow.AddDays(-31);
        var order = Order(user, at: old);
        await using (var db = fixture.NewContext())
        {
            db.Orders.Add(order);
            db.OrderSources.Add(Link(order.Id, receipt.SourceId!.Value));
            db.Runs.Add(Run(user, order.Id, 1, status: Geef.Atelier.Core.Domain.RunStatus.Completed, at: old));
            await db.SaveChangesAsync();
        }

        var recentUser = NewUser();
        var recentReceipt = await IntakeAsync(provider, recentUser, "recent.txt", Encoding.UTF8.GetBytes("Recent text " + Guid.NewGuid()));
        var recent = DateTimeOffset.UtcNow.AddDays(-1);
        var recentOrder = Order(recentUser, at: recent);
        await using (var db = fixture.NewContext())
        {
            db.Orders.Add(recentOrder);
            db.OrderSources.Add(Link(recentOrder.Id, recentReceipt.SourceId!.Value));
            db.Runs.Add(Run(recentUser, recentOrder.Id, 1, status: Geef.Atelier.Core.Domain.RunStatus.Completed, at: recent));
            await db.SaveChangesAsync();
        }

        var cleanup = provider.GetServices<IHostedService>().OfType<SourceCleanupBackgroundService>().Single();
        await cleanup.RunOnceAsync(CancellationToken.None);

        await using var check = fixture.NewContext();
        var source = await check.Sources.AsNoTracking().SingleAsync(s => s.Id == receipt.SourceId);
        Assert.Null(source.StoragePath);
        Assert.NotNull(source.ExtractedText);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, SourceFileStore.OwnerFolder(user))));

        var kept = await check.Sources.AsNoTracking().SingleAsync(s => s.Id == recentReceipt.SourceId);
        Assert.NotNull(kept.StoragePath);
        Assert.True(File.Exists(Path.Combine(_root, kept.StoragePath)));
    }

    [Fact]
    public void FileStoreNeverLeavesTheRoot()
    {
        var store = new SourceFileStore(Microsoft.Extensions.Options.Options.Create(new SourceOptions { StorageRoot = _root }), NullLogger<SourceFileStore>.Instance);
        Assert.Null(store.Resolve("../outside.pdf"));
        Assert.Null(store.Resolve("/etc/passwd"));
        Assert.Null(store.Resolve("a/../../x"));
        Assert.NotNull(store.Resolve("abc/def.pdf"));
        Assert.False(store.TryDelete("../outside.pdf"));
    }

    [Fact]
    public async Task UploadsAreStreamedThroughAPrivateFolderThatIsEmptyAfterwards()
    {
        await using var provider = Build(o => o.MaxFileBytes = 64 * 1_024);
        var user = NewUser();

        var pdf = await IntakeAsync(provider, user, "a.pdf", SyntheticDocuments.Pdf(2, p => string.Concat(Enumerable.Repeat($"Page {p} has words. ", 15))));
        var refused = await IntakeAsync(provider, user, "a.exe", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00]);
        var large = await IntakeAsync(provider, user, "big.txt", new byte[65 * 1_024]);

        Assert.True(pdf.Usable, pdf.Reason);
        Assert.False(refused.Usable);
        Assert.False(large.Usable);
        var incoming = Path.Combine(_root, IncomingUpload.IncomingFolder);
        Assert.True(!Directory.Exists(incoming) || Directory.GetFileSystemEntries(incoming).Length == 0);
    }

    [Fact]
    public async Task OnlyAFewUploadsRunAtTheSameTimeAndTheRestHearsThatTheServiceIsBusy()
    {
        await using var provider = Build(o =>
        {
            o.MaxConcurrentIntakes = 1;
            o.ExtractionQueueTimeout = TimeSpan.FromMilliseconds(300);
        });
        var user = NewUser();
        var slow = new HeldStream(Encoding.UTF8.GetBytes("Slow upload " + Guid.NewGuid()));

        Task<SourceReceipt> first;
        await using (var scope = provider.CreateAsyncScope())
        {
            first = scope.ServiceProvider.GetRequiredService<ISourceIntakeService>()
                .IntakeAsync(new SourceUpload("slow.txt", null, slow.Length, slow), user, SourceOrigin.WebUpload);
            await slow.Reading.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var busy = await IntakeAsync(provider, user, "other.txt", Encoding.UTF8.GetBytes("Other " + Guid.NewGuid()));
            Assert.False(busy.Usable);
            Assert.Equal("The service is busy with other uploads. Please try again in a minute.", busy.Reason);

            slow.Release.SetResult();
            Assert.True((await first).Usable);
        }

        Assert.True((await IntakeAsync(provider, user, "later.txt", Encoding.UTF8.GetBytes("Later " + Guid.NewGuid()))).Usable);
    }

    [Fact]
    public async Task UsersKeepOnlyALimitedNumberAndSizeOfFilesThatNoOrderUses()
    {
        await using var provider = Build(o => o.MaxUnattachedSourcesPerUser = 2);
        var user = NewUser();
        var firstBytes = Encoding.UTF8.GetBytes("First " + Guid.NewGuid());
        var first = await IntakeAsync(provider, user, "1.txt", firstBytes);
        Assert.True((await IntakeAsync(provider, user, "2.txt", Encoding.UTF8.GetBytes("Second " + Guid.NewGuid()))).Usable);

        var third = await IntakeAsync(provider, user, "3.txt", Encoding.UTF8.GetBytes("Third " + Guid.NewGuid()));
        Assert.False(third.Usable);
        Assert.Equal("You have 2 files that are not used in an order yet. Please try again later or contact an administrator.", third.Reason);
        Assert.True((await IntakeAsync(provider, user, "1 again.txt", firstBytes)).Duplicate);
        Assert.True((await IntakeAsync(provider, NewUser(), "3.txt", Encoding.UTF8.GetBytes("Third " + Guid.NewGuid()))).Usable);

        var order = Order(user);
        await using (var db = fixture.NewContext())
        {
            db.Orders.Add(order);
            db.OrderSources.Add(Link(order.Id, first.SourceId!.Value));
            await db.SaveChangesAsync();
        }

        Assert.True((await IntakeAsync(provider, user, "3.txt", Encoding.UTF8.GetBytes("Third " + Guid.NewGuid()))).Usable);

        await using var bySize = Build(o => o.MaxUnattachedBytesPerUser = 1_048_576);
        var sizeUser = NewUser();
        Assert.True((await IntakeAsync(bySize, sizeUser, "a.txt", Encoding.UTF8.GetBytes(new string('a', 600_000) + Guid.NewGuid()))).Usable);
        var tooMuch = await IntakeAsync(bySize, sizeUser, "b.txt", Encoding.UTF8.GetBytes(new string('b', 600_000) + Guid.NewGuid()));
        Assert.False(tooMuch.Usable);
        Assert.Equal("Your files that are not used in an order yet would take more than 1 MB. Please try again later or contact an administrator.", tooMuch.Reason);
    }

    [Fact]
    public async Task TheSameFileUploadedInParallelIsStoredOnce()
    {
        await using var provider = Build(o => o.MaxConcurrentUploadsPerUser = 8);
        var user = NewUser();
        var bytes = Encoding.UTF8.GetBytes("Parallel upload " + Guid.NewGuid());

        var receipts = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => IntakeAsync(provider, user, $"copy {i}.txt", bytes)));

        Assert.All(receipts, r => Assert.True(r.Usable, r.Reason));
        Assert.Single(receipts.Select(r => r.SourceId).Distinct());
        await using var db = fixture.NewContext();
        Assert.Equal(1, await db.Sources.CountAsync(s => s.OwnerUsername == user));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, SourceFileStore.OwnerFolder(user))));
    }

    [Fact]
    public async Task TheDatabaseRefusesASecondCopyOfTheSameFileForTheSameOwner()
    {
        var user = NewUser();
        var sha = new string('c', 63) + "1";
        await using (var db = fixture.NewContext())
            await new SourceRepository(db).AddAsync(Source(user, sha: sha));

        await using (var db = fixture.NewContext())
            await Assert.ThrowsAsync<SourceAlreadyStoredException>(() => new SourceRepository(db).AddAsync(Source(user, sha: sha)));
        await using (var db = fixture.NewContext())
            await new SourceRepository(db).AddAsync(Source(NewUser(), sha: sha));
    }

    [Fact]
    public async Task AnEarlierCopyDeletedDuringTheUploadIsReplacedByANewSource()
    {
        await using var provider = Build(replace: services =>
            services.AddScoped<ISourceRepository>(sp => new VanishingCopyRepository(sp.GetRequiredService<SourceRepository>(), fixture)));
        var user = NewUser();
        var bytes = Encoding.UTF8.GetBytes("Expiring upload " + Guid.NewGuid());
        var first = await IntakeAsync(provider, user, "a.txt", bytes);

        var again = await IntakeAsync(provider, user, "a.txt", bytes);

        Assert.True(again.Usable, again.Reason);
        Assert.False(again.Duplicate);
        Assert.NotEqual(first.SourceId, again.SourceId);
        await using var db = fixture.NewContext();
        Assert.True(await db.Sources.AnyAsync(s => s.Id == again.SourceId));
    }

    [Fact]
    public async Task CleanupKeepsASourceWhoseExpiryWasJustRenewed()
    {
        var user = NewUser();
        var now = DateTimeOffset.UtcNow;
        var source = Source(user, expiresAt: now.AddMinutes(-5));
        await using (var db = fixture.NewContext())
            await new SourceRepository(db).AddAsync(source);

        await using (var db = fixture.NewContext())
        {
            var repository = new SourceRepository(db);
            await repository.ExtendExpiryAsync(source.Id, now.AddHours(24));
            Assert.False(await repository.DeleteIfExpiredAndUnattachedAsync(source.Id, now));
            Assert.True(await repository.DeleteIfExpiredAndUnattachedAsync(source.Id, now.AddHours(25)));
        }
    }

    [Fact]
    public async Task MarkerLinesInTextAndWordFilesNeverBecomePages()
    {
        await using var provider = Build();
        var user = NewUser();

        var text = await IntakeAsync(provider, user, "notes.md", Encoding.UTF8.GetBytes($"Intro {Guid.NewGuid()}\n[[page 5]]\nOutro"));
        var docx = await IntakeAsync(provider, user, "notes.docx", SyntheticDocuments.Docx(["Intro " + Guid.NewGuid(), "[[page 2]]", "Outro"]));

        await using var db = fixture.NewContext();
        foreach (var id in new[] { text.SourceId, docx.SourceId })
        {
            var stored = await db.Sources.AsNoTracking().SingleAsync(s => s.Id == id);
            Assert.Equal([1], PageMarkers.SplitPages(stored.ExtractedText!).Select(p => p.Page));
            Assert.Contains("[ [page", stored.ExtractedText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ALoneCarriageReturnInWordAndTextFilesNeverStartsAPage()
    {
        await using var provider = Build();
        var user = NewUser();

        var docx = await IntakeAsync(provider, user, "cr.docx", HostileDocuments.Docx($"<w:p><w:r><w:t>intro {Guid.NewGuid()}&#13;[[page 7]]&#13;secret</w:t></w:r></w:p>"));
        var markdown = await IntakeAsync(provider, user, "cr.md", Encoding.UTF8.GetBytes($"intro {Guid.NewGuid()}\r[[page 7]]\rsecret"));
        var text = await IntakeAsync(provider, user, "cr.txt", Encoding.UTF8.GetBytes($"intro {Guid.NewGuid()}\r[[page 7]]\r\nsecret"));

        await using var db = fixture.NewContext();
        foreach (var receipt in new[] { docx, markdown, text })
        {
            Assert.True(receipt.Usable, receipt.Reason);
            var stored = await db.Sources.AsNoTracking().SingleAsync(s => s.Id == receipt.SourceId);
            Assert.Equal([1], PageMarkers.SplitPages(stored.ExtractedText!).Select(p => p.Page));
            Assert.DoesNotContain('\r', stored.ExtractedText!);
            Assert.EndsWith("\n[ [page 7]]\nsecret", stored.ExtractedText!, StringComparison.Ordinal);
            Assert.Equal(PageMarkers.StripMarkers(stored.ExtractedText!).Length, stored.CharCount);
        }
    }

    [Fact]
    public async Task FilesWithFarMoreTextThanTheLimitAreRefusedAndNothingIsStored()
    {
        await using var provider = Build(o => o.MaxExtractedChars = 5_000);
        var user = NewUser();

        var text = await IntakeAsync(provider, user, "long.txt", Encoding.UTF8.GetBytes(new string('a', 5_001)));
        var docx = await IntakeAsync(provider, user, "long.docx", SyntheticDocuments.Docx([.. Enumerable.Range(0, 30).Select(i => $"Paragraph {i} {new string('w', 200)}")]));
        var pdf = await IntakeAsync(provider, user, "long.pdf", SyntheticDocuments.Pdf(12, p => string.Concat(Enumerable.Repeat($"Page {p} explains the plan. ", 20))));
        var fits = await IntakeAsync(provider, user, "fits.txt", Encoding.UTF8.GetBytes(new string('b', 4_990) + " ok"));

        foreach (var refused in new[] { text, docx, pdf })
        {
            Assert.False(refused.Usable);
            Assert.Null(refused.SourceId);
            Assert.Equal("This file contains far more text than we can use. Please upload a shorter version.", refused.Reason);
        }

        Assert.True(fits.Usable, fits.Reason);
        await using var db = fixture.NewContext();
        Assert.Equal(1, await db.Sources.CountAsync(s => s.OwnerUsername == user));
    }

    [Fact]
    public async Task TheWebProcessRefusesTooMuchTextEvenWhenTheReaderDeliversIt()
    {
        var runner = new FixedTextRunner(new string('z', 2_001));
        await using var provider = Build(o => o.MaxExtractedChars = 2_000, replace: services => services.AddSingleton<ISourceExtractionRunner>(runner));
        var user = NewUser();

        var refused = await IntakeAsync(provider, user, "a.txt", Encoding.UTF8.GetBytes("Small file " + Guid.NewGuid()));

        Assert.False(refused.Usable);
        Assert.Equal(SourceExtraction.TooMuchTextReason, refused.Reason);
        Assert.Equal(1, runner.Calls);
        await using var db = fixture.NewContext();
        Assert.False(await db.Sources.AnyAsync(s => s.OwnerUsername == user));
    }

    [Fact]
    public async Task TheQuotaAlsoCountsCharactersOfText()
    {
        await using var provider = Build(o => o.MaxUnattachedCharsPerUser = 1_000);
        var user = NewUser();

        var first = await IntakeAsync(provider, user, "1.txt", Encoding.UTF8.GetBytes(new string('a', 560) + Guid.NewGuid()));
        var second = await IntakeAsync(provider, user, "2.txt", Encoding.UTF8.GetBytes(new string('b', 560) + Guid.NewGuid()));

        Assert.True(first.Usable, first.Reason);
        Assert.False(second.Usable);
        Assert.Equal("Your files that are not used in an order yet would hold more than 1,000 characters of text. Please try again later or contact an administrator.", second.Reason);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, SourceFileStore.OwnerFolder(user))));

        var order = Order(user);
        await using (var db = fixture.NewContext())
        {
            db.Orders.Add(order);
            db.OrderSources.Add(Link(order.Id, first.SourceId!.Value));
            await db.SaveChangesAsync();
        }

        Assert.True((await IntakeAsync(provider, user, "2.txt", Encoding.UTF8.GetBytes(new string('b', 560) + Guid.NewGuid()))).Usable);
        await using var check = fixture.NewContext();
        var usage = await new SourceRepository(check).GetUnattachedUsageAsync(user);
        Assert.Equal(1, usage.Count);
        Assert.InRange(usage.Chars, 1, 1_000);
    }

    [Fact]
    public async Task SevenParallelUploadsNeverPassTheQuotaTogether()
    {
        await using var provider = Build(o =>
        {
            o.MaxUnattachedSourcesPerUser = 3;
            o.MaxConcurrentUploadsPerUser = 8;
        });
        var user = NewUser();

        var receipts = await Task.WhenAll(Enumerable.Range(0, 7).Select(i =>
            IntakeAsync(provider, user, $"{i}.txt", Encoding.UTF8.GetBytes($"Parallel file {i} {Guid.NewGuid()}"))));

        Assert.Equal(3, receipts.Count(r => r.Usable));
        Assert.All(receipts.Where(r => !r.Usable), r => Assert.Equal("You have 3 files that are not used in an order yet. Please try again later or contact an administrator.", r.Reason));
        await using var db = fixture.NewContext();
        Assert.Equal(3, await db.Sources.CountAsync(s => s.OwnerUsername == user));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(_root, SourceFileStore.OwnerFolder(user))).Length);

        await using var bySize = Build(o =>
        {
            o.MaxUnattachedBytesPerUser = 2_500;
            o.MaxConcurrentUploadsPerUser = 8;
        });
        var sizeUser = NewUser();
        var sized = await Task.WhenAll(Enumerable.Range(0, 7).Select(i =>
            IntakeAsync(bySize, sizeUser, $"{i}.txt", Encoding.UTF8.GetBytes(new string((char)('a' + i), 1_000)))));
        Assert.Equal(2, sized.Count(r => r.Usable));
        await using var check = fixture.NewContext();
        Assert.True(await check.Sources.Where(s => s.OwnerUsername == sizeUser).SumAsync(s => s.SizeBytes) <= 2_500);
    }

    [Fact]
    public async Task OnePersonRunsAtMostTwoUploadsAndOthersAreNotBlocked()
    {
        await using var provider = Build(o => o.MaxConcurrentIntakes = 4);
        var busy = NewUser();
        var first = new HeldStream(Encoding.UTF8.GetBytes("First held " + Guid.NewGuid()));
        var second = new HeldStream(Encoding.UTF8.GetBytes("Second held " + Guid.NewGuid()));

        var running = new[]
        {
            IntakeAsync(provider, busy, "1.txt", first),
            IntakeAsync(provider, busy, "2.txt", second),
        };
        await Task.WhenAll(first.Reading.Task, second.Reading.Task).WaitAsync(TimeSpan.FromSeconds(10));

        var third = await IntakeAsync(provider, busy, "3.txt", Encoding.UTF8.GetBytes("Third " + Guid.NewGuid()));
        var other = await IntakeAsync(provider, NewUser(), "other.txt", Encoding.UTF8.GetBytes("Other person " + Guid.NewGuid()));

        Assert.False(third.Usable);
        Assert.Equal(SourceRefusal.TooManyUploads, third.Refusal);
        Assert.Equal("You are already adding 2 files at the same time. Please wait until one is done, then try again.", third.Reason);
        Assert.True(other.Usable, other.Reason);

        first.Release.SetResult();
        second.Release.SetResult();
        Assert.All(await Task.WhenAll(running), r => Assert.True(r.Usable, r.Reason));
        Assert.True((await IntakeAsync(provider, busy, "3.txt", Encoding.UTF8.GetBytes("Third " + Guid.NewGuid()))).Usable);
    }

    [Fact]
    public async Task PastedTextNeedsNoUploadSlot()
    {
        await using var provider = Build(o =>
        {
            o.MaxConcurrentIntakes = 1;
            o.MaxConcurrentUploadsPerUser = 1;
            o.ExtractionQueueTimeout = TimeSpan.FromMilliseconds(300);
        });
        var user = NewUser();
        var slow = new HeldStream(Encoding.UTF8.GetBytes("Slow upload " + Guid.NewGuid()));

        await using var scope = provider.CreateAsyncScope();
        var intake = scope.ServiceProvider.GetRequiredService<ISourceIntakeService>();
        var held = intake.IntakeAsync(new SourceUpload("slow.txt", null, 0, slow), user, SourceOrigin.WebUpload);
        await slow.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var pasted = await intake.IntakeTextAsync("Brief", "Pasted while a file is uploading. " + Guid.NewGuid(), user, SourceOrigin.Pasted);

        Assert.True(pasted.Usable, pasted.Reason);
        slow.Release.SetResult();
        Assert.True((await held).Usable);
    }

    [Fact]
    public async Task AnUploadThatBreaksOffIsRefusedAndLeavesNothingBehind()
    {
        await using var provider = Build();
        var user = NewUser();

        var receipt = await IntakeAsync(provider, user, "broken.txt", new BreakingStream(Encoding.UTF8.GetBytes("Half a file " + Guid.NewGuid())));

        Assert.False(receipt.Usable);
        Assert.Equal(SourceRefusal.Interrupted, receipt.Refusal);
        Assert.Equal("The upload was interrupted. Please try again.", receipt.Reason);
        var incoming = Path.Combine(_root, IncomingUpload.IncomingFolder);
        Assert.True(!Directory.Exists(incoming) || Directory.GetFileSystemEntries(incoming).Length == 0);
    }

    [Fact]
    public async Task AHostileWordFileIsRefusedThroughTheIntakeAndNothingIsStored()
    {
        await using var provider = Build();
        var user = NewUser();

        var receipt = await IntakeAsync(provider, user, "deep.docx", HostileDocuments.DeeplyNestedDocx(20_000));

        Assert.False(receipt.Usable);
        Assert.Null(receipt.SourceId);
        Assert.False(string.IsNullOrWhiteSpace(receipt.Reason));
        await using var db = fixture.NewContext();
        Assert.False(await db.Sources.AnyAsync(s => s.OwnerUsername == user));
    }

    [Fact]
    public async Task OrphanScanRemovesOnlyOldUnknownFilesInsideTheRoot()
    {
        var clock = new MutableTime(DateTimeOffset.UtcNow);
        await using var provider = Build(time: clock);
        var user = NewUser();
        var kept = await IntakeAsync(provider, user, "kept.txt", Encoding.UTF8.GetBytes("Kept " + Guid.NewGuid()));
        string keptPath;
        await using (var db = fixture.NewContext())
            keptPath = Path.Combine(_root, (await db.Sources.AsNoTracking().SingleAsync(s => s.Id == kept.SourceId)).StoragePath!);

        var folder = Path.Combine(_root, SourceFileStore.OwnerFolder(user));
        var unknownOld = Touch(Path.Combine(folder, $"{Guid.NewGuid():N}.pdf"));
        var partOld = Touch(Path.Combine(folder, $"{Guid.NewGuid():N}.pdf.part"));
        var unknownFresh = Path.Combine(folder, $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(unknownFresh, "fresh");
        var staleUpload = Path.Combine(_root, IncomingUpload.IncomingFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staleUpload);
        Touch(Path.Combine(staleUpload, IncomingUpload.FileName));
        Directory.SetLastWriteTimeUtc(staleUpload, DateTime.UtcNow.AddDays(-3));
        var outside = Path.Combine(Path.GetTempPath(), $"geef-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var outsideFile = Touch(Path.Combine(outside, "victim.pdf"));
        var link = Path.Combine(folder, $"{Guid.NewGuid():N}.pdf");
        File.CreateSymbolicLink(link, outsideFile);
        var linkedFolder = Path.Combine(_root, "linked");
        Directory.CreateSymbolicLink(linkedFolder, outside);
        File.SetLastWriteTimeUtc(keptPath, DateTime.UtcNow.AddDays(-3));

        try
        {
            var cleanup = provider.GetServices<IHostedService>().OfType<SourceCleanupBackgroundService>().Single();
            var removed = await cleanup.RemoveOrphansAsync(CancellationToken.None);

            Assert.Equal(3, removed);
            Assert.False(File.Exists(unknownOld));
            Assert.False(File.Exists(partOld));
            Assert.False(Directory.Exists(staleUpload));
            Assert.True(File.Exists(keptPath));
            Assert.True(File.Exists(unknownFresh));
            Assert.True(File.Exists(outsideFile));
            Assert.True(File.Exists(link) || new FileInfo(link).LinkTarget is not null);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task OrphanScanDeletesNothingWhenMostStoredFilesAreUnknownToTheDatabase()
    {
        await using var provider = Build();
        var user = NewUser();
        var kept = await IntakeAsync(provider, user, "kept.txt", Encoding.UTF8.GetBytes("Kept " + Guid.NewGuid()));
        string keptPath;
        await using (var db = fixture.NewContext())
            keptPath = Path.Combine(_root, (await db.Sources.AsNoTracking().SingleAsync(s => s.Id == kept.SourceId)).StoragePath!);
        File.SetLastWriteTimeUtc(keptPath, DateTime.UtcNow.AddDays(-3));
        var folder = Path.Combine(_root, SourceFileStore.OwnerFolder(user));
        var restored = Enumerable.Range(0, SourceOrphanScanner.GuardMinimumFiles + 2).Select(_ => Touch(Path.Combine(folder, $"{Guid.NewGuid():N}.pdf"))).ToList();
        var cleanup = provider.GetServices<IHostedService>().OfType<SourceCleanupBackgroundService>().Single();

        Assert.Equal(0, await cleanup.RemoveOrphansAsync(CancellationToken.None));
        Assert.All(restored, path => Assert.True(File.Exists(path)));

        foreach (var path in restored.Skip(3))
            File.Delete(path);
        Assert.Equal(3, await cleanup.RemoveOrphansAsync(CancellationToken.None));
        Assert.True(File.Exists(keptPath));
    }

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "old");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
        return path;
    }

    private Task<SourceReceipt> IntakeAsync(ServiceProvider provider, string user, string name, byte[] bytes, string? declared = null) =>
        IntakeAsync(provider, user, name, new MemoryStream(bytes), declared);

    private static async Task<SourceReceipt> IntakeAsync(ServiceProvider provider, string user, string name, Stream content, string? declared = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var intake = scope.ServiceProvider.GetRequiredService<ISourceIntakeService>();
        return await intake.IntakeAsync(new SourceUpload(name, declared, content.CanSeek ? content.Length : 0, content), user, SourceOrigin.WebUpload);
    }

    private ServiceProvider Build(Action<SourceOptions>? configure = null, TimeProvider? time = null, Action<IServiceCollection>? replace = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<AtelierDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddScoped<SourceRepository>();
        services.AddScoped<ISourceRepository>(sp => sp.GetRequiredService<SourceRepository>());
        services.AddScoped<ISourceRetentionStore>(sp => sp.GetRequiredService<SourceRepository>());
        services.Configure<SourceOptions>(o =>
        {
            o.StorageRoot = _root;
            o.ExtractionWorkerAssembly = typeof(Program).Assembly.Location;
            configure?.Invoke(o);
        });
        if (time is not null)
            services.AddSingleton(time);
        services.AddSources(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        replace?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class HeldStream(byte[] content) : MemoryStream(content)
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reading.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class BreakingStream(byte[] content) : MemoryStream(content)
    {
        private bool _served;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_served)
                throw new IOException("The connection was closed.");
            _served = true;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 4)], cancellationToken);
        }
    }

    private sealed class FixedTextRunner(string text) : ISourceExtractionRunner
    {
        public int Calls { get; private set; }

        public Task<SourceExtraction> ExtractAsync(SourceKind kind, string inputPath, string workDirectory, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new SourceExtraction(text, null, [], SourceQuality.Good, [], null));
        }
    }

    private sealed class VanishingCopyRepository(ISourceRepository inner, PostgresFixture fixture) : ISourceRepository
    {
        public Task AddAsync(SourceEntity source, CancellationToken ct = default) => inner.AddAsync(source, ct);

        public Task<SourceEntity?> GetAsync(Guid sourceId, bool includeText, CancellationToken ct = default) => inner.GetAsync(sourceId, includeText, ct);

        public async Task<SourceEntity?> FindByHashAsync(string ownerUsername, string sha256, CancellationToken ct = default)
        {
            var found = await inner.FindByHashAsync(ownerUsername, sha256, ct);
            if (found is not null)
            {
                await using var db = fixture.NewContext();
                await db.Sources.Where(s => s.Id == found.Id).ExecuteDeleteAsync(ct);
            }

            return found;
        }

        public Task<IReadOnlyList<SourceEntity>> GetManyAsync(IReadOnlyCollection<Guid> sourceIds, bool includeText, CancellationToken ct = default) => inner.GetManyAsync(sourceIds, includeText, ct);

        public Task<IReadOnlyList<OrderSourceItem>> ListForOrderAsync(Guid orderId, bool includeText, CancellationToken ct = default) => inner.ListForOrderAsync(orderId, includeText, ct);

        public Task<bool> IsAttachedAsync(Guid sourceId, CancellationToken ct = default) => inner.IsAttachedAsync(sourceId, ct);

        public Task<bool> DeleteIfUnattachedAsync(Guid sourceId, CancellationToken ct = default) => inner.DeleteIfUnattachedAsync(sourceId, ct);

        public Task<IReadOnlyList<SourceEntity>> ListExpiredUnattachedAsync(DateTimeOffset now, int limit, CancellationToken ct = default) => inner.ListExpiredUnattachedAsync(now, limit, ct);

        public Task AddRunUsagesAsync(IReadOnlyList<RunSourceUsageEntity> usages, CancellationToken ct = default) => inner.AddRunUsagesAsync(usages, ct);

        public Task<IReadOnlyList<RunSourceUsageEntity>> ListRunUsagesAsync(Guid runId, CancellationToken ct = default) => inner.ListRunUsagesAsync(runId, ct);
    }

    private sealed class MutableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
