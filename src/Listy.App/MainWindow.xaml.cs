using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;
using Listy.Core;

namespace Listy.App;

public partial class MainWindow : Window
{
    private LibrarySnapshot? _library;
    private string? _root;
    private CancellationTokenSource? _scanCancellation;
    private readonly ObservableCollection<Track> _tracks = [];
    private IReadOnlyList<PlaylistPlan> _plans = [];
    private bool _ready, _dirty, _busy, _loading, _changingLanguage, _editorOpened;
    private string? _editorTitleKey = "T050";
    private string? _editorPath, _originalText;
    private string _report = Text.T("T071");
    private const string PlaylistFilter = "M3U8 (UTF-8)|*.m3u8|M3U (UTF-8)|*.m3u|PLS|*.pls|XSPF|*.xspf|WPL|*.wpl";
    private static string AudioFilter => Text.T("T072") + "|" + string.Join(";", LibraryScanner.AudioExtensions.Select(e => "*" + e));

    public MainWindow()
    {
        InitializeComponent();
        LanguageBox.SelectedIndex = Text.Language == "en" ? 1 : 0;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.Name);
        NameBox.Text = Text.T("T133");
        SubfolderBox.Text = Text.T("T134");
        foreach (var ext in LibraryScanner.AudioExtensions)
        {
            var box = new CheckBox { Content = ext.TrimStart('.').ToUpperInvariant(), Tag = ext, IsChecked = true };
            box.Checked += Options_Changed;
            box.Unchecked += Options_Changed;
            ExtensionPanel.Children.Add(box);
        }
        TracksGrid.ItemsSource = _tracks;
        _ready = true;
        RefreshPlan();
        UpdateEditor();
    }

    private void Status(string text) => StatusText.Text = text;
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _busy) return;
        _changingLanguage = true;
        try
        {
            Text.SetLanguage(LanguageBox.SelectedIndex == 1 ? "en" : "pl");
            if (_report == Text.Catalog("pl")["T071"] || _report == Text.Catalog("en")["T071"])
                _report = Text.T("T071");
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.Name);
            TrackCount.Text = (_library?.Tracks.Count ?? 0).ToString("N0");
            AlbumCount.Text = (_library?.Albums.Count ?? 0).ToString("N0");
            AlbumList.Items.Refresh();
            TracksGrid.Items.Refresh();
            RefreshPlan();
            if (_editorTitleKey != null) EditorTitle.Text = Text.T(_editorTitleKey);
            if (_editorOpened) EditorSubtitle.Text = _editorPath ?? Text.T("T097");
            UpdateEditor();
            Status(Text.T("T069"));
            try { LanguagePreference.Save(Text.Language); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Status(Text.T("T135") + " " + ex.Message); }
        }
        finally { _changingLanguage = false; }
    }
    private void Error(Exception ex)
    {
        _report = ex.ToString();
        Status(ex.Message);
        MessageBox.Show(this, ex.Message, Text.T("T073"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void Busy(bool value)
    {
        _busy = value;
        Tabs.IsEnabled = !value;
        ScanButton.IsEnabled = !value;
        AlbumList.IsEnabled = !value;
        Recursive.IsEnabled = !value;
        LanguageBox.IsEnabled = !value;
        CancelButton.IsEnabled = value;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFolderDialog { Title = Text.T("T032"), Multiselect = false };
        if (_root != null) dialog.InitialDirectory = _root;
        if (dialog.ShowDialog(this) != true) return;
        _root = dialog.FolderName;
        await ScanLibrary();
    }
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_root == null) { Browse_Click(sender, e); return; }
        await ScanLibrary();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _scanCancellation?.Cancel();

    private async Task ScanLibrary()
    {
        if (_root == null) return;
        Busy(true);
        _scanCancellation = new();
        Status(Text.T("T074"));
        var recursive = Recursive.IsChecked == true;
        try
        {
            var snapshot = await Task.Run(() => LibraryScanner.Scan(_root, recursive, _scanCancellation.Token));
            _library = snapshot;
            LibraryLabel.Text = Path.GetFileName(snapshot.Root.TrimEnd(Path.DirectorySeparatorChar));
            LibraryPath.Text = snapshot.Root;
            TrackCount.Text = snapshot.Tracks.Count.ToString("N0");
            AlbumCount.Text = snapshot.Albums.Count.ToString("N0");
            AlbumList.ItemsSource = snapshot.Albums;
            SavedGrid.ItemsSource = snapshot.Playlists;
            RefreshPlan();
            Status(Text.T("T075", snapshot.Tracks.Count, snapshot.Albums.Count, snapshot.Playlists.Count, snapshot.Warnings.Count));
            _report = snapshot.Warnings.Count == 0 ? Text.T("T076") : string.Join(Environment.NewLine, snapshot.Warnings);
        }
        catch (OperationCanceledException) { _root = _library?.Root; Status(Text.T("T077")); }
        catch (Exception ex) { _root = _library?.Root; Error(ex); }
        finally { _scanCancellation.Dispose(); _scanCancellation = null; Busy(false); }
    }

    private void Options_Changed(object sender, RoutedEventArgs e) { if (_ready) RefreshPlan(); }
    private void RefreshPlan()
    {
        if (!_ready) return;
        SubfolderBox.IsEnabled = ModeBox.SelectedIndex == 2;
        NameBox.Visibility = ModeBox.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        AlbumNameHint.Visibility = ModeBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            var extensions = ExtensionPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true)
                .Select(c => (string)c.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _plans = _library == null ? [] : PlaylistPlanner.Create(_library, extensions, (OutputMode)ModeBox.SelectedIndex,
                (PlaylistFormat)FormatBox.SelectedIndex, NameBox.Text, SubfolderBox.Text);
            PlanGrid.ItemsSource = _plans;
            PlanEmpty.Visibility = _plans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PlanEmpty.Text = _library == null ? Text.T("T037")
                : Text.T("T078");
            var newCount = _plans.Count(p => !File.Exists(p.Path));
            PlanSummary.Text = Text.T("T079", _plans.Count, _plans.Sum(p => p.Count), newCount);
            GenerateButton.IsEnabled = newCount > 0;
        }
        catch (Exception ex)
        {
            _plans = [];
            PlanGrid.ItemsSource = _plans;
            PlanEmpty.Visibility = Visibility.Visible;
            PlanEmpty.Text = ex.Message;
            PlanSummary.Text = Text.T("T080");
            GenerateButton.IsEnabled = false;
        }
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _plans.Count == 0) return;
        var plans = _plans.ToArray();
        var relative = RelativeBox.IsChecked == true;
        var format = (PlaylistFormat)FormatBox.SelectedIndex;
        Busy(true);
        _scanCancellation = new();
        var cancellation = _scanCancellation.Token;
        Status(Text.T("T081"));
        var result = await Task.Run(() =>
        {
            var log = new List<string>();
            int written = 0, skipped = 0, errors = 0;
            foreach (var plan in plans)
            {
                if (cancellation.IsCancellationRequested) { log.Add(Text.T("T082")); break; }
                try
                {
                    if (File.Exists(plan.Path)) { skipped++; log.Add(Text.T("T083") + plan.Path); continue; }
                    Directory.CreateDirectory(plan.Folder);
                    if ((File.GetAttributes(plan.Folder) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException(Text.T("T084"));
                    PlaylistCodec.Save(plan.Path, plan.Tracks, format, relative);
                    written++; log.Add(Text.T("T085") + plan.Path);
                }
                catch (Exception ex) { errors++; log.Add(Text.T("T086", plan.Path, ex.Message)); }
            }
            return (written, skipped, errors, log);
        });
        _scanCancellation.Dispose(); _scanCancellation = null;
        Busy(false);
        await ScanLibrary();
        _report = string.Join(Environment.NewLine, result.log);
        Status(Text.T("T087", result.written, result.skipped, result.errors));
    }

    private void Plan_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && PlanGrid.SelectedItem is PlaylistPlan plan)
            Status(Text.T("T088", plan.Path, plan.Count));
    }
    private void PlanPreview_Click(object sender, RoutedEventArgs e)
    {
        if (PlanGrid.SelectedItem is not PlaylistPlan plan) { Status(Text.T("T089")); return; }
        if (!CanReplaceEditor()) return;
        SetEditor(plan.Tracks, null, (PlaylistFormat)FormatBox.SelectedIndex, plan.Name, null);
        EditorRelative.IsChecked = RelativeBox.IsChecked;
        _editorPath = plan.Path;
        MarkDirty();
        Tabs.SelectedIndex = 2;
    }
    private void Plan_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => PlanPreview_Click(sender, e);

    private void Album_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _changingLanguage || AlbumList.SelectedItem is not Album album || !CanReplaceEditor()) return;
        SetEditor(album.Tracks, null, PlaylistFormat.M3U8, album.Name, null);
        MarkDirty();
        Tabs.SelectedIndex = 2;
    }
    private void Saved_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && SavedGrid.SelectedItem is PlaylistFile file) Status(file.Path + Text.T("T090"));
    }
    private void EditSaved_Click(object sender, RoutedEventArgs e)
    {
        if (SavedGrid.SelectedItem is PlaylistFile file) OpenEditor(file.Path);
        else Status(Text.T("T091"));
    }
    private void OpenPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Text.T("T092"), Filter = Text.T("T093") };
        if (dialog.ShowDialog(this) == true) OpenEditor(dialog.FileName);
    }
    private void OpenEditor(string path)
    {
        if (!CanReplaceEditor()) return;
        try
        {
            var tracks = PlaylistCodec.Read(path);
            SetEditor(tracks, path, PlaylistCodec.Detect(path), Path.GetFileName(path), File.ReadAllText(path));
            Tabs.SelectedIndex = 2;
            Status(Text.T("T094", tracks.Count, tracks.Count(t => !t.IsRemote && !File.Exists(t.Location))));
        }
        catch (Exception ex) { Error(ex); }
    }
    private bool CanReplaceEditor() => !_dirty || Confirm(Text.T("T095"), Text.T("T096"));

    private bool Confirm(string message, string title)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxHeight = 390 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var yes = new Button { Content = Text.T("T130"), MinWidth = 85 };
        var no = new Button { Content = Text.T("T131"), IsCancel = true, IsDefault = true, MinWidth = 85, Style = (Style)FindResource("Primary") };
        buttons.Children.Add(yes); buttons.Children.Add(no); panel.Children.Add(buttons);
        var dialog = new Window { Owner = this, Title = title, Icon = Icon, Content = panel, Width = 580, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        yes.Click += (_, _) => dialog.DialogResult = true;
        return dialog.ShowDialog() == true;
    }

    private void SetEditor(IEnumerable<Track> tracks, string? path, PlaylistFormat format, string title, string? raw)
    {
        _loading = true;
        _editorOpened = true;
        _editorTitleKey = null;
        _tracks.Clear();
        foreach (var track in tracks) _tracks.Add(track);
        _editorPath = path; _originalText = raw; _dirty = false;
        EditorFormat.SelectedIndex = (int)format;
        EditorTitle.Text = title;
        EditorSubtitle.Text = path ?? Text.T("T097");
        _loading = false;
        UpdateEditor();
    }
    private void MarkDirty() { _dirty = true; _originalText = null; UpdateEditor(); }
    private void UpdateEditor()
    {
        if (!_ready) return;
        DirtyLabel.Visibility = _dirty ? Visibility.Visible : Visibility.Collapsed;
        EditorCount.Text = Text.T("T098", _tracks.Count);
        TracksEmpty.Visibility = _tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateRaw();
    }
    private void UpdateRaw()
    {
        if (!_ready || _loading) return;
        try
        {
            var format = (PlaylistFormat)EditorFormat.SelectedIndex;
            var target = _editorPath == null ? Path.Combine(_root ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Text.T("T133") + PlaylistCodec.Extension(format))
                : Path.ChangeExtension(_editorPath, PlaylistCodec.Extension(format));
            RawPreview.Text = _originalText ?? PlaylistCodec.Serialize(_tracks, target, format, EditorRelative.IsChecked == true);
        }
        catch (Exception ex) { RawPreview.Text = ex.Message; }
    }
    private void PreviewTab_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && e.Source == PreviewTabs) UpdateRaw(); }
    private void EditorOptions_Changed(object sender, RoutedEventArgs e) { if (_ready && !_loading) MarkDirty(); }
    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (CanReplaceEditor())
        {
            SetEditor([], null, PlaylistFormat.M3U8, Text.T("T099"), null);
            _editorTitleKey = "T099";
        }
    }
    private void AddTracks_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Text.T("T100"), Filter = AudioFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddTracks(dialog.FileNames);
    }
    private void AddTracks(IEnumerable<string> paths)
    {
        var existing = _tracks.Select(t => t.Location).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var path in paths)
            if (File.Exists(path) && LibraryScanner.AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) && existing.Add(path))
            { _tracks.Add(new(path)); added++; }
        if (added > 0) MarkDirty();
        Status(Text.T("T101", added));
    }
    private void Tracks_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddTracks(paths);
    }
    private void RemoveTracks_Click(object sender, RoutedEventArgs e)
    {
        var selected = TracksGrid.SelectedItems.Cast<Track>().ToArray();
        foreach (var track in selected) _tracks.Remove(track);
        if (selected.Length > 0) MarkDirty();
    }
    private void Up_Click(object sender, RoutedEventArgs e) => MoveTracks(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => MoveTracks(1);
    private void MoveTracks(int direction)
    {
        var selection = TracksGrid.SelectedItems.Cast<Track>().ToHashSet();
        if (selection.Count == 0) return;
        var indices = Enumerable.Range(0, _tracks.Count).Where(i => selection.Contains(_tracks[i]));
        if (direction > 0) indices = indices.Reverse();
        var changed = false;
        foreach (var index in indices.ToArray())
        {
            var target = index + direction;
            if (target >= 0 && target < _tracks.Count && !selection.Contains(_tracks[target]))
            { _tracks.Move(index, target); changed = true; }
        }
        TracksGrid.SelectedItems.Clear();
        foreach (var track in selection) TracksGrid.SelectedItems.Add(track);
        if (changed) MarkDirty();
    }
    private async void SaveEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0) { Status(Text.T("T102")); return; }
        var format = (PlaylistFormat)EditorFormat.SelectedIndex;
        var dialog = new SaveFileDialog
        {
            Title = Text.T("T103"), Filter = PlaylistFilter, FilterIndex = (int)format + 1,
            DefaultExt = PlaylistCodec.Extension(format), AddExtension = true, OverwritePrompt = true,
            FileName = Path.GetFileNameWithoutExtension(_editorPath ?? Text.T("T133")) + PlaylistCodec.Extension(format)
        };
        var initial = _editorPath == null ? _root : Path.GetDirectoryName(_editorPath);
        if (initial != null && Directory.Exists(initial)) dialog.InitialDirectory = initial;
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var actualFormat = (PlaylistFormat)(dialog.FilterIndex - 1);
            PlaylistPlanner.ValidateName(Path.GetFileNameWithoutExtension(dialog.FileName));
            PlaylistCodec.Save(dialog.FileName, _tracks, actualFormat, EditorRelative.IsChecked == true, true);
            _editorPath = dialog.FileName;
            _editorTitleKey = null;
            _loading = true; EditorFormat.SelectedIndex = (int)actualFormat; _loading = false;
            _dirty = false; _originalText = File.ReadAllText(dialog.FileName);
            EditorTitle.Text = Path.GetFileName(dialog.FileName); EditorSubtitle.Text = dialog.FileName;
            UpdateEditor();
            if (_root != null) await ScanLibrary();
            Status(Text.T("T104") + dialog.FileName);
        }
        catch (Exception ex) { Error(ex); }
    }
    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SavedGrid.SelectedItems.Count != 1 || SavedGrid.SelectedItem is not PlaylistFile file)
        { Status(Text.T("T105")); return; }
        var name = AskName(Path.GetFileNameWithoutExtension(file.Path));
        if (name == null) return;
        try
        {
            var target = PlaylistPlanner.Rename(file.Path, name);
            if (string.Equals(_editorPath, file.Path, StringComparison.OrdinalIgnoreCase))
            { _editorPath = target; EditorTitle.Text = Path.GetFileName(target); EditorSubtitle.Text = target; }
            await ScanLibrary();
            Status(Text.T("T106") + Path.GetFileName(target));
        }
        catch (Exception ex) { Error(ex); }
    }
    private string? AskName(string current)
    {
        var input = new TextBox { Text = current, Margin = new Thickness(0, 12, 0, 18) };
        var ok = new Button { Content = Text.T("T047"), IsDefault = true, Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = Text.T("T107"), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(input); panel.Children.Add(ok);
        var dialog = new Window { Owner = this, Title = Text.T("T047"), Content = panel, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => { try { PlaylistPlanner.ValidateName(input.Text); dialog.DialogResult = true; } catch (Exception ex) { MessageBox.Show(dialog, ex.Message); } };
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text : null;
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var files = SavedGrid.SelectedItems.Cast<PlaylistFile>().ToArray();
        if (files.Length == 0) { Status(Text.T("T108")); return; }
        var names = string.Join(Environment.NewLine, files.Take(10).Select(f => f.Path));
        if (files.Length > 10) names += Text.T("T109", files.Length - 10);
        if (!Confirm(Text.T("T110", files.Length, names), Text.T("T111"))) return;
        Busy(true); CancelButton.IsEnabled = false;
        var errors = new List<string>(); var count = 0;
        foreach (var file in files)
        {
            try
            {
                _ = PlaylistCodec.Detect(file.Path);
                FileSystem.DeleteFile(file.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                count++;
            }
            catch (Exception ex) { errors.Add(file.Path + ": " + ex.Message); }
        }
        Busy(false);
        await ScanLibrary();
        _report = Text.T("T112", count) + string.Join(Environment.NewLine, errors);
        Status(Text.T("T113", count, errors.Count));
    }
    private void Report_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextBox { Text = _report, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18) };
        new Window { Owner = this, Title = Text.T("T114"), Content = text, Width = 760, Height = 470, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy) { _scanCancellation?.Cancel(); e.Cancel = true; Status(Text.T("T115")); return; }
        if (!CanReplaceEditor()) e.Cancel = true;
    }
}
