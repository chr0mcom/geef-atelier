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
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        Assert.Contains(tooLarge.Warnings, w => w.Code == SourceWarningCodes.OverBudget);
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

        var cleanup = provider.GetServices<IHostedService>().OfType<SourceCleanupBackgroundService>().Single();
        await cleanup.RunOnceAsync(CancellationToken.None);

        await using var check = fixture.NewContext();
        var source = await check.Sources.AsNoTracking().SingleAsync(s => s.Id == receipt.SourceId);
        Assert.Null(source.StoragePath);
        Assert.NotNull(source.ExtractedText);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, SourceFileStore.OwnerFolder(user))));
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

    private async Task<SourceReceipt> IntakeAsync(ServiceProvider provider, string user, string name, byte[] bytes, string? declared = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var intake = scope.ServiceProvider.GetRequiredService<ISourceIntakeService>();
        return await intake.IntakeAsync(new SourceUpload(name, declared, bytes.Length, new MemoryStream(bytes)), user, SourceOrigin.WebUpload);
    }

    private ServiceProvider Build(Action<SourceOptions>? configure = null, TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<AtelierDbContext>(o => o.UseNpgsql(fixture.ConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddScoped<SourceRepository>();
        services.AddScoped<ISourceRepository>(sp => sp.GetRequiredService<SourceRepository>());
        services.AddScoped<ISourceRetentionStore>(sp => sp.GetRequiredService<SourceRepository>());
        services.Configure<SourceOptions>(o =>
        {
            o.StorageRoot = _root;
            configure?.Invoke(o);
        });
        if (time is not null)
            services.AddSingleton(time);
        services.AddSources(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private sealed class MutableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
