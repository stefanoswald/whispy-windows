using System.Text;
using System.Text.RegularExpressions;

namespace Whispy;

/// <summary>
/// Deterministic rule-based cleanup — direct port of the proven Mac Whispy
/// pipeline. (Windows v1 has no LLM pass; the rules carry the load, and a
/// local llama.cpp pass can slot in later.)
/// </summary>
public static class TextCleaner
{
    public static string Clean(string input, WritingMode mode, AppSettings settings)
    {
        var text = input;

        if (mode == WritingMode.Raw)
        {
            text = NormalizeWhitespace(text);
            return Vocabulary.Correct(text, settings.Vocabulary, settings.Corrections);
        }

        if (settings.RemoveFillers)
        {
            text = RemoveFillers(text);
        }
        text = CollapseRepeatedWords(text);
        text = FixPunctuationSpacing(text, technical: mode == WritingMode.Code);
        if (mode != WritingMode.Code)
        {
            text = SplitRamblingSentences(text);
        }
        text = CapitalizeSentences(text, fixPersonalI: mode != WritingMode.Code);
        text = NormalizeWhitespace(text);

        if (mode is WritingMode.Clean or WritingMode.Email)
        {
            if (text.Length > 0 && char.IsLetterOrDigit(text[^1]))
            {
                text += ".";
            }
        }

        return Vocabulary.Correct(text, settings.Vocabulary, settings.Corrections);
    }

    // ---- fillers -----------------------------------------------------------

    private static string RemoveFillers(string input)
    {
        var text = input;
        text = Regex.Replace(text,
            "\\b(?:um+|uh+|uhm|erm|hmm+|mmm+|ah{2,})\\b[,.]?\\s*", "",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text,
            ",\\s*(?:you know|i mean|like|sort of|kind of)\\s*,", ",",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text,
            "(^|[.!?]\\s+)(?:you know|i mean),\\s*", "$1",
            RegexOptions.IgnoreCase);
        return text;
    }

    private static string CollapseRepeatedWords(string input) =>
        Regex.Replace(input, "\\b(\\w+)(\\s+\\1\\b)+", "$1", RegexOptions.IgnoreCase);

    // ---- punctuation & spacing ---------------------------------------------

    private static string FixPunctuationSpacing(string input, bool technical)
    {
        var text = input;
        text = Regex.Replace(text, "[ \\t]+([,.!?;:])", "$1");
        if (!technical)
        {
            text = Regex.Replace(text, "([,;:!?])([A-Za-z])", "$1 $2");
            // Space after a period only before a capital (protects file.txt, 3.14).
            text = Regex.Replace(text, "(\\.)([A-Z])", "$1 $2");
            text = Regex.Replace(text, ",{2,}", ",");
            text = Regex.Replace(text, "(?<!\\.)\\.\\.(?!\\.)", ".");
        }
        return text;
    }

    // ---- sentence structure --------------------------------------------------

    private static string SplitRamblingSentences(string input)
    {
        var sb = new StringBuilder();
        foreach (var sentence in SplitKeepingDelimiters(input))
        {
            sb.Append(SplitIfRambling(sentence, 0));
        }
        return sb.ToString();
    }

    private static string SplitIfRambling(string sentence, int depth)
    {
        if (depth >= 3) return sentence;
        var wordCount = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount < 28) return sentence;

        var matches = Regex.Matches(sentence, ",?\\s+(and|but|so|because)\\s+",
            RegexOptions.IgnoreCase);
        int middle = sentence.Length / 2;
        Match? best = null;
        foreach (Match m in matches)
        {
            if (m.Index <= sentence.Length / 4 || m.Index >= sentence.Length * 3 / 4) continue;
            if (best == null || Math.Abs(m.Index - middle) < Math.Abs(best.Index - middle))
            {
                best = m;
            }
        }
        if (best == null) return sentence;

