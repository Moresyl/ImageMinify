using System.Xml.Linq;

namespace ImageMinify.Tests;

/// <summary>
/// Guards the palettes' structure by reading the dictionaries themselves, so a mistake in the XAML
/// that compiles cleanly still fails here.
/// </summary>
/// <remarks>
/// The source is read rather than the compiled BAML. Loading BAML means a <c>pack://</c> URI, and
/// resolving one of those needs a live <c>Application</c> to have registered the scheme's handlers —
/// which no unit test should have to start, and which makes whichever test touches it first behave
/// differently from the rest. The csproj copies the two files next to the test binary.
/// </remarks>
public class PaletteTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static TheoryData<string> Palettes => new() { "Light", "Dark" };

    /// <summary>
    /// The one fill in either palette that may not be see-through.
    /// </summary>
    /// <remarks>
    /// Brush.Bg.Base is the window's ground and every other surface is a translucent sheet stacked on
    /// it. Give the base any alpha below full and the window has nothing behind it — the desktop does
    /// not show through, because a WPF window composites against black — so the whole interface turns
    /// dark and every card reads as grey. That shipped once: the window had been painted transparent
    /// to reveal a Mica backdrop that the compositor then declined to draw.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Palettes))]
    public void BackgroundBase_IsOpaque(string palette)
    {
        var color = ReadBrushes(palette)["Brush.Bg.Base"];

        Assert.True(
            IsOpaque(color),
            $"Palette.{palette}.xaml declares Brush.Bg.Base as {color}, which is not fully opaque.");
    }

    /// <summary>
    /// Both palettes have to declare exactly the same keys. One that exists in only a single palette
    /// resolves to nothing after a theme swap, which is a hole or a crash rather than a wrong colour.
    /// </summary>
    [Fact]
    public void Palettes_DeclareTheSameKeys()
    {
        var light = ReadKeys("Light");
        var dark = ReadKeys("Dark");

        Assert.Empty(light.Except(dark));
        Assert.Empty(dark.Except(light));
    }

    /// <summary>
    /// Every brush names a colour that exists. A <c>StaticResource</c> pointing at a key that was
    /// renamed throws at load time, taking the whole application down on the theme it belongs to.
    /// </summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public void EveryBrush_NamesAColourThatExists(string palette)
    {
        foreach (var (key, color) in ReadBrushes(palette))
        {
            Assert.True(
                color.StartsWith('#'),
                $"Palette.{palette}.xaml: {key} resolves to '{color}', which is not a colour literal.");
        }
    }

    /// <summary>An <c>#RRGGBB</c> literal, or an <c>#AARRGGBB</c> one whose alpha is FF.</summary>
    private static bool IsOpaque(string color) =>
        color.Length == 7 || (color.Length == 9 && color.StartsWith("#FF", StringComparison.OrdinalIgnoreCase));

    /// <summary>Every brush in the palette, with its <c>StaticResource</c> colour already resolved.</summary>
    private static Dictionary<string, string> ReadBrushes(string palette)
    {
        var root = Load(palette);

        var colors = root.Elements()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(element => (string)element.Attribute(Xaml + "Key")!, element => element.Value.Trim());

        return root.Elements()
            .Where(element => element.Name.LocalName == "SolidColorBrush")
            .ToDictionary(
                element => (string)element.Attribute(Xaml + "Key")!,
                element => Resolve((string)element.Attribute("Color")!, colors));
    }

    private static HashSet<string> ReadKeys(string palette) =>
        Load(palette)
            .Elements()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .OfType<string>()
            .ToHashSet();

    /// <summary>Turns <c>{StaticResource Color.Bg.Base}</c> into the literal it points at.</summary>
    private static string Resolve(string value, IReadOnlyDictionary<string, string> colors)
    {
        if (!value.StartsWith('{'))
        {
            return value;
        }

        var key = value.Trim('{', '}').Replace("StaticResource", string.Empty).Trim();
        return colors.TryGetValue(key, out var color) ? color : $"(unresolved: {key})";
    }

    private static XElement Load(string palette) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Themes", $"Palette.{palette}.xaml")).Root!;
}
