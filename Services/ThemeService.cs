using System.Windows;

namespace ImageMinify.Services;

/// <summary>
/// Swaps the live palette dictionary. Because every colour in the application is reached through a
/// <c>DynamicResource</c>, replacing the dictionary is the whole of a theme change — no window
/// needs to be reloaded and no control needs to be told.
/// </summary>
public static class ThemeService
{
    private const string LightPalette = "Themes/Palette.Light.xaml";
    private const string DarkPalette = "Themes/Palette.Dark.xaml";

    /// <summary>Which palette is currently merged into the application's resources.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>
    /// Merges the palette for <paramref name="isDark"/>, replacing whichever one is loaded. Idempotent.
    /// </summary>
    public static void Apply(bool isDark)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        IsDark = isDark;

        var dictionaries = application.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary
        {
            Source = new Uri(isDark ? DarkPalette : LightPalette, UriKind.Relative),
        };

        // The palette must stay at the position it was declared in, ahead of Controls.xaml. Its keys
        // do not collide with anything, but a dictionary appended at the end would be searched first
        // on every lookup in the application, for no reason.
        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (IsPalette(dictionaries[i]))
            {
                dictionaries[i] = replacement;
                return;
            }
        }

        dictionaries.Insert(0, replacement);
    }

    /// <summary>Flips to the other palette and reports which one is now live.</summary>
    public static bool Toggle()
    {
        Apply(!IsDark);
        return IsDark;
    }

    private static bool IsPalette(ResourceDictionary dictionary)
    {
        // Source is null for a dictionary declared inline, which the palettes never are — but the
        // root application dictionary itself shows up in this list on some load paths, so the null
        // check is what keeps this from matching it.
        var source = dictionary.Source?.OriginalString;
        return source is not null && source.Contains("Palette.", StringComparison.OrdinalIgnoreCase);
    }
}
