namespace Seed.Market.Indicators;

/// <summary>
/// Lightweight VADER-inspired sentiment scorer for headlines.
/// Returns a compound score in [-1, 1] where -1 is extremely negative, +1 is extremely positive.
/// Uses a curated crypto-specific lexicon rather than a full VADER port.
/// </summary>
public static class VaderSentiment
{
    private static readonly Dictionary<string, float> Lexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        // Strong positive
        ["surge"] = 2.5f, ["soar"] = 2.5f, ["skyrocket"] = 3f, ["moon"] = 2f,
        ["bullish"] = 2f, ["breakout"] = 2f, ["rally"] = 2f, ["pump"] = 1.5f,
        ["boom"] = 2f, ["all-time high"] = 3f, ["ath"] = 2.5f, ["adoption"] = 1.5f,
        ["breakthrough"] = 2f, ["milestone"] = 1.5f, ["record"] = 1.5f,

        // Moderate positive
        ["gain"] = 1f, ["rise"] = 1f, ["climb"] = 1f, ["up"] = 0.5f,
        ["growth"] = 1f, ["positive"] = 1f, ["bullrun"] = 2f, ["accumulate"] = 1f,
        ["approve"] = 1.5f, ["approval"] = 1.5f, ["etf"] = 0.5f, ["institutional"] = 0.5f,
        ["upgrade"] = 1f, ["recover"] = 1f, ["recovery"] = 1f, ["support"] = 0.5f,
        ["partnership"] = 1f, ["launch"] = 0.5f, ["integration"] = 0.5f,

        // Strong negative
        ["crash"] = -3f, ["plunge"] = -2.5f, ["collapse"] = -3f, ["dump"] = -2f,
        ["bearish"] = -2f, ["bloodbath"] = -3f, ["liquidation"] = -2f,
        ["scam"] = -2.5f, ["hack"] = -2.5f, ["exploit"] = -2f, ["rug"] = -3f,
        ["ponzi"] = -3f, ["fraud"] = -3f, ["insolvent"] = -3f, ["bankrupt"] = -3f,
        ["ban"] = -2f, ["crackdown"] = -2f, ["panic"] = -2f,

        // Moderate negative
        ["drop"] = -1f, ["fall"] = -1f, ["decline"] = -1f, ["down"] = -0.5f,
        ["loss"] = -1f, ["sell"] = -0.5f, ["selloff"] = -1.5f, ["fear"] = -1.5f,
        ["risk"] = -0.5f, ["warning"] = -1f, ["concern"] = -0.5f, ["uncertain"] = -0.5f,
        ["regulation"] = -0.5f, ["investigate"] = -1f, ["lawsuit"] = -1.5f,
        ["delay"] = -0.5f, ["reject"] = -1.5f, ["fail"] = -1f,

        // Intensifiers
        ["very"] = 0f, ["extremely"] = 0f, ["massive"] = 0f, ["huge"] = 0f,
    };

    private static readonly HashSet<string> Intensifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "very", "extremely", "massive", "huge", "major", "significant",
        "unprecedented", "historic", "biggest"
    };

    private static readonly HashSet<string> Negators = new(StringComparer.OrdinalIgnoreCase)
    {
        "not", "no", "never", "neither", "nobody", "nothing",
        "nowhere", "nor", "cannot", "can't", "won't", "don't", "doesn't"
    };

    public static float Score(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0f;

        var words = Tokenize(text);
        float totalScore = 0;
        int scoredWords = 0;
        bool negated = false;
        bool intensified = false;

        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];

            if (Negators.Contains(word))
            {
                negated = true;
                continue;
            }

            if (Intensifiers.Contains(word))
            {
                intensified = true;
                continue;
            }

            if (Lexicon.TryGetValue(word, out float val) && val != 0)
            {
                if (intensified) val *= 1.5f;
                if (negated) val *= -0.75f;
                totalScore += val;
                scoredWords++;
            }

            negated = false;
            intensified = false;
        }

        // Also check bigrams for multi-word entries
        for (int i = 0; i < words.Length - 1; i++)
        {
            string bigram = $"{words[i]} {words[i + 1]}";
            if (Lexicon.TryGetValue(bigram, out float val) && val != 0)
            {
                totalScore += val;
                scoredWords++;
            }
        }

        if (scoredWords == 0) return 0f;

        // Normalize: compound score approach
        float compound = totalScore / MathF.Sqrt(totalScore * totalScore + 15f);
        return MathF.Max(-1f, MathF.Min(1f, compound));
    }

    private static string[] Tokenize(string text)
    {
        var cleaned = text.ToLowerInvariant()
            .Replace("'", "'")
            .Replace("'", "'");

        return cleaned.Split(
            [' ', ',', '.', '!', '?', ';', ':', '"', '(', ')', '[', ']', '{', '}', '\t', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
