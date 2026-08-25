using System.Collections.Frozen;
using System.Globalization;
using System.Windows;

namespace ImageMinify.Helpers;

/// <summary>The two languages the interface is written in.</summary>
public enum Language
{
    Chinese,
    English,
}

/// <summary>
/// Every word the interface says, in whichever language is currently chosen.
/// </summary>
/// <remarks>
/// <para>
/// Two consumers, one table. The markup reaches the strings as <c>DynamicResource</c> keys, which is
/// what makes a language change instant and total — the same mechanism <c>ThemeService</c> uses for
/// colours, and for the same reason: no window is reloaded and no control is told anything. Code
/// reaches them through <see cref="Get"/> and <see cref="Format"/>, which read the table directly and
/// so keep working in a test, where there is no <see cref="Application"/> at all.
/// </para>
/// <para>
/// The table itself is in <c>Strings.Table.cs</c>, one row per string with both languages on
/// the same line. Anything else — a file per language, or a pair of resource dictionaries — puts the
/// two halves of a translation where they cannot be read against each other, and a wording that
/// drifted apart in one of them is then invisible.
/// </para>
/// <para>
/// It lives in <c>Helpers</c> rather than beside <c>ThemeService</c> because the models need it: a
/// preset's name and a result's status are display strings, and a model reaching sideways into
/// <c>Services</c> to say a word would be the wrong direction for that dependency to run.
/// </para>
/// </remarks>
public static partial class Strings
{
    /// <summary>
    /// Marks the dictionary this class owns, so it can be found again without holding a reference to
    /// it across a resource rebuild.
    /// </summary>
    private const string Marker = "Str.__Language";

    private static FrozenDictionary<string, string> _lookup;

    /// <summary>Which language the interface is in.</summary>
    public static Language Current { get; private set; } = Language.Chinese;

    /// <remarks>
    /// The starting language is chosen here rather than in the field initializer above, and it has to
    /// be: the initializers of a partial class run in an unspecified order across its files, the table
    /// is declared in the other one, and reading it from an initializer finds it null. A static
    /// constructor runs after every initializer in every part, which is the guarantee this needs.
    /// </remarks>
    static Strings()
    {
        _lookup = Build(Language.Chinese);
    }

    /// <summary>Puts the interface into <paramref name="language"/>. Idempotent.</summary>
    /// <remarks>
    /// Raises nothing. Text that was assembled in code and stored — a summary line, a status message
    /// — does not re-resolve itself the way a <c>DynamicResource</c> does, but the caller that
    /// changed the language is also the one holding that text, so an event would only be a longer
    /// route back to the same place.
    /// </remarks>
    public static void Apply(Language language)
    {
        Current = language;
        _lookup = Build(language);

        PublishToResources();
    }

    /// <summary>The string for <paramref name="key"/>, or the key itself if there is no such string.</summary>
    /// <remarks>
    /// Returning the key rather than throwing or returning empty is deliberate: a missing string then
    /// shows up in the interface as the thing that is missing, named, instead of as a blank space
    /// that could be a layout bug or a dozen other things.
    /// </remarks>
    public static string Get(string key) =>
        _lookup.TryGetValue(key, out var value) ? value : key;

    /// <summary>The string for <paramref name="key"/> with <paramref name="args"/> substituted in.</summary>
    /// <remarks>
    /// Invariant culture, because every argument reaching this point has already been formatted for
    /// display by the code that knew what it was — a file size, a percentage, a count. Formatting
    /// them a second time against the current culture is how a figure ends up rounded twice.
    /// </remarks>
    public static string Format(string key, params object?[] args)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, Get(key), args);
        }
        catch (FormatException)
        {
            // A placeholder that does not match what was passed. The literal is more use here than
            // an exception out of a status line.
            return Get(key);
        }
    }

    /// <summary>Every key in the table, for the test that checks nothing was left half-translated.</summary>
    public static IReadOnlyCollection<string> Keys => _lookup.Keys;

    private static FrozenDictionary<string, string> Build(Language language) =>
        Table.ToFrozenDictionary(
            row => row.Key,
            row => language == Language.English ? row.English : row.Chinese,
            StringComparer.Ordinal);

    /// <remarks>
    /// Appended rather than inserted. WPF searches merged dictionaries from the last one back, so
    /// this puts the strings first in the search path — which is where the dictionary the interface
    /// hits for every visible label belongs, and costs the other lookups one failed hash probe
    /// against the smallest dictionary in the application.
    /// </remarks>
    private static void PublishToResources()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var replacement = new ResourceDictionary { [Marker] = Current.ToString() };

        foreach (var pair in _lookup)
        {
            replacement[pair.Key] = pair.Value;
        }

        var dictionaries = application.Resources.MergedDictionaries;

        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (dictionaries[i].Contains(Marker))
            {
                // Replacing the whole dictionary rather than assigning key by key: one invalidation
                // pass instead of one per string, over a tree where every label is a subscriber.
                dictionaries[i] = replacement;
                return;
            }
        }

        dictionaries.Add(replacement);
    }
}
