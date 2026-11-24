using System.Globalization;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Checks;

/// <summary>
/// Reads constraints (length, language, placeholders, forbidden forms, forbidden and required terms) from a German or
/// English request without an AI model. Conservative by design: a rule is only taken when its wording is
/// unambiguous; a length that may describe a file, a part of the text or something else becomes a note instead of a
/// check, and so do contradictory lengths or languages. Every rule keeps the passage it was read from.
/// </summary>
public static class RequestConstraintExtractor
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const int ClauseWindow = 80;
    private const int MaxQuoteLength = 120;
    private const int MaxTermLength = 60;
    private const int MaxTermWords = 8;

    private const string Num = @"(?<![\d.,])(?:\d{1,3}(?:[.,  ]\d{3})+|\d+)(?![\d])";
    private const string Unit = @"(?<unit>W(?:ö|oe|o)rt(?:ern|er|en|e)?|words?|Zeichen|Anschl(?:ä|ae)gen?|characters?|chars?)(?!\p{L})";
    private const string Sep = @"\s*(?:-\s*)?";
    private const string Approx = @"(?:(?:ca\.?|circa|etwa|rund|ungefähr|about|around|approximately|roughly)\s*)?";
    private const string OpenQuote = "[„\"“”«»‚‘]";
    private const string CloseQuote = "[“”\"«»‘’]";
    private const string Term = "(?<term>[^„\"“”«»‚‘’\\n]{1,60}?)";

    private static readonly Regex RangeBetween = new(
        $@"(?:zwischen|between)\s+(?<a>{Num})\s+(?:und|and)\s+(?<b>{Num}){Sep}{Unit}", Options);

    private static readonly Regex RangeDash = new(
        $@"(?<a>{Num})\s*(?:–|—|-|bis|to)\s*(?<b>{Num}){Sep}{Unit}", Options);

    private static readonly Regex AtMostBefore = new(
        $@"(?<![\p{{L}}])(?:max(?:imal|imum)?\.?(?:\s+of)?|höchstens|hoechstens|bis|nicht\s+mehr\s+als|keinesfalls\s+mehr\s+als|nicht\s+länger\s+als|bis\s+zu|at\s+most|no\s+more\s+than|not\s+more\s+than|no\s+longer\s+than|up\s+to|under|unter|weniger\s+als|less\s+than|fewer\s+than|within)\s*{Approx}(?<a>{Num}){Sep}{Unit}",
        Options);

    private static readonly Regex AtMostAfter = new(
        $@"(?<a>{Num}){Sep}{Unit}\s*(?:max(?:imal|imum)?\.?|or\s+(?:less|fewer)|oder\s+weniger|höchstens)(?!\p{{L}})", Options);

    private static readonly Regex AtLeastBefore = new(
        $@"(?<![\p{{L}}])(?:mindestens|min\.|wenigstens|at\s+least|no\s+less\s+than|not\s+less\s+than|no\s+fewer\s+than|minimum(?:\s+of)?|mehr\s+als|more\s+than)\s*{Approx}(?<a>{Num}){Sep}{Unit}",
        Options);

    private static readonly Regex AtLeastAfter = new(
        $@"(?<a>{Num}){Sep}{Unit}\s*(?:or\s+more|oder\s+mehr|mindestens)(?!\p{{L}})", Options);

    private static readonly Regex AboutBefore = new(
        $@"(?:(?<![\p{{L}}])(?:ca\.?|circa|zirka|etwa|rund|ungefähr|ungefaehr|about|around|approximately|approx\.?|roughly)|~|≈)\s*(?<a>{Num}){Sep}{Unit}",
        Options);

    private static readonly Regex StatedFramed = new(
        $@"(?:(?<![\p{{L}}])(?:in|auf)\s+(?<a>{Num}){Sep}{Unit})|(?:(?<![\p{{L}}])(?:Länge|Laenge|Umfang|Textlänge|Wortzahl|Wortanzahl|Zeichenzahl|length|word\s+count|character\s+count)\s*(?::|von|of|=)?\s*(?<a>{Num}){Sep}{Unit})",
        Options);

    private static readonly Regex Bare = new($@"(?<a>{Num}){Sep}{Unit}", Options);

    private static readonly Regex OtherCounts = new(
        $@"(?:(?:max(?:imal|imum)?\.?|höchstens|mindestens|at\s+most|at\s+least|up\s+to|bis\s+zu|ca\.?|circa|etwa|rund|ungefähr|about|around|approximately|in|auf)\s*)(?<a>{Num})(?:\s*(?:–|-|bis|to)\s*{Num})?\s*(?:Seiten|Seite|pages?|Absätze|Absätzen|Absatz|paragraphs?|Sätze|Sätzen|Satz|sentences?|Zeilen|lines?)(?!\p{{L}})",
        Options);

    private static readonly Regex PartNoun = new(
        @"(?<![\p{L}])(?:Betreff(?:zeile)?|subject(?:\s+line)?|Titel|titles?|(?:Zwischen)?(?:ü|ue)berschrift(?:en)?|headlines?|headings?|Einleitung(?:ssatz)?|intro(?:duction)?|opening|closing|Einstieg|Teaser|Schluss(?:satz|teil|wort)?|Fazit|conclusion|Abs(?:ä|ae)tze?n?|Absatz|paragraphs?|Abschnitt(?:e|en)?|sections?|Kapitel|chapters?|S(?:ä|ae)tze?n?|Satz|sentences?|Zeilen?|lines?|Folien?|slides?|Stichpunkte?n?|bullets?|bullet\s+points?|Aufz(?:ä|ae)hlungspunkte?|Bildunterschrift(?:en)?|captions?|Alt-?Text|Meta-?Description|jede[rsnm]?|jeweils|je|each)(?![\p{L}])",
        Options);

    private static readonly Regex PartAfter = new(
        @"^\s*(?:pro|je|per|each|für\s+jede[nrs]?|for\s+each|in\s+each|jeweils|Flie(?:ß|ss)text|(?:of\s+)?body(?:\s+text)?|Haupttext|(?:of\s+)?main\s+text|\(?\s*(?:ohne|exkl\w*|excluding|not\s+counting|without|zzgl\.?|zuz(?:ü|ue)glich|plus))(?![\p{L}])", Options);

    private static readonly Regex WholeTextObject = new(
        @"(?<![\p{L}])(?:schreib\w*|verfass\w*|erstell\w*|formulier\w*|write|draft|compose|create)\s+(?:mir\s+|me\s+)?(?:ein|eine|einen|a|an|one)\s+(?:(?!(?:der|die|das|den|dem|des|the|mit|with|und|and|für|for)\s)[\p{L}-]+,?\s+){0,3}$", Options);

    private static readonly Regex ApproximateCue = new(
        @"(?<![\p{L}])(?:ca\.?|circa|zirka|etwa|rund|ungefähr|ungefaehr|Richtwert|about|around|approximately|approx\.?|roughly)(?![\p{L}])", Options);

    private static readonly Regex SourceCue = new(
        @"(?<![\p{L}])(?:Original(?:text|s|e|en)?|originals?|Vorlage|Quelle(?:n)?|Quelltext|Quelldokument|Anhang|Anh(?:ä|ae)nge|angeh(?:ä|ae)ngt\w*|beigef(?:ü|ue)gt\w*|Datei(?:en)?|files?|attached|attachments?|sources?|transcripts?|Transkript\w*)(?![\p{L}])",
        Options);

    private static readonly Regex SourceVerb = new(
        @"(?<![\p{L}])(?:hat|has|umfasst|enth(?:ä|ae)lt|contains|comprises)(?![\p{L}])", Options);

    private static readonly Regex OutputFrame = new(@"(?:(?<![\p{L}])(?:in|auf)\s+(?:\S+\s+)?)$", Options);

    private static readonly Regex SummaryVerb = new(
        @"(?<![\p{L}])(?:fass\w*|zusammenfass\w*|zusammen|summar\w*|k(?:ü|ue)rz\w*|shorten\w*|condens\w*|(?:ü|ue)bersetz\w*|translat\w*)(?![\p{L}])", Options);

    private static readonly Regex WriteVerb = new(
        @"(?<![\p{L}])(?:schreib\w*|verfass\w*|erstell\w*|formulier\w*|entw(?:i|e)rf\w*|write|draft|compose|create|prepare)(?![\p{L}])", Options);

    private static readonly Regex PlaceholderBan = new(
        @"(?:(?<![\p{L}])(?:keine|ohne)\s+(?:\p{L}+\s+)?Platzhalter)|(?:Platzhalter\s+(?:sind\s+)?(?:nicht\s+erlaubt|verboten|unerw(?:ü|ue)nscht))|(?:(?<![\p{L}])(?:no|without|avoid)\s+(?:any\s+)?placeholders?)|(?:(?:do\s+not|don't|never)\s+(?:use|leave|include|add)\s+(?:any\s+)?placeholders?)",
        Options);

    private static readonly Regex NoLists = new(
        @"(?<![\p{L}])(?:(?:ohne|keine)\s+(?:Aufz(?:ä|ae)hlung(?:en|szeichen|spunkte)?|Stichpunkte?|Stichpunktlisten?|Spiegelstriche?|Listen?|Aufz(?:ä|ae)hlungslisten?|Bullet[-\s]?Points?|Bulletpoints?)|(?:no|without|avoid|(?:do\s+not|don't|never)\s+use)\s+(?:any\s+)?(?:bullet(?:[-\s]?points?|s)?|(?:numbered\s+)?lists?))",
        Options);

    private static readonly Regex NoHeadings = new(
        @"(?<![\p{L}])(?:(?:ohne|keine)\s+(?:Zwischen)?(?:ü|ue)berschrift(?:en)?|(?:no|without|avoid|(?:do\s+not|don't|never)\s+use)\s+(?:any\s+)?(?:sub)?(?:headings?|headers?|headlines?))",
        Options);

    private static readonly Regex NoEmojis = new(
        @"(?<![\p{L}])(?:(?:ohne|keine)\s+(?:Emojis?|Smileys?)|(?:no|without|avoid|(?:do\s+not|don't|never)\s+use)\s+(?:any\s+)?(?:emojis?|smileys?))",
        Options);

    private static readonly Regex PatternFollow = new(
        @"^\s*(?:$|[.,;:!?)\n]|und(?!\p{L})|oder(?!\p{L})|and(?!\p{L})|or(?!\p{L})|sondern|but(?!\p{L})|verwenden|benutzen|nutzen|use(?!\p{L})|setzen|machen|enthalten|bitte|please|—|–|-)",
        Options);

    private static readonly Regex NegatedBefore = new(@"(?:nicht|not)\s*$", Options);

    private static readonly Regex[] ForbiddenTermTriggers =
    [
        new($@"(?<![\p{{L}}])(?:verwende|benutze|nutze|schreibe|sage|nenne|erw(?:ä|ae)hne|verwenden|benutzen|nutzen|schreiben)\s+(?:bitte\s+)?(?:nicht|niemals|nie|kein(?:e|en)?)\s+(?:das\s+Wort|den\s+Begriff|die\s+(?:Formulierung|Wendung|Floskel|Phrase|Anrede|Bezeichnung))?\s*(?={OpenQuote})", Options),
        new($@"(?<![\p{{L}}])(?:nicht|niemals|nie)\s+(?:das\s+Wort|den\s+Begriff|die\s+(?:Formulierung|Wendung|Floskel|Phrase|Anrede|Bezeichnung))\s*(?={OpenQuote})", Options),
        new($@"(?<![\p{{L}}])kein(?:e|en)?\s+(?:Wort|W(?:ö|oe)rter|Begriff|Begriffe|Formulierung(?:en)?|Wendung(?:en)?|Floskel(?:n)?|Phrase(?:n)?|Bezeichnung(?:en)?)\s+(?:wie\s+)?(?={OpenQuote})", Options),
        new($@"(?<![\p{{L}}])(?:vermeide|vermeiden|ohne)\s+(?:bitte\s+)?(?:das\s+Wort|den\s+Begriff|die\s+(?:Formulierung|Wendung|Floskel|Phrase|Bezeichnung)|(?:Formulierungen|Begriffe|Floskeln|W(?:ö|oe)rter|Wendungen)\s+wie)?\s*(?={OpenQuote})", Options),
        new($@"(?<![\p{{L}}])(?:don't|do\s+not|never|avoid|without)\s+(?:(?:use|using|say|saying|write|writing|mention|mentioning|include|including)\s+)?(?:the\s+(?:words?|terms?|phrases?|expressions?)\s+|(?:words|phrases|terms|expressions)\s+like\s+)?(?={OpenQuote})", Options),
    ];

    private static readonly Regex[] ForbiddenTermAfter =
    [
        new($@"{OpenQuote}{Term}{CloseQuote}\s+(?:darf|soll|sollte)\s+(?:bitte\s+)?(?:nicht|nie|niemals|keinesfalls)\s+(?:vorkommen|verwendet|benutzt|auftauchen|erscheinen|genannt|stehen|fallen)", Options),
        new($@"{OpenQuote}{Term}{CloseQuote}\s+(?:must|should)\s+(?:not|never)\s+(?:appear|be\s+used|be\s+mentioned|be\s+written)", Options),
    ];

    private static readonly Regex[] RequiredTermTriggers =
    [
        new($@"(?<![\p{{L}}])(?:erw(?:ä|ae)hne|nenne|verwende|benutze)\s+(?:bitte\s+)?unbedingt\s+(?:das\s+Wort|den\s+Begriff|die\s+(?:Formulierung|Wendung|Phrase))?\s*(?={OpenQuote})", Options),
        new($@"(?<![\p{{L}}])(?:must|should)\s+(?:include|contain|mention|use)\s+(?:the\s+(?:word|term|phrase|expression)\s+)?(?={OpenQuote})", Options),
        new($@"(?<![\p{{L}}])(?:be\s+sure|make\s+sure)\s+to\s+(?:mention|include|use)\s+(?:the\s+(?:word|term|phrase|expression)\s+)?(?={OpenQuote})", Options),
    ];

    private static readonly Regex[] RequiredTermAround =
    [
        new($@"(?<![\p{{L}}])(?:muss|soll|sollte|m(?:ü|ue)ssen|sollen)\s+(?:\p{{L}}+\s+){{0,3}}?(?:das\s+Wort|den\s+Begriff|die\s+(?:Formulierung|Wendung|Phrase))?\s*{OpenQuote}{Term}{CloseQuote}\s+(?:enthalten|erw(?:ä|ae)hnen|nennen|beinhalten|einbauen|aufgreifen|verwenden)(?!\p{{L}})", Options),
        new($@"{OpenQuote}{Term}{CloseQuote}\s+(?:muss|soll|sollte)\s+(?:unbedingt\s+)?(?:vorkommen|enthalten\s+sein|erw(?:ä|ae)hnt\s+werden|genannt\s+werden|auftauchen|fallen)", Options),
        new($@"{OpenQuote}{Term}{CloseQuote}\s+(?:must|should)\s+(?:appear|be\s+(?:mentioned|included|used))", Options),
    ];

    private static readonly Regex QuotedTermAt = new($@"^\s*{OpenQuote}{Term}{CloseQuote}", Options);

    private static readonly Regex FurtherQuotedTerm = new(
        $@"^\s*(?:,|/|oder|und|or|and|sowie)\s*(?:,\s*)?{OpenQuote}{Term}{CloseQuote}", Options);

    private static readonly Regex ExplicitGerman = new(
        @"(?<![\p{L}])(?:auf\s+Deutsch|in\s+(?:deutscher\s+Sprache|German|Deutsch)|ins\s+Deutsche|(?:into|to)\s+German|deutschsprachig\w*|German[-\s]language|(?:deutsche|German)\s+(?:Fassung|Version|(?:Ü|Ue)bersetzung|translation|version))(?!\p{L})",
        Options);

    private static readonly Regex ExplicitEnglish = new(
        @"(?<![\p{L}])(?:auf\s+Englisch|in\s+(?:englischer\s+Sprache|English|Englisch)|ins\s+Englische|(?:into|to)\s+English|englischsprachig\w*|English[-\s]language|(?:englische|English)\s+(?:Fassung|Version|(?:Ü|Ue)bersetzung|translation|version))(?!\p{L})",
        Options);

    private static readonly Regex ExplicitOther = new(
        @"(?<![\p{L}])(?:auf|in|ins|into|to)\s+(?:Französisch|Franzoesisch|Französische|French|Spanisch|Spanische|Spanish|Italienisch|Italienische|Italian|Niederländisch|Niederländische|Dutch|Polnisch|Polnische|Polish|Türkisch|Türkische|Turkish|Russisch|Russische|Russian|Portugiesisch|Portugiesische|Portuguese|Chinesisch|Chinesische|Chinese|Japanisch|Japanische|Japanese|Arabisch|Arabische|Arabic|Ukrainisch|Ukrainische|Ukrainian)(?!\p{L})",
        Options);

    private static readonly Regex LanguageWord = new(
        @"(?<![\p{L}])(?:englisch\w*|english|deutsch\w*|german|französisch\w*|french|spanisch\w*|spanish|italienisch\w*|italian|niederländisch\w*|dutch|polnisch\w*|polish|türkisch\w*|turkish|russisch\w*|russian|sprache\w*|languages?|(?:ü|ue)bersetz\w*|translat\w*)(?!\p{L})",
        Options);

    /// <summary>Extracts the constraints of <paramref name="requestText"/>, the request in the person's own words.</summary>
    public static OrderConstraints Extract(string requestText)
    {
        if (string.IsNullOrWhiteSpace(requestText))
            return OrderConstraints.None;

        var text = requestText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var notes = new List<string>();

        var length = ExtractLength(text, notes);
        var language = ExtractLanguage(text, notes);
        var placeholderBan = PlaceholderBan.Match(text);
        var patterns = ExtractPatterns(text);
        var forbidden = ExtractTerms(text, ForbiddenTermTriggers, ForbiddenTermAfter);
        var required = ExtractTerms(text, RequiredTermTriggers, RequiredTermAround);

        var conflicting = forbidden.Select(f => f.Term)
            .Intersect(required.Select(r => r.Term), StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (conflicting.Count > 0)
        {
            forbidden = forbidden.Where(f => !conflicting.Contains(f.Term)).ToList();
            required = required.Where(r => !conflicting.Contains(r.Term)).ToList();
            notes.Add("The request both asks for and forbids the same words, so they were not checked.");
        }

        return new OrderConstraints(
            length,
            language,
            placeholderBan.Success,
            placeholderBan.Success ? Shorten(placeholderBan.Value) : null,
            patterns,
            forbidden,
            required,
            notes.Distinct(StringComparer.Ordinal).ToList());
    }

    private sealed record LengthCandidate(int Start, int End, LengthConstraint Constraint);

    private static LengthConstraint? ExtractLength(string text, List<string> notes)
    {
        var taken = new List<(int Start, int End)>();
        var accepted = new List<LengthCandidate>();
        var partMentioned = false;

        void Consider(Regex regex, LengthKind statedKind, bool framed)
        {
            foreach (Match match in regex.Matches(text))
            {
                var kind = statedKind;
                if (Overlaps(taken, match.Index, match.Index + match.Length))
                    continue;
                taken.Add((match.Index, match.Index + match.Length));

                var quote = Shorten(match.Value);
                var unit = UnitOf(match.Groups["unit"].Value);
                if (!TryParseNumber(match.Groups["a"].Value, out var a))
                    continue;
                var b = a;
                if (match.Groups["b"].Success && !TryParseNumber(match.Groups["b"].Value, out b))
                    continue;

                if (IsPartOfText(text, match))
                {
                    partMentioned = true;
                    notes.Add($"A length for one part of the text was not checked (\"{quote}\").");
                    continue;
                }

                if (kind is LengthKind.Range or LengthKind.Stated && IsApproximate(text, match))
                    kind = LengthKind.About;

                var outputFrame = HasOutputFrame(text, match);
                var loose = kind is LengthKind.About || !framed;
                var describesSource = !outputFrame
                    && (IsAboutSource(text, match)
                        || loose && (HasSourceVerb(text, match) || HasSummaryVerb(text, match)));
                if (describesSource)
                {
                    notes.Add($"A length that seems to describe a file or an original was not checked (\"{quote}\").");
                    continue;
                }

                if (!framed && !IsCompoundForWrittenText(text, match))
                {
                    notes.Add($"A length was mentioned, but it is not clear what it is for, so it was not checked (\"{quote}\").");
                    continue;
                }

                var constraint = kind switch
                {
                    LengthKind.Range => new LengthConstraint(unit, Math.Min(a, b), Math.Max(a, b), quote, LengthKind.Range),
                    LengthKind.AtMost => new LengthConstraint(unit, null, a, quote, LengthKind.AtMost),
                    LengthKind.AtLeast => new LengthConstraint(unit, a, null, quote, LengthKind.AtLeast),
                    _ => new LengthConstraint(unit, Math.Min(a, b), Math.Max(a, b), quote, kind),
                };

                if (!IsPlausible(constraint))
                {
                    notes.Add($"A length that does not fit a whole text was not checked (\"{quote}\").");
                    continue;
                }

                accepted.Add(new LengthCandidate(match.Index, match.Index + match.Length, constraint));
            }
        }

        Consider(RangeBetween, LengthKind.Range, framed: true);
        Consider(RangeDash, LengthKind.Range, framed: true);
        Consider(AtMostBefore, LengthKind.AtMost, framed: true);
        Consider(AtMostAfter, LengthKind.AtMost, framed: true);
        Consider(AtLeastBefore, LengthKind.AtLeast, framed: true);
        Consider(AtLeastAfter, LengthKind.AtLeast, framed: true);
        Consider(AboutBefore, LengthKind.About, framed: true);
        Consider(StatedFramed, LengthKind.Stated, framed: true);
        Consider(Bare, LengthKind.Stated, framed: false);

        foreach (Match other in OtherCounts.Matches(text))
            notes.Add($"Pages, paragraphs, sentences and lines are not counted yet (\"{Shorten(other.Value)}\").");

        if (accepted.Count == 0)
            return null;
        if (partMentioned)
        {
            notes.Add("The request sets lengths for parts of the text, so the length of the whole text was not checked.");
            return null;
        }

        var merged = Merge(accepted.Select(c => c.Constraint).ToList());
        if (merged is null)
            notes.Add("The request gives more than one length, so the length was not checked.");
        return merged;
    }

    /// <summary>
    /// Combines several lengths of the same unit into their intersection (for example "ca. 150–220 words, at most 250
    /// words" gives 150–220). Different units or lengths that exclude each other give <c>null</c>.
    /// </summary>
    private static LengthConstraint? Merge(IReadOnlyList<LengthConstraint> constraints)
    {
        var first = constraints[0];
        if (constraints.Count == 1)
            return first;
        if (constraints.Any(c => c.Unit != first.Unit))
            return null;

        var lows = constraints.Where(c => c.Min.HasValue).Select(c => c.Min!.Value).ToList();
        var highs = constraints.Where(c => c.Max.HasValue).Select(c => c.Max!.Value).ToList();
        int? low = lows.Count > 0 ? lows.Max() : null;
        int? high = highs.Count > 0 ? highs.Min() : null;
        if (low is not null && high is not null && low > high)
            return null;

        var approximate = constraints.Any(c => c.Kind == LengthKind.About);
        var kind = (low, high) switch
        {
            (not null, not null) => approximate ? LengthKind.About : LengthKind.Range,
            (null, not null) => LengthKind.AtMost,
            _ => LengthKind.AtLeast,
        };
        if (kind == LengthKind.Range && low == high)
            kind = LengthKind.Stated;
        var quote = Shorten(string.Join(" / ", constraints.Select(c => c.Quote).Distinct(StringComparer.Ordinal)));
        return new LengthConstraint(first.Unit, low, high, quote, kind);
    }

    private static bool IsApproximate(string text, Match match)
    {
        var start = ClauseStart(text, match.Index);
        return ApproximateCue.IsMatch(text[start..match.Index]);
    }

    private static string? ExtractLanguage(string text, List<string> notes)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in ExplicitGerman.Matches(text))
            if (!IsAboutSource(text, match))
                targets.Add("de");
        foreach (Match match in ExplicitEnglish.Matches(text))
            if (!IsAboutSource(text, match))
                targets.Add("en");
        foreach (Match match in ExplicitOther.Matches(text))
            if (!IsAboutSource(text, match))
                targets.Add("other");

        if (targets.Count > 1)
        {
            notes.Add("The request names more than one language, so the language was not checked.");
            return null;
        }

        if (targets.Count == 1)
        {
            var target = targets.Single();
            if (target == "other")
            {
                notes.Add("Only German and English texts are checked for their language.");
                return null;
            }
            return target;
        }

        if (LanguageWord.IsMatch(text))
            return null;
        var guess = TextLanguageGuess.Guess(text);
        return guess is not null && HeadDisagrees(text, guess) ? null : guess;
    }

    /// <summary>
    /// True when the first sentence of the request (its instruction, typically) leans to another language than the
    /// whole text, as when a German request quotes an English e-mail to answer.
    /// </summary>
    private static bool HeadDisagrees(string text, string guess)
    {
        var end = text.IndexOfAny(['.', ':', '!', '?', '\n']);
        var head = end < 0 ? text : text[..end];
        var (german, english) = TextLanguageGuess.Count(head);
        return guess == "de" ? english > german : german > english;
    }

    private static IReadOnlyList<PatternRule> ExtractPatterns(string text)
    {
        var rules = new List<PatternRule>();
        AddPattern(rules, text, NoLists, ForbiddenPattern.Lists);
        AddPattern(rules, text, NoHeadings, ForbiddenPattern.Headings);
        AddPattern(rules, text, NoEmojis, ForbiddenPattern.Emojis);
        return rules;
    }

    private static void AddPattern(List<PatternRule> rules, string text, Regex regex, ForbiddenPattern pattern)
    {
        foreach (Match match in regex.Matches(text))
        {
            if (NegatedBefore.IsMatch(text[..match.Index]))
                continue;
            if (!PatternFollow.IsMatch(text[(match.Index + match.Length)..]))
                continue;
            rules.Add(new PatternRule(pattern, Shorten(match.Value)));
            return;
        }
    }

    private static List<TermRule> ExtractTerms(string text, Regex[] triggers, Regex[] around)
    {
        var rules = new List<TermRule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string term, string quote)
        {
            var cleaned = term.Trim().Trim('.', ',', ';', ':', '!', '?').Trim();
            if (!IsUsableTerm(cleaned) || !seen.Add(cleaned))
                return;
            rules.Add(new TermRule(cleaned, Shorten(quote)));
        }

        foreach (var trigger in triggers)
        {
            foreach (Match match in trigger.Matches(text))
            {
                var position = match.Index + match.Length;
                var first = QuotedTermAt.Match(text[position..]);
                if (!first.Success)
                    continue;
                var end = position + first.Length;
                Add(first.Groups["term"].Value, text[match.Index..end]);

                var rest = text[end..];
                var next = FurtherQuotedTerm.Match(rest);
                while (next.Success)
                {
                    end += next.Length;
                    Add(next.Groups["term"].Value, text[match.Index..end]);
                    rest = text[end..];
                    next = FurtherQuotedTerm.Match(rest);
                }
            }
        }

        foreach (var regex in around)
            foreach (Match match in regex.Matches(text))
                Add(match.Groups["term"].Value, match.Value);

        return rules;
    }

    private static bool IsUsableTerm(string term)
    {
        if (term.Length == 0 || term.Length > MaxTermLength)
            return false;
        if (term.Contains('[', StringComparison.Ordinal) || term.Contains(']', StringComparison.Ordinal))
            return false;
        if (!term.Any(char.IsLetterOrDigit))
            return false;
        return term.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxTermWords;
    }

    private static bool Overlaps(List<(int Start, int End)> taken, int start, int end) =>
        taken.Any(t => start < t.End && t.Start < end);

    private static LengthUnit UnitOf(string unit) =>
        unit.StartsWith('w') || unit.StartsWith('W') ? LengthUnit.Words : LengthUnit.Characters;

    private static bool TryParseNumber(string raw, out int value)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsPlausible(LengthConstraint constraint)
    {
        var (low, high) = constraint.Unit == LengthUnit.Words ? (10, 100_000) : (50, 1_000_000);
        var numbers = new[] { constraint.Min, constraint.Max }.Where(n => n.HasValue).Select(n => n!.Value).ToList();
        if (numbers.Any(n => n < low || n > high))
            return false;
        return constraint.Kind != LengthKind.Range || constraint.Min < constraint.Max;
    }

    private static int ClauseStart(string text, int index)
    {
        var start = Math.Max(0, index - ClauseWindow);
        for (var i = index - 1; i >= start; i--)
        {
            if (text[i] is '.' or '!' or '?' or ';' or '\n')
            {
                if (text[i] == '.' && i > 0 && IsAbbreviationDot(text, i))
                    continue;
                return i + 1;
            }
        }
        return start;
    }

    private static int ClauseEnd(string text, int index)
    {
        var end = Math.Min(text.Length, index + ClauseWindow);
        for (var i = index; i < end; i++)
        {
            if (text[i] is '!' or '?' or ';' or '\n')
                return i;
            if (text[i] == '.' && !IsAbbreviationDot(text, i))
                return i;
        }
        return end;
    }

    private static bool IsAbbreviationDot(string text, int dot)
    {
        var start = dot;
        while (start > 0 && char.IsLetter(text[start - 1]))
            start--;
        var word = text[start..dot].ToLowerInvariant();
        return word is "ca" or "max" or "min" or "bzw" or "z" or "b" or "usw" or "vgl" or "approx" or "inkl" or "ggf" or "evtl" or "etc" or "e" or "g" or "i" or "d" or "h"
               || (dot + 1 < text.Length && char.IsDigit(text[dot + 1]) && dot > 0 && char.IsDigit(text[dot - 1]));
    }

    private static bool IsPartOfText(string text, Match match)
    {
        var start = ClauseStart(text, match.Index);
        var before = text[start..match.Index];
        foreach (Match part in PartNoun.Matches(before))
        {
            if (!WholeTextObject.IsMatch(before[..part.Index]))
                return true;
        }
        var afterEnd = ClauseEnd(text, match.Index + match.Length);
        var after = text[(match.Index + match.Length)..afterEnd];
        return PartAfter.IsMatch(after);
    }

    private static bool IsAboutSource(string text, Match match)
    {
        var start = ClauseStart(text, match.Index);
        return SourceCue.IsMatch(text[start..match.Index]);
    }

    private static bool HasSourceVerb(string text, Match match)
    {
        var start = ClauseStart(text, match.Index);
        return SourceVerb.IsMatch(text[start..match.Index]);
    }

    private static bool HasSummaryVerb(string text, Match match)
    {
        var start = ClauseStart(text, match.Index);
        var end = ClauseEnd(text, match.Index + match.Length);
        return SummaryVerb.IsMatch(text[start..match.Index]) || SummaryVerb.IsMatch(text[(match.Index + match.Length)..end]);
    }

    private static bool HasOutputFrame(string text, Match match)
    {
        var start = ClauseStart(text, match.Index);
        var before = text[start..match.Index];
        return OutputFrame.IsMatch(before) || match.Value.StartsWith("in ", StringComparison.OrdinalIgnoreCase)
            || match.Value.StartsWith("auf ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompoundForWrittenText(string text, Match match)
    {
        var number = match.Groups["a"];
        var unit = match.Groups["unit"];
        var between = text[(number.Index + number.Length)..unit.Index];
        if (!between.Contains('-', StringComparison.Ordinal))
            return false;
        var start = ClauseStart(text, match.Index);
        return WriteVerb.IsMatch(text[start..match.Index]);
    }

    private static string Shorten(string value)
    {
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxQuoteLength ? collapsed : collapsed[..MaxQuoteLength].TrimEnd() + "…";
    }
}
