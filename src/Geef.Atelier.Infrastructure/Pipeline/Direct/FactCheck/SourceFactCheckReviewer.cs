using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

/// <summary>
/// Reviewer of the direct path that checks the facts of the text against its material with a model of another family
/// (contract stage 3, 4.3). Whether a finding blocks is decided by the code from where its quotes stand, never by the model.
/// <list type="bullet">
///   <item>The mode comes from the run context (<see cref="AtelierContextKeys.FactCheckMode"/>, set by the grounding step);
///   <see cref="FactCheckMode.Off"/> approves without a call.</item>
///   <item>Every wording is checked in full (<c>check_facts</c>), above <see cref="DirectOptions.FactCheckMaxMaterialChars"/>
///   without the content of the files. Only a wording that an earlier round of the run already checked word for word
///   (<see cref="FactCheckRound.HashOf"/>) takes over that round's findings without a call. A text changed for any reason (a fact
///   finding, the length, the language, …) is therefore checked again in full; a look at the earlier findings alone would leave
///   new sentences unchecked. Findings of earlier answered rounds whose passage still
///   stands in the text word for word keep their blocking effect, and source conflicts always stay
///   (<see cref="WithEarlierFindings"/>): a check that does not flag a passage again never makes it disappear.</item>
///   <item>A call that cannot reach its model or runs into <see cref="DirectOptions.FactCheckTimeout"/> is never repeated with the
///   same model: it is tried exactly once with <c>fallback</c> (another provider, never the writer's model) within
///   <see cref="DirectOptions.FactCheckFallbackTimeout"/>; when that fails too, the reviewer throws, the SDK isolates it as failed
///   and the result says the check could not be completed. A model that ran into its time limit is not called again in the
///   same run (a later round goes straight to the fallback). A failed reviewer does not block convergence
///   (<c>FailedReviewerHandling.TreatAsNonBlocking</c>), so an aborted check never starts a correction round.</item>
///   <item>Every call that ran into its time limit is logged once with <see cref="HangEvent"/>: call, model, duration, limit, run,
///   round and whether it was the fallback, never material or text.</item>
/// </list>
/// Severities: <see cref="FactCheckMode.On"/> blocking → <see cref="FindingSeverity.Error"/> and rejected, hints →
/// <see cref="FindingSeverity.Info"/>, source conflicts → <see cref="FindingSeverity.Warning"/>; <see cref="FactCheckMode.Report"/>
/// everything <see cref="FindingSeverity.Warning"/> and approved; <see cref="FactCheckMode.Shadow"/> everything
/// <see cref="FindingSeverity.Info"/> with metadata <c>shadow</c> and approved. <see cref="Finding.Message"/> is the model's
/// reason (for the writer's correction and the run details, never for the result page).
/// </summary>
internal sealed class SourceFactCheckReviewer(
    ReviewerProfile profile,
    IFactChecker checker,
    FactCheckLedger ledger,
    Func<ReviewerProfile, ReviewerProfile?> fallback,
    DirectOptions options,
    Guid runId,
    ILogger logger) : IReviewer
{
    public const string ReviewerName = "source-fact-check";
    public const string FindingCategory = FactFindingMetadata.FindingCategory;

    /// <summary>Metadata key that marks a finding of <see cref="FactCheckMode.Shadow"/>; the person never sees it.</summary>
    public const string ShadowKey = "shadow";

    /// <summary>Event of the log line written for every fact-check call that ran into its time limit.</summary>
    public static readonly EventId HangEvent = new(7301, "FactCheckHang");

    /// <summary>Message of a finding whose model gave no reason.</summary>
    public const string NoReasonMessage = "The fact check flagged this passage.";

    /// <inheritdoc/>
    public string Name => ReviewerName;

    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public async Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var iteration = context.TryGet(GeefKeys.CurrentIteration, out var current) && current > 0 ? current : 1;
        if (!context.TryGet(AtelierContextKeys.FactCheckMode, out var mode))
        {
            logger.LogError("Run {RunId}: no fact-check mode in the run context in round {Iteration}; the facts are not checked.", runId, iteration);
            mode = FactCheckMode.Off;
        }

        if (mode == FactCheckMode.Off)
        {
            ledger.Record(new FactCheckRound(iteration, mode, FactCheckScope.Full, FactRoundStatus.Off, [], 0, null, false));
            return Result(ReviewDecision.Approved, [], started);
        }

        if (!context.TryGet(AtelierContextKeys.FactMaterial, out var material) || material is null)
        {
            ledger.Record(new FactCheckRound(iteration, mode, FactCheckScope.Full, FactRoundStatus.Failed, [], 0, null, false));
            throw new FactCheckFailedException("The material of the fact check is missing from the run context.");
        }

        var draft = context.TryGet(AtelierContextKeys.CurrentDraft, out var text) ? text ?? string.Empty : string.Empty;
        var hash = FactCheckRound.HashOf(draft);
        var same = ledger.CheckedWording(hash, iteration);
        var round = same is not null
            ? same with { Iteration = iteration, Mode = mode, Calls = 0, UsedFallbackModel = false }
            : await CheckAsync(iteration, mode, material, draft, hash, cancellationToken);
        round = WithEarlierFindings(round, draft, ledger.AnsweredBefore(iteration), takenOver: same is not null);

        ledger.Record(round);
        var findings = round.Open.Select(f => ToFinding(f, mode)).ToList();
        var rejected = mode == FactCheckMode.On && round.Open.Any(f => f.Blocking);
        return Result(rejected ? ReviewDecision.Rejected : ReviewDecision.Approved, findings, started);
    }

    /// <summary>
    /// <paramref name="round"/> with what earlier answered rounds still leave open (<see cref="FactCarryOver.Merge"/>): their
    /// findings whose passage still stands in <paramref name="draft"/>, with their blocking effect, and every source conflict. Flagged
    /// passages an earlier check could not list stay counted in <see cref="FactCheckRound.NotShown"/> (the most any earlier check
    /// left out, added to what this check left out), because nobody can tell whether they are still in the text. A round
    /// <paramref name="takenOver"/> word for word made no check of its own.
    /// </summary>
    internal static FactCheckRound WithEarlierFindings(FactCheckRound round, string draft, IReadOnlyList<FactCheckRound> earlier, bool takenOver)
    {
        var own = takenOver ? 0 : round.NotShown;
        if (earlier.Count == 0)
            return round with { CheckNotShown = own, NotShown = own };
        var latestFirst = earlier.OrderByDescending(r => r.Iteration).ToList();
        return round with
        {
            Open = FactCarryOver.Merge(round.Open, latestFirst.Select(r => r.Open), draft),
            CheckNotShown = own,
            NotShown = own + latestFirst.Max(r => r.CheckNotShown),
        };
    }

    /// <summary>The material as the check gets it: without the content of the files when material and text are over the limit.</summary>
    internal static FactMaterial Scoped(FactMaterial material, string draft, int maxChars) =>
        material.Scope == FactCheckScope.Full && (long)material.TotalChars + draft.Length > maxChars
            ? material with { Scope = FactCheckScope.WithoutFiles }
            : material;

    private async Task<FactCheckRound> CheckAsync(int iteration, FactCheckMode mode, FactMaterial material, string draft, string hash, CancellationToken ct)
    {
        var scoped = Scoped(material, draft, options.FactCheckMaxMaterialChars);
        var calls = 0;
        var usedFallback = false;
        FactCheckCall? call = null;
        if (!ledger.HasHung(profile.Model))
        {
            call = await checker.CheckAsync(scoped, draft, profile, options.FactCheckTimeout, iteration, ct);
            calls++;
            NoteHang(profile, call, options.FactCheckTimeout, iteration, fallback: false);
        }
        else
        {
            logger.LogWarning("Run {RunId}: {Model} hung earlier in this run, so round {Iteration} is not checked with it again.", runId, profile.Model, iteration);
        }

        if ((call is null || LlmFactChecker.IsUnavailable(call.ErrorCode)) && Fallback() is { } second)
        {
            if (ledger.HasHung(second.Model))
            {
                logger.LogWarning("Run {RunId}: the fallback {Fallback} hung earlier in this run; round {Iteration} is not checked.", runId, second.Model, iteration);
            }
            else
            {
                logger.LogWarning("Run {RunId}: fact check with {Model} failed ({Code}) in round {Iteration}; trying {Fallback} once.",
                    runId, call?.Model ?? profile.Model, call?.ErrorCode ?? LlmFactChecker.TimeoutCode, iteration, second.Model);
                call = await checker.CheckAsync(scoped, draft, second, options.FactCheckFallbackTimeout, iteration, ct);
                calls++;
                usedFallback = true;
                NoteHang(second, call, options.FactCheckFallbackTimeout, iteration, fallback: true);
            }
        }

        if (call is null || call.ErrorCode is not null || call.Proposed is null)
        {
            ledger.Record(new FactCheckRound(iteration, mode, scoped.Scope, FactRoundStatus.Failed, [], calls, call?.Model, usedFallback, hash));
            throw new FactCheckFailedException($"The fact check could not be completed ({call?.ErrorCode ?? (call is null ? "not called again after a hang" : "no answer")}).");
        }

        var filtered = FactFindingFilter.Apply(call.Proposed, draft, scoped);
        var notShown = call.Dropped + filtered.Trace.Count(t => t.Decision == FactFilterDecision.DroppedOverLimit);
        logger.LogInformation("Run {RunId}: fact check round {Iteration} with {Model}: {Proposed} proposed, {Kept} kept, {NotShown} not shown ({Trace}).",
            runId, iteration, call.Model, call.Proposed.Count, filtered.Findings.Count, notShown,
            string.Join(",", filtered.Trace.Select(t => t.Decision)));
        return new FactCheckRound(iteration, mode, scoped.Scope, FactRoundStatus.Checked, filtered.Findings, calls, call.Model, usedFallback, hash, notShown);
    }

    private void NoteHang(ReviewerProfile attempted, FactCheckCall call, TimeSpan limit, int iteration, bool fallback)
    {
        if (call.ErrorCode == LlmFactChecker.TimeoutCode)
            ledger.MarkHung(attempted.Model);
        LogHang(FactCheckSchema.CheckName, call.ErrorCode, call.Model, call.DurationMs, limit, iteration, fallback);
    }

    private void LogHang(string call, string? errorCode, string model, int durationMs, TimeSpan limit, int iteration, bool fallback)
    {
        if (errorCode != LlmFactChecker.TimeoutCode)
            return;
        logger.LogWarning(HangEvent,
            "Fact check hang: {Call} with {Model} ran into its limit of {LimitSeconds} s after {DurationMs} ms (run {RunId}, round {Iteration}, fallback {Fallback}).",
            call, model, (int)limit.TotalSeconds, durationMs, runId, iteration, fallback);
    }

    private ReviewerProfile? Fallback() => options.FactCheckFallbackModel ? fallback(profile) : null;

    private Finding ToFinding(FactFinding finding, FactCheckMode mode)
    {
        var metadata = new Dictionary<string, object>
        {
            [FactFindingMetadata.Code] = CodeOf(finding.Kind),
            [FactFindingMetadata.Id] = finding.Id,
            [FactFindingMetadata.Kind] = finding.Kind.ToString(),
            [FactFindingMetadata.Blocking] = finding.Blocking,
            [FactFindingMetadata.Mode] = mode.ToString(),
        };
        Add(metadata, FactFindingMetadata.Quote, finding.TextQuote ?? finding.MaterialQuote);
        Add(metadata, FactFindingMetadata.Instruction, finding.InstructionQuote);
        Add(metadata, FactFindingMetadata.Material, finding.MaterialQuote);
        Add(metadata, FactFindingMetadata.Material2, finding.MaterialQuote2);
        Add(metadata, FactFindingMetadata.MaterialFile, finding.MaterialFile);
        if (finding.Origin is { } origin)
            metadata[FactFindingMetadata.Origin] = origin.ToString();
        if (finding.MaterialFrom is { } from)
            metadata[FactFindingMetadata.MaterialFrom] = from.ToString();
        if (mode == FactCheckMode.Shadow)
            metadata[ShadowKey] = true;

        return new Finding
        {
            ReviewerName = Name,
            Fingerprint = Fingerprint(finding),
            Message = string.IsNullOrWhiteSpace(finding.WhatIsWrong) ? NoReasonMessage : finding.WhatIsWrong.Trim(),
            Severity = SeverityOf(finding, mode),
            Category = FindingCategory,
            ArtifactReference = "draft",
            Metadata = metadata,
        };
    }

    /// <summary>Severity of <paramref name="finding"/> in <paramref name="mode"/>; only a blocking finding in <see cref="FactCheckMode.On"/> is an error.</summary>
    internal static FindingSeverity SeverityOf(FactFinding finding, FactCheckMode mode) => mode switch
    {
        FactCheckMode.On when finding.Kind == FactFindingKind.SourceConflict => FindingSeverity.Warning,
        FactCheckMode.On => finding.Blocking ? FindingSeverity.Error : FindingSeverity.Info,
        FactCheckMode.Report => FindingSeverity.Warning,
        _ => FindingSeverity.Info,
    };

    /// <summary>Value of the metadata <c>code</c> for <paramref name="kind"/>.</summary>
    internal static string CodeOf(FactFindingKind kind) => kind switch
    {
        FactFindingKind.Unsupported => FactFindingMetadata.UnsupportedCode,
        FactFindingKind.Instruction => FactFindingMetadata.InstructionCode,
        FactFindingKind.Superseded => FactFindingMetadata.SupersededCode,
        _ => FactFindingMetadata.SourceConflictCode,
    };

    private static void Add(Dictionary<string, object> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            metadata[key] = value;
    }

    private string Fingerprint(FactFinding finding)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{finding.Kind}:{finding.TextQuote}:{finding.MaterialQuote}:{finding.MaterialQuote2}"));
        return $"{Name}:{Convert.ToBase64String(hash)[..12]}";
    }

    private ReviewResult Result(ReviewDecision decision, IReadOnlyList<Finding> findings, DateTimeOffset started) => new()
    {
        ReviewerName = Name,
        Decision = decision,
        Findings = findings,
        Duration = DateTimeOffset.UtcNow - started,
    };
}
