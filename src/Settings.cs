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

/// <summary>Small persisted app settings (theme, tag groups, group locations).</summary>
public class AppSettings
{
    public string Theme { get; set; } = "Dark";

    /// <summary>User-defined tag groups.</summary>
    public List<TagGroup> TagGroups { get; set; } = new();

    /// <summary>Names of the tag groups pinned to the "Main Window (Top)" bar.</summary>
    public List<string> MainWindowTopGroups { get; set; } = new();

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
