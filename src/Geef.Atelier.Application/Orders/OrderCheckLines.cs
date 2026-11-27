using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Application.Orders;

/// <summary>
/// Pure: turns a report into the sentences of "What was checked" (contract stage 3, 4.7) and the sentences about problems in the
/// material (4.6). A check is named as met only when it ran and passed; a check that ran and did not pass is named as not met; a
/// check that did not run is not named. The recalculation of the text and of the files never says that calculations or totals are
/// right: it names only what looks clearly wrong (asking to check it) and otherwise at most that nothing clearly wrong was found,
/// adding that not every calculation can be checked; this holds for earlier reports as well. The line about the facts names the
/// pages looked up on the web only when the delivered run had them in its material (<see cref="RunReport.LookedUp"/>), and those
/// pages never count as files. Web and MCP use only these sentences.
/// </summary>
public static class OrderCheckLines
{
    public const string Heading = "What was checked";

    public const string AutomaticLead = "Checked automatically: the text ";
    public const string AutomaticFailedLead = "Checked automatically and not met – see the open points below: the text ";
    public const string AutomaticEnd = ".";
    public const string AutomaticSeparator = ", ";

    public const string PassedNotEmpty = "is not empty";
    public const string PassedStartsDirectly = "starts with the text itself, not with a remark to the reader";
    public const string PassedLength = "has the requested length";
    public const string PassedLanguage = "is in the requested language";
    public const string PassedNoPlaceholders = "has no placeholders";
    public const string PassedAvoidsLead = "avoids ";
    public const string PassedRequiredWords = "uses the words you asked for";

    public const string AvoidsLists = "lists";
    public const string AvoidsHeadings = "headings";
    public const string AvoidsEmojis = "emojis";
    public const string AvoidsWords = "the words you ruled out";
    public const string AvoidsLastSeparator = " and ";

    public const string FailedEmpty = "is empty";
    public const string FailedPreamble = "may start with a remark to the reader";
    public const string FailedLength = "does not have the requested length";
    public const string FailedLanguage = "is not in the requested language";
    public const string FailedPlaceholders = "has placeholders";
    public const string FailedLists = "uses lists";
    public const string FailedHeadings = "uses headings";
    public const string FailedEmojis = "uses emojis";
    public const string FailedForbiddenWords = "uses words you ruled out";
    public const string FailedRequiredWords = "lacks words you asked for";
    public const string FailedArithmetic = "has a calculation that looks wrong";

    /// <summary>Lead of the rules of the first request a changed version keeps; they are never named as checks of the change.</summary>
    public const string FirstRequestLead = "Checked automatically against the rules of your first request (your change may have replaced them): the text ";

    /// <summary>Lead of the rules of the first request a changed version does not keep; they are notes, never blocking.</summary>
    public const string FirstRequestFailedLead =
        "Rules of your first request the new version does not keep – please check whether your change meant this (see the open points below): the text ";

    public const string FirstPassedLength = "has the length it asked for";
    public const string FirstPassedLanguage = "is in the language it asked for";
    public const string FirstPassedRequiredWords = "uses the words it asked for";
    public const string FirstAvoidsWords = "the words it ruled out";
    public const string FirstFailedLength = "does not have the length it asked for";
    public const string FirstFailedLanguage = "is not in the language it asked for";
    public const string FirstFailedForbiddenWords = "uses words it ruled out";
    public const string FirstFailedRequiredWords = "lacks words it asked for";

    /// <summary>Own line when the calculations of the text were searched and none looked clearly wrong; it never says they are right.</summary>
    public const string ArithmeticSearched =
        "The text was searched for calculations that are clearly wrong, and none was found. Not every calculation can be checked this way.";

    public const string FactsComparedLead = "Facts in the text were compared with your request, your answers and your files.";
    public const string FactsComparedLeadNoFiles = "Facts in the text were compared with your request and your answers.";
    public const string FactsNothingFound = " Nothing was found that they do not back.";
    public const string FactsOpenOne = " 1 passage needs your attention – see below.";
    public const string FactsOpenFormat = " {0} passages need your attention – see below.";
    public const string FactsNotShownOne = " 1 more flagged passage could not be checked or listed.";
    public const string FactsNotShownFormat = " {0} more flagged passages could not be checked or listed.";
    public const string FactsNotShownOnlyOne = " 1 flagged passage could not be checked or listed.";
    public const string FactsNotShownOnlyFormat = " {0} flagged passages could not be checked or listed.";

