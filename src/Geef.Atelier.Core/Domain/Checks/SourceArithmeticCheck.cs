using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Checks;

/// <summary>How a total is written in a file.</summary>
public enum ArithmeticForm { Expression = 0, ListTotal = 1, SentenceTotal = 2, TableTotal = 3 }

/// <summary>Which figure of a total that does not add up the text uses.</summary>
public enum TotalUseInText { Computed = 0, Stated = 1, Neither = 2 }

/// <summary>
/// A total in a file that does not match its items. Numbers keep the spelling of the file. For
/// <see cref="ArithmeticForm.Expression"/> <see cref="Items"/> holds one entry, the left side as written (<c>3 × 24 €</c>);
/// otherwise one entry per item, so that <c>string.Join(" + ", Items)</c> reads as the sum.
/// </summary>
public sealed record SourceArithmeticIssue(string SourceId, string FileName, ArithmeticForm Form, string Quote,
    IReadOnlyList<string> Items, string Stated, string Computed, TotalUseInText UseInText);

/// <summary>
/// How many totals were found and checked in the files and which of them do not add up: at most
/// <see cref="SourceArithmeticCheck.MaxListedIssues"/> in <see cref="Issues"/>, the rest only counted in <see cref="IssuesNotListed"/>.
/// </summary>
public sealed record SourceArithmeticResult(int TotalsFound, IReadOnlyList<SourceArithmeticIssue> Issues, int IssuesNotListed = 0);

/// <summary>
/// Recalculates totals in files and written calculations in a text without an AI model; never blocks a file, never corrects
/// silently. A false alarm is worse than a miss here (the fact check covers totals a second time), so every reading that is
/// not certain is left out: dates, times, years, phone numbers, ranges, percentages, figures with a unit, and numbers whose
/// decimal separator could be read both ways unless the file settles it, and calculations made only of single digits without a
/// unit, which are figures of speech such as "1 + 1 = 3" more often than sums. Items and total must have the same unit, and a
/// total inside a sentence counts only with a currency. A total counts as correct when the stated total only rounds the items.
/// Nothing is said about a total when the reading is not clear: when a run of its last items (at least two) adds up to it (the
/// total may cover only those), when an item line holds more than one figure (a quantity and a price), when words around it
/// speak of deductions, payments made, balances, taxes or amounts per person (<see cref="UnclearWords"/>), or, for a written
/// calculation, when more figures or a plus sign stand right before it in the same sentence. Such totals are neither reported nor
/// counted as checked. At most <see cref="MaxListedIssues"/> totals that do not add up are listed; the rest are only counted.
/// </summary>
public static partial class SourceArithmeticCheck
{
    /// <summary>Words that mark a total in lists, sentences and tables (forms 2–4). German compounds that start with <c>gesamt</c> or end with <c>summe</c> count as well.</summary>
    public static IReadOnlyList<string> TotalWords { get; } = ["summe", "gesamt", "insgesamt", "zusammen", "total", "sum", "altogether", "in total"];

    public const decimal Tolerance = 0.005m;

    /// <summary>Totals that do not add up listed in a result at most; more are counted in <see cref="SourceArithmeticResult.IssuesNotListed"/>.</summary>
    public const int MaxListedIssues = 10;

