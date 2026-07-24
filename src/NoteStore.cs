using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PurpleStarNotes;

/// <summary>
/// Loads and saves notes to a JSON file under the user's AppData folder:
///   %AppData%\PurpleStarNotes\notes.json
/// </summary>
public static class NoteStore
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PurpleStarNotes");

    private static string FilePath => Path.Combine(Folder, "notes.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static List<Note> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<Note>();

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<Note>>(json, Options) ?? new List<Note>();
        }
        catch
        {
            // If the file is corrupt or unreadable, start fresh rather than crashing.
            return new List<Note>();
        }
    }

    public static void Save(IEnumerable<Note> notes)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string json = JsonSerializer.Serialize(notes, Options);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Saving is best-effort; swallow IO errors so the UI stays responsive.
        }
    }
}