    public const string FactsChecked = FactsComparedLead + FactsNothingFound;
    public const string FactsOpenPointsFormat = FactsComparedLead + FactsOpenFormat;
    public const string FactsOpenPointOne = FactsComparedLead + FactsOpenOne;
    public const string FactsCheckedNoFiles = FactsComparedLeadNoFiles + FactsNothingFound;
    public const string FactsOpenPointOneNoFiles = FactsComparedLeadNoFiles + FactsOpenOne;

    public const string FactsWithoutFilesLead =
        "Your files were too long to compare with the text, so the facts in it were not checked. Only whether the text follows your instructions was checked;";
    public const string FactsWithoutFilesNothing = " nothing was found against them.";
    public const string FactsWithoutFiles = FactsWithoutFilesLead + FactsWithoutFilesNothing;
    public const string FactsWithoutFilesOpenOne = " 1 passage needs your attention – see below.";
    public const string FactsWithoutFilesOpenFormat = " {0} passages need your attention – see below.";

    public const string FactsNotCompleted = "The fact check could not be completed.";
    public const string FactsVersionNotChecked = "The facts of this version of the text were not checked: it was changed after the fact check.";
    public const string FactsEarlierOne = " 1 passage flagged in an earlier version is still in the text – see below.";
    public const string FactsEarlierFormat = " {0} passages flagged in an earlier version are still in the text – see below.";
    public const string FactsNotChecked = "Facts were not checked.";

    public const string TotalsSearchedNone =
        "Your files were searched for totals that are clearly wrong, and none was found. Not every total can be checked this way.";
    public const string TotalsSearchedOne =
        "Your files were searched for totals that are clearly wrong: 1 looks wrong – please check it below. Not every total can be checked this way.";
    public const string TotalsSearchedFormat =
        "Your files were searched for totals that are clearly wrong: {0} look wrong – please check them below. Not every total can be checked this way.";
    public const string Contradictions = "Your files were checked for contradictions.";

    public const string ChangedHeading = "Changed after the check:";
    public const string ChangedRemovedFormat = "Removed or replaced: “{0}”";
    public const string ChangedLength = "Shortened to the requested length.";
    public const string ChangedLengthLonger = "Made longer to the requested length.";
    public const string ChangedLengthOther = "Brought to the requested length.";
    public const string ChangedLanguage = "Written in the requested language.";
    public const string ChangedPlaceholders = "Placeholders were taken out.";
    public const string ChangedPreamble = "The remark before the text was taken out.";
    public const string ChangedLists = "Lists were taken out.";
    public const string ChangedHeadings = "Headings were taken out.";
    public const string ChangedEmojis = "Emojis were taken out.";
    public const string ChangedForbiddenWords = "Words you ruled out were taken out.";
    public const string ChangedRequiredWords = "Words you asked for were added.";
    public const string ChangedArithmetic = "A calculation in the text was corrected.";
    public const string ChangedMayRemain =
        "The new wording of the text was not checked against your facts, so what these passages said may still be in it in other words.";

    public const string ToReviewOpenPoints = "Please look at the open points below.";
    public const string ToReviewNothing = "There is nothing from these checks that you need to look at.";
    public const string ToReviewFactsNotFullyChecked = "The facts in the text were not fully checked, so please read it carefully before you use it.";

    public const string Limits = "These checks look for the problems named here. They do not prove that everything in the text is right.";

    public const string TotalMismatchFormat = "Your file \"{0}\" gives {1} as the total, but {2} = {3}.";
    public const string TotalTextUsesFormat = " The text uses {0}. Please check which figure is right.";
    public const string TotalTextSilent = " The text does not mention the total. Please check the file.";
    public const string TotalsNotListedOne = "1 more total in your files looks wrong; it is not listed here. Please check your files.";
    public const string TotalsNotListedFormat = "{0} more totals in your files look wrong; they are not listed here. Please check your files.";

    public const string SourceConflictSameFormat = "{0} {1} “{2}” and also “{3}”.";
    public const string SourceConflictFormat = "{0} {1} “{2}”, but {3} {4} “{5}”.";
    public const string Says = "says";
    public const string Say = "say";
    public const string SourceConflictUnknownFormat = "Your request, your answers or your files say “{0}” and “{1}”.";
    public const string FromRequest = "your request";
    public const string FromPersonAnswers = "your answers";
    public const string FromAssistantAnswers = "your assistant's answers";
    public const string FromProfile = "your About me details";
    public const string FromProfileHidden = "the About me details of the person who ordered this text";
    public const string FromFiles = "your files";
    public const string FromFileFormat = "your file \"{0}\"";
    public const string ProfileQuoteHidden = "[hidden here]";

