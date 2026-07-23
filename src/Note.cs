using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SimpleNotes;

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
    /// <summary>Plain-text snippet shown in the sidebar (cached for speed).</summary>
    public string Preview
    {
        get => _preview;
        set { _preview = value; OnPropertyChanged(); OnPropertyChanged(nameof(Snippet)); }
    }

    public List<string> Tags { get; set; } = new();

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
    public string Snippet => string.IsNullOrWhiteSpace(Preview) ? "No additional text" : Preview;

    [JsonIgnore]
    public string ModifiedDisplay => "Modified " + Modified.ToString("dddd, MMM d, yyyy, hh:mm tt");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
