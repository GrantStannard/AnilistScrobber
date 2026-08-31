using System;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.AniListScrobbler.Matching;

/// <summary>
/// Compares anime titles, which differ across databases mostly in punctuation, romanisation
/// and trailing season markers.
/// </summary>
public static class TitleSimilarity
{
    /// <summary>
    /// Reduces a title to lowercase alphanumerics and single spaces, dropping accents.
    /// </summary>
    /// <param name="value">The title.</param>
    /// <returns>The normalised form.</returns>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Scores two titles from 0 (unrelated) to 1 (identical once normalised).
    /// </summary>
    /// <param name="left">The first title.</param>
    /// <param name="right">The second title.</param>
    /// <returns>The similarity ratio.</returns>
    public static double Compare(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1;
        }

        var distance = Levenshtein(a, b);
        var longest = Math.Max(a.Length, b.Length);
        return 1.0 - ((double)distance / longest);
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
