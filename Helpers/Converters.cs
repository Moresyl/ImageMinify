using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ImageMinify.Models;

namespace ImageMinify.Helpers;

/// <summary>
/// Every value converter the views use.
/// </summary>
/// <remarks>
/// One file rather than one per class. These are three-line adapters between a view model property
/// and a dependency property; scattering them across a dozen files makes the set harder to see and
/// invites a second converter that does what an existing one already does.
/// </remarks>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true;

        if (IsInverted(parameter))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is Visibility.Visible;
        return IsInverted(parameter) ? !visible : visible;
    }

    /// <summary>
    /// Reads the "invert" flag out of the converter parameter.
    /// </summary>
    /// <remarks>
    /// A parameter rather than a second converter class, because half the bindings in a window are
    /// the negation of the other half and two near-identical resources would be one more thing to
    /// keep in step.
    /// </remarks>
    internal static bool IsInverted(object? parameter) =>
        parameter is string text && text.Equals("invert", StringComparison.OrdinalIgnoreCase);
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>
/// True when the bound value equals the converter parameter. Drives the segmented pickers.
/// </summary>
/// <remarks>
/// The alternative — a bool property per option — is one property, one backing field and one change
/// notification for every choice on screen, all of which have to be kept mutually exclusive by hand.
/// This binds each segment straight to the single enum or string that actually holds the answer.
/// </remarks>
public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return value is null && parameter is null;
        }

        // Compared as text so one converter serves both the enum pickers and the format picker,
        // whose value is a string. Enum.ToString gives the member name, which is what the XAML
        // parameter spells.
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Only the segment being turned on has anything to say. The one being turned off fires too,
        // and writing its parameter back would overwrite the choice the user just made.
        if (value is not true || parameter is null)
        {
            return Binding.DoNothing;
        }

        var text = parameter.ToString() ?? string.Empty;
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (type.IsEnum)
        {
            return Enum.TryParse(type, text, ignoreCase: true, out var parsed) ? parsed : Binding.DoNothing;
        }

        if (type == typeof(bool))
        {
            return bool.TryParse(text, out var flag) ? flag : Binding.DoNothing;
        }

        return text;
    }
}

/// <summary>
/// True when its two inputs are equal. The two-value form of <see cref="EqualityConverter"/>, for
/// the case where the thing being compared against is itself a binding rather than a literal.
/// </summary>
/// <remarks>
/// A converter parameter cannot be bound, so a template that has to ask "is <em>this</em> item the
/// selected one?" — where both sides come from data — has no way to express it with a single-value
/// converter. The preset buttons are that case.
/// </remarks>
public sealed class MultiEqualityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return false;
        }

        var first = values[0]?.ToString();
        var second = values[1]?.ToString();

        return !string.IsNullOrEmpty(first) && string.Equals(first, second, StringComparison.Ordinal);
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound object is not null.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var present = value is not null and not "";

        if (BoolToVisibilityConverter.IsInverted(parameter))
        {
            present = !present;
        }

        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when a count is greater than zero. Empty-state panels invert it.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var any = value is int count && count > 0;

        if (BoolToVisibilityConverter.IsInverted(parameter))
        {
            any = !any;
        }

        return any ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a severity onto the palette. Resolved through the application's resources rather than a
/// hard-coded colour, so the answer follows the theme.
/// </summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    /// <summary>Which family of the palette to return: the text colour, the fill, or the stroke.</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var suffix = (parameter as string) switch
        {
            "Bg" => ".Bg",
            "Stroke" => ".Stroke",
            _ => string.Empty,
        };

        var family = (value as StatusSeverity?) switch
        {
            StatusSeverity.Success => "Brush.Success",
            StatusSeverity.Warning => "Brush.Warning",
            StatusSeverity.Error => "Brush.Danger",
            _ => "Brush.Info",
        };

        return Application.Current?.TryFindResource(family + suffix) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a severity onto its glyph.</summary>
public sealed class SeverityToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as StatusSeverity?) switch
        {
            StatusSeverity.Success => "Icon.Success",
            StatusSeverity.Warning => "Icon.Warning",
            StatusSeverity.Error => "Icon.Error",
            _ => "Icon.Info",
        };

        return Application.Current?.TryFindResource(key) as Geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Colours a result row's outcome: compressed, kept as-is, or failed.</summary>
public sealed class ResultStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as ResultState?) switch
        {
            ResultState.Compressed => "Brush.Success",
            ResultState.Skipped => "Brush.Text.Tertiary",
            _ => "Brush.Danger",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// A magnification, as a percentage.
/// </summary>
/// <remarks>
/// A decimal place only below 10%, where the whole usable range is 5% to 9% and rounding would print
/// the same number for every one of them. Above that a fraction of a percent is noise on a figure
/// nobody reads to two significant figures.
/// </remarks>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double fraction || double.IsNaN(fraction))
        {
            return string.Empty;
        }

        return (fraction * 100).ToString(fraction < 0.1d ? "0.#" : "0", culture) + "%";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
