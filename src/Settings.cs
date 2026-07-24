using System;
using System.IO;
using System.Text.Json;

namespace PurpleStarNotes;

/// <summary>Small persisted app settings (currently just the chosen theme).</summary>
public class AppSettings
{
    public string Theme { get; set; } = "Dark";

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