    private const string ItemSeparator = " + ";

    /// <summary>The sentences of "What was checked" for <paramref name="report"/>; <paramref name="hasOpenPoints"/> chooses the line about what to review.</summary>
    public static OrderChecksView Build(RunReport report, bool hasOpenPoints)
    {
        ArgumentNullException.ThrowIfNull(report);
        var checkedLines = new List<string>();
        checkedLines.AddRange(Automatic(report.Checks, report.Revision));
        checkedLines.Add(Facts(report));
        if (report.SourceArithmetic is { } totals && Totals(totals) is { } totalsLine)
            checkedLines.Add(totalsLine);
        if (ComparedFiles(report))
            checkedLines.Add(Contradictions);

        var toReview = hasOpenPoints ? ToReviewOpenPoints : FactsFullyChecked(report) ? ToReviewNothing : ToReviewFactsNotFullyChecked;
        return new OrderChecksView(checkedLines, Changed(report), toReview, Limits);
    }

    /// <summary>
    /// One sentence per total in the files that looks wrong, asking to check it (at most the listed ones, then one sentence for the rest), and per
    /// contradiction in the material. A contradiction that only repeats a total already named is left out. Each side of a
    /// contradiction is named by where it stands (request, answers, About me details, a file); passages from the About me details
    /// are shown only when <paramref name="showProfile"/> is true (the owner of the order).
    /// </summary>
    public static IReadOnlyList<string> SourceIssueSentences(RunReport report, bool showProfile = true)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sentences = new List<string>();
        var issues = report.SourceArithmetic?.Issues ?? [];
        sentences.AddRange(issues.Select(TotalSentence));
        if (report.SourceArithmetic is { IssuesNotListed: > 0 } more)
            sentences.Add(more.IssuesNotListed == 1 ? TotalsNotListedOne : string.Format(TotalsNotListedFormat, more.IssuesNotListed));

        if (report.FactCheck is { } factCheck)
        {
            var shown = factCheck.State is not (FactCheckState.Off or FactCheckState.NotShown);
            foreach (var conflict in factCheck.SourceConflicts)
            {
                if (!shown && conflict.FromVersion is null)
                    continue;
                if (string.IsNullOrWhiteSpace(conflict.MaterialQuote) || string.IsNullOrWhiteSpace(conflict.MaterialQuote2)
                    || issues.Any(i => RepeatsTotal(conflict, i)))
                    continue;
                sentences.Add(ConflictSentence(conflict, showProfile));
            }
        }
        return sentences.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The sentence about a total in a file that looks wrong, as <see cref="SourceIssueSentences"/> lists it.</summary>
    public static string TotalSentence(SourceArithmeticIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var items = string.Join(ItemSeparator, issue.Items);
        var use = issue.UseInText switch
        {
            TotalUseInText.Computed => string.Format(TotalTextUsesFormat, issue.Computed),
            TotalUseInText.Stated => string.Format(TotalTextUsesFormat, issue.Stated),
            _ => TotalTextSilent,
        };
        return string.Format(TotalMismatchFormat, issue.FileName, issue.Stated, items, issue.Computed) + use;
    }

    /// <summary>True when the fact check ran on the delivered text with the content of at least one file.</summary>
    public static bool ComparedFiles(RunReport report) =>
        report.FactCheck is { State: FactCheckState.Checked or FactCheckState.OpenPoints, Scope: FactCheckScope.Full } && HasFiles(report);

    /// <summary>True when the facts of the delivered text were checked against everything and every flagged passage could be listed.</summary>
    public static bool FactsFullyChecked(RunReport report) =>
        report.FactCheck is { State: FactCheckState.Checked or FactCheckState.OpenPoints, Scope: FactCheckScope.Full, NotShown: 0 };

    private static bool HasFiles(RunReport report) => report.Sources.Any(s => !s.LookedUp && s.Mode != SourceUsageMode.Excluded);

