using System.Text.RegularExpressions;
using ImageMinify.Helpers;

namespace ImageMinify.Tests;

/// <summary>
/// Guards the string table against the two ways it goes wrong: a row that is only half translated,
/// and a key the interface asks for that nobody wrote.
/// </summary>
/// <remarks>
/// Neither failure is visible while writing the code. A missing key resolves to nothing in XAML and
/// shows as a blank label; a row translated on one side only shows the other language's words in the
/// middle of a sentence. Both survive a clean build and a full manual pass in whichever language the
/// person testing happens to read.
/// </remarks>
public sealed class StringsTests : IDisposable
{
    /// <summary>Matches a placeholder in a format string, e.g. <c>{0}</c> or <c>{1:F2}</c>.</summary>
    private static readonly Regex Placeholder = new(@"\{(\d+)[^}]*\}", RegexOptions.Compiled);

    /// <summary>Matches a <c>{DynamicResource Str.Something}</c> reference in markup.</summary>
    private static readonly Regex MarkupKey = new(@"\{DynamicResource (Str\.[A-Za-z0-9.]+)\}", RegexOptions.Compiled);

    private static readonly Regex XmlComment = new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void EveryKey_HasBothLanguages()
    {
        var missing = new List<string>();

        foreach (var key in Strings.Keys)
        {
            Strings.Apply(Language.Chinese);
            var chinese = Strings.Get(key);

            Strings.Apply(Language.English);
            var english = Strings.Get(key);

            if (string.IsNullOrWhiteSpace(chinese) || string.IsNullOrWhiteSpace(english))
            {
                missing.Add(key);
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// A translation may reorder its placeholders, but it may not invent or drop one: an extra
    /// <c>{3}</c> throws at run time, and a missing one silently loses the figure the sentence was
    /// written to carry.
    /// </summary>
    [Fact]
    public void EveryTranslation_UsesTheSamePlaceholders()
    {
        var mismatched = new List<string>();

        foreach (var key in Strings.Keys)
        {
            Strings.Apply(Language.Chinese);
            var chinese = Indices(Strings.Get(key));

            Strings.Apply(Language.English);
            var english = Indices(Strings.Get(key));

            if (!chinese.SetEquals(english))
            {
                mismatched.Add(key);
            }
        }

        Assert.Empty(mismatched);
    }

    [Fact]
    public void EnglishTranslations_ContainNoChineseCharacters()
    {
        Strings.Apply(Language.English);

        var untranslated = Strings.Keys
            .Where(key => Regex.IsMatch(Strings.Get(key), @"\p{IsCJKUnifiedIdeographs}"))
            .ToList();

        Assert.Empty(untranslated);
    }

    /// <summary>
    /// The check that catches a label added to the window without a row added to the table. A key
    /// with no string resolves to nothing in XAML, so the failure in the interface is a blank space.
    /// </summary>
    [Fact]
    public void EveryKeyTheWindowAsksFor_IsInTheTable()
    {
        var markup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.xaml"));
        var known = Strings.Keys.ToHashSet(StringComparer.Ordinal);

        var referenced = MarkupKey.Matches(markup)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Guards the guard: a markup file that stopped matching the pattern would otherwise pass by
        // finding nothing at all to check.
        Assert.True(referenced.Count > 50, $"Only {referenced.Count} keys found in the markup.");

        var unwritten = referenced.Where(key => !known.Contains(key)).ToList();

        Assert.Empty(unwritten);
    }

    [Fact]
    public void WindowMarkup_HasNoHardCodedChineseText()
    {
        var markup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.xaml"));
        var visibleMarkup = XmlComment.Replace(markup, string.Empty);

        Assert.DoesNotMatch(@"\p{IsCJKUnifiedIdeographs}", visibleMarkup);
    }

    [Fact]
    public void Apply_ChangesWhatGetReturns()
    {
        Strings.Apply(Language.Chinese);
        var chinese = Strings.Get("Str.Run.Start");

        Strings.Apply(Language.English);

        Assert.Equal(Language.English, Strings.Current);
        Assert.NotEqual(chinese, Strings.Get("Str.Run.Start"));
    }

    /// <summary>An unknown key comes back as itself, so a gap shows up named rather than blank.</summary>
    [Fact]
    public void Get_ReturnsTheKey_WhenThereIsNoSuchString()
    {
        Assert.Equal("Str.Nope", Strings.Get("Str.Nope"));
    }

    /// <summary>
    /// Format is called from a status line and a summary dialog, where a mismatch between the
    /// placeholders and the arguments must not take down the run that produced them.
    /// </summary>
    [Fact]
    public void Format_FallsBackToTheLiteral_WhenTheArgumentsDoNotFit()
    {
        Assert.Equal(Strings.Get("Str.Source.Count"), Strings.Format("Str.Source.Count"));
    }

    /// <summary>Leaves the table in the language the rest of the suite assumes.</summary>
    public void Dispose() => Strings.Apply(Language.Chinese);

    private static HashSet<string> Indices(string value) =>
        Placeholder.Matches(value).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
}
