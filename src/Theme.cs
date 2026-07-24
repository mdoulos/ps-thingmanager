using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PurpleStarNotes;

public enum AppTheme { Dark, Light }

/// <summary>
/// Runtime theming. All themed brushes live in the Application resources under
/// keys like "TextPrimaryBrush" and are referenced from XAML via DynamicResource,
/// so replacing them here instantly re-skins the whole UI.
/// </summary>
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    // key -> (dark color, light color)
    private static readonly Dictionary<string, (string dark, string light)> Palette = new()
    {
        ["WindowBg"]      = ("#16161E", "#F4F4F8"),
        ["SidebarBg"]     = ("#1B1B24", "#ECECF2"),
        ["PanelBg"]       = ("#16161E", "#FFFFFF"),
        ["Selected"]      = ("#262633", "#E4DEF7"),
        ["Hover"]         = ("#20202B", "#E7E7EE"),
        ["Field"]         = ("#20202B", "#E9E9F0"),
        ["Border"]        = ("#2C2C3A", "#D9D9E2"),
        ["Accent"]        = ("#7C5CFF", "#7C5CFF"),
        ["AccentSoft"]    = ("#552E2A5A", "#33B9A9FF"),
        ["MenuBg"]        = ("#23232F", "#FFFFFF"),
        ["TextPrimary"]   = ("#ECECF1", "#20202A"),
        ["TextSecondary"] = ("#9A9AAB", "#5A5A6A"),
        ["TextMuted"]     = ("#6E6E7E", "#8A8A99"),
    };

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Color DefaultTextColor =>
        Current == AppTheme.Dark ? C("#ECECF1") : C("#20202A");

    public static Color MutedColor =>
        Current == AppTheme.Dark ? C("#6E6E7E") : C("#8A8A99");

    /// <summary>Apply a theme by (re)creating every themed brush resource.</summary>
    public static void Apply(AppTheme theme)
    {
        Current = theme;
        var res = Application.Current.Resources;
        foreach (var kv in Palette)
        {
            string hex = theme == AppTheme.Dark ? kv.Value.dark : kv.Value.light;
            res[kv.Key + "Brush"] = new SolidColorBrush(C(hex));
        }
    }

    public static void Toggle() =>
        Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
