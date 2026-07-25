using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PurpleStarNotes;

/// <summary>A named group that tags can belong to (a tag may be in several).</summary>
public class TagGroup
{
    public string Name { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}

/// <summary>Per-tag appearance: an optional color and icon glyph.</summary>
public class TagMeta
{
    public string Tag { get; set; } = "";
    public string? Color { get; set; }   // hex like "#E5484D"; null = default accent
    public string? Icon { get; set; }     // Segoe MDL2 glyph; null = default tag glyph
}

/// <summary>Display options for a group-bookmark location (e.g. Main Window Top).</summary>
public class GroupBarOptions
{
    public bool ShowTagColors { get; set; } = true;
    public bool ShowGroupName { get; set; } = true;
    public string Presentation { get; set; } = "Normal";   // "Normal" | "Stretch"
}

/// <summary>Small persisted app settings (theme, tag groups, group locations).</summary>
public class AppSettings
{
    public string Theme { get; set; } = "Dark";

    /// <summary>User-defined tag groups.</summary>
    public List<TagGroup> TagGroups { get; set; } = new();

    /// <summary>Names of the tag groups pinned to the "Main Window (Top)" bar.</summary>
    public List<string> MainWindowTopGroups { get; set; } = new();

    /// <summary>Per-tag color/icon overrides.</summary>
    public List<TagMeta> TagStyles { get; set; } = new();

    /// <summary>Display options for the Main Window (Top) bookmark bar.</summary>
    public GroupBarOptions MainWindowTopOptions { get; set; } = new();

    private static string FilePath =>
        Path.Combine(NoteStore.Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
                       ?? new AppSettings();
        }
        catch { /* fall through to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(NoteStore.Folder);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }

    public AppTheme ThemeEnum =>
        string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Light : AppTheme.Dark;
}
