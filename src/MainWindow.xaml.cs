using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;

namespace PurpleStarNotes;

/// <summary>A note reduced to heading + snippet for the combined group view.</summary>
public class CombinedCard
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Note> _notes = new();
    private readonly ObservableCollection<string> _currentTags = new();
    private Note? _current;
    private readonly DispatcherTimer _saveTimer;
    private bool _syncing;
    private string _search = "";
    private string _tagFilter = "";
    private bool _changelogMode;
    private string _combinedTag = "";          // non-empty => main pane shows the combined view
    private readonly AppSettings _settings;

    // Working copies used while the Tag Groups / Group Locations popups are open.
    private List<TagGroup> _workingGroups = new();
    private string? _workingGroupName;         // group currently selected in the popup
    private List<string> _workingTopGroups = new();
    private string _renamingTag = "";          // tag being renamed in the rename popup

    // Default font size for each paragraph category (overridable per note).
    private static readonly Dictionary<string, double> DefaultSizes = new()
    {
        ["Normal"] = 14, ["H1"] = 32, ["H2"] = 24, ["H3"] = 18,
        ["Bulleted"] = 14, ["Numbered"] = 14, ["Check"] = 14
    };

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();               // theme already applied in App.OnStartup
        UpdateThemeGlyph();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); PersistCurrent(); };

        TagsList.ItemsSource = _currentTags;
        TagsPopupList.ItemsSource = _currentTags;
        NumberedMenuIcon.Content = MakeNumberedIcon(14);
        VersionText.Text = "v" + Updater.CurrentVersion.ToString(3);

        LoadAllNotes();
        UpdateGroupChrome();

        Closing += (_, _) => { PersistCurrent(); NoteStore.Save(_notes); };
    }

    // ============================================================
    // Loading / list management
    // ============================================================

    private void LoadAllNotes()
    {
        var loaded = NoteStore.Load();
        if (loaded.Count == 0)
            loaded.Add(CreateWelcomeNote());

        foreach (var n in loaded.OrderByDescending(n => n.Modified))
            _notes.Add(n);

        EnsureChangelogNotes();

        RefreshList();

        // Never open a changelog entry on startup; pick the newest regular note.
        var firstRegular = _notes.Where(n => !n.IsChangelog)
                                 .OrderByDescending(n => n.Modified)
                                 .FirstOrDefault();
        if (firstRegular != null)
            NotesList.SelectedItem = firstRegular;
    }

    private void RefreshList()
    {
        UpdateTagButtonVisibility();
        UpdateSidebarChrome();

        foreach (var n in _notes)
            n.VisibleTags = VisibleTagsFor(n);

        IEnumerable<Note> view = _notes.OrderByDescending(n => n.Modified);

        if (_changelogMode)
        {
            // Changelog view: only the internal changelog entries.
            view = view.Where(n => n.IsChangelog);
        }
        else
        {
            // Normal view: changelog entries are hidden and reachable only via the menu.
            view = view.Where(n => !n.IsChangelog);
            if (!string.IsNullOrEmpty(_tagFilter))
                view = view.Where(n => n.Tags.Any(t => t.Equals(_tagFilter, StringComparison.OrdinalIgnoreCase)));
        }

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

    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NotesList.SelectedItem is not Note note || note == _current)
            return;

        // Selecting a note leaves the combined group view and shows the editor.
        if (_combinedTag.Length > 0)
        {
            _combinedTag = "";
            ShowCombinedView(false);
        }

        PersistCurrent();
        LoadNoteIntoEditor(note);
    }

    // Toggle the main pane between the editor and the combined group view.
    private void ShowCombinedView(bool on)
    {
        CombinedView.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        EditorRoot.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    // Sidebar title + the "+" button depend on whether the changelog is showing.
    private void UpdateSidebarChrome()
    {
        SidebarTitle.Text = _changelogMode ? "Changelog" : "Notes";
        AddButton.Visibility = _changelogMode ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LoadNoteIntoEditor(Note note)
    {
        _syncing = true;
        _current = note;

        TitleBox.Text = note.Title;
        Editor.Document = ParseDocument(note.ContentXaml);
        NormalizeChecklists();

        _currentTags.Clear();
        foreach (var t in note.Tags)
            if (!t.Equals(Note.ChangelogTag, StringComparison.OrdinalIgnoreCase))
                _currentTags.Add(t);   // the changelog tag stays hidden even here

        UpdateEditorChrome(note);

        _syncing = false;
        UpdateFormatIcon();
        UpdateAlignIcon();
    }

    // Changelog entries show an "Exit Changelog" button instead of the
    // description/delete actions in the top-right of the editor.
    private void UpdateEditorChrome(Note note)
    {
        bool cl = note.IsChangelog;
        SnippetButton.Visibility = cl ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.Visibility = cl ? Visibility.Collapsed : Visibility.Visible;
        ExitChangelogButton.Visibility = cl ? Visibility.Visible : Visibility.Collapsed;

        // Changelog entries are read-only; regular notes stay editable. Hide the
        // tag editor and formatting toolbar for a clean, view-only changelog.
        Editor.IsReadOnly = cl;
        TitleBox.IsReadOnly = cl;
        TagsRow.Visibility = cl ? Visibility.Collapsed : Visibility.Visible;
        FormatToolbar.Visibility = cl ? Visibility.Collapsed : Visibility.Visible;

        // Changelog content sits inside a distinct, full-height box separated from
        // the top bar; a normal note's editor stays borderless.
        if (cl)
        {
            EditorBox.Background = (Brush)FindResource("FieldBrush");
            EditorBox.BorderBrush = (Brush)FindResource("BorderBrush");
            EditorBox.BorderThickness = new Thickness(1);
            EditorBox.CornerRadius = new CornerRadius(10);
            EditorBox.Padding = new Thickness(18, 14, 18, 14);
            EditorBox.Margin = new Thickness(0, 6, 0, 0);
        }
        else
        {
            EditorBox.Background = Brushes.Transparent;
            EditorBox.BorderBrush = Brushes.Transparent;
            EditorBox.BorderThickness = new Thickness(0);
            EditorBox.CornerRadius = new CornerRadius(0);
            EditorBox.Padding = new Thickness(0);
            EditorBox.Margin = new Thickness(0);
        }
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

    private void PersistCurrent()
    {
        // Changelog entries are view-only: never overwrite their content or bump
        // their date (which would scramble the version ordering).
        if (_current == null || _current.IsChangelog)
            return;

        _current.Title = TitleBox.Text;
        try
        {
            _current.ContentXaml = XamlWriter.Save(Editor.Document);
        }
        catch
        {
            // Keep the previously saved content if serialization ever fails.
        }

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
    // New / delete / sort / search
    // ============================================================

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        PersistCurrent();
        var note = new Note { Title = "" };
        _notes.Add(note);
        _changelogMode = false;   // leave the changelog when creating a note
        _combinedTag = "";
        _tagFilter = "";
        ShowCombinedView(false);
        _search = "";
        SearchBox.Text = "";
        RefreshList();
        NotesList.SelectedItem = note;
        TitleBox.Focus();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current != null)
            DeleteNote(_current);
    }

    private void DeleteNote(Note note)
    {
        var result = MessageBox.Show(this,
            $"Delete \"{note.DisplayTitle}\"?",
            "Delete note", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK)
            return;

        if (note == _current)
            _current = null;
        _notes.Remove(note);

        // Keep at least one regular note around (changelog entries don't count).
        if (!_notes.Any(n => !n.IsChangelog))
            _notes.Add(new Note { Title = "" });

        NoteStore.Save(_notes);
        RefreshList();
        var next = _notes.Where(n => !n.IsChangelog)
                         .OrderByDescending(n => n.Modified)
                         .FirstOrDefault();
        if (next != null)
            NotesList.SelectedItem = next;
    }

    // ---- Right-click context menu on note list items ----

    private static Note? NoteFrom(object sender)
    {
        // Resolve the note from the context menu's placement target (the note row).
        if (sender is MenuItem mi && mi.Parent is ContextMenu cm &&
            cm.PlacementTarget is FrameworkElement target && target.DataContext is Note n)
            return n;
        return (sender as FrameworkElement)?.DataContext as Note;
    }

    private void CtxTags_Click(object sender, RoutedEventArgs e)
    {
        var note = NoteFrom(sender);
        if (note == null) return;
        NotesList.SelectedItem = note;   // load it so _currentTags is populated
        OpenTagsPopup();
    }

    private void CtxSnippet_Click(object sender, RoutedEventArgs e)
    {
        var note = NoteFrom(sender);
        if (note == null) return;
        NotesList.SelectedItem = note;
        OpenSnippetPopup();
    }

    private void CtxDelete_Click(object sender, RoutedEventArgs e)
    {
        var note = NoteFrom(sender);
        if (note != null)
            DeleteNote(note);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text;
        RefreshList();
    }

    // ---- Filter the note list by tag ----

    private IEnumerable<string> AllTags() =>
        _notes.SelectMany(n => n.Tags)
              .Where(t => !t.Equals(Note.ChangelogTag, StringComparison.OrdinalIgnoreCase))
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

    // Tags to show on a note in the sidebar: all of them, minus the hidden
    // changelog tag and the active filter tag.
    private List<string> VisibleTagsFor(Note n) =>
        n.Tags.Where(t =>
                !t.Equals(Note.ChangelogTag, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(_tagFilter) ||
                 !t.Equals(_tagFilter, StringComparison.OrdinalIgnoreCase)))
              .ToList();

    // The Tags button only appears once at least one tag exists; a stale filter
    // (its tag was removed) is cleared.
    private void UpdateTagButtonVisibility()
    {
        bool any = AllTags().Any();
        TagsHeaderButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrEmpty(_tagFilter) &&
            !AllTags().Any(t => t.Equals(_tagFilter, StringComparison.OrdinalIgnoreCase)))
            _tagFilter = "";
    }

    private void TagsHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        BuildTagFilterMenu();
        TagFilterPopup.IsOpen = true;
    }

    private void BuildTagFilterMenu()
    {
        TagFilterPanel.Children.Clear();

        // All notes.
        TagFilterPanel.Children.Add(MakeFilterRow("All notes", "", string.IsNullOrEmpty(_tagFilter)));

        var allTags = AllTags().ToList();
        var grouped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Tag groups (above the normal tags, each under its own header).
        foreach (var g in GroupsSorted())
        {
            var members = g.Tags
                .Where(t => allTags.Any(a => a.Equals(t, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (members.Count == 0)
                continue;
            TagFilterPanel.Children.Add(MakeGroupHeaderRow(g.Name));
            foreach (var t in members)
            {
                grouped.Add(t);
                TagFilterPanel.Children.Add(MakeFilterRow(
                    t, t, t.Equals(_tagFilter, StringComparison.OrdinalIgnoreCase), indent: 12));
            }
        }

        // Ungrouped tags.
        var ungrouped = allTags.Where(t => !grouped.Contains(t)).ToList();
        if (ungrouped.Count > 0 && grouped.Count > 0)
            TagFilterPanel.Children.Add(MakeSeparator());
        foreach (var t in ungrouped)
            TagFilterPanel.Children.Add(
                MakeFilterRow(t, t, t.Equals(_tagFilter, StringComparison.OrdinalIgnoreCase)));

        // Changelog (visually separated at the bottom of the tag list).
        TagFilterPanel.Children.Add(MakeSeparator());
        TagFilterPanel.Children.Add(MakeActionRow("Changelog", "\uE81C", (_, _) =>
        {
            TagFilterPopup.IsOpen = false;
            EnterChangelog();
        }));

        // Tag/group management actions.
        TagFilterPanel.Children.Add(MakeSeparator());
        TagFilterPanel.Children.Add(MakeActionRow("Manage Tags", "\uE8EC", (_, _) =>
        {
            TagFilterPopup.IsOpen = false;
            OpenManageTags();
        }));
        bool hasGroups = _settings.TagGroups.Count > 0;
        TagFilterPanel.Children.Add(MakeActionRow(hasGroups ? "Manage Groups" : "Add Group", "\uE8B7", (_, _) =>
        {
            TagFilterPopup.IsOpen = false;
            OpenTagGroups();
        }));
    }

    private Button MakeFilterRow(string label, string value, bool selected, double indent = 0)
    {
        var b = new Button { Style = (Style)FindResource("MenuRowButton"), Tag = value };
        if (indent > 0)
            b.Margin = new Thickness(indent, 0, 0, 0);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock
        {
            Text = value == "" ? "\uE8FD" : "\uE8EC",   // all-notes (list) / tag glyph
            FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
            Width = 24, VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty,
            value == "" ? "TextSecondaryBrush" : "AccentBrush");
        var txt = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(icon);
        sp.Children.Add(txt);
        b.Content = sp;
        if (selected)
            b.Background = (Brush)FindResource("AccentSoftBrush");
        b.Click += TagFilter_Selected;
        return b;
    }

    // Non-clickable header row shown above a tag group's member tags.
    private FrameworkElement MakeGroupHeaderRow(string name)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 7, 10, 2) };
        var icon = new TextBlock
        {
            Text = "\uE8B7", FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12, Width = 22, VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        var txt = new TextBlock
        {
            Text = name, FontWeight = FontWeights.SemiBold, FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        txt.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        sp.Children.Add(icon);
        sp.Children.Add(txt);
        return sp;
    }

    private Border MakeSeparator()
    {
        var b = new Border { Height = 1, Margin = new Thickness(6, 5, 6, 5) };
        b.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        return b;
    }

    private Button MakeActionRow(string label, string glyph, RoutedEventHandler handler)
    {
        var b = new Button { Style = (Style)FindResource("MenuRowButton") };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14, Width = 24, VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var txt = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(icon);
        sp.Children.Add(txt);
        b.Content = sp;
        b.Click += handler;
        return b;
    }

    private void TagFilter_Selected(object sender, RoutedEventArgs e)
    {
        _tagFilter = (string)((Button)sender).Tag;
        // Selecting All notes or a tag leaves the changelog / combined view.
        bool wasSpecial = _changelogMode || _combinedTag.Length > 0;
        _changelogMode = false;
        _combinedTag = "";
        ShowCombinedView(false);
        TagFilterPopup.IsOpen = false;
        RefreshList();
        if (wasSpecial || _current == null || !NotesList.Items.Contains(_current))
        {
            if (NotesList.Items.Count > 0)
                NotesList.SelectedItem = NotesList.Items[0];
        }
    }

    private IEnumerable<TagGroup> GroupsSorted() =>
        _settings.TagGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase);

    // ============================================================
    // Hamburger menu / changelog
    // ============================================================

    private void MenuButton_Click(object sender, RoutedEventArgs e) => MenuPopup.IsOpen = true;

    private void ChangelogMenu_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        EnterChangelog();
    }

    // Show only the internal changelog entries and open the newest one.
    private void EnterChangelog()
    {
        PersistCurrent();
        _changelogMode = true;
        _combinedTag = "";
        ShowCombinedView(false);
        _tagFilter = "";
        _search = "";
        SearchBox.Text = "";
        RefreshList();

        if (NotesList.Items.Count > 0)
            NotesList.SelectedItem = NotesList.Items[0];
    }

    private void ExitChangelog_Click(object sender, RoutedEventArgs e) => ExitChangelog();
    private void CtxExitChangelog_Click(object sender, RoutedEventArgs e) => ExitChangelog();

    // Leave the changelog and return to the normal note list.
    private void ExitChangelog()
    {
        PersistCurrent();
        _changelogMode = false;
        RefreshList();

        var firstRegular = _notes.Where(n => !n.IsChangelog)
                                 .OrderByDescending(n => n.Modified)
                                 .FirstOrDefault();
        if (firstRegular != null)
            NotesList.SelectedItem = firstRegular;
    }

    // ============================================================
    // Group bookmark bar + combined group view
    // ============================================================

    private void UpdateGroupChrome()
    {
        GroupLocationsMenuItem.Visibility =
            _settings.TagGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshGroupBar();
    }

    // Build the "Main Window (Top)" bookmark bar from the pinned tag groups.
    private void RefreshGroupBar()
    {
        GroupBarPanel.Children.Clear();
        var pinned = _settings.MainWindowTopGroups
            .Select(name => _settings.TagGroups.FirstOrDefault(
                g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Where(g => g != null && g.Tags.Count > 0)
            .Cast<TagGroup>()
            .ToList();

        if (pinned.Count == 0)
        {
            GroupBar.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var g in pinned)
        {
            var section = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 18, 3) };
            var label = new TextBlock
            {
                Text = g.Name, FontWeight = FontWeights.SemiBold, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            section.Children.Add(label);
            foreach (var tag in g.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                section.Children.Add(MakeBookmarkButton(tag));
            GroupBarPanel.Children.Add(section);
        }
        GroupBar.Visibility = Visibility.Visible;
    }

    private Button MakeBookmarkButton(string tag)
    {
        var b = new Button
        {
            Tag = tag, Margin = new Thickness(0, 0, 6, 0),
            Style = (Style)FindResource("BookmarkButton")
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0)
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var txt = new TextBlock { Text = tag, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        txt.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        sp.Children.Add(icon);
        sp.Children.Add(txt);
        b.Content = sp;
        b.Click += (_, _) => EnterCombined(tag);
        return b;
    }

    // Show the combined heading+snippet view for all notes carrying a tag.
    private void EnterCombined(string tag)
    {
        PersistCurrent();
        _changelogMode = false;
        _combinedTag = tag;
        _tagFilter = tag;
        _search = "";
        SearchBox.Text = "";
        _current = null;            // stop RefreshList reselecting into the editor
        RefreshList();
        NotesList.SelectedItem = null;

        CombinedTitle.Text = tag;
        var cards = _notes
            .Where(n => !n.IsChangelog &&
                        n.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(n => n.Modified)
            .Select(n => new CombinedCard { Title = n.DisplayTitle, Body = CombinedBody(n) })
            .ToList();
        CombinedList.ItemsSource = cards;
        ShowCombinedView(true);
    }

    // Combined-card snippet: Description, else custom snippet, else first 20 words.
    private static string CombinedBody(Note n)
    {
        if (!string.IsNullOrWhiteSpace(n.Description))
            return n.Description.Trim();
        if (!string.IsNullOrWhiteSpace(n.CustomSnippet))
            return n.CustomSnippet.Trim();
        return FirstWords(n.Preview, 20);
    }

    private static string FirstWords(string text, int count)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= count
            ? string.Join(' ', words)
            : string.Join(' ', words.Take(count)) + "…";
    }

    // ============================================================
    // Manage Tags
    // ============================================================

    private int NoteCountForTag(string tag) =>
        _notes.Count(n => !n.IsChangelog &&
                          n.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)));

    private void OpenManageTags()
    {
        BuildManageTagsList();
        ManageTagsPopup.IsOpen = true;
    }

    private void CloseManageTags_Click(object sender, RoutedEventArgs e) => ManageTagsPopup.IsOpen = false;

    private void BuildManageTagsList()
    {
        ManageTagsList.Children.Clear();
        var tags = AllTags().ToList();
        if (tags.Count == 0)
        {
            var empty = new TextBlock { Text = "No tags yet.", Margin = new Thickness(4, 6, 4, 6) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            ManageTagsList.Children.Add(empty);
            return;
        }
        foreach (var tag in tags)
            ManageTagsList.Children.Add(MakeManageTagRow(tag));
    }

    private FrameworkElement MakeManageTagRow(string tag)
    {
        var grid = new Grid { Margin = new Thickness(2, 2, 2, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = tag, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Grid.SetColumn(name, 0);

        int count = NoteCountForTag(tag);
        var countText = new TextBlock
        {
            Text = count + (count == 1 ? " note" : " notes"),
            VerticalAlignment = VerticalAlignment.Center, FontSize = 12,
            Margin = new Thickness(8, 0, 8, 0)
        };
        countText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        Grid.SetColumn(countText, 1);

        var rename = new Button
        {
            Content = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
            Style = (Style)FindResource("IconButton"), Width = 30, Height = 30,
            ToolTip = "Rename tag", Tag = tag
        };
        rename.Click += (s, _) => OpenRenameTag((string)((Button)s).Tag);
        Grid.SetColumn(rename, 2);

        var del = new Button
        {
            Content = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
            Style = (Style)FindResource("IconButton"), Width = 30, Height = 30,
            ToolTip = "Delete tag", Tag = tag
        };
        del.Click += (s, _) => DeleteTag((string)((Button)s).Tag);
        Grid.SetColumn(del, 3);

        grid.Children.Add(name);
        grid.Children.Add(countText);
        grid.Children.Add(rename);
        grid.Children.Add(del);
        return grid;
    }

    private void DeleteTag(string tag)
    {
        int count = NoteCountForTag(tag);
        var first = MessageBox.Show(this,
            $"Delete the tag \"{tag}\"? It will be removed from {count} note{(count == 1 ? "" : "s")}.",
            "Delete tag", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (first != MessageBoxResult.OK)
            return;
        var second = MessageBox.Show(this,
            $"Are you sure? This permanently removes \"{tag}\" and cannot be undone.",
            "Delete tag", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (second != MessageBoxResult.OK)
            return;

        RemoveTagEverywhere(tag);
        BuildManageTagsList();
    }

    // Remove a tag from every note and every group, and clear it from filters.
    private void RemoveTagEverywhere(string tag)
    {
        foreach (var n in _notes)
            n.Tags.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        foreach (var g in _settings.TagGroups)
            g.Tags.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));

        if (_tagFilter.Equals(tag, StringComparison.OrdinalIgnoreCase))
            _tagFilter = "";
        if (_combinedTag.Equals(tag, StringComparison.OrdinalIgnoreCase))
        {
            _combinedTag = "";
            ShowCombinedView(false);
        }
        SyncCurrentTagsFromNote();

        _settings.Save();
        NoteStore.Save(_notes);
        UpdateGroupChrome();
        RefreshList();
    }

    private void OpenRenameTag(string tag)
    {
        _renamingTag = tag;
        RenameTagHeading.Text = $"Rename \"{tag}\"";
        RenameTagInput.Text = tag;
        RenameTagPopup.IsOpen = true;
        RenameTagInput.Focus();
        RenameTagInput.SelectAll();
    }

    private void RenameTagInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitRenameTag();
        else if (e.Key == Key.Escape) RenameTagPopup.IsOpen = false;
    }

    private void RenameTagSave_Click(object sender, RoutedEventArgs e) => CommitRenameTag();
    private void RenameTagCancel_Click(object sender, RoutedEventArgs e) => RenameTagPopup.IsOpen = false;

    private void CommitRenameTag()
    {
        string oldTag = _renamingTag;
        string newTag = RenameTagInput.Text.Trim().Trim('/');
        if (!string.IsNullOrEmpty(newTag) && !string.IsNullOrEmpty(oldTag) &&
            !newTag.Equals(oldTag, StringComparison.OrdinalIgnoreCase))
            RenameTagEverywhere(oldTag, newTag);
        RenameTagPopup.IsOpen = false;
        BuildManageTagsList();
    }

    private void RenameTagEverywhere(string oldTag, string newTag)
    {
        foreach (var n in _notes)
        {
            for (int i = 0; i < n.Tags.Count; i++)
                if (n.Tags[i].Equals(oldTag, StringComparison.OrdinalIgnoreCase))
                    n.Tags[i] = newTag;
            n.Tags = DedupeTags(n.Tags);
        }
        foreach (var g in _settings.TagGroups)
        {
            for (int i = 0; i < g.Tags.Count; i++)
                if (g.Tags[i].Equals(oldTag, StringComparison.OrdinalIgnoreCase))
                    g.Tags[i] = newTag;
            g.Tags = DedupeTags(g.Tags);
        }
        if (_tagFilter.Equals(oldTag, StringComparison.OrdinalIgnoreCase)) _tagFilter = newTag;
        if (_combinedTag.Equals(oldTag, StringComparison.OrdinalIgnoreCase)) _combinedTag = newTag;
        SyncCurrentTagsFromNote();

        _settings.Save();
        NoteStore.Save(_notes);
        UpdateGroupChrome();
        RefreshList();
    }

    private void SyncCurrentTagsFromNote()
    {
        if (_current == null) return;
        _currentTags.Clear();
        foreach (var t in _current.Tags)
            if (!t.Equals(Note.ChangelogTag, StringComparison.OrdinalIgnoreCase))
                _currentTags.Add(t);
    }

    private static List<string> DedupeTags(List<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var t in tags)
            if (seen.Add(t))
                result.Add(t);
        return result;
    }

    // ============================================================
    // Tag Groups popup
    // ============================================================

    private void OpenTagGroups()
    {
        // Edit a copy; commit only on Save.
        _workingGroups = _settings.TagGroups
            .Select(g => new TagGroup { Name = g.Name, Tags = new List<string>(g.Tags) })
            .ToList();
        _workingGroupName = _workingGroups
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Name;
        NewGroupInput.Text = "";
        RebuildGroupsListBox();
        BuildGroupTagsChecklist();
        TagGroupsPopup.IsOpen = true;
    }

    private void RebuildGroupsListBox()
    {
        GroupsListBox.SelectionChanged -= GroupsListBox_SelectionChanged;
        GroupsListBox.Items.Clear();
        foreach (var g in _workingGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            GroupsListBox.Items.Add(g.Name);
        GroupsListBox.SelectionChanged += GroupsListBox_SelectionChanged;
        if (_workingGroupName != null)
            GroupsListBox.SelectedItem = _workingGroupName;
    }

    private void GroupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _workingGroupName = GroupsListBox.SelectedItem as string;
        BuildGroupTagsChecklist();
    }

    private void NewGroupInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddWorkingGroup();
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e) => AddWorkingGroup();

    private void AddWorkingGroup()
    {
        string name = NewGroupInput.Text.Trim();
        NewGroupInput.Text = "";
        if (string.IsNullOrEmpty(name) ||
            _workingGroups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return;
        _workingGroups.Add(new TagGroup { Name = name });
        _workingGroupName = name;
        RebuildGroupsListBox();
        BuildGroupTagsChecklist();
    }

    private TagGroup? CurrentWorkingGroup() =>
        _workingGroups.FirstOrDefault(g =>
            g.Name.Equals(_workingGroupName, StringComparison.OrdinalIgnoreCase));

    private void BuildGroupTagsChecklist()
    {
        GroupTagsChecklist.Children.Clear();
        var group = CurrentWorkingGroup();
        if (group == null)
        {
            AddChecklistHint("Add or pick a group first.");
            return;
        }
        var tags = AllTags().ToList();
        if (tags.Count == 0)
        {
            AddChecklistHint("No tags yet.");
            return;
        }
        foreach (var tag in tags)
        {
            var cb = new CheckBox
            {
                Content = tag, Margin = new Thickness(2, 5, 2, 5), FontSize = 13, Tag = tag,
                IsChecked = group.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase))
            };
            cb.SetResourceReference(CheckBox.ForegroundProperty, "TextPrimaryBrush");
            cb.Checked += GroupTag_Toggled;
            cb.Unchecked += GroupTag_Toggled;
            GroupTagsChecklist.Children.Add(cb);
        }
    }

    private void AddChecklistHint(string text)
    {
        var hint = new TextBlock { Text = text, Margin = new Thickness(2, 6, 2, 6) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        GroupTagsChecklist.Children.Add(hint);
    }

    private void GroupTag_Toggled(object sender, RoutedEventArgs e)
    {
        var group = CurrentWorkingGroup();
        if (group == null || sender is not CheckBox cb || cb.Tag is not string tag)
            return;
        group.Tags.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (cb.IsChecked == true)
            group.Tags.Add(tag);
    }

    private void TagGroupsClear_Click(object sender, RoutedEventArgs e)
    {
        var group = CurrentWorkingGroup();
        if (group == null) return;
        group.Tags.Clear();
        BuildGroupTagsChecklist();
    }

    private void TagGroupsDelete_Click(object sender, RoutedEventArgs e)
    {
        var group = CurrentWorkingGroup();
        if (group == null) return;
        var confirm = MessageBox.Show(this,
            $"Delete the group \"{group.Name}\"? (Tags themselves are not deleted.)",
            "Delete group", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        _workingGroups.Remove(group);
        _workingGroupName = _workingGroups
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Name;
        RebuildGroupsListBox();
        BuildGroupTagsChecklist();
    }

    private void TagGroupsCancel_Click(object sender, RoutedEventArgs e) => TagGroupsPopup.IsOpen = false;

    private void TagGroupsSave_Click(object sender, RoutedEventArgs e)
    {
        _settings.TagGroups = _workingGroups
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new TagGroup { Name = g.Name.Trim(), Tags = DedupeTags(g.Tags) })
            .ToList();
        // Forget pinned locations whose group no longer exists.
        _settings.MainWindowTopGroups = _settings.MainWindowTopGroups
            .Where(name => _settings.TagGroups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        _settings.Save();
        TagGroupsPopup.IsOpen = false;
        UpdateGroupChrome();
    }

    // ============================================================
    // Group Locations popup
    // ============================================================

    private const string MainTopLocation = "Main Window (Top)";

    private void GroupLocationsMenu_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        OpenGroupLocations();
    }

    private void OpenGroupLocations()
    {
        if (_settings.TagGroups.Count == 0)
            return;
        _workingTopGroups = new List<string>(_settings.MainWindowTopGroups);

        LocationsListBox.SelectionChanged -= LocationsListBox_SelectionChanged;
        LocationsListBox.Items.Clear();
        LocationsListBox.Items.Add(MainTopLocation);
        LocationsListBox.SelectionChanged += LocationsListBox_SelectionChanged;
        LocationsListBox.SelectedIndex = 0;

        BuildLocationGroupsChecklist();
        GroupLocationsPopup.IsOpen = true;
    }

    private void LocationsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => BuildLocationGroupsChecklist();

    private void BuildLocationGroupsChecklist()
    {
        LocationGroupsChecklist.Children.Clear();
        // Only one location exists for now (Main Window Top), so all groups map to it.
        foreach (var g in GroupsSorted())
        {
            var cb = new CheckBox
            {
                Content = g.Name, Margin = new Thickness(2, 5, 2, 5), FontSize = 13, Tag = g.Name,
                IsChecked = _workingTopGroups.Any(n => n.Equals(g.Name, StringComparison.OrdinalIgnoreCase))
            };
            cb.SetResourceReference(CheckBox.ForegroundProperty, "TextPrimaryBrush");
            cb.Checked += LocationGroup_Toggled;
            cb.Unchecked += LocationGroup_Toggled;
            LocationGroupsChecklist.Children.Add(cb);
        }
    }

    private void LocationGroup_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not string name)
            return;
        _workingTopGroups.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (cb.IsChecked == true)
            _workingTopGroups.Add(name);
    }

    private void GroupLocationsClear_Click(object sender, RoutedEventArgs e)
    {
        _workingTopGroups.Clear();
        BuildLocationGroupsChecklist();
    }

    private void GroupLocationsCancel_Click(object sender, RoutedEventArgs e) => GroupLocationsPopup.IsOpen = false;

    private void GroupLocationsSave_Click(object sender, RoutedEventArgs e)
    {
        _settings.MainWindowTopGroups = new List<string>(_workingTopGroups);
        _settings.Save();
        GroupLocationsPopup.IsOpen = false;
        RefreshGroupBar();
    }

    // ============================================================
    // Tags
    // ============================================================

    private void TagInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        AddTag(TagInput.Text);
        TagInput.Text = "";
    }

    private void TagsPopupInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        AddTag(TagsPopupInput.Text);
        TagsPopupInput.Text = "";
    }

    private void AddTag(string raw)
    {
        if (_current == null)
            return;
        string tag = raw.Trim().Trim('/');
        if (string.IsNullOrEmpty(tag) ||
            _currentTags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            return;
        _currentTags.Add(tag);
        _current.Tags = _currentTags.ToList();
        _current.VisibleTags = VisibleTagsFor(_current);
        UpdateTagButtonVisibility();
        ScheduleSave();
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is string tag)
        {
            _currentTags.Remove(tag);
            if (_current != null)
            {
                _current.Tags = _currentTags.ToList();
                _current.VisibleTags = VisibleTagsFor(_current);
            }
            UpdateTagButtonVisibility();
            ScheduleSave();
        }
    }

    private void OpenTagsPopup()
    {
        if (_current == null) return;
        TagsPopupInput.Text = "";
        TagsPopup.IsOpen = true;
    }

    private void CloseTagsPopup_Click(object sender, RoutedEventArgs e) => TagsPopup.IsOpen = false;

    // ---- Snippet ----

    private void SnippetButton_Click(object sender, RoutedEventArgs e) => OpenSnippetPopup();

    private void OpenSnippetPopup()
    {
        if (_current == null) return;
        DescriptionInput.Text = _current.Description;
        SnippetInput.Text = _current.CustomSnippet;
        SnippetPopup.IsOpen = true;
    }

    private void SnippetSave_Click(object sender, RoutedEventArgs e)
    {
        if (_current != null)
        {
            _current.Description = DescriptionInput.Text;
            _current.CustomSnippet = SnippetInput.Text.Trim();
            NoteStore.Save(_notes);
        }
        SnippetPopup.IsOpen = false;
    }

    private void SnippetClear_Click(object sender, RoutedEventArgs e)
    {
        DescriptionInput.Text = "";
        SnippetInput.Text = "";
        if (_current != null)
        {
            _current.Description = "";
            _current.CustomSnippet = "";
            NoteStore.Save(_notes);
        }
    }

    private void SnippetCancel_Click(object sender, RoutedEventArgs e) => SnippetPopup.IsOpen = false;

    // ============================================================
    // Inline formatting (B / I / U / S)
    // ============================================================

    private void Bold_Click(object sender, RoutedEventArgs e)
    { EditingCommands.ToggleBold.Execute(null, Editor); Editor.Focus(); }

    private void Italic_Click(object sender, RoutedEventArgs e)
    { EditingCommands.ToggleItalic.Execute(null, Editor); Editor.Focus(); }

    private void Underline_Click(object sender, RoutedEventArgs e)
    { EditingCommands.ToggleUnderline.Execute(null, Editor); Editor.Focus(); }

    private void Strike_Click(object sender, RoutedEventArgs e)
    {
        var current = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                      as TextDecorationCollection;
        bool hasStrike = current != null &&
                         current.Any(d => d.Location == TextDecorationLocation.Strikethrough);
        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
            hasStrike ? null : TextDecorations.Strikethrough);
        Editor.Focus();
    }

    // ============================================================
    // Paragraph format dropdown (Normal / H1-3 / lists / checklist)
    // ============================================================

    private void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateFormatIcon();
        HighlightMenu(FormatMenuPanel, CurrentFormatCat());
        FormatPopup.IsOpen = true;
    }

    // Highlight the row whose Tag matches the current value so the active
    // choice is obvious.
    private void HighlightMenu(System.Windows.Controls.Panel panel, string value)
    {
        var selected = (Brush)FindResource("AccentSoftBrush");
        foreach (var child in panel.Children)
            if (child is Button b && b.Tag is string tag)
                b.Background = tag == value ? selected : Brushes.Transparent;
    }

    private void Format_Selected(object sender, RoutedEventArgs e)
    {
        FormatPopup.IsOpen = false;
        if (_current == null)
            return;

        string cat = (string)((Button)sender).Tag;
        Editor.Focus();

        if (cat == "Bulleted")
        {
            RemoveChecklistInSelection();
            EditingCommands.ToggleBullets.Execute(null, Editor);
        }
        else if (cat == "Numbered")
        {
            RemoveChecklistInSelection();
            EditingCommands.ToggleNumbering.Execute(null, Editor);
        }
        else
        {
            RemoveListIfAny();
            ApplyCategory(cat);
        }

        UpdateFormatIcon();
        ScheduleSave();
    }

    private void ApplyCategory(string cat)
    {
        foreach (var p in ParagraphsInSelection().ToList())
        {
            RemoveChecklist(p);
            foreach (var inl in p.Inlines.ToList())
                inl.ClearValue(TextElement.FontSizeProperty);

            if (cat is "H1" or "H2" or "H3")
            {
                p.Tag = cat;
                p.FontWeight = FontWeights.Bold;
                p.FontSize = SizeFor(cat);
            }
            else if (cat == "Check")
            {
                p.FontWeight = FontWeights.Normal;
                p.FontSize = SizeFor("Normal");
                MakeCheckItem(p);   // sets tag + style + glyph
            }
            else // Normal
            {
                p.Tag = "Normal";
                p.FontWeight = FontWeights.Normal;
                p.FontSize = SizeFor("Normal");
            }
        }
    }

    private void RemoveListIfAny()
    {
        var p = Editor.Selection.Start.Paragraph;
        if (p?.Parent is ListItem li && li.Parent is List list)
        {
            var cmd = list.MarkerStyle == TextMarkerStyle.Decimal
                ? EditingCommands.ToggleNumbering
                : EditingCommands.ToggleBullets;
            cmd.Execute(null, Editor);
        }
    }

    // ============================================================
    // Checklist support
    //
    // Check items are represented purely as text: the paragraph is tagged
    // "Check"/"CheckDone" and its first inline is a Run holding a box glyph
    // (checkbox controls are NOT embedded, so the document always serializes
    // as plain text and can never crash on reload).
    // ============================================================

    private const string CheckTag = "Check";
    private const string CheckDoneTag = "CheckDone";
    private const string BoxEmpty = "☐";   // checkbox glyph
    private const string BoxDone = "☑";
    private static readonly Color AccentColor = (Color)ColorConverter.ConvertFromString("#7C5CFF");
    // Render the box glyphs from one symbol font with text (not emoji) presentation
    // so the empty and checked boxes are the same size.
    private static readonly FontFamily CheckFont = new FontFamily("Segoe UI Symbol");
    // Checkbox size and line spacing follow the note's normal text size, not a
    // fixed value, so lines stay tight and scale with the text.
    private double CheckGlyphFontSize => SizeFor("Normal") * 1.6;
    private double CheckLineHeight => SizeFor("Normal") * 1.75;
    private static string GlyphText(bool done) => (done ? BoxDone : BoxEmpty) + "︎ ";
    private Run NewGlyph(bool done) => new Run(GlyphText(done))
    {
        Foreground = new SolidColorBrush(AccentColor),
        FontFamily = CheckFont,
        FontSize = CheckGlyphFontSize,
        BaselineAlignment = BaselineAlignment.Center
    };

    // Make check paragraphs pack tightly based on text size (not the big box).
    private void StyleCheckParagraph(Paragraph p)
    {
        p.LineHeight = CheckLineHeight;
        p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        p.Margin = new Thickness(0, 0, 0, 3);
    }

    private static bool IsCheck(Paragraph p)
    {
        var t = p.Tag as string;
        return t == CheckTag || t == CheckDoneTag;
    }

    private static bool LooksLikeBox(string? t)
    {
        t = t?.TrimStart();
        return t != null && (t.StartsWith(BoxEmpty) || t.StartsWith(BoxDone));
    }

    private void AddChecklist(Paragraph p) => MakeCheckItem(p);

    // Turns a paragraph into a check item: sets the tag + tight spacing and
    // ensures a box glyph is present as the first inline (idempotent).
    private void MakeCheckItem(Paragraph p, bool done = false)
    {
        p.Tag = done ? CheckDoneTag : CheckTag;
        StyleCheckParagraph(p);
        if (!(p.Inlines.FirstInline is Run r && LooksLikeBox(r.Text)))
        {
            var glyph = NewGlyph(done);
            if (p.Inlines.FirstInline != null)
                p.Inlines.InsertBefore(p.Inlines.FirstInline, glyph);
            else
                p.Inlines.Add(glyph);
        }
    }

    private static bool IsCheckItemEmpty(Paragraph p)
    {
        string text = new TextRange(p.ContentStart, p.ContentEnd).Text;
        text = text.Replace(BoxEmpty, "").Replace(BoxDone, "").Replace("︎", "").Trim();
        return text.Length == 0;
    }

    private void RemoveChecklist(Paragraph p)
    {
        if (!IsCheck(p))
            return;
        if (p.Inlines.FirstInline is Run r && LooksLikeBox(r.Text))
            p.Inlines.Remove(r);
        foreach (var inl in p.Inlines.ToList())
        {
            inl.TextDecorations = null;
            inl.ClearValue(TextElement.ForegroundProperty);
        }
        // Revert the tight check-list line spacing.
        p.ClearValue(Block.LineHeightProperty);
        p.ClearValue(Block.LineStackingStrategyProperty);
        p.ClearValue(Block.MarginProperty);
        p.Tag = "Normal";
    }

    private void RemoveChecklistInSelection()
    {
        foreach (var p in ParagraphsInSelection().ToList())
            RemoveChecklist(p);
    }

    // Clicking the box glyph toggles the item (crosses out / restores the text).
    // Returns the check paragraph whose box glyph is under the point, or null.
    // Only the box glyph itself counts (not the gap or the text after it).
    private Paragraph? CheckboxHit(Point pt)
    {
        var tp = Editor.GetPositionFromPoint(pt, true);
        if (tp?.Paragraph is Paragraph p && IsCheck(p) &&
            p.Inlines.FirstInline is Run r && LooksLikeBox(r.Text))
        {
            // GetCharacterRect returns a thin caret rect at a position, so take
            // the caret X before the box (left edge) and just after it (right edge).
            Rect a = r.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var afterBox = r.ContentStart.GetPositionAtOffset(1, LogicalDirection.Forward);
            Rect c = (afterBox ?? r.ContentEnd).GetCharacterRect(LogicalDirection.Forward);
            double left = a.Left;
            double right = c.Left;
            if (right - left < 6)           // measurement failed: assume a square box
                right = left + a.Height;
            // Keep the hit region tight to the box itself so it doesn't spill into
            // the gap/text to its right (only a hair of slack on each edge).
            if (pt.X >= left - 2 && pt.X <= right && pt.Y >= a.Top - 2 && pt.Y <= a.Bottom + 2)
                return p;
        }
        return null;
    }

    private void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var p = CheckboxHit(e.GetPosition(Editor));
        if (p != null)
        {
            ToggleCheckParagraph(p);
            e.Handled = true;
        }
    }

    // Show a hand cursor when hovering over a checkbox glyph.
    private void Editor_MouseMove(object sender, MouseEventArgs e)
    {
        Editor.Cursor = CheckboxHit(e.GetPosition(Editor)) != null ? Cursors.Hand : Cursors.IBeam;
    }

    // Enter inside a check item starts a new check item; Enter on an empty
    // check item exits the list (like ordered/unordered lists).
    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            return;

        var p = Editor.CaretPosition?.Paragraph;
        if (p == null || !IsCheck(p))
            return;

        e.Handled = true;
        if (IsCheckItemEmpty(p))
        {
            RemoveChecklist(p);   // leave the list
            return;
        }

        EditingCommands.EnterParagraphBreak.Execute(null, Editor);
        var np = Editor.CaretPosition?.Paragraph;
        if (np != null)
        {
            MakeCheckItem(np);
            if (np.Inlines.FirstInline != null)
                Editor.CaretPosition = np.Inlines.FirstInline.ContentEnd;
        }
        ScheduleSave();
    }

    private void ToggleCheckParagraph(Paragraph p)
    {
        bool done = (p.Tag as string) == CheckDoneTag;
        bool ns = !done;
        p.Tag = ns ? CheckDoneTag : CheckTag;

        var glyph = p.Inlines.FirstInline as Run;
        if (glyph != null)
        {
            glyph.Text = GlyphText(ns);
            glyph.FontFamily = CheckFont;
            glyph.FontSize = CheckGlyphFontSize;
            glyph.BaselineAlignment = BaselineAlignment.Center;
        }

        foreach (var inl in p.Inlines.ToList())
        {
            if (inl == glyph)
                continue;
            inl.TextDecorations = ns ? TextDecorations.Strikethrough : null;
            if (ns)
                inl.Foreground = new SolidColorBrush(ThemeManager.MutedColor);
            else
                inl.ClearValue(TextElement.ForegroundProperty);
        }
        ScheduleSave();
    }

    // Called after loading a document: migrates any old embedded checkboxes to
    // glyphs and makes sure every check paragraph has a correct box glyph.
    private void NormalizeChecklists()
    {
        foreach (var p in AllParagraphs().ToList())
        {
            // Migrate legacy InlineUIContainer/CheckBox content to a glyph.
            var containers = p.Inlines.OfType<InlineUIContainer>().ToList();
            if (containers.Count > 0)
            {
                bool wasChecked = containers[0].Child is CheckBox cb && cb.IsChecked == true;
                foreach (var c in containers)
                    p.Inlines.Remove(c);
                p.Tag = wasChecked ? CheckDoneTag : CheckTag;
            }

            if (!IsCheck(p))
                continue;

            StyleCheckParagraph(p);
            bool done = (p.Tag as string) == CheckDoneTag;
            if (p.Inlines.FirstInline is Run r && LooksLikeBox(r.Text))
            {
                r.Text = GlyphText(done);
                r.Foreground = new SolidColorBrush(AccentColor);
                r.FontFamily = CheckFont;
                r.FontSize = CheckGlyphFontSize;
                r.BaselineAlignment = BaselineAlignment.Center;
            }
            else
            {
                var glyph = NewGlyph(done);
                if (p.Inlines.FirstInline != null)
                    p.Inlines.InsertBefore(p.Inlines.FirstInline, glyph);
                else
                    p.Inlines.Add(glyph);
            }

            foreach (var inl in p.Inlines.ToList())
            {
                if (inl == p.Inlines.FirstInline)
                    continue;
                inl.TextDecorations = done ? TextDecorations.Strikethrough : null;
                if (done)
                    inl.Foreground = new SolidColorBrush(ThemeManager.MutedColor);
            }
        }
    }

    private void RefreshCheckedItemColors()
    {
        foreach (var p in AllParagraphs())
        {
            if ((p.Tag as string) != CheckDoneTag)
                continue;
            foreach (var inl in p.Inlines.ToList())
                if (inl != p.Inlines.FirstInline)
                    inl.Foreground = new SolidColorBrush(ThemeManager.MutedColor);
        }
    }

    // Strip any legacy embedded controls from serialized XAML before parsing,
    // so an old note's checkbox can never be turned into a live control.
    private static string StripUIContainers(string xaml)
        => System.Text.RegularExpressions.Regex.Replace(
            xaml, "<InlineUIContainer.*?</InlineUIContainer>", "",
            System.Text.RegularExpressions.RegexOptions.Singleline);

    // ============================================================
    // Font size +/- with per-category memory
    // ============================================================

    private void SizeUp_Click(object sender, RoutedEventArgs e) => ChangeSize(+1);
    private void SizeDown_Click(object sender, RoutedEventArgs e) => ChangeSize(-1);

    private void ChangeSize(int delta)
    {
        if (_current == null)
            return;
        Editor.Focus();

        var sel = Editor.Selection;
        object v = sel.GetPropertyValue(TextElement.FontSizeProperty);
        string cat = CaretCategory();
        double cur = v is double d ? d : SizeFor(cat);
        double ns = Math.Max(6, Math.Min(200, cur + delta));

        if (!sel.IsEmpty)
        {
            sel.ApplyPropertyValue(TextElement.FontSizeProperty, ns);
        }
        else if (sel.Start.Paragraph is Paragraph p)
        {
            p.FontSize = ns;
            foreach (var inl in p.Inlines.ToList())
                inl.ClearValue(TextElement.FontSizeProperty);
        }

        _current.CategorySizes[cat] = ns;
        ScheduleSave();
    }

    private double SizeFor(string cat)
    {
        if (_current != null && _current.CategorySizes.TryGetValue(cat, out var s))
            return s;
        return DefaultSizes.TryGetValue(cat, out var def) ? def : 14;
    }

    private string CaretCategory()
    {
        var p = Editor.Selection.Start.Paragraph;
        if (p == null || p.Parent is ListItem)
            return "Normal";
        return p.Tag as string ?? "Normal";
    }

    // ============================================================
    // Text color
    // ============================================================

    private void ColorButton_Click(object sender, RoutedEventArgs e) => ColorPopup.IsOpen = true;

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = false;
        if (_current == null || ((Button)sender).Tag is not string hex)
            return;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
            Editor.Focus();
            ScheduleSave();
        }
        catch { /* ignore bad swatch value */ }
    }

    private void MoreColors_Click(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = false;
        if (_current == null)
            return;

        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        if (Editor.Selection.GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush b)
            dlg.Color = System.Drawing.Color.FromArgb(b.Color.A, b.Color.R, b.Color.G, b.Color.B);

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dlg.Color;
            var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            Editor.Focus();
            ScheduleSave();
        }
    }

    private void ClearColor_Click(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = false;
        if (_current == null)
            return;
        foreach (var inl in InlinesInSelection())
            inl.ClearValue(TextElement.ForegroundProperty);
        Editor.Focus();
        ScheduleSave();
    }

    // ============================================================
    // Alignment + indentation
    // ============================================================

    private void AlignButton_Click(object sender, RoutedEventArgs e)
    {
        HighlightMenu(AlignMenuPanel, CurrentAlign());
        AlignPopup.IsOpen = true;
    }

    private string CurrentAlign() =>
        (Editor.Selection.Start.Paragraph?.TextAlignment ?? TextAlignment.Left) switch
        {
            TextAlignment.Center => "Center",
            TextAlignment.Right => "Right",
            TextAlignment.Justify => "Justify",
            _ => "Left"
        };

    private void Align_Selected(object sender, RoutedEventArgs e)
    {
        AlignPopup.IsOpen = false;
        Editor.Focus();
        switch ((string)((Button)sender).Tag)
        {
            case "Center": EditingCommands.AlignCenter.Execute(null, Editor); break;
            case "Right": EditingCommands.AlignRight.Execute(null, Editor); break;
            case "Justify": EditingCommands.AlignJustify.Execute(null, Editor); break;
            default: EditingCommands.AlignLeft.Execute(null, Editor); break;
        }
        UpdateAlignIcon();
        ScheduleSave();
    }

    private void Indent_Click(object sender, RoutedEventArgs e)
    { EditingCommands.IncreaseIndentation.Execute(null, Editor); Editor.Focus(); ScheduleSave(); }

    private void Outdent_Click(object sender, RoutedEventArgs e)
    { EditingCommands.DecreaseIndentation.Execute(null, Editor); Editor.Focus(); ScheduleSave(); }

    // ============================================================
    // Theme toggle
    // ============================================================

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        UpdateThemeGlyph();
        RefreshCheckedItemColors();
        _settings.Theme = ThemeManager.Current.ToString();
        _settings.Save();
    }

    private void UpdateThemeGlyph()
    {
        // Show the icon of the mode you'd switch TO.
        ThemeToggleButton.Content = ThemeManager.Current == AppTheme.Dark ? "" : "";
    }

    // ============================================================
    // Toolbar state sync
    // ============================================================

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        try
        {
            object weight = Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            var caretCat = CaretCategory();
            // Headings are bold at the paragraph level; don't force the Bold toggle on for them.
            bool headingBold = caretCat is "H1" or "H2" or "H3";
            BoldBtn.IsChecked = !headingBold && weight is FontWeight fw && fw == FontWeights.Bold;

            object style = Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty);
            ItalicBtn.IsChecked = style is FontStyle fs && fs == FontStyles.Italic;

            var deco = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                       as TextDecorationCollection;
            UnderlineBtn.IsChecked = deco != null &&
                deco.Any(d => d.Location == TextDecorationLocation.Underline);
            StrikeBtn.IsChecked = deco != null &&
                deco.Any(d => d.Location == TextDecorationLocation.Strikethrough);

            UpdateFormatIcon();
            UpdateAlignIcon();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void UpdateFormatIcon() => FormatIcon.Content = MakeFormatIcon(CurrentFormatCat(), 14);

    private string CurrentFormatCat()
    {
        var p = Editor.Selection.Start.Paragraph;
        if (p == null)
            return "Normal";
        if (p.Parent is ListItem li && li.Parent is List list)
            return list.MarkerStyle == TextMarkerStyle.Decimal ? "Numbered" : "Bulleted";
        var t = p.Tag as string;
        if (t == CheckTag || t == CheckDoneTag)
            return "Check";
        return t ?? "Normal";
    }

    private static UIElement MakeFormatIcon(string cat, double size)
    {
        if (cat == "Numbered")
            return MakeNumberedIcon(size);
        if (cat is "H1" or "H2" or "H3")
            return new TextBlock
            {
                Text = cat, FontWeight = FontWeights.Bold, FontSize = size - 1,
                VerticalAlignment = VerticalAlignment.Center
            };
        string glyph = cat switch
        {
            "Bulleted" => "\uE8FD",
            "Check" => "\uE9D5",
            _ => "\uE8E4"
        };
        return new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = size, VerticalAlignment = VerticalAlignment.Center
        };
    }

    // A numbered-list icon drawn to match the bulleted/check list icons:
    // three short lines, each with a number on the left.
    private static UIElement MakeNumberedIcon(double size)
    {
        var vb = new Viewbox
        {
            Width = size, Height = size, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
        };
        var canvas = new Canvas { Width = 16, Height = 16 };
        double[] ys = { 3.0, 8.0, 13.0 };
        for (int i = 0; i < 3; i++)
        {
            var num = new TextBlock
            {
                Text = (i + 1).ToString(), FontSize = 7, FontWeight = FontWeights.SemiBold,
                Width = 6, TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(num, 0);
            Canvas.SetTop(num, ys[i] - 5);
            var line = new System.Windows.Shapes.Rectangle { Width = 8, Height = 2, RadiusX = 1, RadiusY = 1 };
            Canvas.SetLeft(line, 7.5);
            Canvas.SetTop(line, ys[i] - 1);
            num.SetBinding(TextBlock.ForegroundProperty, FollowButtonForeground());
            line.SetBinding(System.Windows.Shapes.Shape.FillProperty, FollowButtonForeground());
            canvas.Children.Add(num);
            canvas.Children.Add(line);
        }
        vb.Child = canvas;
        return vb;
    }

    private static System.Windows.Data.Binding FollowButtonForeground() =>
        new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(
                System.Windows.Data.RelativeSourceMode.FindAncestor)
            { AncestorType = typeof(System.Windows.Controls.Primitives.ButtonBase) }
        };

    private void UpdateAlignIcon()
    {
        var ta = Editor.Selection.Start.Paragraph?.TextAlignment ?? TextAlignment.Left;
        AlignIcon.Text = ta switch
        {
            TextAlignment.Center => "",
            TextAlignment.Right => "",
            TextAlignment.Justify => "",
            _ => ""
        };
    }

    // ============================================================
    // Document helpers
    // ============================================================

    // Parse saved XAML, always stripping any legacy embedded controls first so
    // they can never be created (that is what crashed old check-list notes).
    private static FlowDocument ParseDocument(string? xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml))
            return new FlowDocument();
        try
        {
            return LoadXaml(StripUIContainers(xaml));
        }
        catch
        {
            return new FlowDocument();
        }
    }

    private static FlowDocument LoadXaml(string xaml)
    {
        using var sr = new StringReader(xaml);
        using var xr = XmlReader.Create(sr);
        return (FlowDocument)XamlReader.Load(xr);
    }

    private IEnumerable<Paragraph> AllParagraphs() => Flatten(Editor.Document.Blocks);

    private static IEnumerable<Paragraph> Flatten(BlockCollection blocks)
    {
        foreach (var b in blocks)
        {
            if (b is Paragraph p)
                yield return p;
            else if (b is List list)
                foreach (var li in list.ListItems)
                    foreach (var pp in Flatten(li.Blocks))
                        yield return pp;
            else if (b is Section s)
                foreach (var pp in Flatten(s.Blocks))
                    yield return pp;
        }
    }

    private IEnumerable<Paragraph> ParagraphsInSelection()
    {
        var sel = Editor.Selection;
        foreach (var p in AllParagraphs())
            if (p.ContentStart.CompareTo(sel.End) <= 0 && p.ContentEnd.CompareTo(sel.Start) >= 0)
                yield return p;
    }

    private IEnumerable<Inline> InlinesInSelection()
    {
        var sel = Editor.Selection;
        foreach (var p in ParagraphsInSelection().ToList())
            foreach (var inl in p.Inlines.ToList())
                if (inl.ContentStart.CompareTo(sel.End) <= 0 && inl.ContentEnd.CompareTo(sel.Start) >= 0)
                    yield return inl;
    }

    // ============================================================
    // Updates
    // ============================================================

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        object? original = UpdateButton.Content;
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "Checking…";
        try
        {
            UpdateInfo? info = await Updater.CheckAsync();
            if (info == null)
            {
                MessageBox.Show(this, "Couldn't determine the latest version. Please try again later.",
                    "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!Updater.IsNewer(info.Version))
            {
                MessageBox.Show(this, $"You're up to date (v{Updater.CurrentVersion.ToString(3)}).",
                    "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                var open = MessageBox.Show(this,
                    $"Version {info.Tag} is available, but no installer was found on the release. " +
                    "Open the downloads page?",
                    "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
                    Updater.OpenReleasesPage();
                return;
            }
            var choice = MessageBox.Show(this,
                $"Version {info.Tag} is available (you have v{Updater.CurrentVersion.ToString(3)}).\n\n" +
                "Download and install it now? The app will close to finish updating.",
                "Update available", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (choice != MessageBoxResult.OK)
                return;

            UpdateButton.Content = "Downloading…";
            PersistCurrent();
            string installer = await Updater.DownloadInstallerAsync(info.DownloadUrl);
            Updater.RunInstallerAndExit(installer);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Update check failed:\n" + ex.Message,
                "Check for updates", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = original;
        }
    }

    // ============================================================
    // Seed content
    // ============================================================

    // Internal changelog entries: one note per version, titled with the version.
    // Added on first launch (and any new versions on later launches); matched by
    // title so they are never duplicated.
    private void EnsureChangelogNotes()
    {
        var existing = new HashSet<string>(
            _notes.Where(n => n.IsChangelog).Select(n => n.Title),
            StringComparer.OrdinalIgnoreCase);

        bool added = false;
        foreach (var entry in ChangelogEntries())
            if (!existing.Contains(entry.Title))
            {
                _notes.Add(entry);
                added = true;
            }

        if (added)
            NoteStore.Save(_notes);
    }

    // The changelog is authored in /CHANGELOG.md (embedded as a resource) and
    // parsed into one note per version, so adding a release is a docs-only change.
    private static IEnumerable<Note> ChangelogEntries()
    {
        // The "Unreleased" section holds pending notes that CI has not stamped
        // with a version yet, so it isn't shown as a changelog entry.
        var parsed = ParseChangelog(LoadChangelogMarkdown())
            .Where(e => !e.Version.Equals("Unreleased", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Break same-date ties by file order (top of the file = newest) so the
        // list matches the file even when several versions share a date.
        var notes = new List<Note>();
        for (int i = 0; i < parsed.Count; i++)
            notes.Add(MakeChangelogNote(
                parsed[i].Version,
                parsed[i].Date.AddSeconds(parsed.Count - i),
                parsed[i].Lines.ToArray()));
        return notes;
    }

    private static string LoadChangelogMarkdown()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("CHANGELOG.md");
            if (stream == null)
                return "";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";   // never let a missing/unreadable changelog break startup
        }
    }

    // Parse a Keep-a-Changelog-style file: "## <version> — <yyyy-MM-dd>" headers
    // (em dash or hyphen; date optional) followed by "-"/"*" bullet lines.
    private static List<(string Version, DateTime Date, List<string> Lines)> ParseChangelog(string md)
    {
        var result = new List<(string, DateTime, List<string>)>();
        if (string.IsNullOrWhiteSpace(md))
            return result;

        string? version = null;
        DateTime date = DateTime.Today;
        List<string>? lines = null;

        void Flush()
        {
            if (version != null && lines is { Count: > 0 })
                result.Add((version, date, lines));
        }

        foreach (var raw in md.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("## "))
            {
                Flush();
                lines = new List<string>();
                date = DateTime.Today;

                string header = line.Substring(3).Trim();
                int sep = header.IndexOf('—');   // em dash
                if (sep < 0) sep = header.IndexOf('-');
                if (sep >= 0)
                {
                    version = header.Substring(0, sep).Trim();
                    string datePart = header.Substring(sep + 1).Trim();
                    if (DateTime.TryParse(datePart, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var d))
                        date = d;
                }
                else
                {
                    version = header;
                }
            }
            else if (lines != null && (line.StartsWith("- ") || line.StartsWith("* ")))
            {
                lines.Add(line.Substring(2).Trim());
            }
        }
        Flush();
        return result;
    }

    private static Note MakeChangelogNote(string title, DateTime date, string[] lines)
    {
        var note = new Note
        {
            Title = title,
            Tags = new List<string> { Note.ChangelogTag },
            Modified = date
        };

        var doc = new FlowDocument();
        var list = new System.Windows.Documents.List();
        foreach (var line in lines)
            list.ListItems.Add(new ListItem(new Paragraph(new Run(line))));
        doc.Blocks.Add(list);

        note.ContentXaml = XamlWriter.Save(doc);
        note.Preview = string.Join(" ", lines);
        return note;
    }

    private static Note CreateWelcomeNote()
    {
        var note = new Note
        {
            Title = "Welcome to Purple Star Notes",
            Tags = new List<string> { "Getting Started" },
            Modified = DateTime.Now
        };

        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("This is your first note. A few things to try:")));

        var list = new System.Windows.Documents.List();
        foreach (var line in new[]
        {
            "Click the + button (top-left) to create a new note.",
            "Use the Formatting menu for headings, lists, and check lists.",
            "Select text, then use B / I / U, the − / + size buttons, or the color button.",
            "Toggle light and dark mode from the bottom-left.",
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
