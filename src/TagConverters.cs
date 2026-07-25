using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;

namespace PurpleStarNotes;

/// <summary>Shared helpers + converters for per-tag icon and color.</summary>
public static class TagStyleLookup
{
    // The default tag glyph (Segoe MDL2 "Tag") and accent color used when a tag
    // has no custom icon/color.
    public const string DefaultIcon = "\uE8EC";   // Segoe MDL2 "Tag"
    public const string AccentHex = "#7C5CFF";

    public static TagMeta? MetaFor(string? tag)
    {
        if (string.IsNullOrEmpty(tag) || MainWindow.ActiveSettings == null)
            return null;
        return MainWindow.ActiveSettings.TagStyles
            .FirstOrDefault(m => m.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
    }

    public static string IconFor(string? tag)
    {
        var m = MetaFor(tag);
        return string.IsNullOrEmpty(m?.Icon) ? DefaultIcon : m!.Icon!;
    }

    public static Brush ColorBrushFor(string? tag)
    {
        var m = MetaFor(tag);
        string hex = string.IsNullOrEmpty(m?.Color) ? AccentHex : m!.Color!;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(AccentHex)); }
    }
}

/// <summary>Binds a tag name to its icon glyph (custom or the default tag glyph).</summary>
public class TagIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => TagStyleLookup.IconFor(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Binds a tag name to its color brush (custom or the accent color).</summary>
public class TagColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => TagStyleLookup.ColorBrushFor(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
