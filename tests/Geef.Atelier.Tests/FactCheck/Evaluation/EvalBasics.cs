using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Tools.FactCheckEval;

/// <summary>Outcome of one acceptance gate.</summary>
public enum GateVerdict { Passed, Failed, NotEvaluable }

/// <summary>The verdict of gate <see cref="Gate"/> for one part of the data (<see cref="Scope"/>), with the figures behind it.</summary>
public sealed record GateResult(string Gate, string Scope, GateVerdict Verdict, string Detail)
{
    /// <summary>One line for the console: gate, part, verdict and figures.</summary>
    public override string ToString() => $"{Gate} [{Scope}] {Verdict switch
    {
        GateVerdict.Passed => "passed",
        GateVerdict.Failed => "FAILED",
        _ => "NOT EVALUABLE",
    }}: {Detail}";

    /// <summary>A gate that is passed or failed when <paramref name="evaluable"/>, otherwise not evaluable.</summary>
    public static GateResult Of(string gate, string scope, bool evaluable, bool passed, string detail) =>
        new(gate, scope, !evaluable ? GateVerdict.NotEvaluable : passed ? GateVerdict.Passed : GateVerdict.Failed, detail);

    /// <summary>A gate that cannot be judged for <paramref name="reason"/>.</summary>
    public static GateResult NotEvaluable(string gate, string scope, string reason) => new(gate, scope, GateVerdict.NotEvaluable, reason);
}

/// <summary>Development / hold-out split, input fingerprints and percentiles shared by every measurement of the tool.</summary>
public static class EvalBasics
{
    /// <summary>Name of the development set.</summary>
    public const string Dev = "dev";

    /// <summary>Name of the held-out set.</summary>
    public const string Holdout = "holdout";

    /// <summary>Development set: first byte of the SHA-256 of <paramref name="text"/> modulo 10 below 7 (same rule as <c>LengthCorpusTests</c>).</summary>
    public static bool InDevelopmentSet(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text))[0] % 10 < 7;

    /// <summary><see cref="Dev"/> or <see cref="Holdout"/> for <paramref name="text"/>.</summary>
    public static string SplitOf(string text) => InDevelopmentSet(text) ? Dev : Holdout;

    /// <summary>Lower-case hex SHA-256 over <paramref name="parts"/> joined by a zero character.</summary>
    public static string Fingerprint(params string?[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', parts.Select(p => p ?? string.Empty)))));

    /// <summary>Linear interpolated percentile of an ascending list; 0 for an empty list.</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
            return 0;
        var rank = p * (sorted.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }

    /// <summary>A list of ids for a message: the first five, then an ellipsis.</summary>
    public static string Few(IEnumerable<string> ids)
    {
        var list = ids.ToList();
        return string.Join(", ", list.Take(5)) + (list.Count > 5 ? ", …" : string.Empty);
    }
}

/// <summary>
/// Reads an amount as written in a German or English file: thousands separators, decimal comma or point, currency signs and
/// words around it. Same rules as <c>tools/Eval/geef_eval/factcheck_corpus.py</c> (<c>parse_number</c>).
/// </summary>
public static partial class Amounts
{
    [GeneratedRegex(@"-?\d[\d.,']*")]
    private static partial Regex NumberToken();

    [GeneratedRegex(@"^\d{1,3}([.,'])\d{3}(\1\d{3})*$")]
    private static partial Regex GroupedThousands();

    /// <summary>The first number in <paramref name="text"/>, or null when there is none.</summary>
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = NumberToken().Match(text.Replace('−', '-').Replace(" ", string.Empty, StringComparison.Ordinal));
        if (!match.Success)
            return null;
        var token = match.Value.TrimEnd('.', ',', '\'');
        var negative = token.StartsWith('-');
        token = token.TrimStart('-');
        string normalized;
        if (token.Contains('.') && token.Contains(','))
        {
            var decimalSeparator = token.LastIndexOf('.') > token.LastIndexOf(',') ? '.' : ',';
            var thousands = decimalSeparator == '.' ? "," : ".";
            normalized = token.Replace(thousands, string.Empty, StringComparison.Ordinal).Replace("'", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }
        else if (GroupedThousands().IsMatch(token))
        {
            normalized = token.Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Replace("'", string.Empty, StringComparison.Ordinal);
        }
        else
        {
            normalized = token.Replace("'", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? negative ? -value : value : null;
    }

    /// <summary>True when both texts hold the same amount (within 0.005).</summary>
    public static bool Same(string? a, string? b) =>
        Parse(a) is { } x && Parse(b) is { } y && Math.Abs(x - y) <= 0.005m;
}
