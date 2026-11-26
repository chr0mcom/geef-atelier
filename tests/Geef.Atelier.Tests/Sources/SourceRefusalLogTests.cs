using System.Text;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Persistence.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Sources;

/// <summary>
/// Counting of refused files against real Postgres: each of the refusal points of <see cref="SourceIntakeService"/> gives its code,
/// every refusal leaves exactly one row with the way and without the file name, and a counting failure never changes the receipt.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class SourceRefusalLogTests(PostgresFixture fixture)
{
    private const string SecretName = "Quarterly secret plan";

    [Fact]
    public async Task A_file_larger_than_the_limit_is_counted_as_too_large_whether_its_size_was_known_or_streamed()
    {
        await using var host = UploadTestHost.Start(fixture, sources: o => o.MaxFileBytes = 1_000);
        var user = NewUser();

        var known = await IntakeAsync(host, user, SecretName + ".txt", new byte[1_001], knownLength: true);
        var streamed = await IntakeAsync(host, user, SecretName + ".txt", new byte[1_001], knownLength: false);

        AssertRefused(known, SourceRefusalCodes.TooLarge);
        Assert.Equal(SourceRefusal.TooLarge, known.Refusal);
        AssertRefused(streamed, SourceRefusalCodes.TooLarge);
        var rows = await RowsAsync(user);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(SourceRefusalCodes.TooLarge, r.Code));
        Assert.Contains(rows, r => r.SizeBytes == 1_001);
        Assert.Contains(rows, r => r.SizeBytes is null);
    }

    [Fact]
    public async Task A_person_with_all_upload_places_taken_is_counted_as_user_busy()
    {
        await using var host = UploadTestHost.Start(fixture, sources: o => o.MaxConcurrentUploadsPerUser = 1);
        var user = NewUser();
        var held = new HeldUploadStream(Encoding.UTF8.GetBytes("Held upload " + Guid.NewGuid()));

        var running = IntakeAsync(host, user, "held.txt", held);
        await held.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var refused = await IntakeAsync(host, user, "second.txt", Encoding.UTF8.GetBytes("Second " + Guid.NewGuid()));
        held.Release.SetResult();
        Assert.True((await running).Usable);

        AssertRefused(refused, SourceRefusalCodes.UserBusy);
        Assert.Equal(SourceRefusalCodes.UserBusy, Assert.Single(await RowsAsync(user)).Code);
    }

    [Fact]
    public async Task All_slots_busy_is_counted_as_busy()
    {
        await using var host = UploadTestHost.Start(fixture, sources: o =>
        {
            o.MaxConcurrentIntakes = 1;
            o.ExtractionQueueTimeout = TimeSpan.FromMilliseconds(300);
        });
        var user = NewUser();
        var held = new HeldUploadStream(Encoding.UTF8.GetBytes("Held upload " + Guid.NewGuid()));

        var running = IntakeAsync(host, NewUser(), "held.txt", held);
        await held.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var refused = await IntakeAsync(host, user, "other.txt", Encoding.UTF8.GetBytes("Other " + Guid.NewGuid()));
        held.Release.SetResult();
        await running;

        AssertRefused(refused, SourceRefusalCodes.Busy);
        Assert.Equal(SourceRefusalCodes.Busy, Assert.Single(await RowsAsync(user)).Code);
    }

    [Fact]
    public async Task An_upload_that_breaks_off_is_counted_as_interrupted()
    {
        await using var host = UploadTestHost.Start(fixture);
        var user = NewUser();

        var refused = await IntakeAsync(host, user, "broken.txt", new BreakingUploadStream(Encoding.UTF8.GetBytes("Half a file " + Guid.NewGuid())));

        AssertRefused(refused, SourceRefusalCodes.Interrupted);
        Assert.Equal(SourceRefusalCodes.Interrupted, Assert.Single(await RowsAsync(user)).Code);
    }

    [Fact]
    public async Task A_sources_volume_that_cannot_be_written_is_counted_as_save_failed()
    {
        var blocked = Path.Combine(Path.GetTempPath(), $"geef-blocked-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(blocked, "not a directory");
        try
        {
            await using var host = UploadTestHost.Start(fixture, sources: o => o.StorageRoot = blocked);
            var user = NewUser();

            var refused = await IntakeAsync(host, user, "a.txt", Encoding.UTF8.GetBytes("Text " + Guid.NewGuid()));

            AssertRefused(refused, SourceRefusalCodes.SaveFailed);
            Assert.Equal(SourceRefusalCodes.SaveFailed, Assert.Single(await RowsAsync(user)).Code);
        }
        finally
        {
            File.Delete(blocked);
        }
    }

    [Fact]
    public async Task A_file_that_cannot_be_moved_into_the_store_is_counted_as_save_failed()
    {
        await using var host = UploadTestHost.Start(fixture, replace: s => s.AddSingleton<ISourceFileStore, FailingFileStore>());
        var user = NewUser();

        var refused = await IntakeAsync(host, user, "a.txt", Encoding.UTF8.GetBytes("Text " + Guid.NewGuid()));

        AssertRefused(refused, SourceRefusalCodes.SaveFailed);
        Assert.Equal(SourceRefusalCodes.SaveFailed, Assert.Single(await RowsAsync(user)).Code);
    }

    [Fact]
    public async Task A_copy_that_vanishes_while_it_is_stored_is_counted_as_save_failed()
    {
        await using var host = UploadTestHost.Start(fixture, replace: s =>
            s.AddScoped<ISourceRetentionStore>(sp => new RetentionOverride(sp.GetRequiredService<SourceRepository>(), throwAlreadyStored: true)));
        var user = NewUser();

        var refused = await IntakeAsync(host, user, "a.txt", Encoding.UTF8.GetBytes("Text " + Guid.NewGuid()));

        AssertRefused(refused, SourceRefusalCodes.SaveFailed);
        Assert.Equal(SourceRefusalCodes.SaveFailed, Assert.Single(await RowsAsync(user)).Code);
    }

    [Fact]
    public async Task An_empty_file_is_counted_as_empty_with_size_zero()
    {
        await using var host = UploadTestHost.Start(fixture);
        var user = NewUser();

        var refused = await IntakeAsync(host, user, "empty.txt", []);

        AssertRefused(refused, SourceRefusalCodes.Empty);
        var row = Assert.Single(await RowsAsync(user));
        Assert.Equal((SourceRefusalCodes.Empty, 0L, "txt"), (row.Code, row.SizeBytes, row.Extension));
    }

    [Fact]
    public async Task A_full_quota_is_counted_as_quota_before_reading_and_when_storing()
    {
        await using var host = UploadTestHost.Start(fixture, sources: o => o.MaxUnattachedSourcesPerUser = 1);
        var user = NewUser();
        Assert.True((await IntakeAsync(host, user, "1.txt", Encoding.UTF8.GetBytes("First " + Guid.NewGuid()))).Usable);

        var before = await IntakeAsync(host, user, "2.txt", Encoding.UTF8.GetBytes("Second " + Guid.NewGuid()));

        await using var racing = UploadTestHost.Start(fixture, replace: s =>
            s.AddScoped<ISourceRetentionStore>(sp => new RetentionOverride(sp.GetRequiredService<SourceRepository>(), refuseOnStore: true)));
        var other = NewUser();
        var whenStoring = await IntakeAsync(racing, other, "3.txt", Encoding.UTF8.GetBytes("Third " + Guid.NewGuid()));

        AssertRefused(before, SourceRefusalCodes.Quota);
        AssertRefused(whenStoring, SourceRefusalCodes.Quota);
        Assert.Equal(SourceRefusalCodes.Quota, Assert.Single(await RowsAsync(user)).Code);
        var stored = Assert.Single(await RowsAsync(other));
        Assert.Equal((SourceRefusalCodes.Quota, SourceKind.Text), (stored.Code, stored.Kind));
    }

    [Fact]
    public async Task Detected_types_are_counted_with_their_codes_and_the_extension_of_the_given_name()
    {
        await using var host = UploadTestHost.Start(fixture);
        var user = NewUser();

        var exe = await IntakeAsync(host, user, "invoice.PDF", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00], declared: "application/pdf");
        var png = await IntakeAsync(host, user, "photo.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0xFF, 0xFE], declared: "image/png");
        var doc = await IntakeAsync(host, user, "letter.doc", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00], declared: "application/msword");

        AssertRefused(exe, SourceRefusalCodes.UnsupportedType);
        AssertRefused(png, SourceRefusalCodes.UnsupportedType);
        AssertRefused(doc, SourceRefusalCodes.OldWord);
        var rows = await RowsAsync(user);
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r is { Code: SourceRefusalCodes.UnsupportedType, Extension: "pdf", DeclaredType: "application/pdf", Kind: null, SizeBytes: 10 });
        Assert.Contains(rows, r => r is { Code: SourceRefusalCodes.UnsupportedType, Extension: "png", DeclaredType: "image/png" });
        Assert.Contains(rows, r => r is { Code: SourceRefusalCodes.OldWord, Extension: "doc", DeclaredType: "application/msword" });
        Assert.All(rows, r => Assert.Equal(RefusalPath.Web, r.Path));
    }

    [Fact]
    public async Task A_scan_is_counted_once_as_pdf_no_text_with_its_kind()
    {
        await using var host = UploadTestHost.Start(fixture);
        var user = NewUser();

        var scan = await IntakeAsync(host, user, "scan.pdf", SyntheticDocuments.Pdf(2, _ => string.Empty), path: RefusalPath.McpTicket);

        AssertRefused(scan, SourceRefusalCodes.PdfNoText);
        Assert.Equal(PdfSourceExtractor.NoTextReason, scan.Reason);
        var row = Assert.Single(await RowsAsync(user));
        Assert.Equal((SourceRefusalCodes.PdfNoText, RefusalPath.McpTicket, SourceKind.Pdf, "pdf"), (row.Code, row.Path, row.Kind, row.Extension));
        await using var db = fixture.NewContext();
        Assert.False(await db.Sources.AnyAsync(s => s.OwnerUsername == user));
    }

    [Fact]
    public async Task A_hostile_word_file_is_counted_with_a_known_code()
    {
        await using var host = UploadTestHost.Start(fixture);
        var user = NewUser();

        var refused = await IntakeAsync(host, user, "deep.docx", HostileDocuments.DeeplyNestedDocx(20_000), path: RefusalPath.UploadPage);

        Assert.False(refused.Usable);
        Assert.Contains(refused.RefusalCode!, new[] { SourceRefusalCodes.ArchiveRefused, SourceRefusalCodes.TooComplex, SourceRefusalCodes.Unreadable });
        var row = Assert.Single(await RowsAsync(user));
        Assert.Equal((refused.RefusalCode, RefusalPath.UploadPage), (row.Code, row.Path));
    }

    [Fact]
    public async Task Far_too_much_text_is_counted_as_too_much_text()
    {
        await using var host = UploadTestHost.Start(fixture, sources: o => o.MaxExtractedChars = 2_000,
            replace: s => s.AddSingleton<ISourceExtractionRunner>(new FixedTextRunner(new string('z', 2_001))));
        var user = NewUser();

        var refused = await IntakeAsync(host, user, "a.md", Encoding.UTF8.GetBytes("Small file " + Guid.NewGuid()), path: RefusalPath.McpInline);

        AssertRefused(refused, SourceRefusalCodes.TooMuchText);
        var row = Assert.Single(await RowsAsync(user));
        Assert.Equal((SourceRefusalCodes.TooMuchText, RefusalPath.McpInline, "md"), (row.Code, row.Path, row.Extension));
    }

    [Fact]
    public async Task A_usable_file_and_a_duplicate_leave_no_row()
    {
        await using var host = UploadTestHost.Start(fixture);
        var user = NewUser();
        var bytes = Encoding.UTF8.GetBytes("Kept " + Guid.NewGuid());

        Assert.True((await IntakeAsync(host, user, "a.txt", bytes)).Usable);
        Assert.True((await IntakeAsync(host, user, "b.txt", bytes)).Duplicate);

        Assert.Empty(await RowsAsync(user));
    }

    [Fact]
    public async Task No_column_of_a_row_holds_the_file_name_or_the_content()
    {
        await using var host = UploadTestHost.Start(fixture);
        var user = NewUser();
        var content = "Unique content " + Guid.NewGuid();
        var name = $"{SecretName} {Guid.NewGuid():N}.bin";

        var refused = await IntakeAsync(host, user, name, [0x00, 0x01, 0x02, 0x03, .. Encoding.UTF8.GetBytes(content)]);

        Assert.False(refused.Usable);
        await using var db = fixture.NewContext();
        var raw = await db.Database.SqlQuery<string>($"""
            SELECT row_to_json(r)::text AS "Value" FROM "SourceRefusals" r WHERE r."OwnerUsername" = {user}
            """).ToListAsync();
        var json = Assert.Single(raw);
        Assert.DoesNotContain(SecretName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unique content", json, StringComparison.Ordinal);
        Assert.Contains("\"Extension\":\"bin\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Path\":\"web\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_database_that_cannot_be_reached_changes_neither_the_receipt_nor_the_answer()
    {
        await using var working = UploadTestHost.Start(fixture);
        var user = NewUser();
        var expected = await IntakeAsync(working, user, "scan.pdf", SyntheticDocuments.Pdf(1, _ => string.Empty));

        var log = new SourceRefusalLog(BrokenScopes(), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<SourceRefusalLog>.Instance);
        await using var broken = UploadTestHost.Start(fixture, replace: s => s.AddSingleton<ISourceRefusalLog>(log));
        var other = NewUser();
        var receipt = await IntakeAsync(broken, other, "scan.pdf", SyntheticDocuments.Pdf(1, _ => string.Empty));

        Assert.Equal((expected.Usable, expected.Reason, expected.RefusalCode, expected.Refusal), (receipt.Usable, receipt.Reason, receipt.RefusalCode, receipt.Refusal));
        Assert.Empty(await RowsAsync(other));
        await log.RecordAsync(new SourceRefusalRecord(other, RefusalPath.Web, SourceRefusalCodes.Empty, null, null, null, 0));
    }

    [Fact]
    public async Task A_failing_insert_is_logged_as_a_warning_without_details_of_the_file()
    {
        await using var host = UploadTestHost.Start(fixture);
        var log = new SourceRefusalLog(BrokenScopes(), TimeProvider.System, host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SourceRefusalLog>>());

        await log.RecordAsync(new SourceRefusalRecord(NewUser(), RefusalPath.McpInline, SourceRefusalCodes.McpPlaceholder, null, "pdf", "application/pdf", 12));

        var line = Assert.Single(host.Logs.Lines, l => l.Contains("could not be counted", StringComparison.Ordinal));
        Assert.StartsWith("Warning", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_log_cleans_what_it_is_given_before_storing()
    {
        await using var host = UploadTestHost.Start(fixture);
        var user = NewUser();
        var log = host.Services.GetRequiredService<ISourceRefusalLog>();

        await log.RecordAsync(new SourceRefusalRecord(user, RefusalPath.McpInline, SourceRefusalCodes.McpPlaceholder, null, "../../etc/passwd", "Text/Plain; name=secret.txt", -5));

        var row = Assert.Single(await RowsAsync(user));
        Assert.Equal((RefusalPath.McpInline, SourceRefusalCodes.McpPlaceholder, (string?)null, "text/plain", (long?)null), (row.Path, row.Code, row.Extension, row.DeclaredType, row.SizeBytes));
        Assert.Equal(host.Clock.Now, row.CreatedAt, TimeSpan.FromMilliseconds(1));
    }

    private static void AssertRefused(SourceReceipt receipt, string code)
    {
        Assert.False(receipt.Usable);
        Assert.Null(receipt.SourceId);
        Assert.Equal(code, receipt.RefusalCode);
    }

    private async Task<List<SourceRefusalEntity>> RowsAsync(string user)
    {
        await using var db = fixture.NewContext();
        return await db.SourceRefusals.AsNoTracking().Where(r => r.OwnerUsername == user).ToListAsync();
    }

    private static Task<SourceReceipt> IntakeAsync(UploadTestHost host, string user, string name, byte[] bytes, bool knownLength = true,
        string? declared = null, RefusalPath path = RefusalPath.Web) =>
        IntakeAsync(host, user, name, new MemoryStream(bytes), knownLength, declared, path);

    private static Task<SourceReceipt> IntakeAsync(UploadTestHost host, string user, string name, Stream content, bool knownLength = false,
        string? declared = null, RefusalPath path = RefusalPath.Web) =>
        host.InScopeAsync(sp => sp.GetRequiredService<ISourceIntakeService>()
            .IntakeAsync(new SourceUpload(name, declared, knownLength ? content.Length : 0, content), user, SourceOrigin.WebUpload, CancellationToken.None, path));

    private static IServiceScopeFactory BrokenScopes()
    {
        var services = new ServiceCollection();
        services.AddDbContext<Geef.Atelier.Infrastructure.Persistence.AtelierDbContext>(o =>
            o.UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2"));
        services.AddScoped<SourceRefusalRepository>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class FailingFileStore : ISourceFileStore
    {
        public Task<string> SaveFromFileAsync(string ownerUsername, Guid sourceId, string extension, string receivedPath, CancellationToken ct) =>
            throw new IOException("The disk is full.");

        public bool TryDelete(string relativePath) => false;

        public string? Resolve(string relativePath) => null;
    }

    private sealed class FixedTextRunner(string text) : ISourceExtractionRunner
    {
        public Task<SourceExtraction> ExtractAsync(SourceKind kind, string inputPath, string workDirectory, CancellationToken ct) =>
            Task.FromResult(new SourceExtraction(text, null, [], SourceQuality.Good, [], null));
    }

    private sealed class RetentionOverride(ISourceRetentionStore inner, bool throwAlreadyStored = false, bool refuseOnStore = false) : ISourceRetentionStore
    {
        public Task<IReadOnlyList<(Guid SourceId, string StoragePath)>> ListReleasableOriginalsAsync(DateTimeOffset finishedBefore, int limit, CancellationToken ct = default) =>
            inner.ListReleasableOriginalsAsync(finishedBefore, limit, ct);

        public Task ClearStoragePathAsync(Guid sourceId, CancellationToken ct = default) => inner.ClearStoragePathAsync(sourceId, ct);

        public Task ExtendExpiryAsync(Guid sourceId, DateTimeOffset expiresAt, CancellationToken ct = default) => inner.ExtendExpiryAsync(sourceId, expiresAt, ct);

        public Task<bool> DeleteIfExpiredAndUnattachedAsync(Guid sourceId, DateTimeOffset now, CancellationToken ct = default) =>
            inner.DeleteIfExpiredAndUnattachedAsync(sourceId, now, ct);

        public Task<SourceUsage> GetUnattachedUsageAsync(string ownerUsername, CancellationToken ct = default) => inner.GetUnattachedUsageAsync(ownerUsername, ct);

        public Task<SourceUsage?> AddWithinQuotaAsync(SourceEntity source, SourceQuota quota, CancellationToken ct = default)
        {
            if (throwAlreadyStored)
                throw new SourceAlreadyStoredException(new InvalidOperationException("Raced."));
            return refuseOnStore ? Task.FromResult<SourceUsage?>(new SourceUsage(quota.MaxCount, 0, 0)) : inner.AddWithinQuotaAsync(source, quota, ct);
        }

        public Task<IReadOnlySet<string>> FindKnownStoragePathsAsync(IReadOnlyCollection<string> storagePaths, CancellationToken ct = default) =>
            inner.FindKnownStoragePathsAsync(storagePaths, ct);
    }
}
