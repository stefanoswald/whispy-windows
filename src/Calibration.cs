using System.Text.RegularExpressions;

namespace Whispy;

/// <summary>
/// Voice calibration: the user reads a known script (a phonetically rich
/// passage plus their vocabulary terms). Comparing what was said to what the
/// engine heard yields a recognition-accuracy score and the exact way each
/// term gets misheard — which becomes a correction rule.
/// This does NOT retrain Whisper (a fixed model); it teaches Whispy how to
/// repair this speaker's specific errors.
/// </summary>
public static class Calibration
{
    public const string Passage =
        "The quick gray fox judged the bright yellow zebra while thunder rolled over the church. " +
        "She measured five thick books, then asked whether the jolly captain would bring oysters, " +
        "cheese, and vanilla ice cream for the picnic. Please pause, breathe, and speak clearly.";

    public const int MaxTerms = 20;

    public static string Script(List<string> terms)
    {
        var lines = new List<string> { Passage, "" };
        int i = 1;
        foreach (var term in terms.Take(MaxTerms))
        {
            lines.Add($"Number {i}: {term}.");
            i++;
        }
        return string.Join(Environment.NewLine, lines);
    }

    public class TermResult
    {
        public string Term = "";
        public string Heard = "";   // "" if the line was not found

        /// <summary>True only when the vocabulary matcher can't already repair
        /// what was heard (e.g. "lydia" for "Lygia"); "11 labs" for
        /// "ElevenLabs" is fixed automatically and needs no rule.</summary>
        public bool NeedsCorrection
        {
            get
            {
                if (Heard.Length == 0) return false;
                var repaired = Vocabulary.Correct(Heard, new List<string> { Term });
                return Squash(repaired) != Squash(Term);
            }
        }
    }

    /// <summary>Letters and digits only, lowercased.</summary>
    public static string Squash(string s) =>
        new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public class Analysis
    {
        public double Accuracy;     // 0..1 word accuracy on the passage
        public List<TermResult> Results = new();
        public List<TermResult> Suggestions => Results.Where(r => r.NeedsCorrection).ToList();
        public int MissedCount => Results.Count(r => r.Heard.Length == 0);
    }

    public static Analysis Analyze(string transcript, List<string> terms)
    {
        var normalized = Normalize(transcript);
        var used = terms.Take(MaxTerms).ToList();

        var passagePart = normalized;
        int cut = normalized.IndexOf("number 1 ", StringComparison.Ordinal);
        if (cut >= 0) passagePart = normalized.Substring(0, cut);
        var accuracy = WordAccuracy(Normalize(Passage), passagePart);

        var heardByIndex = new Dictionary<int, string>();
        foreach (Match m in Regex.Matches(normalized, "number (\\d+) (.*?)(?= number \\d+ |$)"))
        {
            if (int.TryParse(m.Groups[1].Value, out int index))
            {
                heardByIndex[index] = m.Groups[2].Value.Trim();
            }
        }

        var analysis = new Analysis { Accuracy = accuracy };
        for (int i = 0; i < used.Count; i++)
        {
            analysis.Results.Add(new TermResult
            {
                Term = used[i],
                Heard = heardByIndex.TryGetValue(i + 1, out var h) ? h : ""
            });
        }
        return analysis;
    }

    /// <summary>Lowercase, punctuation stripped, number words as digits, single spaces.</summary>
    public static string Normalize(string s)
    {
        var cleaned = Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        return NumberWords.Digitize(cleaned);
    }

    public static double WordAccuracy(string expected, string actual)
    {
        var e = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var a = actual.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (e.Length == 0) return 1;
        return Math.Max(0, 1 - (double)Levenshtein(e, a) / e.Length);
    }

    private static int Levenshtein(string[] a, string[] b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
