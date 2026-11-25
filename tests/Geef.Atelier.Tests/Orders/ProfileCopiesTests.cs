using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// What the fact check copied from the About me details: hidden from anyone but the owner, removed from the report and the stored
/// findings when the account goes (review round 1 of stage 3, security MINOR 4).
/// </summary>
public sealed class ProfileCopiesTests
{
    private const string Profile = "Name: Alex Beispiel. Ich schreibe nie mit Ausrufezeichen.";
    private const string Briefing = "Schreib eine Einladung." + BriefingSections.Separator + BriefingSections.ProfileHeading + "\n" + Profile;

    private static readonly FactFinding FromProfile = new("f1", FactFindingKind.Instruction, "Wir freuen uns sehr auf Sie!", "Alex never uses exclamation marks.",
        "Ich schreibe nie mit Ausrufezeichen.", null, null, null, InstructionOrigin.Memory, true, QuoteMatchKind.Exact);

    private static readonly FactFinding Conflict = new("f2", FactFindingKind.SourceConflict, null, "Name differs.", null,
        "Name: Alex Beispiel", "Name: Alexa Beispiel", "gaeste.txt", null, false, QuoteMatchKind.NotFound, MaterialPart.Profile, MaterialPart.Files, "gaeste.txt");

    private static readonly FactFinding OldConflict = Conflict with { MaterialFrom = null, Material2From = null };

    private static RunReport Report(params FactFinding[] open) =>
        new(2, RunReport.DirectKind, OrderConstraints.None, [], [], [], 1, 1, null, [], WriterNotesStatus.Off, WriterNotes.Empty, [], 0,
            new FactCheckSummary(FactCheckMode.Report, FactCheckScope.Full, FactCheckState.OpenPoints,
                open.Where(f => f.Kind != FactFindingKind.SourceConflict).ToList(), open.Where(f => f.Kind == FactFindingKind.SourceConflict).ToList(),
                [], 1, "m", false, "fact-check-1"), null, []);

    [Fact]
    public void A_report_loses_every_reason_and_every_passage_from_the_profile_but_keeps_the_quotes_of_the_text()
    {
        var cleaned = ProfileCopies.WithoutProfile(Report(FromProfile, Conflict), Profile);

        var passage = Assert.Single(cleaned.FactCheck!.Open);
        Assert.Equal(("Wir freuen uns sehr auf Sie!", ProfileCopies.HiddenPassage, ProfileCopies.HiddenReason), (passage.TextQuote, passage.InstructionQuote, passage.WhatIsWrong));
        var conflict = Assert.Single(cleaned.FactCheck.SourceConflicts);
        Assert.Equal((ProfileCopies.HiddenPassage, "Name: Alexa Beispiel"), (conflict.MaterialQuote, conflict.MaterialQuote2));
        Assert.DoesNotContain("Ausrufezeichen", RunReportJson.Serialize(cleaned), StringComparison.Ordinal);
        Assert.DoesNotContain("Alex Beispiel", RunReportJson.Serialize(cleaned), StringComparison.Ordinal);
    }

    [Fact]
    public void An_older_report_without_the_part_of_a_passage_is_cleaned_by_where_the_passage_stands()
    {
        var conflict = Assert.Single(ProfileCopies.WithoutProfile(Report(OldConflict), Profile).FactCheck!.SourceConflicts);

        Assert.Equal((ProfileCopies.HiddenPassage, "Name: Alexa Beispiel"), (conflict.MaterialQuote, conflict.MaterialQuote2));
    }

