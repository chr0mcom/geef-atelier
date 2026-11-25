using System.Data.Common;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// <see cref="AccountStore"/> against real Postgres: the profile goes with the account in one transaction, and the runs that stay
/// under a deleted or given-up user name lose their copy of the profile block (review round 2 of stage 2, R4 and MINOR-1).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class AccountStoreTests(PostgresFixture fixture)
{
    private const string Marker = "PROFIL-MARKER-R4";
    private const string Request = "Schreib eine Einladung zum Sommerfest.";

    private static string Briefing() =>
        Request + BriefingSections.Separator + BriefingSections.ProfileHeading + "\nName: " + Marker;

    private static AtelierUser User(string name) =>
        new(Guid.NewGuid().ToString(), name, "hash", null, true, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static UserProfileEntity Profile(string name, string freeText) => new()
    {
        Username = name, DisplayName = "Alex Beispiel", FreeText = freeText, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static string Name(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<Guid> RunAsync(string owner)
    {
        await using var db = fixture.NewContext();
        return await new RunPersistenceService(db).CreateRunAsync(Briefing(), "{}", owner, cancellationToken: CancellationToken.None);
    }

    private async Task<AtelierUser> SeedAsync(string name, string? profile = "mine")
    {
        await using var db = fixture.NewContext();
        var user = User(name);
        await new AtelierUserRepository(db).AddAsync(user, CancellationToken.None);
        if (profile is not null)
            await new UserProfileRepository(db).UpsertAsync(Profile(name, profile));
        return user;
    }

    private async Task<string> BriefingOfAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return (await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).BriefingText;
    }

    private async Task<UserProfileEntity?> ProfileOfAsync(string name)
    {
        await using var db = fixture.NewContext();
        return await new UserProfileRepository(db).GetAsync(name);
    }

    private static AccountStore Store(AtelierDbContext db) => new(db, new AtelierUserRepository(db));

    [Fact]
    public async Task Deleting_an_account_deletes_its_profile_and_its_runs_lose_the_profile_block_but_keep_the_request()
    {
        var alex = Name("alex");
        var bea = Name("bea");
        var user = await SeedAsync(alex);
        await SeedAsync(bea, "other");
        var alexRun = await RunAsync(alex);
        var beaRun = await RunAsync(bea);

        await using (var db = fixture.NewContext())
            await Store(db).DeleteAsync(user, CancellationToken.None);

        Assert.Null(await ProfileOfAsync(alex));
        await using (var db = fixture.NewContext())
            Assert.Null(await new AtelierUserRepository(db).FindByUserIdAsync(user.UserId, CancellationToken.None));
        var stripped = await BriefingOfAsync(alexRun);
        Assert.DoesNotContain(Marker, stripped, StringComparison.Ordinal);
        Assert.StartsWith(Request, stripped, StringComparison.Ordinal);
        Assert.Contains(BriefingSections.ProfileHiddenNote, stripped, StringComparison.Ordinal);
        Assert.Equal(Briefing(), await BriefingOfAsync(beaRun));
        Assert.NotNull(await ProfileOfAsync(bea));
    }

    [Fact]
    public async Task Deleting_an_account_also_removes_what_the_fact_check_copied_from_the_profile_into_report_and_findings()
    {
        var alex = Name("alex");
        var user = await SeedAsync(alex);
        var runId = await RunAsync(alex);
        var fromProfile = new FactFinding("f1", FactFindingKind.Instruction, "Wir freuen uns sehr auf Sie!", "Breaks: " + Marker,
            "Name: " + Marker, null, null, null, InstructionOrigin.Memory, true, QuoteMatchKind.Exact);
        var report = new RunReport(2, RunReport.DirectKind, OrderConstraints.None, [], [], [], 1, 1, null, [], WriterNotesStatus.Off, WriterNotes.Empty, [], 0,
            new FactCheckSummary(FactCheckMode.Report, FactCheckScope.Full, FactCheckState.OpenPoints, [fromProfile], [], [], 1, "m", false, "fact-check-1"), null, []);
        var findingId = Guid.NewGuid();
        await using (var db = fixture.NewContext())
        {
            var iterationId = Guid.NewGuid();
            db.Iterations.Add(new IterationEntity { Id = iterationId, RunId = runId, IterationNumber = 1, ArtifactText = "Wir freuen uns sehr auf Sie!", CreatedAt = DateTimeOffset.UtcNow });
            db.Findings.Add(new FindingEntity
            {
                Id = findingId, IterationId = iterationId, ReviewerName = "source-fact-check", Severity = FindingSeverity.Minor,
                Message = "Breaks: " + Marker, CreatedAt = DateTimeOffset.UtcNow, Category = FactFindingMetadata.FindingCategory, Quote = "Name: " + Marker,
            });
            db.Events.Add(new EventEntity
            {
                Id = 0, RunId = runId, EventType = "EvaluationApprovedEvent", CreatedAt = DateTimeOffset.UtcNow,
                PayloadJson = Geef.Atelier.Tests.Orders.ProfileCopiesTests.EventPayload("Breaks: " + Marker, "Name: " + Marker, "Name: " + Marker),
            });
            await db.SaveChangesAsync();
            var json = RunReportJson.Serialize(report);
            await db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(u => u.SetProperty(r => r.ReportJson, json));
        }

        await using (var db = fixture.NewContext())
            await Store(db).DeleteAsync(user, CancellationToken.None);

        await using (var db = fixture.NewContext())
        {
            var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
            Assert.DoesNotContain(Marker, run.ReportJson!, StringComparison.Ordinal);
            Assert.Equal("Wir freuen uns sehr auf Sie!", Assert.Single(RunReportJson.TryParse(run.ReportJson)!.FactCheck!.Open).TextQuote);
            var finding = await db.Findings.AsNoTracking().SingleAsync(f => f.Id == findingId);
            Assert.Equal((ProfileCopies.HiddenReason, ProfileCopies.HiddenPassage), (finding.Message, finding.Quote));
            var stored = await db.Events.AsNoTracking().SingleAsync(e => e.RunId == runId);
            Assert.DoesNotContain(Marker, stored.PayloadJson, StringComparison.Ordinal);
            Assert.Contains(ProfileCopies.HiddenReason, stored.PayloadJson, StringComparison.Ordinal);
        }
    }

    private async Task AddEventsAsync(Guid runId, IEnumerable<string> payloads)
    {
        await using var db = fixture.NewContext();
        foreach (var payload in payloads)
            db.Events.Add(new EventEntity { Id = 0, RunId = runId, EventType = "SdkEvent", CreatedAt = DateTimeOffset.UtcNow, PayloadJson = payload });
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<string>> EventsOfAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.Events.AsNoTracking().Where(e => e.RunId == runId).Select(e => e.PayloadJson).ToListAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deleting_or_renaming_removes_the_briefing_from_every_stored_sdk_event_also_of_runs_cleaned_before(bool rename)
    {
        var alex = Name("alex");
        var user = await SeedAsync(alex);
        var runId = await RunAsync(alex);
        var cleanedBefore = await RunAsync(alex);
        await using (var db = fixture.NewContext())
        {
            var without = BriefingSections.WithoutProfile(Briefing());
            await db.Runs.Where(r => r.Id == cleanedBefore).ExecuteUpdateAsync(u => u.SetProperty(r => r.BriefingText, without));
        }
        var payloads = Geef.Atelier.Tests.Orders.ProfileCopiesTests.SdkEventPayloads(Briefing(), "Name: " + Marker);
        await AddEventsAsync(runId, payloads);
        await AddEventsAsync(cleanedBefore, payloads);
        Assert.All(await EventsOfAsync(cleanedBefore), p => Assert.Contains(Marker, p, StringComparison.Ordinal));

        await using (var db = fixture.NewContext())
        {
            if (rename)
                await Store(db).UpdateAsync(user with { Username = Name("alexandra") }, alex, CancellationToken.None);
            else
                await Store(db).DeleteAsync(user, CancellationToken.None);
        }

        foreach (var run in new[] { runId, cleanedBefore })
        {
            var stored = await EventsOfAsync(run);
            Assert.Equal(payloads.Count, stored.Count);
            Assert.All(stored, p => Assert.DoesNotContain(Marker, p, StringComparison.Ordinal));
            Assert.Contains(stored, p => p.Contains(Request, StringComparison.Ordinal) && p.Contains("\"Input\"", StringComparison.Ordinal));
            Assert.DoesNotContain(Marker, await BriefingOfAsync(run), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Renaming_moves_the_profile_replaces_one_left_under_the_new_name_and_strips_the_runs_left_under_the_old_name()
    {
        var alex = Name("alex");
        var alexandra = Name("alexandra");
        var user = await SeedAsync(alex);
        await using (var db = fixture.NewContext())
            await new UserProfileRepository(db).UpsertAsync(Profile(alexandra, "stale"));
        var oldRun = await RunAsync(alex);

        await using (var db = fixture.NewContext())
            await Store(db).UpdateAsync(user with { Username = alexandra }, alex, CancellationToken.None);

        Assert.Null(await ProfileOfAsync(alex));
        Assert.Equal("mine", (await ProfileOfAsync(alexandra))!.FreeText);
        Assert.DoesNotContain(Marker, await BriefingOfAsync(oldRun), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_without_a_new_name_touches_neither_profile_nor_runs()
    {
        var alex = Name("alex");
        var user = await SeedAsync(alex);
        var run = await RunAsync(alex);

        await using (var db = fixture.NewContext())
            await Store(db).UpdateAsync(user with { Email = "a@example.org" }, alex, CancellationToken.None);

        Assert.Equal("mine", (await ProfileOfAsync(alex))!.FreeText);
        Assert.Equal(Briefing(), await BriefingOfAsync(run));
    }

    [Fact]
    public async Task Adding_an_account_deletes_a_profile_left_under_its_name()
    {
        var carl = Name("carl");
        await using (var db = fixture.NewContext())
            await new UserProfileRepository(db).UpsertAsync(Profile(carl, "left over"));

        await using (var db = fixture.NewContext())
            await Store(db).AddAsync(User(carl), CancellationToken.None);

        Assert.Null(await ProfileOfAsync(carl));
    }

    [Theory]
    [InlineData("UPDATE \"UserProfiles\"")]
    [InlineData("UPDATE \"Runs\"")]
    public async Task A_failure_in_the_profile_or_run_step_of_a_rename_rolls_back_the_account_too(string failingCommand)
    {
        var alex = Name("alex");
        var alexandra = Name("alexandra");
        var user = await SeedAsync(alex);
        var run = await RunAsync(alex);

        await using (var db = FailingContext(failingCommand))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Store(db).UpdateAsync(user with { Username = alexandra, IsActive = false }, alex, CancellationToken.None));
        }

        await using (var db = fixture.NewContext())
        {
            var stored = await new AtelierUserRepository(db).FindByUserIdAsync(user.UserId, CancellationToken.None);
            Assert.Equal((alex, true), (stored!.Username, stored.IsActive));
        }
        Assert.Equal("mine", (await ProfileOfAsync(alex))!.FreeText);
        Assert.Null(await ProfileOfAsync(alexandra));
        Assert.Equal(Briefing(), await BriefingOfAsync(run));
    }

    [Fact]
    public async Task A_failure_while_deleting_keeps_account_profile_and_runs()
    {
        var alex = Name("alex");
        var user = await SeedAsync(alex);
        var run = await RunAsync(alex);

        await using (var db = FailingContext("UPDATE \"Runs\""))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(db).DeleteAsync(user, CancellationToken.None));

        await using (var db = fixture.NewContext())
            Assert.NotNull(await new AtelierUserRepository(db).FindByUserIdAsync(user.UserId, CancellationToken.None));
        Assert.NotNull(await ProfileOfAsync(alex));
        Assert.Equal(Briefing(), await BriefingOfAsync(run));
    }

    private AtelierDbContext FailingContext(string marker) =>
        new(new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(new FailingCommand(marker))
            .Options);

    private sealed class FailingCommand(string marker) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            command.CommandText.Contains(marker, StringComparison.Ordinal)
                ? throw new InvalidOperationException("Simulated database failure.")
                : base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