    /// <summary>
    /// Words (lower case, whole words) that make the meaning of a total unclear: deductions and payments made, income against
    /// spending and balances, taxes, and amounts per person or piece. A total with one of them in its items, in the clause of the
    /// total or in a later clause after the total in the same line or sentence ("…, zusammen 300 € (inkl. MwSt.)") is not checked;
    /// only the words of shares (<see cref="ShareWords"/>) are harmless in such a later clause ("…, zusammen 62 €, also rund 3 € pro
    /// Familie"). Words that end with one of <see cref="UnclearEndings"/> (compounds and plurals such as "Frühbucherrabatt",
    /// "Zuschüsse", "Mehrwertsteuer") and words with "budget" count as well.
    /// </summary>
    public static IReadOnlyCollection<string> UnclearWords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "abzgl", "abzüglich", "abzug", "abzüge", "minus", "rabatt", "skonto", "nachlass", "ermäßigung", "anzahlung", "vorauszahlung",
        "angezahlt", "erstattet", "erstattung", "gutschrift", "zuschuss", "einnahmen", "ausgaben", "saldo",
        "gesamtsaldo", "rest", "restbetrag", "restsumme", "offen", "zuzüglich", "zzgl", "mwst", "ust", "steuer", "steuern",
        "umsatzsteuer", "mehrwertsteuer", "inkl", "inklusive", "enthalten", "davon", "netto", "brutto", "pro", "je", "jeweils",
        "à", "stück", "stk", "less", "discount", "deposit", "refund", "credit", "income", "expenses", "balance",
        "remaining", "tax", "vat", "incl", "including", "excluding", "excl", "net", "gross", "per", "each",
        "abgezogen", "taxes", "deducted", "discounted", "subsidy", "subsidies",
    };

    /// <summary>Endings (lower case) of deduction words: a word ending with one of them makes a total unclear like <see cref="UnclearWords"/>.</summary>
    public static IReadOnlyList<string> UnclearEndings { get; } =
    [
        "rabatt", "rabatte", "abzug", "abzüge", "nachlass", "nachlässe", "ermäßigung", "ermäßigungen", "anzahlung", "anzahlungen",
        "vorauszahlungen", "zuschuss", "zuschüsse", "gutschrift", "gutschriften", "erstattung", "erstattungen", "skonto", "skonti",
        "discount", "discounts", "deposit", "deposits", "refund", "refunds", "credits", "rebate", "rebates",
        "rabatts", "rabattes", "abzugs", "abzuges", "nachlasses", "zuschusses", "steuer", "steuern",
    ];

    /// <summary>Words (lower case) of shares, the only words of <see cref="UnclearWords"/> that are harmless in a later clause after the total.</summary>
    public static IReadOnlyCollection<string> ShareWords { get; } = new HashSet<string>(StringComparer.Ordinal) { "pro", "je", "jeweils", "per", "each" };

    /// <summary>
    /// Words (lower case) of payments already made. Only in the clause of the total itself ("Bereits gezahlt insgesamt: 300 €") do
    /// they make it unclear, because items are often described as paid ("We paid $350 for the room and …, altogether …").
    /// </summary>
    public static IReadOnlyCollection<string> PaidWords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "bezahlt", "gezahlt", "eingezahlt", "überwiesen", "bereits", "schon", "paid", "already",
    };

    private const int MaxQuoteChars = 300;
    private const int MaxIntegerDigits = 12;
    private const int MaxItems = 100;
    private const int MaxSentenceAmounts = 60;
    private const int MaxSentenceTotals = 10;

    private static readonly (string Token, string Unit)[] Currencies =
        [("€", "EUR"), ("EUR", "EUR"), ("Euro", "EUR"), ("euro", "EUR"), ("$", "USD"), ("USD", "USD"), ("CHF", "CHF"), ("£", "GBP")];

    private static readonly HashSet<string> NotAmountWords = new(StringComparer.Ordinal) { "prozent", "percent", "uhr", "pct" };

    private static readonly HashSet<string> Abbreviations = new(StringComparer.Ordinal)
    {
        "zzgl", "abzgl", "inkl", "exkl", "bzw", "usw", "evtl", "ggf", "ca", "nr", "no", "vs", "etc", "approx", "incl", "excl", "rd",
        "tel", "str", "mio", "mrd", "tsd", "max", "min", "gem", "lt", "zb", "bsp", "vgl", "abs", "art", "kap", "fig",
    };

    [GeneratedRegex(@"[0-9]+(?:[.,'’  ][0-9]{3}(?![0-9]))*(?:[.,][0-9]+)?(?:,[-–—](?![0-9\-–—]))?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberCore();

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@",(?=\s|$)|;|\(|\)|\s[–—-]\s|\.(?=\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ClauseMark();

    [GeneratedRegex(@"^\s*(?:[-*•]\s*)?[0-9]{1,3}[.)]\s", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingEnumeration();

    [GeneratedRegex(@"^gesamte[nmrs]?$", RegexOptions.CultureInvariant)]
    private static partial Regex GesamtAdjective();

    [GeneratedRegex(@"^\s*:?-{2,}:?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableSeparatorCell();

    [GeneratedRegex(@"[!?;]\s+|\.\s+(?=[\p{Lu}""„(])", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<word>\p{L}+)\.\s+(?=[0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceEndBeforeDigit();

    /// <summary>Recalculates the totals in <paramref name="sources"/>; <see cref="SourceArithmeticIssue.UseInText"/> stays Neither until <see cref="WithTextUse"/>.</summary>
    public static SourceArithmeticResult Check(IReadOnlyList<(string SourceId, string FileName, string Text)> sources)
    {
        var found = 0;
        var issues = new List<SourceArithmeticIssue>();
        foreach (var (sourceId, fileName, text) in sources ?? [])
        {
            var scan = new Scan(text ?? string.Empty);
            scan.Expressions();
            scan.Tables();
            scan.Lists();
            scan.Sentences();
            found += scan.Found;
            issues.AddRange(scan.Issues.Select(i => new SourceArithmeticIssue(sourceId ?? string.Empty, fileName ?? string.Empty,
                i.Form, i.Quote, i.Items, i.Stated, i.Computed, TotalUseInText.Neither)));
        }
        return new SourceArithmeticResult(found, issues.Take(MaxListedIssues).ToList(), Math.Max(0, issues.Count - MaxListedIssues));
    }

    /// <summary>
    /// Sets <see cref="SourceArithmeticIssue.UseInText"/> from the figures that occur in <paramref name="draft"/>: the computed
    /// total when the text has it, else the stated total when the text has that, else neither. Figures are compared by value and
    /// unit, so <c>72,00 €</c> in the text matches <c>72 €</c> in the file.
    /// </summary>
    public static SourceArithmeticResult WithTextUse(SourceArithmeticResult result, string draft)
    {
        if (result is null)
            return new SourceArithmeticResult(0, []);
        var amounts = SplitLines(draft ?? string.Empty).SelectMany(l => Lex(l.Line, l.Offset)).ToList();
        var issues = result.Issues.Select(issue =>
        {
            var use = Occurs(issue.Computed, amounts) ? TotalUseInText.Computed
                : Occurs(issue.Stated, amounts) ? TotalUseInText.Stated
                : TotalUseInText.Neither;
            return issue with { UseInText = use };
        }).ToList();
        return result with { Issues = issues };
    }

    /// <summary>
    /// True when <paramref name="text"/> holds <paramref name="figure"/> as a whole amount: the same value (the same unit when the
    /// figure has one), so "150 €" does not name "50 €" and "15. Mai" is no amount at all.
    /// </summary>
    public static bool NamesFigure(string? text, string figure)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(figure))
            return false;
        return Occurs(figure, SplitLines(text).SelectMany(l => Lex(l.Line, l.Offset)).ToList());
    }

    /// <summary>
    /// True when <paramref name="text"/> holds at least one amount and every amount in it is one of the amounts written in
    /// <paramref name="figures"/> (same value, same unit when the figure has one).
    /// </summary>
    public static bool NamesOnly(string? text, IEnumerable<string> figures)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var allowed = (figures ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).SelectMany(f => Lex(f, 0)).ToList();
        var amounts = SplitLines(text).SelectMany(l => Lex(l.Line, l.Offset)).ToList();
        return amounts.Count > 0 && amounts.All(a => allowed.Any(t => (t.Unit.Length == 0 || a.Unit == t.Unit) && a.Values().Any(t.Values().Contains)));
    }

    /// <summary>
    /// The words of <paramref name="text"/> (lower case, in order) besides its amounts and the words that mark a total; two passages
    /// with the same such words differ only in their figures and total words.
    /// </summary>
    public static IReadOnlyList<string> WordsBesideFigures(string? text)
    {
        var words = new List<string>();
        foreach (var (offset, line) in SplitLines(text ?? string.Empty))
        {
            var taken = Lex(line, offset).Select(a => (Start: a.Start - offset, End: a.End - offset))
                .Concat(TotalWordSpans(line).Select(t => (t.Start, t.End)))
                .ToList();
            foreach (Match word in WordPattern().Matches(line))
            {
                if (!taken.Any(t => word.Index < t.End && t.Start < word.Index + word.Length))
                    words.Add(word.Value.ToLowerInvariant());
            }
        }
        return words;
    }

    /// <summary>Written calculations (<c>a + b = c</c>) in <paramref name="text"/> whose result is wrong.</summary>
    public static IReadOnlyList<SourceArithmeticIssue> CheckExpressions(string text) => ExpressionsIn(text).Issues;

    /// <summary>
    /// Written calculations in <paramref name="text"/>: how many were recalculated and which of them are clearly wrong. A
    /// calculation whose reading is not clear (more figures or a plus word right before it, only single digits, part of a longer
    /// expression, a decimal separator that could be read both ways) is left out and not counted.
    /// </summary>
    internal static (int Found, IReadOnlyList<SourceArithmeticIssue> Issues) ExpressionsIn(string? text)
    {
        var scan = new Scan(text ?? string.Empty);
        scan.Expressions();
        return (scan.Found, scan.Issues.Select(i => new SourceArithmeticIssue(string.Empty, string.Empty, i.Form, i.Quote, i.Items,
            i.Stated, i.Computed, TotalUseInText.Neither)).ToList());
    }

    private static bool Occurs(string figure, List<Amount> amounts)
    {
        var parsed = Lex(figure, 0);
        if (parsed.Count != 1)
            return false;
        var target = parsed[0];
        var values = target.Values().ToHashSet();
        return amounts.Any(a => (target.Unit.Length == 0 || a.Unit == target.Unit) && a.Values().Any(values.Contains));
    }

    private enum Convention { De = 0, En = 1 }

    private sealed record Amount(int Start, int End, string Text, string Prefix, string Suffix, string Unit, bool Negative,
        decimal? DeValue, int DeDecimals, decimal? EnValue, int EnDecimals, char? GroupChar, bool DashSuffix)
    {
        public bool ValidIn(Convention convention) => (convention == Convention.De ? DeValue : EnValue) is not null;

        public decimal Value(Convention convention) =>
            (Negative ? -1 : 1) * (convention == Convention.De ? DeValue!.Value : EnValue!.Value);

        public int Decimals(Convention convention) => convention == Convention.De ? DeDecimals : EnDecimals;

        public bool Ambiguous => DeValue is not null && EnValue is not null && DeValue != EnValue;

        public IEnumerable<decimal> Values()
        {
            if (DeValue is not null)
                yield return Value(Convention.De);
            if (EnValue is not null)
                yield return Value(Convention.En);
        }
    }

    private sealed record Discrepancy(ArithmeticForm Form, string Quote, IReadOnlyList<string> Items, string Stated, string Computed);

    private sealed record LineInfo(int Offset, string Line, List<Amount> Amounts);

    private sealed class Scan
    {
        private readonly List<LineInfo> _lines;
        private readonly HashSet<int> _usedTotals = [];
        private readonly Dictionary<int, Amount> _expressionResults = [];
        private readonly HashSet<int> _tableLines = [];
        private readonly Convention? _evidence;

        public Scan(string text)
        {
            _lines = SplitLines(text).Select(l => new LineInfo(l.Offset, l.Line, Lex(l.Line, l.Offset))).ToList();
            var all = _lines.SelectMany(l => l.Amounts).ToList();
            var deOnly = all.Count(a => a.DeValue is not null && a.EnValue is null);
            var enOnly = all.Count(a => a.EnValue is not null && a.DeValue is null);
            _evidence = deOnly > enOnly ? Convention.De : enOnly > deOnly ? Convention.En : null;
        }

        public int Found { get; private set; }

        public List<Discrepancy> Issues { get; } = [];

        public void Expressions()
        {
            for (var index = 0; index < _lines.Count; index++)
            {
                var info = _lines[index];
                var amounts = info.Amounts;
                var resultsOnLine = new List<Amount>();
                for (var k = 1; k < amounts.Count; k++)
                {
                    if (Gap(info, amounts[k - 1], amounts[k]).Trim() != "=")
                        continue;
                    var rhs = amounts[k];
                    var operands = new List<Amount> { amounts[k - 1] };
                    var ops = new List<char>();
                    var j = k - 1;
                    while (j > 0 && Operator(Gap(info, amounts[j - 1], amounts[j])) is { } op)
                    {
                        operands.Insert(0, amounts[j - 1]);
                        ops.Insert(0, op);
                        j--;
                    }
                    if (ops.Count == 0)
                        continue;
                    if (EndsWithOperator(info.Line[..(operands[0].Start - info.Offset)]) || StartsWithOperator(info.Line[(rhs.End - info.Offset)..]))
                        continue;

                    resultsOnLine.Add(rhs);
                    if (MoreBefore(info, operands[0], rhs, resultsOnLine.Count > 1 ? resultsOnLine[^2] : null) || operands.Append(rhs).All(IsDigitFigure))
                        continue;
                    if (!_usedTotals.Add(rhs.Start))
                        continue;
                    var outcome = Evaluate(operands, ops, rhs);
                    if (outcome is null)
                        continue;
                    Found++;
                    if (outcome is { Ok: false } bad)
                    {
                        var left = info.Line[(operands[0].Start - info.Offset)..(operands[^1].End - info.Offset)].Trim();
                        var quote = info.Line[(operands[0].Start - info.Offset)..(rhs.End - info.Offset)].Trim();
                        Issues.Add(new Discrepancy(ArithmeticForm.Expression, Cap(quote), [left], rhs.Text, bad.Computed));
                    }
                }
                if (resultsOnLine.Count == 1)
                    _expressionResults[index] = resultsOnLine[0];
            }
        }

        public void Tables()
        {
            var index = 0;
            while (index < _lines.Count)
            {
                if (!IsTableLine(index))
                {
                    index++;
                    continue;
                }
                var start = index;
                while (index < _lines.Count && IsTableLine(index))
                    _tableLines.Add(index++);
                CheckTable(start, index);
            }
        }

        public void Lists()
        {
            for (var index = 0; index < _lines.Count; index++)
            {
                if (_tableLines.Contains(index) || !HasTotalWord(_lines[index].Line) || TotalLineAmount(index) is not { } stated)
                    continue;
                var items = new List<Amount>();
                string? unit = null;
                var first = index;
                var mixed = false;
                for (var j = index - 1; j >= 0; j--)
                {
                    if (_tableLines.Contains(j) || _lines[j].Line.Trim().Length == 0 || HasTotalWord(_lines[j].Line))
                        break;
                    if (LineAmount(j) is not { } item || unit is not null && item.Unit != unit || items.Count > MaxItems)
                        break;
                    if (!_expressionResults.ContainsKey(j) && MixedItem(j, item))
                    {
                        mixed = true;
                        break;
                    }
                    unit ??= item.Unit;
                    items.Insert(0, item);
                    first = j;
                }
                if (mixed || items.Count < 2 || items.Count > MaxItems || !UnitsFit(unit!, stated.Unit))
                    continue;
                var line = _lines[index].Line;
                var clauseEnd = ClauseEnd(line, stated.End - _lines[index].Offset);
                var totalClause = line[..clauseEnd];
                var context = string.Join('\n', _lines.Skip(first).Take(index - first).Select(l => l.Line).Append(totalClause));
                CheckTotal(ArithmeticForm.ListTotal, items, stated, line.Trim(), context, totalClause, line[clauseEnd..]);
            }
        }

        public void Sentences()
        {
            for (var index = 0; index < _lines.Count; index++)
            {
                if (_tableLines.Contains(index))
                    continue;
                var info = _lines[index];
                var next = 0;
                var position = 0;
                foreach (var end in SentenceEnds(info.Line))
                {
                    var amounts = new List<Amount>();
                    while (next < info.Amounts.Count && info.Amounts[next].Start - info.Offset < end)
                    {
                        var amount = info.Amounts[next++];
                        if (amount.Start - info.Offset >= position && amount.End - info.Offset <= end)
                            amounts.Add(amount);
                    }
                    if (amounts.Count is >= 3 and <= MaxSentenceAmounts)
                        CheckSentence(info, position, end, amounts);
                    position = end;
                }
            }
        }

        private static IEnumerable<int> SentenceEnds(string line)
        {
            foreach (Match match in SentenceEnd().Matches(line))
                yield return match.Index + match.Length;
            yield return line.Length;
        }

        /// <summary>
        /// Ends of sentences followed by a figure ("Wir erwarten 30 Gäste. 3 × 24 € …"): a full stop after a word of at least two
        /// letters that is no abbreviation; never after a digit, so ordinals and dates ("am 15. 3.") do not end a sentence.
        /// </summary>
        private static IEnumerable<int> SentenceEndsBeforeDigits(string line)
        {
            foreach (Match match in SentenceEndBeforeDigit().Matches(line))
            {
                var word = match.Groups["word"].Value.ToLowerInvariant();
                if (word.Length >= 2 && !Abbreviations.Contains(word))
                    yield return match.Index + match.Length;
            }
        }

        private void CheckSentence(LineInfo info, int from, int to, List<Amount> amounts)
        {
            var sentence = info.Line[from..to];
            var previousTotalEnd = from;
            foreach (var (wordStart, wordEnd, postfix) in TotalWordSpans(sentence).Take(MaxSentenceTotals))
            {
                var absoluteStart = info.Offset + from + wordStart;
                var absoluteEnd = info.Offset + from + wordEnd;
                var after = amounts.Where(a => a.Start >= absoluteEnd).ToList();
                if (after.Count > 0)
                {
                    var clauseEnd = info.Offset + from + ClauseEnd(sentence, after[0].End - info.Offset - from);
                    after = after.Where(a => a.End <= clauseEnd).ToList();
                }
                var before = amounts.Where(a => a.End <= absoluteStart && a.Start >= info.Offset + previousTotalEnd).ToList();
                Amount? stated = null;
                var afterPreferred = Preferred(after);
                if (afterPreferred.Count == 1)
                {
                    stated = afterPreferred[0];
                }
                else if (after.Count == 0 && postfix && before.Count > 0 && absoluteStart - before[^1].End <= 3)
                {
                    stated = before[^1];
                    before.RemoveAt(before.Count - 1);
                }
                if (stated is null)
                    continue;
                var calculation = info.Line[previousTotalEnd..(wordStart + from)].Contains('=');
                previousTotalEnd = stated.End - info.Offset;
                if (calculation || stated.Unit.Length == 0 || before.Count < 2 || before.Any(a => a.Unit != stated.Unit))
                    continue;
                var statedStart = stated.Start - info.Offset - from;
                var statedEnd = stated.End - info.Offset - from;
                var end = ClauseEnd(sentence, Math.Max(wordEnd, statedEnd));
                var phrase = sentence[ClauseStart(sentence, Math.Min(wordStart, statedStart))..end];
                CheckTotal(ArithmeticForm.SentenceTotal, before, stated, sentence.Trim(), sentence[..end], phrase, sentence[end..]);
            }
        }

        private void CheckTable(int start, int end)
        {
            var rows = new List<(int Line, List<(int From, int To, string Text)> Cells)>();
            for (var index = start; index < end; index++)
                rows.Add((index, Cells(_lines[index].Line)));

            var separator = rows.FindIndex(r => r.Cells.Count > 0 && r.Cells.All(c => TableSeparatorCell().IsMatch(c.Text)));
            var dataStart = separator >= 0 ? separator + 1 : 0;
            var previousTotal = dataStart - 1;
            for (var r = dataStart; r < rows.Count; r++)
            {
                var firstFilled = rows[r].Cells.FindIndex(c => c.Text.Trim().Length > 0);
                if (firstFilled < 0 || !HasTotalWord(rows[r].Cells[firstFilled].Text) || CellAmounts(rows[r], firstFilled).Count > 0)
                    continue;
                for (var column = firstFilled + 1; column < rows[r].Cells.Count; column++)
                {
                    if (CellAmount(rows[r], column) is not { } stated)
                        continue;
                    var items = new List<Amount>();
                    var checkable = true;
                    for (var rr = previousTotal + 1; rr < r && checkable; rr++)
                    {
                        if (column >= rows[rr].Cells.Count)
                            continue;
                        var text = rows[rr].Cells[column].Text.Trim();
                        var amounts = CellAmounts(rows[rr], column);
                        if (amounts.Count == 0)
                        {
                            checkable = text.Trim('-', '–', '—', ' ').Length == 0;
                            continue;
                        }
                        if (CellAmount(rows[rr], column) is { } item)
                            items.Add(item);
                        else
                            checkable = false;
                    }
                    if (!checkable || items.Count < 2 || items.Count > MaxItems || items.Select(i => i.Unit).Distinct().Count() != 1 || !UnitsFit(items[0].Unit, stated.Unit))
                        continue;
                    var context = string.Join('\n', rows.Take(r + 1).Select(row => _lines[row.Line].Line));
                    CheckTotal(ArithmeticForm.TableTotal, items, stated, _lines[rows[r].Line].Line.Trim(), context, rows[r].Cells[firstFilled].Text);
                }
                previousTotal = r;
            }
        }

        private List<Amount> CellAmounts((int Line, List<(int From, int To, string Text)> Cells) row, int column)
        {
            var info = _lines[row.Line];
            var cell = row.Cells[column];
            return info.Amounts.Where(a => a.Start - info.Offset >= cell.From && a.End - info.Offset <= cell.To).ToList();
        }

        private Amount? CellAmount((int Line, List<(int From, int To, string Text)> Cells) row, int column)
        {
            var preferred = Preferred(CellAmounts(row, column));
            return preferred.Count == 1 ? preferred[0] : null;
        }

        private static List<(int From, int To, string Text)> Cells(string line)
        {
            var cells = new List<(int, int, string)>();
            var first = line.IndexOf('|');
            var last = line.TrimEnd().LastIndexOf('|');
            if (first < 0)
                return cells;
            var position = first + 1;
            var stop = last > first ? last : line.Length;
            while (position <= stop)
            {
                var next = line.IndexOf('|', position);
                if (next < 0 || next > stop)
                    next = stop;
                cells.Add((position, next, line[position..next]));
                position = next + 1;
            }
            return cells;
        }

        private bool IsTableLine(int index) => _lines[index].Line.TrimStart().StartsWith('|');

        private Amount? LineAmount(int index)
        {
            if (_expressionResults.TryGetValue(index, out var result))
                return result;
            var preferred = Preferred(_lines[index].Amounts);
            return preferred.Count == 1 ? preferred[0] : null;
        }

        /// <summary>
        /// The total of a list's total line: its only amount, or, when the line holds more, the only one in the clause of the total
        /// word ("zusammen 62 €, also rund 3 € pro Familie" → 62 €).
        /// </summary>
        private Amount? TotalLineAmount(int index)
        {
            if (LineAmount(index) is { } single)
                return single;
            var info = _lines[index];
            foreach (var (wordStart, wordEnd, _) in TotalWordSpans(info.Line))
            {
                var after = info.Amounts.Where(a => a.Start - info.Offset >= wordEnd).ToList();
                if (after.Count == 0)
                    continue;
                var clauseEnd = ClauseEnd(info.Line, after[0].End - info.Offset);
                var preferred = Preferred(after.Where(a => a.End - info.Offset <= clauseEnd).ToList());
                return preferred.Count == 1 ? preferred[0] : null;
            }
            return null;
        }

        private void CheckTotal(ArithmeticForm form, List<Amount> items, Amount stated, string quote, string context, string phrase, string after = "")
        {
            if (!_usedTotals.Add(stated.Start) || HasUnclearWord(context) || UnclearPhrase(phrase, stated) || UnclearAfter(after))
                return;
            var plus = Enumerable.Repeat('+', items.Count - 1).ToList();
            var outcome = Evaluate(items, plus, stated);
            if (outcome is null)
                return;
            if (outcome.Ok)
            {
                Found++;
                return;
            }
            for (var length = items.Count - 1; length >= 2; length--)
            {
                if (Evaluate(items[^length..], Enumerable.Repeat('+', length - 1).ToList(), stated) is { Ok: true })
                    return;
            }
            Found++;
            Issues.Add(new Discrepancy(form, Cap(quote), items.Select(i => i.Text).ToList(), stated.Text, outcome.Computed));
        }

        private bool MixedItem(int index, Amount item)
        {
            var info = _lines[index];
            var amounts = info.Amounts.Where((a, i) => i > 0 || !IsEnumeration(info, a)).ToList();
            if (item.Unit.Length == 0)
                return amounts.Count > 1;
            return amounts.Count(a => a.Unit == item.Unit) > 1
                || amounts.Any(a => a.Unit.Length == 0) && HasQuantityMarker(info.Line);
        }

        private static bool HasQuantityMarker(string line) =>
            line.IndexOfAny(['×', '@', '*']) >= 0
            || WordPattern().Matches(line).Any(w => w.Value.ToLowerInvariant() is "x" or "à" or "a" or "zu" or "je" or "pro" or "stück" or "stk" or "mal" or "each" or "per" or "at");

        private static bool IsEnumeration(LineInfo info, Amount amount)
        {
            var start = amount.Start - info.Offset;
            var end = amount.End - info.Offset;
            return amount.Unit.Length == 0 && info.Line[..start].Trim().Length == 0
                && end < info.Line.Length && info.Line[end] is '.' or ')';
        }

        /// <summary>
        /// True when, in the same sentence before the calculation, an amount with the unit of its result or a plus sign or plus word
        /// stands: the calculation may then be only the end of a longer one. A bare number such as "25 Gäste" is no such amount.
        /// </summary>
        private static bool MoreBefore(LineInfo info, Amount firstOperand, Amount result, Amount? previousResult)
        {
            var from = previousResult is null ? 0 : previousResult.End - info.Offset;
            var to = firstOperand.Start - info.Offset;
            foreach (var end in SentenceEnds(info.Line).Concat(SentenceEndsBeforeDigits(info.Line)))
            {
                if (end <= to && end > from)
                    from = end;
            }
            if (from >= to)
                return false;
            if (result.Unit.Length > 0 && info.Amounts.Any(a => a.Unit == result.Unit && a.Start - info.Offset >= from && a.End - info.Offset <= to))
                return true;
            var before = info.Line[from..to];
            return before.IndexOfAny(['+', '−', '×', '*', '·']) >= 0
                || WordPattern().Matches(before).Any(w => w.Value.ToLowerInvariant() is "plus" or "minus" or "zzgl" or "zuzüglich" or "abzgl" or "abzüglich");
        }

        private sealed record Outcome(bool Ok, string Computed);

        private Outcome? Evaluate(IReadOnlyList<Amount> operands, IReadOnlyList<char> ops, Amount stated)
        {
            var candidates = new[] { Convention.De, Convention.En }
                .Where(c => stated.ValidIn(c) && operands.All(o => o.ValidIn(c)))
                .ToList();
            if (candidates.Count == 0)
                return null;

            var results = new List<(Convention Convention, decimal Precedence, decimal LeftToRight)>();
            foreach (var convention in candidates)
            {
                try
                {
                    var values = operands.Select(o => o.Value(convention)).ToList();
                    var (precedence, leftToRight) = (WithPrecedence(values, ops), LeftToRight(values, ops));
                    var tolerance = ToleranceFor(operands, stated, convention);
                    var target = stated.Value(convention);
                    if (Math.Abs(precedence - target) <= tolerance || Math.Abs(leftToRight - target) <= tolerance)
                        return new Outcome(true, string.Empty);
                    results.Add((convention, precedence, leftToRight));
                }
                catch (OverflowException)
                {
                    return null;
                }
            }

            Convention chosen;
            if (results.Count == 1)
                chosen = results[0].Convention;
            else if (!stated.Ambiguous && operands.All(o => !o.Ambiguous))
                chosen = _evidence ?? Convention.De;
            else if (_evidence is { } evidence)
                chosen = evidence;
            else
                return new Outcome(true, string.Empty);

            var computed = results.First(r => r.Convention == chosen).Precedence;
            var decimals = Math.Max(stated.Decimals(chosen), operands.Max(o => o.Decimals(chosen)));
            var group = stated.GroupChar ?? operands.Select(o => o.GroupChar).FirstOrDefault(g => g is not null);
            return new Outcome(false, Format(computed, chosen, decimals, group, stated.Prefix, stated.Suffix, stated.DashSuffix));
        }

        private static decimal ToleranceFor(IReadOnlyList<Amount> operands, Amount stated, Convention convention)
        {
            var statedDecimals = stated.Decimals(convention);
            var operandDecimals = operands.Max(o => o.Decimals(convention));
            if (operandDecimals <= statedDecimals)
                return Tolerance;
            var half = 0.5m;
            for (var i = 0; i < statedDecimals; i++)
                half /= 10;
            return Math.Max(Tolerance, half);
        }

        private string Gap(LineInfo info, Amount left, Amount right) =>
            info.Line[(left.End - info.Offset)..(right.Start - info.Offset)];
    }

    private static bool IsDigitFigure(Amount amount) =>
        amount.Unit.Length == 0 && amount.DeValue is { } value && value < 10 && amount.DeDecimals == 0 && amount.EnValue == value;

    private static decimal WithPrecedence(List<decimal> values, IReadOnlyList<char> ops)
    {
        var terms = new List<decimal> { values[0] };
        var signs = new List<int> { 1 };
        for (var i = 0; i < ops.Count; i++)
        {
            var value = values[i + 1];
            switch (ops[i])
            {
                case '*':
                    terms[^1] *= value;
                    break;
                case '-':
                    terms.Add(value);
                    signs.Add(-1);
                    break;
                default:
                    terms.Add(value);
                    signs.Add(1);
                    break;
            }
        }
        var sum = 0m;
        for (var i = 0; i < terms.Count; i++)
            sum += signs[i] * terms[i];
        return sum;
    }

    private static decimal LeftToRight(List<decimal> values, IReadOnlyList<char> ops)
    {
        var result = values[0];
        for (var i = 0; i < ops.Count; i++)
        {
            result = ops[i] switch
            {
                '*' => result * values[i + 1],
                '-' => result - values[i + 1],
                _ => result + values[i + 1],
            };
        }
        return result;
    }

    private static char? Operator(string gap)
    {
        var trimmed = gap.Trim();
        switch (trimmed)
        {
            case "+":
                return '+';
            case "−":
                return '-';
            case "×" or "*" or "·" or "x" or "X":
                return '*';
            case "-" or "–" when gap.Length >= 3 && char.IsWhiteSpace(gap[0]) && char.IsWhiteSpace(gap[^1]):
                return '-';
            default:
                return null;
        }
    }

    private static bool EndsWithOperator(string before)
    {
        var trimmed = before.TrimEnd();
        if (trimmed.TrimStart() is "-" or "–" or "*" or "•")
            return false;
        return trimmed.Length > 0 && trimmed[^1] is '+' or '−' or '×' or '*' or '·' or '-' or '–' or '(' or '/' or '=';
    }

    private static bool StartsWithOperator(string after)
    {
        var trimmed = after.TrimStart();
        return trimmed.Length > 0 && trimmed[0] is '+' or '−' or '×' or '*' or '·' or '-' or '–' or ')' or '/' or '=';
    }

    private static List<Amount> Preferred(List<Amount> amounts)
    {
        var withUnit = amounts.Where(a => a.Unit.Length > 0).ToList();
        return withUnit.Count > 0 ? withUnit : amounts;
    }

    private static bool UnitsFit(string items, string stated) => items == stated;

    private static bool HasUnclearWord(string text) =>
        WordPattern().Matches(text).Any(w => IsUnclear(w.Value.ToLowerInvariant()));

    private static bool UnclearAfter(string text) =>
        WordPattern().Matches(text).Select(w => w.Value.ToLowerInvariant()).Any(w => !ShareWords.Contains(w) && IsUnclear(w));

    private static bool IsUnclear(string word) =>
        UnclearWords.Contains(word)
        || word.Contains("budget", StringComparison.Ordinal)
        || UnclearEndings.Any(e => word.EndsWith(e, StringComparison.Ordinal));

    /// <summary>
    /// True when the clause of the total itself speaks of payments made (<see cref="PaidWords"/>) or names a bare number besides
    /// the total ("Gesamt für 20 Kinder: 300 €"): the total may then be a share, a payment or a multiple of the items.
    /// </summary>
    private static bool UnclearPhrase(string phrase, Amount stated)
    {
        if (WordPattern().Matches(phrase).Any(w => PaidWords.Contains(w.Value.ToLowerInvariant())))
            return true;
        var text = LeadingEnumeration().Replace(phrase, string.Empty);
        var bare = Lex(text, 0).Count(a => a.Unit.Length == 0);
        return bare > (stated.Unit.Length == 0 ? 1 : 0);
    }

    /// <summary>Start of the clause that holds <paramref name="position"/>: just after the last clause mark before it, else 0.</summary>
    private static int ClauseStart(string text, int position)
    {
        var start = 0;
        foreach (Match match in ClauseMark().Matches(text))
        {
            if (match.Index + match.Length > position)
                break;
            start = match.Index + match.Length;
        }
        return Math.Min(start, Math.Max(0, position));
    }

    /// <summary>End of the clause that holds <paramref name="position"/>: the first clause mark at or after it, else the end of the text.</summary>
    private static int ClauseEnd(string text, int position)
    {
        var from = Math.Clamp(position, 0, text.Length);
        var match = ClauseMark().Match(text, from);
        return match.Success ? match.Index : text.Length;
    }

    private static bool HasTotalWord(string text) => TotalWordSpans(text).Any();

    private static IEnumerable<(int Start, int End, bool Postfix)> TotalWordSpans(string text)
    {
        var words = WordPattern().Matches(text);
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i].Value.ToLowerInvariant();
            var next = i + 1 < words.Count ? words[i + 1].Value.ToLowerInvariant() : null;
            if (word == "in" && next == "total" && text[(words[i].Index + 2)..words[i + 1].Index].Trim().Length == 0)
            {
                yield return (words[i].Index, words[i + 1].Index + words[i + 1].Length, true);
                i++;
                continue;
            }
            if (word == "zusammen" && next == "mit")
                continue;
            if (IsTotalWord(word))
                yield return (words[i].Index, words[i].Index + words[i].Length, word is "insgesamt" or "zusammen" or "gesamt" or "altogether" or "total");
        }
    }

    private static bool IsTotalWord(string word) =>
        word is "summe" or "gesamt" or "insgesamt" or "zusammen" or "total" or "sum" or "altogether"
        || word.StartsWith("gesamt", StringComparison.Ordinal) && !GesamtAdjective().IsMatch(word)
        || word.Length > 5 && word.EndsWith("summe", StringComparison.Ordinal);

    private static IEnumerable<(int Offset, string Line)> SplitLines(string text)
    {
        var position = 0;
        while (position <= text.Length)
        {
            var end = text.IndexOf('\n', position);
            if (end < 0)
                end = text.Length;
            var line = text[position..end];
            if (line.EndsWith('\r'))
                line = line[..^1];
            yield return (position, line);
            position = end + 1;
        }
    }

    private static List<Amount> Lex(string line, int offset)
    {
        var amounts = new List<Amount>();
        foreach (Match match in NumberCore().Matches(line))
        {
            var amount = Read(line, match.Index, match.Index + match.Length, offset);
            if (amount is not null)
                amounts.Add(amount);
        }
        return amounts;
    }

    private static Amount? Read(string line, int coreStart, int coreEnd, int offset)
    {
        var core = line[coreStart..coreEnd];
        if (core.Length > 1 && core[0] == '0' && char.IsAsciiDigit(core[1]))
            return null;
        var dash = core.Length > 2 && core[^2] == ',' && core[^1] is '-' or '–' or '—';
        var integerDigits = 0;
        foreach (var c in core)
        {
            if (char.IsAsciiDigit(c))
                integerDigits++;
            else if (c is '.' or ',')
                break;
        }
        if (integerDigits > MaxIntegerDigits + 3)
            return null;

        var spanStart = coreStart;
        var negative = false;
        var unit = string.Empty;
        if (IsSignAt(line, spanStart - 1))
        {
            negative = true;
            spanStart--;
        }
        if (CurrencyBefore(line, spanStart) is { } prefixCurrency)
        {
            unit = prefixCurrency.Unit;
            spanStart = prefixCurrency.Start;
            if (!negative && IsSignAt(line, spanStart - 1))
            {
                negative = true;
                spanStart--;
            }
        }

        var previous = spanStart > 0 ? line[spanStart - 1] : '\0';
        var beforePrevious = spanStart > 1 ? line[spanStart - 2] : '\0';
        var timesBefore = previous is 'x' or 'X' && char.IsAsciiDigit(beforePrevious);
        if (char.IsLetterOrDigit(previous) && !timesBefore || previous is '_' or '/' or '\\' or '#' or '@' or '§' or '-' or '–' or '−' or '^')
            return null;
        if (previous is '.' or ',' && char.IsLetterOrDigit(beforePrevious))
            return null;
        if (previous == ':' && char.IsDigit(beforePrevious))
            return null;
        if (previous == '+' && !char.IsDigit(beforePrevious))
            return null;
        if (previous is ' ' or ' ' && char.IsAsciiDigit(beforePrevious))
            return null;

        var next = coreEnd < line.Length ? line[coreEnd] : '\0';
        var afterNext = coreEnd + 1 < line.Length ? line[coreEnd + 1] : '\0';
        if (next is ' ' or ' ' && char.IsAsciiDigit(afterNext))
            return null;
        if (char.IsNumber(next) || next is '%' or '‰' or '°' or '²' or '³')
            return null;
        if (next is ':' or '/' or '.' or ',' or '-' or '–' && char.IsAsciiDigit(afterNext))
            return null;

        var spanEnd = coreEnd;
        if (char.IsLetter(next))
        {
            if (unit.Length == 0 && CurrencyAt(line, coreEnd) is { } attached)
            {
                unit = attached.Unit;
                spanEnd = attached.End;
            }
            else if (!(next is 'x' or 'X' && !char.IsLetter(afterNext)))
            {
                return null;
            }
        }
        else if (unit.Length == 0 && next is '€' or '$' or '£' && CurrencyAt(line, coreEnd) is { } symbol)
        {
            unit = symbol.Unit;
            spanEnd = symbol.End;
        }
        else if (unit.Length == 0 && next is ' ' or ' ' or ' ' && CurrencyAt(line, coreEnd + 1) is { } spaced)
        {
            unit = spaced.Unit;
            spanEnd = spaced.End;
        }

        if (unit.Length == 0 && FollowedByNonAmountWord(line, coreEnd))
            return null;
        if (unit.Length == 0 && next == '.' && core.Contains('.'))
            return null;

        var currency = unit.Length > 0;
        var de = Parse(core, Convention.De, currency);
        var en = Parse(core, Convention.En, currency);
        if (de.Value is null && en.Value is null)
            return null;
        if (!currency && (IsYear(core) || IsDateLike(core)))
            return null;

        var prefix = line[spanStart..coreStart].Replace("-", string.Empty).Replace("−", string.Empty);
        var suffix = line[coreEnd..spanEnd];
        return new Amount(offset + spanStart, offset + spanEnd, line[spanStart..spanEnd], prefix, suffix, unit, negative,
            de.Value, de.Decimals, en.Value, en.Decimals, de.Group ?? en.Group, dash);
    }

    private static bool IsSignAt(string line, int index)
    {
        if (index < 0 || index >= line.Length || line[index] is not ('-' or '−'))
            return false;
        return index == 0 || char.IsWhiteSpace(line[index - 1]) || line[index - 1] is '(' or ':' or '=' or '[';
    }

    private static bool FollowedByNonAmountWord(string line, int coreEnd)
    {
        var position = coreEnd;
        if (position < line.Length && line[position] is ' ' or ' ')
            position++;
        if (position < line.Length && line[position] == '%')
            return true;
        var end = position;
        while (end < line.Length && char.IsLetter(line[end]))
            end++;
        return end > position && NotAmountWords.Contains(line[position..end].ToLowerInvariant());
    }

    private static (int Start, string Unit)? CurrencyBefore(string line, int coreStart)
    {
        foreach (var end in new[] { coreStart, coreStart - 1 })
        {
            if (end < 1 || end != coreStart && line[end] is not (' ' or ' ' or ' '))
                continue;
            foreach (var (token, unit) in Currencies)
            {
                var start = end - token.Length;
                if (start < 0 || string.CompareOrdinal(line, start, token, 0, token.Length) != 0)
                    continue;
                if (char.IsLetter(token[0]) && start > 0 && char.IsLetter(line[start - 1]))
                    continue;
                return (start, unit);
            }
        }
        return null;
    }

    private static (int End, string Unit)? CurrencyAt(string line, int start)
    {
        foreach (var (token, unit) in Currencies)
        {
            if (start + token.Length > line.Length || string.CompareOrdinal(line, start, token, 0, token.Length) != 0)
                continue;
            var end = start + token.Length;
            if (char.IsLetter(token[^1]) && end < line.Length && char.IsLetter(line[end]))
                continue;
            return (end, unit);
        }
        return null;
    }

    private static (decimal? Value, int Decimals, char? Group) Parse(string core, Convention convention, bool currency)
    {
        var text = core;
        if (text.Length > 2 && text[^2] == ',' && text[^1] is '-' or '–' or '—')
        {
            if (convention == Convention.En)
                return (null, 0, null);
            text = text[..^2];
        }

        var decimalChar = convention == Convention.De ? ',' : '.';
        var lastSeparator = -1;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                lastSeparator = i;
                break;
            }
        }

        var integerPart = text;
        var fraction = string.Empty;
        if (lastSeparator >= 0 && text[lastSeparator] == decimalChar)
        {
            integerPart = text[..lastSeparator];
            fraction = text[(lastSeparator + 1)..];
            if (fraction.Length == 0 || currency && fraction.Length > 2)
                return (null, 0, null);
        }

        char? group = null;
        var groups = new List<string>();
        var current = new StringBuilder();
        foreach (var c in integerPart)
        {
            if (char.IsAsciiDigit(c))
            {
                current.Append(c);
                continue;
            }
            if (!IsGroupChar(c, convention) || group is not null && group != c)
                return (null, 0, null);
            group = c;
            groups.Add(current.ToString());
            current.Clear();
        }
        groups.Add(current.ToString());
        if (groups.Any(g => g.Length == 0) || groups.Count > 1 && (groups[0].Length > 3 || groups.Skip(1).Any(g => g.Length != 3)))
            return (null, 0, null);
        var digits = string.Concat(groups);
        if (digits.Length > MaxIntegerDigits)
            return (null, 0, null);

        var number = fraction.Length > 0 ? digits + "." + fraction : digits;
        return decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? (value, fraction.Length, group)
            : (null, 0, null);
    }

    private static bool IsGroupChar(char c, Convention convention) =>
        c is '\'' or '’' or ' ' or ' ' || (convention == Convention.De ? c == '.' : c == ',');

    private static bool IsYear(string core) =>
        core.Length == 4 && core.All(char.IsAsciiDigit) && int.Parse(core, CultureInfo.InvariantCulture) is >= 1900 and <= 2099;

    private static bool IsDateLike(string core)
    {
        var parts = core.Split('.');
        return parts.Length == 2 && parts[0].Length is 1 or 2 && parts[1].Length is 1 or 2
            && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var day) && day is >= 1 and <= 31
            && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var month) && month is >= 1 and <= 12;
    }

    private static string Format(decimal value, Convention convention, int decimals, char? group, string prefix, string suffix, bool dash)
    {
        var rounded = Math.Round(Math.Abs(value), decimals, MidpointRounding.AwayFromZero);
        var invariant = rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
        var parts = invariant.Split('.');
        var integer = parts[0];
        if (group is { } g && integer.Length > 3)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < integer.Length; i++)
            {
                if (i > 0 && (integer.Length - i) % 3 == 0)
                    builder.Append(g);
                builder.Append(integer[i]);
            }
            integer = builder.ToString();
        }
        var decimalChar = convention == Convention.De ? ',' : '.';
        var number = parts.Length > 1 ? integer + decimalChar + parts[1] : dash ? integer + ",-" : integer;
        var sign = value < 0 && rounded != 0 ? "-" : string.Empty;
        return sign + prefix + number + suffix;
    }

    private static string Cap(string quote) => quote.Length <= MaxQuoteChars ? quote : quote[..(MaxQuoteChars - 1)].TrimEnd() + "…";
}
