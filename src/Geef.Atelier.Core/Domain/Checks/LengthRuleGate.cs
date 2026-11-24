using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Checks;

/// <summary>Where a length read from the request came from: its position and the kind of wording.</summary>
internal sealed record LengthSource(int Start, int End, bool Marked, bool Pair, bool Compound, bool FramedByPreposition);

/// <summary>
/// Decides from the whole request whether a length read by <see cref="RequestConstraintExtractor"/> may become a
/// blocking check. Precision comes first: the length must be the only length of the request outside quotes, code and
/// attached text; the request must not count other things (pieces, variants, pages, minutes) or refer to an earlier or
/// existing text; the length must be bound to a limit word; and it must belong to the text being written, not to
/// something the request talks about. Everything else stays a note.
/// </summary>
internal static class LengthRuleGate
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const int MaxQuotedChars = 2_000;

    private const string Num = @"(?<![\d.,])(?:\d{1,3}(?:[.,  ]\d{3})+|\d+)(?![\d])";
    private const string UnitBody = @"W(?:ö|oe|o)rt(?:ern|er|en|e)?|words?|Zeichen|Anschl(?:ä|ae)gen?|characters?|chars?";

    private static readonly Regex CodeBlock = new(@"```[\s\S]*?(?:```|$)|`[^`\n]{1,200}`", Options);

    private static readonly (char Open, char Close)[] QuotePairs =
    [
        ('„', '“'), ('„', '”'), ('“', '”'), ('”', '”'), ('"', '"'), ('«', '»'), ('»', '«'), ('‚', '‘'), ('‘', '’'),
    ];

    private static readonly Regex SingleQuoted = new(@"(?<=^|[\s(\[:])'(?=\S)[^'\n]{1,300}?(?<=\S)'(?![\p{L}])", Options);

    private static readonly Regex AttachedTextLead = new(
        @"(?<![\p{L}])(?:diese[nmrs]?|folgende\w*|unten|hier|this|these|following|below|here)(?![\p{L}])[^\n]{0,200}:[ \t]*\n", Options);

    private static readonly Regex LengthMention = new(
        $@"(?:{Num}(?:\s*k)?|\d+[.,]\d+\s*k)\s*(?:-\s*)?(?:{UnitBody}|w)(?![\p{{L}}])|(?<![\p{{L}}])(?:\p{{L}}*(?:hundert|tausend|zwanzig|dreißig|dreissig|zig|undred|housand|ty)|zwei|drei|vier|fünf|sechs|sieben|acht|neun|zehn|elf|zwölf|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|ein\s+paar|a\s+few|a\s+couple(?:\s+of)?)[\s-]+(?:{UnitBody})(?![\p{{L}}])",
        Options);

    private const string LimitWord =
        @"h(?:ö|oe)chstens|maximal|max\.?|bis\s+zu|at\s+most|up\s+to|no\s+more\s+than|not\s+more\s+than|nicht\s+mehr\s+als|mehr\s+als|more\s+than|weniger\s+als|less\s+than|fewer\s+than|unter|under|mindestens|mind\.|min\.|wenigstens|at\s+least|nicht\s+unter|not\s+under|nicht\s+weniger\s+als|no\s+less\s+than|no\s+fewer\s+than|zwischen|between|ca\.?|circa|etwa|rund|ungef(?:ä|ae)hr|about|around|approximately|approx\.?|roughly|genau|exactly";

    private static readonly Regex LimitedNumber = new(
        $@"(?<![\p{{L}}])(?:{LimitWord})\s*{Num}(?!\s*(?:-\s*)?(?:{UnitBody}|w(?![\p{{L}}])|k\s|%|€|\$|Euro|EUR|Prozent|percent|Uhr|o['’]clock|[.,:]\d))",
        Options);

    private static readonly Regex LimitWordBefore = new($@"(?<![\p{{L}}])(?:{LimitWord})\s*$", Options);

    private const string FunctionWord =
        @"und|oder|and|or|bis|to|the|der|die|das|den|dem|des|ein|eine|einen|einem|einer|a|an|in|im|am|um|von|vom|for|für|with|mit|is|ist|sind|are|was|war|we|wir|ich|i|you|du|sie|they|he|she|er|es|it|at|on|auf|zu|zum|zur|of|als|than|wie|like|bei|nach|after|before|vor|but|aber|so|if|wenn|ob|no|not|nicht|kein|keine|this|that|dies|diese|bitte|please|pls|uhr|o'clock|am|pm|h";

    private static readonly Regex DigitBeforeWord = new(
        $@"(?<![\p{{L}}\d.,:/])(?<n>\d+(?:[.,]\d+)?)(?:\.|-|\s*x)?\s*(?<w>[\p{{L}}][\p{{L}}'’-]*)", Options);

    private static readonly Regex Exempt = new(
        @"^(?:st|nd|rd|th|j(?:ä|ae)hrig\w*|euro?s?|eur|dollars?|usd|chf|cents?|franken|pfund|pounds?|bucks|prozent|percent|jan(?:uar|uary)?|feb(?:ruar|ruary)?|märz|maerz|march|apr(?:il)?|mai|may|juni?|june|juli?|july|aug(?:ust)?|sept?(?:ember)?|okt(?:ober)?|oct(?:ober)?|nov(?:ember)?|dez(?:ember)?|dec(?:ember)?)$", Options);

    private static readonly Regex FunctionWordExact = new($"^(?:{FunctionWord})$", Options);

    private static readonly Regex NumberWordBeforeWord = new(
        @"(?<![\p{L}])(?:zwei|drei|vier|fünf|sechs|sieben|acht|neun|zehn|elf|zwölf|zwanzig|dreißig|fünfzig|hundert|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|thirty|fifty|hundred|dozen|dutzend|ein\s+paar|einige|mehrere|verschiedene|several|few|some|multiple|various|a\s+couple(?:\s+of)?)\s+(?<w>[\p{L}][\p{L}'’-]*)",
        Options);

    private static readonly Regex Distributive = new(
        @"(?<![\p{L}])(?:je|pro(?!\s+(?:und|and|&)\s)|jede[rnsm]?|jeweils|each|per|every|for\s+every|für\s+jede[nrs]?|apiece)(?![\p{L}])", Options);

    private static readonly Regex VariantWord = new(
        @"(?<![\p{L}])(?:Variante\w*|Option(?:en)?|Alternative\w*|variants?|options?|versions?|alternatives?|Vorschl(?:ä|ae)ge\w*|Vorschlag|suggestions?|ideas?|Ideen|Beispiele?n?|examples?)(?![\p{L}])",
        Options);

    private static readonly Regex SinglePageOrMinute = new(
        @"(?<![\p{L}])(?:eine?|one|a|an|half\s+a|halbe|halben)\s+(?:DIN-?A4-?)?(?:Seite|page|Minute|minute)(?![\p{L}])", Options);

    private static readonly Regex RevisionWord = new(
        @"(?<![\p{L}])(?:Entwurfs?|Entw(?:ü|ue)rfe|Version|Fassung|vorhin|letzte[rsnm]?|vorherige\w*|previous|last\s+time|original(?!l)\w*|bisherige\w*|(?:ü|ue)berarbeit\w*|revis\w*|rewrit\w*|rework\w*|shorten\w*|k(?:ü|ue)rz\w*|l(?:ä|ae)nger\w*|shorter|longer|zu\s+lang|zu\s+kurz|too\s+long|too\s+short|cut\s+(?:it\s+)?down|erweiter\w*|expand\w*|(?:ü|ue)bersetz\w*|translat\w*|zusammenfass\w*|zusammengefasst|summar\w*)(?![\p{L}])",
        Options);

    private static readonly Regex DraftNoun = new(
        @"(?<![\p{L}])drafts?(?![\p{L}])", Options);

    private static readonly Regex DraftAsCommand = new(
        @"(?:^|[.!?;:\n,]\s*|(?:please|pls|kindly|can\s+you|could\s+you|would\s+you|help\s+me)\s+)$", Options);

    private static readonly Regex SeparableSummary = new(
        @"(?<![\p{L}])fass\w*(?![\p{L}])[^.!?;\n]{0,120}(?<![\p{L}])zusammen(?![\p{L}])", Options);

    private static readonly Regex NegatedComparativeMarker = new(
        $@"(?<![\p{{L}}])(?:nicht|no|not)\s+(?:l(?:ä|ae)nger|k(?:ü|ue)rzer|longer|shorter)\s+(?:als|than)\s*(?:(?:ca\.?|etwa|about|around)\s*)?{Num}", Options);

    private const string ProductionVerbs =
        @"schreib(?:e|en|t|st)?|verfass(?:e|en|t)?|erstell(?:e|en|t)?|formulier(?:e|en|t)?|entwirf|entwerfe|entwerfen|mach(?:e|en|t)?|gib|geben|liefer(?:e|n)?|brauch(?:e|en|t)?|ben(?:ö|oe)tig(?:e|en|t)?|h(?:ä|ae)tte\s+gerne?|m(?:ö|oe)chte|write|draft|compose|create|generate|produce|prepare|make|give|craft|put\s+together|come\s+up\s+with|whip\s+up|need|want|would\s+like";

    private static readonly Regex ProductionVerb = new($@"(?<![\p{{L}}])(?:{ProductionVerbs})(?![\p{{L}}])", Options);

    private static readonly Regex CommandVerb = new(
        $@"(?<![\p{{L}}])(?:{ProductionVerbs}|erkl(?:ä|ae)r(?:e|en|t)?|beschreib(?:e|en|t)?|halte?|bleib(?:e|en|t)?|beschr(?:ä|ae)nk(?:e|en)?|begrenz(?:e|en)?|verwende(?:n|t)?|nutze(?:n)?|benutze(?:n)?|pen|explain|describe|keep|limit|stick|stay|use|aim)(?![\p{{L}}])",
        Options);

    private static readonly Regex ForeignPreposition = new(
        @"(?<![\p{L}])(?:für|fuer|for|über|ueber|about|zu|to|on|auf(?!\s+(?:Deutsch|Englisch|Französisch|Spanisch|Italienisch)(?![\p{L}]))|bei|vom|des|der|zur|zum|von|of|eines|einer|from|aus|an(?=\s+(?:den|die|das|dem|der|des|einen|eine|einem|einer|meine[nmrs]?|unsere[nmrs]?|seine[nmrs]?|ihre[nmrs]?|deine[nmrs]?|alle\w*|jede\w*)(?![\p{L}])))(?![\p{L}])",
        Options);

    private static readonly Regex TrailingFramePreposition = new(@"(?<![\p{L}])(?:mit|von|of|with|in)\s*$", Options);

    private static readonly Regex ResultNounFrame = new(
        @"^\s*(?:(?:mir|me|uns|us|bitte|please|kurz|schnell|quickly)\s+){0,2}(?:(?:ein|eine|einen|einem|a|an|one|the|den|die|das|my|our|meinen|meine|unseren|unsere)\s+)?(?:[\p{L}][\p{L}'’-]*,?\s+){1,3}(?:mit|von|of|with|in)\s*$",
        Options);

    private static readonly Regex Conjunction = new(@"(?<![\p{L}])(?:und|and|aber|but|oder|or|sondern)(?![\p{L}])", Options);

    private static readonly Regex SentenceBoundary = new(@"[.!?;\n]", Options);

    private static readonly Regex NegationPhrase = new(
        @"(?<![\p{L}])(?:auf\s+keinen\s+Fall|under\s+no\s+circumstances|keinesfalls|niemals|nie|nicht|never|not|no|don['’]?t|do\s+not|doesn['’]?t|shouldn['’]?t|mustn['’]?t|won['’]?t|can['’]?t)(?![\p{L}])",
        Options);

    private static readonly Regex ClauseBoundary = new(@"[.!?;:\n,–—]|(?<=\s)-(?=\s)", Options);

    private static readonly Regex SubordinateOrRelativeStart = new(
        @"^\s*(?:dass|daß|that|which|who|whose|whom|der|die|das|dem|den|deren|dessen|welche[rsnm]?|wo|where|wenn|falls|if|when|weil|because|ob|whether|obwohl|although|since|da)(?![\p{L}])",
        Options);

    private static readonly Regex StateVerb = new(
        @"(?<![\p{L}])(?:hat|hatte|hatten|haben\s+wir|has(?!\s+to)|had(?!\s+to)|have(?!\s+to)|ist|is|war|waren|was|were|are|sind|umfasst\w*|enth(?:ä|ae)lt|contains?|comprises?|got|bekommen|erhalten|received)(?![\p{L}])",
        Options);

    private static readonly Regex StateVerbRightAfter = new(
        @"^\s*(?:[\p{L}'’-]+\s+){0,2}?(?:hat|hatte|hatten|has(?!\s+to)|had(?!\s+to)|ist|is|war|waren|was|were|are|sind|umfasst\w*|enth(?:ä|ae)lt|contains?|comprises?)(?![\p{L}])",
        Options);

    private static readonly HashSet<string> FragmentTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "bitte", "please", "pls", "plz", "und", "and", "aber", "but", "also", "so", "insgesamt", "total", "overall", "gesamt",
        "nur", "only", "just", "ganz", "wirklich", "really", "strikt", "strictly", "streng", "maximal", "max", "in", "mit", "with",
        "keep", "halte", "halt", "bleib", "bleibe", "bleiben", "stay", "limit", "aim", "for", "stick", "to", "dich", "an", "yourself",
        "es", "it", "this", "das", "dies", "er", "sie", "ihn", "der", "die", "den", "the", "your", "deine", "dein", "deinen", "ihre",
        "text", "texte", "beitrag", "post", "antwort", "reply", "response", "answer", "mail", "e-mail", "email", "nachricht",
        "message", "artikel", "article", "ergebnis", "result", "output", "essay", "aufsatz", "rede", "speech", "brief", "letter",
        "story", "geschichte", "gedicht", "poem", "whole", "thing", "ganze", "alles", "everything", "länge", "laenge", "length",
        "umfang", "word", "count", "wortzahl", "zeichenzahl", "richtwert", "zielumfang", "target", "soll", "sollte", "sollen",
        "darf", "dürfen", "duerfen", "muss", "müssen", "muessen", "kann", "should", "must", "may", "can", "could", "needs", "need",
        "has", "have", "ought", "will", "wird", "be", "sein", "around", "ideally", "idealerweise", "gern", "gerne", "am", "liebsten",
        "best", "preferably", "möglichst", "moeglichst", "ruhig", "lieber", "rather", "einfach", "kurz", "short",
    };

    private static readonly Regex PartNounAfter = new(
        @"^[^.!?;:\n,(]{0,40}?(?<![\p{L}])(?:für|for|in|im|pro|per)\s+(?:(?:den|die|das|dem|the|each|jede\w*|jeden|a|an)\s+)?(?:Betreff\w*|subject\w*|Titel|titles?|(?:Zwischen)?(?:ü|ue)berschrift\w*|headlines?|headings?|Einleitung\w*|intro\w*|opening|closing|Einstieg|Teaser|Schluss\w*|Fazit|conclusion|Abs(?:ä|ae)tze?n?|Absatz|paragraphs?|Abschnitt\w*|sections?|Kapitel|chapters?|Teil\w*|parts?|Folien?|slides?|Bildunterschrift\w*|captions?|Hook|CTA|Call-to-Action|bio|Meta\w*)(?![\p{L}-])",
        Options);

    private static readonly Regex BareObjectAfterVerb = new(
        @"^\s*(?:(?:mir|me|uns|us|bitte|please|pls|mal|ma|kurz|schnell|quickly|nochmal|einfach|just|now|jetzt)\s+)*(?<w1>[\p{L}][\p{L}'’-]*)(?:\s+(?<w2>[\p{L}][\p{L}'’-]*))?",
        Options);

    private static readonly Regex NotAnObject = new(
        @"^(?:ein|eine|einen|einem|einer|eines|a|an|one|the|den|die|das|dem|der|des|my|our|your|his|her|their|its|this|that|these|those|mein\w*|unser\w*|dein\w*|ihr\w*|sein\w*|dies\w*|jene\w*|euer|eure\w*|me|mir|uns|us|it|es|ihn|sie|something|etwas|was|anything|some|about|über|ueber|for|für|to|zu|zum|zur|on|auf|in|im|with|mit|from|von|vom|of|at|an|up|out|down|back|again|more|less|weniger|mehr|nicht|not|no|kein\w*|so|wie|how|what|why|bitte|please|ca|etwa|rund|around|approximately|roughly|exactly|genau|höchstens|mindestens|maximal|minimal|max|min|bis|up|under|unter|over|below|less|fewer|than|als|und|and|oder|or|but|aber|everything|alles|kurz|short|copy|content|text|werbetext|fließtext|lyrics|news|summary|someone|jemand\w*|dass|which|ob|whether|whatever)$",
        Options);

    /// <summary>
    /// The request with quotations, code and text attached after a lead-in such as "Answer this e-mail:" blanked
    /// out by spaces, so that positions stay the same and a length inside them is not taken as a length of the
    /// text to write.
    /// </summary>
    public static string Mask(string text)
    {
        var chars = text.ToCharArray();
        foreach (Match code in CodeBlock.Matches(text))
            Blank(chars, code.Index, code.Index + code.Length);

        var lead = AttachedTextLead.Match(new string(chars));
        if (lead.Success)
            Blank(chars, lead.Index + lead.Length, chars.Length);

        BlankQuotes(chars);
        var current = new string(chars);
        foreach (Match quoted in SingleQuoted.Matches(current))
            Blank(chars, quoted.Index, quoted.Index + quoted.Length);
        return new string(chars);
    }

    /// <summary>True when <paramref name="text"/> mentions a length in words or characters anywhere.</summary>
    public static bool MentionsLength(string text) => LengthMention.IsMatch(text);

    /// <summary>The first length mentioned in <paramref name="text"/>, shortened for a note; <c>null</c> without one.</summary>
    public static string? FirstMention(string text)
    {
        var mention = LengthMention.Match(text);
        if (!mention.Success)
            return null;
        var limit = LimitWordBefore.Match(text[..mention.Index]);
        return limit.Success ? text[limit.Index..(mention.Index + mention.Length)] : mention.Value;
    }

    /// <summary>
    /// Returns <c>null</c> when the length at <paramref name="source"/> may be a blocking check of the request
    /// <paramref name="masked"/> (already passed through <see cref="Mask"/>), otherwise a short reason for tests and logs.
    /// </summary>
    public static string? Reject(string masked, LengthSource source)
    {
        var mentions = LengthMention.Matches(masked).Cast<Match>().Concat(LimitedNumber.Matches(masked)).ToList();
        if (mentions.Count == 0 || mentions.Count > 2)
            return "count";
        if (mentions.Count == 2 && !(source.Pair && mentions.All(m => m.Index >= source.Start && m.Index + m.Length <= source.End)))
            return "count";
        if (mentions.Count == 1 && !(mentions[0].Index < source.End && source.Start < mentions[0].Index + mentions[0].Length))
            return "count";

        if (source.Compound)
            return "compound";

        var rest = Blanked(masked, source.Start, source.End);
        if (HasOtherQuantity(rest))
            return "quantity";
        if (HasRevisionWord(rest))
            return "revision";
        if (!source.Marked && !source.FramedByPreposition)
            return "marker";

        return ForeignHost(masked, source);
    }

    private static bool HasOtherQuantity(string rest)
    {
        foreach (Match digit in DigitBeforeWord.Matches(rest))
        {
            var number = digit.Groups["n"].Value;
            var word = digit.Groups["w"].Value;
            if (number.Length == 4 && number[0] is '1' or '2' && !number.Contains('.') && !number.Contains(','))
                continue;
            if (word.Length == 1 || digit.Value.Contains('.') && !number.Contains('.') || Exempt.IsMatch(word) || FunctionWordExact.IsMatch(word))
                continue;
            return true;
        }

        foreach (Match counted in NumberWordBeforeWord.Matches(rest))
            if (!FunctionWordExact.IsMatch(counted.Groups["w"].Value))
                return true;

        return Distributive.IsMatch(rest) || VariantWord.IsMatch(rest) || SinglePageOrMinute.IsMatch(rest) || HasBarePluralObject(rest);
    }

    private static bool HasBarePluralObject(string rest)
    {
        foreach (Match verb in ProductionVerb.Matches(rest))
        {
            var after = BareObjectAfterVerb.Match(rest[(verb.Index + verb.Length)..]);
            if (!after.Success)
                continue;
            var w1 = after.Groups["w1"].Value;
            if (NotAnObject.IsMatch(w1))
                continue;
            var head = after.Groups["w2"].Success && !NotAnObject.IsMatch(after.Groups["w2"].Value) && LooksLikeAdjective(w1)
                ? after.Groups["w2"].Value
                : w1;
            if (LooksPlural(head))
                return true;
        }
        return false;
    }

    private static bool LooksLikeAdjective(string word) =>
        word.EndsWith("e", StringComparison.OrdinalIgnoreCase) && char.IsLower(word[0])
        || word.EndsWith("en", StringComparison.OrdinalIgnoreCase) && char.IsLower(word[0])
        || word is "short" or "long" or "catchy" or "funny" or "creative" or "new" or "kurz" or "neue" or "quick" or "simple";

    private static bool LooksPlural(string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.Length < 3)
            return false;
        if (lower.EndsWith('s') && !lower.EndsWith("ss") && !lower.EndsWith("'s") && !lower.EndsWith("us") && !lower.EndsWith("is"))
            return true;
        return char.IsUpper(word[0]) && (lower.EndsWith("en") || lower.EndsWith("er") || lower.EndsWith('e') || lower.EndsWith('n'));
    }

    private static bool HasRevisionWord(string rest)
    {
        var withoutMarkers = NegatedComparativeMarker.Replace(rest, m => new string(' ', m.Length));
        if (RevisionWord.IsMatch(withoutMarkers) || SeparableSummary.IsMatch(withoutMarkers))
            return true;
        foreach (Match draft in DraftNoun.Matches(withoutMarkers))
            if (!DraftAsCommand.IsMatch(withoutMarkers[..draft.Index]))
                return true;
        return false;
    }

    private static string? ForeignHost(string masked, LengthSource source)
    {
        if (PartNounAfter.IsMatch(masked[source.End..]))
            return "part";

        var clauseStart = BoundaryBefore(masked, source.Start, ClauseBoundary);
        var clause = masked[clauseStart..source.Start];
        var conjunction = Conjunction.Matches(clause).LastOrDefault();
        var segment = conjunction is null ? clause : clause[(conjunction.Index + conjunction.Length)..];

        var verb = CommandVerb.Match(segment);
        if (verb.Success)
            return VerbHost(segment[(verb.Index + verb.Length)..], source);

        var reason = FragmentHost(masked, source, clause, segment);
        if (reason is null)
            return null;

        var sentenceStart = BoundaryBefore(masked, source.Start, SentenceBoundary);
        var sentence = masked[sentenceStart..clauseStart];
        var sentenceVerb = CommandVerb.Match(sentence);
        if (!sentenceVerb.Success || SubordinateOrRelativeStart.IsMatch(segment))
            return reason;
        var between = masked[(sentenceStart + sentenceVerb.Index + sentenceVerb.Length)..source.Start];
        return StateVerb.IsMatch(between) ? reason : VerbHost(between, source) is null ? null : reason;
    }

    private static string? VerbHost(string between, LengthSource source)
    {
        if (!source.Marked && !ResultNounFrame.IsMatch(between))
            return "marker";
        var trailing = TrailingFramePreposition.Match(between);
        var checkedPart = trailing.Success ? between[..trailing.Index] : between;
        if (ForeignPreposition.IsMatch(checkedPart))
            return "foreign";
        return StateVerb.IsMatch(checkedPart) ? "state" : null;
    }

    private static string? FragmentHost(string masked, LengthSource source, string clause, string segment)
    {
        if (!source.Marked)
            return "marker";
        if (StateVerb.IsMatch(clause) || StateVerbRightAfter.IsMatch(ClauseRest(masked, source.End)))
            return "state";
        if (SubordinateOrRelativeStart.IsMatch(segment) && !StartsWithResultNoun(segment))
            return "dependent";

        foreach (var token in Tokens(NegationPhrase.Replace(segment, " ")))
            if (!FragmentTokens.Contains(token))
                return "fragment";
        return null;
    }

    private static bool StartsWithResultNoun(string before)
    {
        var tokens = Tokens(before).ToList();
        return tokens.Count >= 2 && tokens[0].ToLowerInvariant() is "der" or "die" or "das" && FragmentTokens.Contains(tokens[1]);
    }

    private static IEnumerable<string> Tokens(string text) =>
        Regex.Matches(text, @"[\p{L}][\p{L}'’-]*", Options).Select(m => m.Value);

    private static int BoundaryBefore(string text, int index, Regex boundaries)
    {
        var start = 0;
        foreach (Match boundary in boundaries.Matches(text[..index]))
        {
            if (boundary.Value == "." && IsInsideWordOrNumber(text, boundary.Index))
                continue;
            if (boundary.Value == "," && IsBetweenDigits(text, boundary.Index))
                continue;
            start = boundary.Index + boundary.Length;
        }
        return start;
    }

    private static string ClauseRest(string text, int index)
    {
        var next = ClauseBoundary.Match(text, index);
        return next.Success ? text[index..next.Index] : text[index..];
    }

    private static bool IsInsideWordOrNumber(string text, int dot)
    {
        if (dot + 1 < text.Length && dot > 0 && char.IsDigit(text[dot - 1]) && char.IsDigit(text[dot + 1]))
            return true;
        var start = dot;
        while (start > 0 && char.IsLetter(text[start - 1]))
            start--;
        var word = text[start..dot].ToLowerInvariant();
        return word is "ca" or "max" or "min" or "mind" or "bzw" or "z" or "b" or "usw" or "vgl" or "approx" or "inkl" or "ggf" or "evtl" or "etc" or "e" or "g" or "i" or "d" or "h" or "zb" or "bspw" or "ggfs" or "inkl";
    }

    private static bool IsBetweenDigits(string text, int index) =>
        index > 0 && index + 1 < text.Length && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]);

    private static string Blanked(string text, int start, int end)
    {
        var chars = text.ToCharArray();
        Blank(chars, start, end);
        return new string(chars);
    }

    private static void Blank(char[] chars, int start, int end)
    {
        for (var i = Math.Max(0, start); i < Math.Min(chars.Length, end); i++)
            if (chars[i] != '\n')
                chars[i] = ' ';
    }

    private static void BlankQuotes(char[] chars)
    {
        var i = 0;
        while (i < chars.Length)
        {
            var open = chars[i];
            var closes = QuotePairs.Where(p => p.Open == open).Select(p => p.Close).ToArray();
            if (closes.Length == 0 || open == '"' && i > 0 && char.IsLetterOrDigit(chars[i - 1]))
            {
                i++;
                continue;
            }

            var window = Math.Min(MaxQuotedChars, chars.Length - i - 1);
            var offset = chars.AsSpan(i + 1, window).IndexOfAny(closes);
            var close = offset < 0 ? -1 : i + 1 + offset;
            if (close < 0)
            {
                i++;
                continue;
            }
            Blank(chars, i, close + 1);
            i = close + 1;
        }
    }
}
