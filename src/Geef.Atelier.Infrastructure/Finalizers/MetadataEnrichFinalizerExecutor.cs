using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Finalizers.FormatConverters;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Infrastructure.Finalizers;

internal sealed class MetadataEnrichFinalizerExecutor(
    ILogger<MetadataEnrichFinalizerExecutor> logger) : IFinalizerExecutor
{
    public FinalizerType Type => FinalizerType.MetadataEnrich;

    public Task<FinalizerExecutionResult> ExecuteAsync(
        FinalizerProfile profile,
        FinalizerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var settings = MetadataEnrichSettings.From(profile.Settings);
        string? enriched = null;
        string? errorMessage = null;

        try
        {
            enriched = settings.EnricherType switch
            {
                MetadataEnrichSettings.FrontMatter => AddFrontMatter(context),
                MetadataEnrichSettings.WordCountFooter => AddWordCountFooter(context),
                MetadataEnrichSettings.ReadingLevel => AddReadingLevel(context),
                _ => throw new InvalidOperationException(
                    $"Unknown enricher type '{settings.EnricherType}'."),
            };
            logger.LogInformation("MetadataEnrich ({Type}) applied for profile {Profile}",
                settings.EnricherType, profile.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MetadataEnrich failed for profile {Profile}", profile.Name);
            errorMessage = $"Enrich failed: {ex.Message}";
        }

        RunArtifact? artifact = null;
        if (errorMessage is not null)
        {
            artifact = new RunArtifact
            {
                Id = Guid.NewGuid(),
                RunId = context.RunId,
                FinalizerProfileName = profile.Name,
                ArtifactType = ArtifactType.Status,
                StorageUri = "error",
                StatusMessage = errorMessage,
                CreatedAt = DateTimeOffset.UtcNow,
            };
        }

        return Task.FromResult(new FinalizerExecutionResult(
            UpdatedText: enriched,
            Artifact: artifact,
            CostEur: null,
            ActorName: profile.Name));
    }

    private static string AddFrontMatter(FinalizerExecutionContext ctx)
    {
        var title = ctx.TemplateName ?? "Document";
        var wordCount = CountWords(PlaintextStripper.Strip(ctx.CurrentText));
        var frontMatter = $"""
            ---
            title: "{EscapeYamlString(title)}"
            generated_at: "{ctx.RunCompletedAt:O}"
            word_count: {wordCount}
            ---

            """;
        // Remove existing front matter if present
        var content = ctx.CurrentText.TrimStart();
        if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
        {
            var endIndex = content.IndexOf("\n---", 4);
            if (endIndex >= 0)
                content = content[(endIndex + 4)..].TrimStart();
        }
        return frontMatter + content;
    }

    private static string AddWordCountFooter(FinalizerExecutionContext ctx)
    {
        var plain = PlaintextStripper.Strip(ctx.CurrentText);
        var wordCount = CountWords(plain);
        var readingMinutes = Math.Max(1, wordCount / 200);
        var footer = $"\n\n---\n*{wordCount:N0} words · ~{readingMinutes} min read*";
        return ctx.CurrentText + footer;
    }

    private static string AddReadingLevel(FinalizerExecutionContext ctx)
    {
        var plain = PlaintextStripper.Strip(ctx.CurrentText);
        var level = EstimateReadingLevel(plain);
        var footer = $"\n\n---\n*Estimated reading level: {level}*";
        return ctx.CurrentText + footer;
    }

    private static int CountWords(string text) =>
        text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static string EstimateReadingLevel(string text)
    {
        // Simplified Flesch-Kincaid grade level estimate
        var sentences = Regex.Split(text, @"[.!?]+").Where(s => s.Trim().Length > 0).ToArray();
        var words = text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || sentences.Length == 0) return "N/A";

        var syllables = words.Sum(CountSyllables);
        var asl = (double)words.Length / sentences.Length;
        var asw = (double)syllables / words.Length;
        var grade = 0.39 * asl + 11.8 * asw - 15.59;
        grade = Math.Clamp(Math.Round(grade, 1), 1, 20);

        return grade switch
        {
            <= 6 => $"Elementary (Grade {grade:F0})",
            <= 9 => $"Middle School (Grade {grade:F0})",
            <= 12 => $"High School (Grade {grade:F0})",
            <= 16 => $"College (Grade {grade:F0})",
            _ => $"Graduate (Grade {grade:F0})",
        };
    }

    private static int CountSyllables(string word)
    {
        word = word.ToLowerInvariant().TrimEnd('.', ',', '!', '?', ';', ':');
        if (word.Length <= 3) return 1;
        var count = Regex.Matches(word, "[aeiouy]+").Count;
        if (word.EndsWith('e') && count > 1) count--;
        return Math.Max(1, count);
    }

    private static string EscapeYamlString(string value) =>
        value.Replace("\"", "\\\"");
}
