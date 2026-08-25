using System.Text.RegularExpressions;

namespace ImageMinify.Helpers;

/// <summary>Selects the current language from a bilingual release note and removes basic Markdown.</summary>
public static partial class ReleaseNotesFormatter
{
    public static string ForDisplay(string? notes, Language language)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        var selected = SelectLanguage(notes, language == Language.Chinese ? "zh" : "en");
        return LinkPattern().Replace(BoldPattern().Replace(HeadingPattern().Replace(selected, string.Empty), "$1"), "$1")
            .Replace("<!-- imageminify-notes:zh -->", string.Empty, StringComparison.Ordinal)
            .Replace("<!-- imageminify-notes:en -->", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string SelectLanguage(string notes, string language)
    {
        var marker = $"<!-- imageminify-notes:{language} -->";
        var start = notes.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return notes;
        }

        var bodyStart = start + marker.Length;
        var next = notes.IndexOf("<!-- imageminify-notes:", bodyStart, StringComparison.Ordinal);
        return notes[bodyStart..(next < 0 ? notes.Length : next)];
    }

    [GeneratedRegex("^#{1,6}\\s+", RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex("\\*\\*([^*]+)\\*\\*")]
    private static partial Regex BoldPattern();

    [GeneratedRegex("\\[([^]]+)]\\([^)]*\\)")]
    private static partial Regex LinkPattern();
}
