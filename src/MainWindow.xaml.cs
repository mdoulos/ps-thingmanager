using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;

namespace SimpleNotes;

public partial class MainWindow : Window
{
    // Master list of every note (backs the sidebar).
    private readonly ObservableCollection<Note> _notes = new();

    // Tags shown for the currently open note.
    private readonly ObservableCollection<string> _currentTags = new();

    private Note? _current;

    // Debounce timer so we persist to disk shortly after the user stops typing
    // instead of on every keystroke.
    private readonly DispatcherTimer _saveTimer;

    // Guards against feedback loops while we programmatically update the UI
    // (setting the title/document/combos would otherwise re-trigger handlers).
    private bool _syncing;

    private string _search = "";

    public MainWindow()
    {
        InitializeComponent();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); PersistCurrent(); };

        TagsList.ItemsSource = _currentTags;

        LoadAllNotes();

        Closing += (_, _) => { PersistCurrent(); NoteStore.Save(_notes); };
    }

    // ============================================================
    // Loading / list management
    // ============================================================

    private void LoadAllNotes()
    {
        var loaded = NoteStore.Load();

        // First run: seed a friendly welcome note so the app isn't empty.
        if (loaded.Count == 0)
            loaded.Add(CreateWelcomeNote());

        foreach (var n in loaded.OrderByDescending(n => n.Modified))
            _notes.Add(n);

        RefreshList();

        if (_notes.Count > 0)
            NotesList.SelectedItem = _notes[0];
    }

    /// <summary>Applies the current search filter and (re)binds the list.</summary>
    private void RefreshList()
    {
        IEnumerable<Note> view = _notes.OrderByDescending(n => n.Modified);

        if (!string.IsNullOrWhiteSpace(_search))
        {
            string q = _search.Trim();
            view = view.Where(n =>
                n.DisplayTitle.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                n.Preview.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                n.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        var selected = _current;
        NotesList.ItemsSource = view.ToList();
        if (selected != null && NotesList.Items.Contains(selected))
            NotesList.SelectedItem = selected;
    }

    // ============================================================
    // Selection & editing
    // ============================================================

    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NotesList.SelectedItem is not Note note || note == _current)
            return;

        // Save whatever was open before switching away.
        PersistCurrent();
        LoadNoteIntoEditor(note);
    }

    private void LoadNoteIntoEditor(Note note)
    {
        _syncing = true;
        _current = note;

        TitleBox.Text = note.Title;

        if (!string.IsNullOrWhiteSpace(note.ContentXaml))
        {
            try
            {
                using var sr = new StringReader(note.ContentXaml);
                using var xr = XmlReader.Create(sr);
                Editor.Document = (FlowDocument)XamlReader.Load(xr);
            }
            catch
            {
                Editor.Document = new FlowDocument();
            }
        }
        else
        {
            Editor.Document = new FlowDocument();
        }

        _currentTags.Clear();
        foreach (var t in note.Tags)
            _currentTags.Add(t);

        _syncing = false;
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e) => ScheduleSave();

    private void Editor_TextChanged(object sender, TextChangedEventArgs e) => ScheduleSave();

    private void ScheduleSave()
    {
        if (_syncing || _current == null)
            return;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes the editor's current state back into the note and to disk.</summary>
    private void PersistCurrent()
    {
        if (_current == null)
            return;

        _current.Title = TitleBox.Text;
        _current.ContentXaml = XamlWriter.Save(Editor.Document);

        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        string text = range.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
        _current.Preview = text.Length > 140 ? text.Substring(0, 140) : text;

        _current.Tags = _currentTags.ToList();
        _current.Modified = DateTime.Now;

        NoteStore.Save(_notes);
    }

    // ============================================================
    // Toolbar buttons: new / sort / delete / search
    // ============================================================

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        PersistCurrent();

        var note = new Note { Title = "" };
        _notes.Add(note);
        _search = "";
        SearchBox.Text = "";

        RefreshList();
        NotesList.SelectedItem = note;   // triggers load
        TitleBox.Focus();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
            return;

        var result = MessageBox.Show(
            $"Delete \"{_current.DisplayTitle}\"?",
            "Delete note", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK)
            return;

        var toRemove = _current;
        _current = null;
        _notes.Remove(toRemove);

        // Never leave the app with zero notes.
        if (_notes.Count == 0)
            _notes.Add(new Note { Title = "" });

        NoteStore.Save(_notes);
        RefreshList();
        NotesList.SelectedItem = _notes.OrderByDescending(n => n.Modified).First();
    }

    private void SortButton_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text;
        RefreshList();
    }

    // ============================================================
    // Tags
    // ============================================================

    private void TagInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        string tag = TagInput.Text.Trim().Trim('/');
        TagInput.Text = "";

        if (string.IsNullOrEmpty(tag) ||
            _currentTags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            return;

        _currentTags.Add(tag);
        ScheduleSave();
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is string tag)
        {
            _currentTags.Remove(tag);
            ScheduleSave();
        }
    }

    // ============================================================
    // Rich-text formatting
    // ============================================================

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBold.Execute(null, Editor);
        Editor.Focus();
    }

    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleItalic.Execute(null, Editor);
        Editor.Focus();
    }

    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleUnderline.Execute(null, Editor);
        Editor.Focus();
    }

    private void Strike_Click(object sender, RoutedEventArgs e)
    {
        var current = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                      as TextDecorationCollection;
        bool hasStrike = current != null &&
                         current.Any(d => d.Location == TextDecorationLocation.Strikethrough);

        Editor.Selection.ApplyPropertyValue(
            Inline.TextDecorationsProperty,
            hasStrike ? null : TextDecorations.Strikethrough);
        Editor.Focus();
    }

    private void Bullets_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBullets.Execute(null, Editor);
        Editor.Focus();
    }

    private void Numbering_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleNumbering.Execute(null, Editor);
        Editor.Focus();
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Editor == null)
            return;

        if (FontFamilyCombo.SelectedItem is ComboBoxItem item && item.Content is string family)
        {
            Editor.Selection.ApplyPropertyValue(
                TextElement.FontFamilyProperty, new FontFamily(family));
            Editor.Focus();
        }
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || FontSizeCombo.SelectedItem is not ComboBoxItem item)
            return;
        ApplyFontSize(item.Content?.ToString());
    }

    private void FontSizeCombo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ApplyFontSize(FontSizeCombo.Text);
    }

    private void ApplyFontSize(string? raw)
    {
        if (_syncing || Editor == null)
            return;

        if (double.TryParse(raw, out double size) && size > 0 && size <= 400)
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            Editor.Focus();
        }
    }

    /// <summary>Keeps the toolbar in sync with the formatting under the caret.</summary>
    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        try
        {
            object weight = Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            BoldBtn.IsChecked = weight is FontWeight fw && fw == FontWeights.Bold;

            object style = Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty);
            ItalicBtn.IsChecked = style is FontStyle fs && fs == FontStyles.Italic;

            var deco = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                       as TextDecorationCollection;
            UnderlineBtn.IsChecked = deco != null &&
                deco.Any(d => d.Location == TextDecorationLocation.Underline);
            StrikeBtn.IsChecked = deco != null &&
                deco.Any(d => d.Location == TextDecorationLocation.Strikethrough);

            object sizeVal = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            if (sizeVal is double d)
                FontSizeCombo.Text = ((int)Math.Round(d)).ToString();

            object famVal = Editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty);
            if (famVal is FontFamily family)
            {
                foreach (var obj in FontFamilyCombo.Items)
                    if (obj is ComboBoxItem ci && ci.Content is string name &&
                        name.Equals(family.Source, StringComparison.OrdinalIgnoreCase))
                    {
                        FontFamilyCombo.SelectedItem = ci;
                        break;
                    }
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    // ============================================================
    // Seed content
    // ============================================================

    private static Note CreateWelcomeNote()
    {
        var note = new Note
        {
            Title = "Welcome to Simple Notes",
            Tags = new List<string> { "Getting Started" },
            Modified = DateTime.Now
        };

        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run(
            "This is your first note. A few things to try:")));

        var list = new System.Windows.Documents.List(); // bulleted list
        foreach (var line in new[]
        {
            "Click the + button (top-left) to create a new note.",
            "Give a note a title above, and add tags like Games/EU4.",
            "Select text and use the toolbar for bold, italics, and font size.",
            "Everything is saved automatically as you type."
        })
        {
            list.ListItems.Add(new ListItem(new Paragraph(new Run(line))));
        }
        doc.Blocks.Add(list);

        note.ContentXaml = XamlWriter.Save(doc);
        note.Preview = "This is your first note. A few things to try: Click the + button...";
        return note;
    }
}