        var first = sentence.Substring(0, best.Index).Trim();
        var rest = CapitalizeFirstLetter(sentence.Substring(best.Index + best.Length).Trim());
        if (!first.EndsWith(".") && !first.EndsWith("!") && !first.EndsWith("?"))
        {
            first += ".";
        }
        return SplitIfRambling(first + " ", depth + 1) + SplitIfRambling(rest, depth + 1);
    }

    private static List<string> SplitKeepingDelimiters(string input)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in input)
        {
            current.Append(ch);
            if (ch is '.' or '!' or '?' or '\n')
            {
                pieces.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0) pieces.Add(current.ToString());
        return pieces;
    }

    // ---- capitalization ------------------------------------------------------

    private static string CapitalizeSentences(string input, bool fixPersonalI)
    {
        var text = input;
        if (fixPersonalI)
        {
            text = Regex.Replace(text, "\\bi\\b", "I");
            text = Regex.Replace(text, "\\bi'(m|ll|ve|d)\\b", "I'$1");
        }
        var sb = new StringBuilder(text.Length);
        bool capitalizeNext = true;
        foreach (var ch in text)
        {
            if (capitalizeNext && char.IsLetter(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(ch);
                if (ch is '.' or '!' or '?' or '\n')
                {
                    capitalizeNext = true;
                }
                else if (!char.IsWhiteSpace(ch))
                {
                    capitalizeNext = false;
                }
            }
        }
        return sb.ToString();
    }

    private static string CapitalizeFirstLetter(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    // ---- whitespace -----------------------------------------------------------

    private static string NormalizeWhitespace(string input)
    {
        var text = Regex.Replace(input, "[ \\t]+", " ");
        text = Regex.Replace(text, " ?\\n ?", "\n");
        text = Regex.Replace(text, "\\n{3,}", "\n\n");
        return text.Trim();
    }
}

/// <summary>
/// Fixes casing/joining of user-defined terms: "magic trick guy" ->
/// "MagicTrickGuy", "open claw" -> "OpenClaw", "fable 5" -> "Fable 5".
/// Case-insensitive, tolerant of spaces/hyphens between word parts.
/// </summary>
public static class Vocabulary
{
    /// <summary>Applies learned corrections (heard -> meant) first, then
    /// canonical spelling/casing for every vocabulary term.</summary>
    public static string Correct(string input, List<string> vocabulary, List<Correction>? corrections = null)
    {
        var text = input;
        foreach (var c in corrections ?? new List<Correction>())
        {
            var heard = c.Heard.Trim();
            var replacement = c.Replacement.Trim();
            if (heard.Length == 0 || replacement.Length == 0) continue;
            text = Replace(heard, replacement, text);
        }
        foreach (var term in vocabulary)
        {
            var trimmed = term.Trim();
            if (trimmed.Length == 0) continue;
            text = Replace(trimmed, trimmed, text);
        }
        return text;
    }

    /// <summary>
    /// Replaces every spoken variant of term with replacement. Word parts may
    /// be separated by spaces/hyphens/nothing, and number words match digits
    /// both ways — "ElevenLabs" catches "11 labs", "eleven labs", "Eleven-Labs".
    /// </summary>
    private static string Replace(string term, string replacement, string text)
    {
        var parts = WordParts(term);
        if (parts.Count == 0) return text;
        var pattern = "\\b" + string.Join("[\\s\\-]*", parts.Select(Alternation)) + "\\b";
        try
        {
            return Regex.Replace(text, pattern, replacement.Replace("$", "$$"), RegexOptions.IgnoreCase);
        }
        catch (RegexParseException)
        {
            return text; // a pathological entry must never break dictation
        }
    }

    private static string Alternation(string part)
    {
        var escaped = Regex.Escape(part);
        var lower = part.ToLowerInvariant();
        if (NumberWords.WordToDigits.TryGetValue(lower, out var digits))
            return $"(?:{escaped}|{digits})";
        if (NumberWords.DigitsToWord.TryGetValue(lower, out var word))
            return $"(?:{escaped}|{word})";
        return escaped;
    }

    /// <summary>Splits a term into word parts: whitespace/hyphen separated,
    /// camel-case boundaries (MagicTrickGuy -> Magic, Trick, Guy) and
    /// letter/digit boundaries (Fable5 -> Fable, 5).</summary>
    public static List<string> WordParts(string term)
    {
        var parts = new List<string>();
        foreach (var chunk in term.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var current = new StringBuilder();
            char? previous = null;
            foreach (var ch in chunk)
            {
                bool boundary = false;
                if (previous.HasValue)
                {
                    var prev = previous.Value;
                    if (char.IsUpper(ch) && (char.IsLower(prev) || char.IsDigit(prev))) boundary = true;
                    if (char.IsDigit(ch) != char.IsDigit(prev)) boundary = true;
                }
                if (boundary && current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                current.Append(ch);
                previous = ch;
            }
            if (current.Length > 0) parts.Add(current.ToString());
        }
        return parts;
    }
}

/// <summary>Speech engines write "eleven" as "11" (and occasionally the
/// reverse). Used by vocabulary matching and calibration.</summary>
public static class NumberWords
{
    public static readonly Dictionary<string, string> WordToDigits = new()
    {
        ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4",
        ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9",
        ["ten"] = "10", ["eleven"] = "11", ["twelve"] = "12", ["thirteen"] = "13",
        ["fourteen"] = "14", ["fifteen"] = "15", ["sixteen"] = "16", ["seventeen"] = "17",
        ["eighteen"] = "18", ["nineteen"] = "19", ["twenty"] = "20", ["thirty"] = "30",
        ["forty"] = "40", ["fifty"] = "50", ["sixty"] = "60", ["seventy"] = "70",
        ["eighty"] = "80", ["ninety"] = "90", ["hundred"] = "100", ["thousand"] = "1000"
    };

    public static readonly Dictionary<string, string> DigitsToWord = BuildReverse();

    private static Dictionary<string, string> BuildReverse()
    {
        var map = new Dictionary<string, string>();
        foreach (var (word, digits) in WordToDigits)
        {
            if (!map.ContainsKey(digits)) map[digits] = word;
        }
        return map;
    }

    /// <summary>Rewrites standalone number words as digits (lowercase input).</summary>
    public static string Digitize(string text) =>
        string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => WordToDigits.TryGetValue(t, out var d) ? d : t));
}
