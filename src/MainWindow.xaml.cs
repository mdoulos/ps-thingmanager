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

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Note> _notes = new();
    private readonly ObservableCollection<string> _currentTags = new();
    private Note? _current;
    private readonly DispatcherTimer _saveTimer;
    private bool _syncing;
    private string _search = "";
    private string _tagFilter = "";
    private readonly AppSettings _settings;

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

        RefreshList();

        if (_notes.Count > 0)
            NotesList.SelectedItem = _notes[0];
    }

    private void RefreshList()
    {
        UpdateTagButtonVisibility();

        IEnumerable<Note> view = _notes.OrderByDescending(n => n.Modified);

        if (!string.IsNullOrEmpty(_tagFilter))
            view = view.Where(n => n.Tags.Any(t => t.Equals(_tagFilter, StringComparison.OrdinalIgnoreCase)));

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

        PersistCurrent();
        LoadNoteIntoEditor(note);
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
            _currentTags.Add(t);

        _syncing = false;
        UpdateFormatIcon();
        UpdateAlignIcon();
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
        if (_current == null)
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

        if (_notes.Count == 0)
            _notes.Add(new Note { Title = "" });

        NoteStore.Save(_notes);
        RefreshList();
        NotesList.SelectedItem = _notes.OrderByDescending(n => n.Modified).First();
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
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

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
        TagFilterPanel.Children.Add(MakeFilterRow("All notes", "", string.IsNullOrEmpty(_tagFilter)));
        foreach (var t in AllTags())
            TagFilterPanel.Children.Add(
                MakeFilterRow(t, t, t.Equals(_tagFilter, StringComparison.OrdinalIgnoreCase)));
    }

    private Button MakeFilterRow(string label, string value, bool selected)
    {
        var b = new Button { Style = (Style)FindResource("MenuRowButton"), Tag = value };
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

    private void TagFilter_Selected(object sender, RoutedEventArgs e)
    {
        _tagFilter = (string)((Button)sender).Tag;
        TagFilterPopup.IsOpen = false;
        RefreshList();
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
        UpdateTagButtonVisibility();
        ScheduleSave();
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is string tag)
        {
            _currentTags.Remove(tag);
            if (_current != null)
                _current.Tags = _currentTags.ToList();
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
        SnippetInput.Text = _current.CustomSnippet;
        SnippetPopup.IsOpen = true;
    }

    private void SnippetSave_Click(object sender, RoutedEventArgs e)
    {
        if (_current != null)
        {
            _current.CustomSnippet = SnippetInput.Text.Trim();
            NoteStore.Save(_notes);
        }
        SnippetPopup.IsOpen = false;
    }

    private void SnippetClear_Click(object sender, RoutedEventArgs e)
    {
        SnippetInput.Text = "";
        if (_current != null)
        {
            _current.CustomSnippet = "";
            NoteStore.Save(_notes);
        }
    }

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
                p.Tag = "Check";
                p.FontWeight = FontWeights.Normal;
                p.FontSize = SizeFor("Normal");
                AddChecklist(p);
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
    private const double CheckGlyphSize = 26;   // ~2x the body text
    private static string GlyphText(bool done) => (done ? BoxDone : BoxEmpty) + "︎ ";
    private static Run NewGlyphRun(bool done) => new Run(GlyphText(done))
    {
        Foreground = new SolidColorBrush(AccentColor),
        FontFamily = CheckFont,
        FontSize = CheckGlyphSize,
        BaselineAlignment = BaselineAlignment.Center
    };

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

    private void AddChecklist(Paragraph p)
    {
        if (IsCheck(p))
            return;
        p.Tag = CheckTag;
        var glyph = NewGlyphRun(false);
        if (p.Inlines.FirstInline != null)
            p.Inlines.InsertBefore(p.Inlines.FirstInline, glyph);
        else
            p.Inlines.Add(glyph);
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
            Rect box = r.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (pt.X >= box.Left - 2 && pt.X <= box.Right + 3 &&
                pt.Y >= box.Top && pt.Y <= box.Bottom)
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
            glyph.FontSize = CheckGlyphSize;
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

            bool done = (p.Tag as string) == CheckDoneTag;
            if (p.Inlines.FirstInline is Run r && LooksLikeBox(r.Text))
            {
                r.Text = GlyphText(done);
                r.Foreground = new SolidColorBrush(AccentColor);
                r.FontFamily = CheckFont;
                r.FontSize = CheckGlyphSize;
                r.BaselineAlignment = BaselineAlignment.Center;
            }
            else
            {
                var glyph = NewGlyphRun(done);
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