    [Fact]
    public void Findings_of_the_fact_check_are_hidden_from_anyone_but_the_owner()
    {
        var findings = new[]
        {
            new FindingEntity { Id = Guid.NewGuid(), IterationId = Guid.NewGuid(), ReviewerName = "source-fact-check", Severity = FindingSeverity.Minor,
                Message = "Alex never uses exclamation marks.", CreatedAt = DateTimeOffset.UtcNow, Category = FactFindingMetadata.FindingCategory, Quote = "Name: Alex Beispiel" },
            new FindingEntity { Id = Guid.NewGuid(), IterationId = Guid.NewGuid(), ReviewerName = "deterministic-checks", Severity = FindingSeverity.Major,
                Message = "The text has 80 words.", CreatedAt = DateTimeOffset.UtcNow, Category = "deterministic-check" },
        };

        Assert.Same(findings, ProfileCopies.ForViewer(findings, Briefing, "alex", "alex"));
        var shown = ProfileCopies.ForViewer(findings, Briefing, "alex", "admin");
        Assert.Equal((ProfileCopies.HiddenReason, ProfileCopies.HiddenPassage), (shown[0].Message, shown[0].Quote));
        Assert.Equal(findings[1], shown[1]);
        Assert.Same(findings, ProfileCopies.ForViewer(findings, "Schreib eine Einladung.", "alex", "admin"));
    }
    /// <summary>A stored evaluation event as the event sink writes it: default <see cref="System.Text.Json"/> options, findings of the SDK.</summary>
    internal static string EventPayload(string message, string instruction, string material) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            Iteration = 1,
            Aggregate = new
            {
                AllFindings = new[]
                {
                    new Geef.Sdk.Results.Finding
                    {
                        ReviewerName = "source-fact-check", Fingerprint = "x", Message = message, Severity = Geef.Sdk.Results.FindingSeverity.Warning,
                        Category = FactFindingMetadata.FindingCategory, ArtifactReference = "draft",
                        Metadata = new Dictionary<string, object>
                        {
                            [FactFindingMetadata.Quote] = "Wir freuen uns sehr auf Sie!", [FactFindingMetadata.Instruction] = instruction,
                            [FactFindingMetadata.Material] = material, [FactFindingMetadata.Origin] = nameof(InstructionOrigin.Memory),
                        },
                    },
                    new Geef.Sdk.Results.Finding
                    {
                        ReviewerName = "length", Fingerprint = "y", Message = "Too long.", Severity = Geef.Sdk.Results.FindingSeverity.Error,
                        Category = "deterministic-check", ArtifactReference = "draft",
                    },
                },
            },
        });

    [Fact]
    public void Stored_events_lose_the_reason_the_instruction_and_profile_quotes_of_fact_check_findings_and_keep_everything_else()
    {
        var payload = EventPayload("Alex never uses exclamation marks.", "Ich schreibe nie mit Ausrufezeichen.", "Name: Alex Beispiel");

        var cleaned = ProfileCopies.WithoutProfileInEvent(payload, Profile)!;

        Assert.DoesNotContain("Alex", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("Ausrufezeichen", cleaned, StringComparison.Ordinal);
        var findings = System.Text.Json.Nodes.JsonNode.Parse(cleaned)!["Aggregate"]!["AllFindings"]!.AsArray();
        Assert.Equal(ProfileCopies.HiddenReason, (string?)findings[0]!["Message"]);
        Assert.Equal(ProfileCopies.HiddenPassage, (string?)findings[0]!["Metadata"]![FactFindingMetadata.Instruction]);
        Assert.Equal(ProfileCopies.HiddenPassage, (string?)findings[0]!["Metadata"]![FactFindingMetadata.Material]);
        Assert.Equal("Wir freuen uns sehr auf Sie!", (string?)findings[0]!["Metadata"]![FactFindingMetadata.Quote]);
        Assert.Equal("Too long.", (string?)findings[1]!["Message"]);
        Assert.Null(ProfileCopies.WithoutProfileInEvent(cleaned, Profile));
        Assert.Null(ProfileCopies.WithoutProfileInEvent("not json", Profile));
    }

    private static readonly System.Text.Json.JsonSerializerOptions SinkOptions = new()
    {
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        WriteIndented = false,
    };

    /// <summary>Every SDK event whose payload holds text that can repeat the briefing, serialized exactly as the event sink stores it.</summary>
    internal static IReadOnlyList<string> SdkEventPayloads(string briefing, string profile)
    {
        var now = DateTimeOffset.UtcNow;
        var context = new Geef.Sdk.Context.RunContext();
        var finding = new Geef.Sdk.Results.Finding
        {
            ReviewerName = "source-fact-check", Fingerprint = "x", Message = "Breaks: " + profile, Severity = Geef.Sdk.Results.FindingSeverity.Warning,
            Category = FactFindingMetadata.FindingCategory, ArtifactReference = "draft",
            Metadata = new Dictionary<string, object> { [FactFindingMetadata.Material] = profile, [FactFindingMetadata.Origin] = nameof(InstructionOrigin.Memory) },
        };
        var review = new Geef.Sdk.Results.ReviewResult
        {
            ReviewerName = "source-fact-check", Decision = Geef.Sdk.Results.ReviewDecision.Rejected, Findings = [finding], Duration = TimeSpan.Zero,
        };
        Geef.Sdk.Events.IGeefEvent[] events =
        [
            new Geef.Sdk.Events.PipelineStartedEvent("r", briefing, now),
            new Geef.Sdk.Events.GroundingCompletedEvent("r", new Geef.Sdk.Results.GroundingResult { Context = context, Notes = ["Brief: " + briefing] }, TimeSpan.Zero, now),
            new Geef.Sdk.Events.ExecutionCompletedEvent("r", 1, new Geef.Sdk.Results.ExecutionResult { UpdatedContext = context, Notes = ["Profile: " + profile] }, TimeSpan.Zero, now),
            new Geef.Sdk.Events.AdvisorConsultationStartedEvent("r", 1, default, "advisor", "c1", new Geef.Sdk.Advisors.AdvisorQuery { Question = briefing, Character = Geef.Sdk.Advisors.AdvisorQueryCharacter.DecisionSupport }, now),
            new Geef.Sdk.Events.ReviewerFaultIsolatedEvent("r", 1, "source-fact-check", "Failed on: " + briefing, now),
            new Geef.Sdk.Events.ReviewerCompletedEvent("r", 1, review, now),
        ];
        return events.Select(e => System.Text.Json.JsonSerializer.Serialize(e, e.GetType(), SinkOptions)).ToList();
    }

    [Fact]
    public void Every_sdk_event_that_can_carry_the_briefing_loses_the_profile_and_keeps_the_request()
    {
        var payloads = SdkEventPayloads(Briefing, Profile);

        Assert.Contains(payloads, p => p.Contains("\"Input\"", StringComparison.Ordinal) && p.Contains("Ausrufezeichen", StringComparison.Ordinal));
        foreach (var payload in payloads)
        {
            Assert.Contains("Ausrufezeichen", payload, StringComparison.Ordinal);
            var cleaned = ProfileCopies.WithoutProfileInEvent(payload, Profile);

            Assert.NotNull(cleaned);
            Assert.DoesNotContain("Ausrufezeichen", cleaned, StringComparison.Ordinal);
            Assert.DoesNotContain("Alex Beispiel", cleaned, StringComparison.Ordinal);
            Assert.Null(ProfileCopies.WithoutProfileInEvent(cleaned, Profile));
        }
        var started = System.Text.Json.Nodes.JsonNode.Parse(ProfileCopies.WithoutProfileInEvent(payloads[0], Profile)!)!;
        Assert.Equal(BriefingSections.WithoutProfile(Briefing), (string?)started["Input"]);
        Assert.StartsWith("Schreib eine Einladung.", (string?)started["Input"], StringComparison.Ordinal);
    }

    [Fact]
    public void Events_of_a_run_whose_briefing_was_cleaned_before_lose_the_profile_found_in_their_own_input()
    {
        var payloads = SdkEventPayloads(Briefing, Profile);

        Assert.Equal(Profile, ProfileCopies.ProfileInEvent(payloads[0]));
        Assert.Null(ProfileCopies.ProfileInEvent(ProfileCopies.WithoutProfileInEvent(payloads[0], null)));
        Assert.DoesNotContain("Ausrufezeichen", ProfileCopies.WithoutProfileInEvent(payloads[0], null), StringComparison.Ordinal);
        Assert.DoesNotContain("Ausrufezeichen", ProfileCopies.WithoutProfileInEvent(payloads[^1], ProfileCopies.ProfileInEvent(payloads[0])), StringComparison.Ordinal);
    }
}
