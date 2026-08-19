using System.Text.RegularExpressions;

namespace Whispy;

/// <summary>Result of scanning a raw transcript for spoken commands.</summary>
public class ParsedTranscript
{
    public string Text = "";
    public bool CopyOnly;
    public bool DeletePreviousInsertion;   // utterance was just "scratch that"
    public WritingMode? ModeOverride;      // "turn this into an email/prompt"
}

/// <summary>
/// Parses spoken voice commands out of a transcript before cleanup runs.
/// Direct port of the proven Mac Whispy logic.
/// </summary>
public static class CommandParser
{
    public static ParsedTranscript Parse(string raw, AppSettings settings)
    {
        var result = new ParsedTranscript { Text = raw.Trim() };
        if (!settings.SpokenCommands) return result;

        // 1. Utterance that is ONLY "scratch that" / "delete that".
        var normalizedAll = Normalize(result.Text);
        if (normalizedAll == "scratch that" || normalizedAll == "delete that")
        {
            result.DeletePreviousInsertion = true;
            result.Text = "";
            return result;
        }

        // 2. Trailing meta commands (chainable).
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (phrase, action) in MetaCommands)
            {
                var pattern = "[,.!?\\s]*\\b" + phrase + "\\b[,.!?\\s]*$";
                var m = Regex.Match(result.Text, pattern, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    result.Text = result.Text.Remove(m.Index, m.Length);
                    action(result);
                    changed = true;
                }
            }
        }

        // 3. In-text corrections.
        result.Text = ApplyScratch(result.Text);

        // 4. Structural commands.
        result.Text = ApplyStructure(result.Text);

        // 5. Spoken punctuation.
        if (settings.SpokenPunctuation)
        {
            result.Text = ApplyPunctuation(result.Text);
        }

        result.Text = result.Text.Trim();
        return result;
    }

    private static readonly (string, Action<ParsedTranscript>)[] MetaCommands =
    {
        ("turn this into an email", r => r.ModeOverride = WritingMode.Email),
        ("turn that into an email", r => r.ModeOverride = WritingMode.Email),
        ("turn this into a prompt", r => r.ModeOverride = WritingMode.Prompt),
        ("turn that into a prompt", r => r.ModeOverride = WritingMode.Prompt),
        ("copy only", r => r.CopyOnly = true),
        ("do not insert", r => r.CopyOnly = true),
        ("don['’]t insert", r => r.CopyOnly = true)
    };

    private static string ApplyScratch(string input)
    {
        var text = input;
        string[] phrases = { "delete last sentence", "scratch that", "delete that" };
        for (int safety = 0; safety < 20; safety++)
        {
            Match? earliest = null;
            foreach (var phrase in phrases)
            {
                var m = Regex.Match(text, "\\b" + phrase + "\\b[,.!?]?", RegexOptions.IgnoreCase);
                if (m.Success && (earliest == null || m.Index < earliest.Index))
                {
                    earliest = m;
                }
            }
            if (earliest == null) break;

            var before = text.Substring(0, earliest.Index).TrimEnd(' ', ',');
            var after = text.Substring(earliest.Index + earliest.Length);

            var boundary = before.LastIndexOfAny(new[] { '.', '!', '?', '\n' });
            text = boundary >= 0
                ? before.Substring(0, boundary + 1) + " " + after
                : after;
        }
        return text;
    }

    private static string ApplyStructure(string input)
    {
        var text = input;
        (string, string)[] rules =
        {
            ("[,.!?]?\\s*\\bnew paragraph\\b[,.!?]?\\s*", "\n\n"),
            ("[,.!?]?\\s*\\bnew line\\b[,.!?]?\\s*", "\n"),
            ("[,.!?]?\\s*\\bbullet point\\b[,.!?]?\\s*", "\n- "),
            ("[,.!?]?\\s*\\bnumbered list\\b[,.!?]?\\s*", "\n1. ")
        };
        foreach (var (pattern, replacement) in rules)
        {
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }
        return text;
    }

    private static string ApplyPunctuation(string input)
    {
        var text = input;
        // "period"/"full stop" only at end of utterance or before a capital —
        // avoids eating "a period of time".
        text = Regex.Replace(text,
            "\\s*\\b(?:[Pp]eriod|[Ff]ull [Ss]top)\\b\\.?(?=\\s+[A-Z]|\\s*$)", ".");
        (string, string)[] simple =
        {
            ("\\s*\\bcomma\\b", ","),
            ("\\s*\\bquestion mark\\b", "?"),
            ("\\s*\\bexclamation (?:point|mark)\\b", "!"),
            ("\\s*\\bsemicolon\\b", ";"),
            ("\\s*\\bcolon\\b", ":")
        };
        foreach (var (pattern, replacement) in simple)
        {
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }
        return text;
    }

    private static string Normalize(string s)
    {
        var words = Regex.Split(s.ToLowerInvariant(), "[^a-z0-9]+")
            .Where(w => w.Length > 0);
        return string.Join(" ", words);
    }
}