    private static IEnumerable<string> Automatic(IReadOnlyList<DraftCheckResult> checks, bool revision)
    {
        var always = revision ? checks.Where(c => !c.IsFirstRequestRule()).ToList() : checks;
        var (passed, failed) = Sort(always, revision: false);
        if (passed.Count > 0)
            yield return AutomaticLead + string.Join(AutomaticSeparator, passed) + AutomaticEnd;
        if (failed.Count > 0)
            yield return AutomaticFailedLead + string.Join(AutomaticSeparator, failed) + AutomaticEnd;

        if (revision)
        {
            var (kept, notKept) = Sort(checks.Where(c => c.IsFirstRequestRule()).ToList(), revision: true);
            if (kept.Count > 0)
                yield return FirstRequestLead + string.Join(AutomaticSeparator, kept) + AutomaticEnd;
            if (notKept.Count > 0)
                yield return FirstRequestFailedLead + string.Join(AutomaticSeparator, notKept) + AutomaticEnd;
        }

        var arithmetic = checks.Where(c => c.Code == DraftCheckCodes.Arithmetic).ToList();
        if (arithmetic.Count > 0 && arithmetic.All(c => c.Passed))
            yield return ArithmeticSearched;
    }

    private static (List<string> Passed, List<string> Failed) Sort(IReadOnlyList<DraftCheckResult> checks, bool revision)
    {
        var passed = new List<string>();
        var failed = new List<string>();
        void Sort(string code, string passedPhrase, string failedPhrase, Func<DraftCheckResult, bool>? applies = null)
        {
            var results = checks.Where(c => c.Code == code && (applies is null || applies(c))).ToList();
            if (results.Count == 0)
                return;
            if (results.All(r => r.Passed))
                passed.Add(passedPhrase);
            else
                failed.Add(failedPhrase);
        }

        Sort(DraftCheckCodes.Empty, PassedNotEmpty, FailedEmpty);
        Sort(DraftCheckCodes.Preamble, PassedStartsDirectly, FailedPreamble);
        Sort(DraftCheckCodes.Length, revision ? FirstPassedLength : PassedLength, revision ? FirstFailedLength : FailedLength);
        Sort(DraftCheckCodes.Language, revision ? FirstPassedLanguage : PassedLanguage, revision ? FirstFailedLanguage : FailedLanguage);
        Sort(DraftCheckCodes.Placeholders, PassedNoPlaceholders, FailedPlaceholders, revision ? c => c.IsFirstRequestRule() : c => c.Blocking);

        var avoids = new List<string>();
        void Avoid(string code, string what, string failedPhrase)
        {
            var results = checks.Where(c => c.Code == code).ToList();
            if (results.Count == 0)
                return;
            if (results.All(r => r.Passed))
                avoids.Add(what);
            else
                failed.Add(failedPhrase);
        }

        Avoid(DraftCheckCodes.Lists, AvoidsLists, FailedLists);
        Avoid(DraftCheckCodes.Headings, AvoidsHeadings, FailedHeadings);
        Avoid(DraftCheckCodes.Emojis, AvoidsEmojis, FailedEmojis);
        Avoid(DraftCheckCodes.ForbiddenTerm, revision ? FirstAvoidsWords : AvoidsWords, revision ? FirstFailedForbiddenWords : FailedForbiddenWords);
        if (avoids.Count > 0)
            passed.Add(PassedAvoidsLead + JoinWithAnd(avoids));

        Sort(DraftCheckCodes.RequiredTerm, revision ? FirstPassedRequiredWords : PassedRequiredWords, revision ? FirstFailedRequiredWords : FailedRequiredWords);
        if (checks.Any(c => c.Code == DraftCheckCodes.Arithmetic && !c.Passed))
            failed.Add(FailedArithmetic);
        return (passed, failed);
    }

