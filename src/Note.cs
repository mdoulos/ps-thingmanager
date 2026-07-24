using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PurpleStarNotes;

/// <summary>
/// A single note. Implements INotifyPropertyChanged so that the note list in
/// the sidebar updates live as the note is edited.
/// </summary>
public class Note : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _title = "Untitled Note";
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTitle)); }
    }

    /// <summary>The note body, serialized as WPF FlowDocument XAML.</summary>
    public string ContentXaml { get; set; } = "";

    private string _preview = "";
    /// <summary>Auto-generated plain-text preview of the body (cached for speed).</summary>
    public string Preview
    {
        get => _preview;
        set { _preview = value; OnPropertyChanged(); OnPropertyChanged(nameof(Snippet)); }
    }

    private string _customSnippet = "";
    /// <summary>User-set snippet; when present it is shown in the sidebar instead of the preview.</summary>
    public string CustomSnippet
    {
        get => _customSnippet;
        set { _customSnippet = value; OnPropertyChanged(); OnPropertyChanged(nameof(Snippet)); }
    }

    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Per-note remembered font size for each text category (Normal/H1/H2/H3).
    /// When the user resizes text, the size for that category is stored here so
    /// new text of the same category picks up the same custom size.
    /// </summary>
    public Dictionary<string, double> CategorySizes { get; set; } = new();

    private DateTime _modified = DateTime.Now;
    public DateTime Modified
    {
        get => _modified;
        set { _modified = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModifiedDisplay)); }
    }

    // ---- Computed display helpers (not persisted) ----

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled Note" : Title;

    [JsonIgnore]
    public string Snippet =>
        !string.IsNullOrWhiteSpace(CustomSnippet) ? CustomSnippet
        : !string.IsNullOrWhiteSpace(Preview) ? Preview
        : "No additional text";

    [JsonIgnore]
    public string ModifiedDisplay => "Modified " + Modified.ToString("dddd, MMM d, yyyy, hh:mm tt");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
