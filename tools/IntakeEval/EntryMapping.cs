using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Profiles;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Intake;

namespace Geef.Atelier.Tools.IntakeEval;

/// <summary>Turns a corpus entry into what the real intake sees: files with excerpts, profile, known answers and the gate input.</summary>
internal static class EntryMapping
{
    private sealed class NoProfiles : IUserProfileRepository
    {
        public Task<UserProfileEntity?> GetAsync(string username, CancellationToken ct = default) => Task.FromResult<UserProfileEntity?>(null);
        public Task UpsertAsync(UserProfileEntity profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string username, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static readonly ProfileMemoryResolution Memory = new(new NoProfiles());

    public static IReadOnlyList<OrderSourceItem> Sources(this CorpusEntry entry)
    {
        var orderId = Guid.NewGuid();
        return (entry.Files ?? []).Select((file, index) =>
        {
            var source = new SourceEntity
            {
                Id = Guid.NewGuid(), OwnerUsername = "intake-eval", FileName = file.Name, ContentType = "text/plain", Kind = Kind(file.Kind),
                SizeBytes = file.Text.Length, Sha256 = new string('0', 64), ExtractedText = file.Text, PageCount = file.Pages,
                CharCount = file.Text.Length, WordCount = file.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
                Quality = SourceQuality.Good, Warnings = [], Origin = SourceOrigin.Pasted, CreatedAt = DateTimeOffset.UnixEpoch,
            };
            var link = new OrderSourceEntity { OrderId = orderId, SourceId = source.Id, Role = SourceRole.Facts, Position = index, AddedAt = DateTimeOffset.UnixEpoch };
            return new OrderSourceItem(link, source);
        }).ToList();
    }

    public static UserProfileEntity? Profile(this CorpusEntry entry) => entry.Profile is not { } p ? null : new UserProfileEntity
    {
        Username = "intake-eval",
        DisplayName = p.DisplayName,
        Role = p.Role,
        Organization = p.Organization,
        Formality = Enum.TryParse<Formality>(p.Formality, true, out var formality) ? formality : Formality.Unset,
        Language = p.Language,
        Signature = p.Signature,
        NoGos = p.NoGos,
        FreeText = p.FreeText,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    public static ClarifierRequest Request(this CorpusEntry entry, IntakeOptions options) =>
        IntakeAnalysis.Request(entry.Request, entry.Sources(), entry.Profile(), options);

    public static string InputHash(this CorpusEntry entry, IntakeOptions options)
    {
        var prompt = string.Join('\0', ClarifierPrompts.System, ClarifierSchema.Name, ClarifierSchema.Json,
            ClarifierPrompts.User(entry.Request(options), options.ExcerptHeadChars, options.ExcerptTailChars));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));
    }

    public static IntakeAnalysisResult Gate(this CorpusEntry entry, ClarifierRequest request, ClarifierOutput output, IntakeOptions options) =>
        IntakeAnalysis.Analyse(
            entry.Request, request.Candidates(), output, Memory.Resolve(new MemoryFacts(entry.Profile()), output.TextType), options.MaxQuestions,
            entry.Sources().MaterialTexts(request), options.DropGuessedDateOptions);

    private static SourceKind Kind(string kind) => kind.ToLowerInvariant() switch
    {
        "pdf" => SourceKind.Pdf,
        "docx" => SourceKind.Docx,
        "markdown" => SourceKind.Markdown,
        _ => SourceKind.Text,
    };
}