    private static string JoinWithAnd(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + AvoidsLastSeparator + items[^1];

    private static string Facts(RunReport report)
    {
        var factCheck = report.FactCheck;
        if (factCheck is null)
            return FactsNotChecked;
        if (factCheck.State is FactCheckState.Off or FactCheckState.NotShown)
            return FactsNotChecked + Earlier(factCheck.Open.Count(f => f.FromVersion is not null && !string.IsNullOrWhiteSpace(f.TextQuote)));
        if (factCheck.State == FactCheckState.NotCompleted)
            return FactsNotCompleted + Earlier(factCheck.Open.Count) + NotShown(factCheck.NotShown, factCheck.Open.Count > 0);
        if (factCheck.State == FactCheckState.VersionNotChecked)
            return FactsVersionNotChecked + Earlier(factCheck.Open.Count) + NotShown(factCheck.NotShown, factCheck.Open.Count > 0);

        var open = factCheck.State == FactCheckState.OpenPoints ? factCheck.Open.Count : 0;
        var notShown = NotShown(factCheck.NotShown, open > 0);
        if (factCheck.Scope == FactCheckScope.WithoutFiles)
        {
            return FactsWithoutFilesLead + open switch
            {
                0 when factCheck.NotShown <= 0 => FactsWithoutFilesNothing,
                0 => string.Empty,
                1 => FactsWithoutFilesOpenOne,
                _ => string.Format(FactsWithoutFilesOpenFormat, open),
            } + notShown;
        }

        var lead = (HasFiles(report), report.LookedUp) switch
        {
            (true, true) => OrderResearchLines.FactsComparedLeadLookedUp,
            (false, true) => OrderResearchLines.FactsComparedLeadLookedUpNoFiles,
            (true, false) => FactsComparedLead,
            _ => FactsComparedLeadNoFiles,
        };
        var found = open switch
        {
            0 when factCheck.NotShown == 0 => FactsNothingFound,
            0 => string.Empty,
            1 => FactsOpenOne,
            _ => string.Format(FactsOpenFormat, open),
        };
        return lead + found + notShown;
    }

    private static string Earlier(int count) => count switch
    {
        0 => string.Empty,
        1 => FactsEarlierOne,
        _ => string.Format(FactsEarlierFormat, count),
    };

    private static string NotShown(int count, bool afterOthers) => count switch
    {
        <= 0 => string.Empty,
        1 => afterOthers ? FactsNotShownOne : FactsNotShownOnlyOne,
        _ => string.Format(afterOthers ? FactsNotShownFormat : FactsNotShownOnlyFormat, count),
    };

    private static string? Totals(SourceArithmeticResult totals)
    {
        var wrong = totals.Issues.Count + totals.IssuesNotListed;
        if (totals.TotalsFound <= 0 && wrong == 0)
            return null;
        return wrong switch
        {
            0 => TotalsSearchedNone,
            1 => TotalsSearchedOne,
            _ => string.Format(TotalsSearchedFormat, wrong),
        };
    }

    /// <summary>
    /// True only when <paramref name="conflict"/> says nothing beyond <paramref name="issue"/>: both of its passages stand in the
    /// file of the total, one names the stated total (and at most the items) and the other only items or the computed total, the
    /// figures compared as whole amounts, and the two passages differ in nothing but their figures and total words (a date, a
    /// day or any other word makes them a contradiction of their own). Also when both passages stand word for word inside the
    /// passage of the total in the file, one holds nothing but the stated total (and total words) and the other every item of the
    /// total and no other figure: then the two passages can only disagree about that total, which its own sentence already
    /// names. In any doubt both are shown.
    /// </summary>
    private static bool RepeatsTotal(FactFinding conflict, SourceArithmeticIssue issue)
    {
        if (conflict.MaterialFrom != MaterialPart.Files || conflict.Material2From != MaterialPart.Files
            || string.IsNullOrWhiteSpace(issue.FileName) || string.IsNullOrWhiteSpace(issue.Stated)
            || !string.Equals(conflict.MaterialFile, issue.FileName, StringComparison.Ordinal)
            || !string.Equals(conflict.Material2File, issue.FileName, StringComparison.Ordinal))
            return false;
        bool Pair(string? stated, string? items) =>
            SourceArithmeticCheck.NamesFigure(stated, issue.Stated)
            && SourceArithmeticCheck.NamesOnly(stated, issue.Items.Append(issue.Stated))
            && SourceArithmeticCheck.NamesOnly(items, issue.Items.Append(issue.Computed));
        return SourceArithmeticCheck.WordsBesideFigures(conflict.MaterialQuote).SequenceEqual(SourceArithmeticCheck.WordsBesideFigures(conflict.MaterialQuote2), StringComparer.Ordinal)
            && (Pair(conflict.MaterialQuote, conflict.MaterialQuote2) || Pair(conflict.MaterialQuote2, conflict.MaterialQuote))
            || InsideTotal(conflict.MaterialQuote, conflict.MaterialQuote2, issue) || InsideTotal(conflict.MaterialQuote2, conflict.MaterialQuote, issue);
    }

    private static bool InsideTotal(string? stated, string? items, SourceArithmeticIssue issue) =>
        !string.IsNullOrWhiteSpace(stated) && !string.IsNullOrWhiteSpace(items) && !string.IsNullOrWhiteSpace(issue.Quote)
        && QuoteVerifier.ContainsWholeWords(issue.Quote, stated)
        && QuoteVerifier.ContainsWholeWords(issue.Quote, items)
        && SourceArithmeticCheck.NamesFigure(stated, issue.Stated)
        && SourceArithmeticCheck.NamesOnly(stated, [issue.Stated])
        && SourceArithmeticCheck.WordsBesideFigures(stated).Count == 0
        && issue.Items.Count > 0
        && issue.Items.All(item => SourceArithmeticCheck.NamesFigure(items, item))
        && SourceArithmeticCheck.NamesOnly(items, issue.Items.Append(issue.Computed));

    private static string ConflictSentence(FactFinding conflict, bool showProfile)
    {
        var first = conflict.MaterialQuote!.Trim();
        var second = conflict.MaterialQuote2!.Trim();
        if (conflict.MaterialFrom is not { } fromFirst || conflict.Material2From is not { } fromSecond)
            return string.Format(SourceConflictUnknownFormat, first, second);

        if (!showProfile)
        {
            if (fromFirst == MaterialPart.Profile)
                first = ProfileQuoteHidden;
            if (fromSecond == MaterialPart.Profile)
                second = ProfileQuoteHidden;
        }

        var firstName = Origin(fromFirst, conflict.MaterialFile, showProfile);
        var secondName = Origin(fromSecond, conflict.Material2File, showProfile);
        return firstName == secondName
            ? string.Format(SourceConflictSameFormat, Capitalized(firstName), Verb(fromFirst, firstName), first, second)
            : string.Format(SourceConflictFormat, Capitalized(firstName), Verb(fromFirst, firstName), first, secondName, Verb(fromSecond, secondName), second);
    }

    private static string Origin(MaterialPart part, string? fileName, bool showProfile) => part switch
    {
        MaterialPart.Request => FromRequest,
        MaterialPart.PersonAnswers => FromPersonAnswers,
        MaterialPart.AssistantAnswers => FromAssistantAnswers,
        MaterialPart.Profile => showProfile ? FromProfile : FromProfileHidden,
        MaterialPart.LookedUp => OrderResearchLines.FromLookedUp,
        _ => string.IsNullOrWhiteSpace(fileName) ? FromFiles : string.Format(FromFileFormat, fileName),
    };

    private static string Verb(MaterialPart part, string name) =>
        part == MaterialPart.Request || part == MaterialPart.Files && name != FromFiles ? Says : Say;

    private static string Capitalized(string phrase) => phrase.Length == 0 ? phrase : char.ToUpperInvariant(phrase[0]) + phrase[1..];

    private static IReadOnlyList<string> Changed(RunReport report)
    {
        var lines = new List<string>();
        foreach (var correction in report.Corrections ?? [])
        {
            var line = correction.Code switch
            {
                FactFindingMetadata.UnsupportedCode or FactFindingMetadata.InstructionCode or FactFindingMetadata.SupersededCode
                    => string.IsNullOrWhiteSpace(correction.QuoteBefore) ? null : string.Format(ChangedRemovedFormat, correction.QuoteBefore.Trim()),
                DraftCheckCodes.Length => report.Constraints.Length?.Kind switch
                {
                    LengthKind.AtMost => ChangedLength,
                    LengthKind.AtLeast => ChangedLengthLonger,
                    _ => ChangedLengthOther,
                },
                DraftCheckCodes.Language => ChangedLanguage,
                DraftCheckCodes.Placeholders => ChangedPlaceholders,
                DraftCheckCodes.Preamble => ChangedPreamble,
                DraftCheckCodes.Lists => ChangedLists,
                DraftCheckCodes.Headings => ChangedHeadings,
                DraftCheckCodes.Emojis => ChangedEmojis,
                DraftCheckCodes.ForbiddenTerm => ChangedForbiddenWords,
                DraftCheckCodes.RequiredTerm => ChangedRequiredWords,
                DraftCheckCodes.Arithmetic => ChangedArithmetic,
                _ => null,
            };
            if (line is not null && !lines.Contains(line, StringComparer.Ordinal))
                lines.Add(line);
        }
        if (report.FactCheck is { State: FactCheckState.VersionNotChecked or FactCheckState.NotCompleted }
            && lines.Any(l => l.StartsWith(ChangedRemovedFormat[..ChangedRemovedFormat.IndexOf('{')], StringComparison.Ordinal)))
            lines.Add(ChangedMayRemain);
        return lines;
    }
}
