using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Listy.Core;

namespace Listy.App;

public partial class MainWindow : Window
{
    private LibrarySnapshot? _library;
    private string? _root, _editorPath, _originalText, _editorTitleKey = "T050";
    private PlaylistDocument? _document;
    private CancellationTokenSource? _scanCancellation;
    private readonly ObservableCollection<Track> _tracks = [];
    private readonly EditorHistory _history = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private IReadOnlyList<PlaylistPlan> _plans = [];
    private IReadOnlyList<string> _planWarnings = [];
    private bool _ready, _dirty, _busy, _loading, _changingLanguage, _editorOpened, _batchAlbums, _allowClose;
    private string _report = Text.T("T071");
    private string[]? _selectedAlbums;
    private Point _dragStart;
    private int _previewRevision;
    private readonly bool _persistSettings;
    private const string PlaylistFilter = "M3U8 (UTF-8)|*.m3u8|M3U (UTF-8)|*.m3u|PLS|*.pls|XSPF|*.xspf|WPL|*.wpl";
    private static string AudioFilter => Text.T("T072") + "|" + string.Join(";", LibraryScanner.AudioExtensions.Select(e => "*" + e));

    public MainWindow() : this(AppSettings.Load()) { }
    public MainWindow(AppSettings settings, bool persistSettings = true)
    {
        InitializeComponent();
        _persistSettings = persistSettings;
        _root = settings.Root; _selectedAlbums = settings.SelectedAlbums;
        LanguageBox.SelectedIndex = Text.Language == "en" ? 1 : 0;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.Name);
        Width = double.IsFinite(settings.Width) ? Math.Clamp(settings.Width, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width)) : 1360;
        Height = double.IsFinite(settings.Height) ? Math.Clamp(settings.Height, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height)) : 900;
        NameBox.Text = settings.Name; SubfolderBox.Text = settings.Subfolder;
        ModeBox.SelectedIndex = Math.Clamp(settings.Mode, 0, 2); FormatBox.SelectedIndex = Math.Clamp(settings.Format, 0, 4);
        PolicyBox.SelectedIndex = Math.Clamp(settings.Policy, 0, 2);
        AutoSortBox.SelectedIndex = Math.Clamp(settings.SortMode, 0, 3);
        RelativeBox.IsChecked = settings.Relative; Recursive.IsChecked = settings.Recursive; ReadTagsBox.IsChecked = settings.ReadMetadata;
        foreach (var ext in LibraryScanner.AudioExtensions)
        {
            var box = new CheckBox { Content = ext.TrimStart('.').ToUpperInvariant(), Tag = ext,
                IsChecked = (settings.Extensions ?? LibraryScanner.AudioExtensions).Contains(ext, StringComparer.OrdinalIgnoreCase) };
            box.Checked += Options_Changed; box.Unchecked += Options_Changed; ExtensionPanel.Children.Add(box);
        }
        TracksGrid.ItemsSource = _tracks;
        CollectionViewSource.GetDefaultView(_tracks).Filter = o => o is Track t && t.Matches(SearchBox.Text) && (MissingOnly.IsChecked != true || t.IsMissing);
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); RefreshPlan(); ApplyFilters(); SaveSettings(); };
        _ready = true; RefreshPlan(); UpdateEditor();
        Loaded += async (_, _) => { if (_root != null && Directory.Exists(_root)) await ScanLibrary(); };
    }
    private void Status(string text) => StatusText.Text = text;
    private void Error(Exception ex)
    {
        _report = ex.ToString(); Status(ex.Message);
        MessageBox.Show(this, ex.Message, Text.T("T073"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void SaveSettings()
    {
        if (!_ready || !_persistSettings) return;
        try
        {
            new AppSettings
            {
                Root = _root, Language = Text.Language, Recursive = Recursive.IsChecked == true,
                Relative = RelativeBox.IsChecked == true, ReadMetadata = ReadTagsBox.IsChecked == true,
                Format = FormatBox.SelectedIndex, Mode = ModeBox.SelectedIndex, Policy = PolicyBox.SelectedIndex,
                SortMode = AutoSortBox.SelectedIndex,
                Name = NameBox.Text, Subfolder = SubfolderBox.Text,
                Extensions = ExtensionPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray(),
                SelectedAlbums = _library?.Albums.Where(a => a.IsSelected).Select(a => a.Directory).ToArray() ?? _selectedAlbums,
                Width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width,
                Height = WindowState == WindowState.Normal ? Height : RestoreBounds.Height
            }.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status(Text.T("SettingsError") + " " + ex.Message); }
    }
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _busy) return;
        _changingLanguage = true;
        try
        {
            Text.SetLanguage(LanguageBox.SelectedIndex == 1 ? "en" : "pl");
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.Name);
            if (_report == Text.Catalog("pl")["T071"] || _report == Text.Catalog("en")["T071"]) _report = Text.T("T071");
            AlbumList.Items.Refresh(); TracksGrid.Items.Refresh(); SavedGrid.Items.Refresh(); RefreshPlan();
            if (_editorTitleKey != null) EditorTitle.Text = Text.T(_editorTitleKey);
            if (_editorOpened) EditorSubtitle.Text = _editorPath ?? Text.T("T097");
            UpdateEditor(); Status(Text.T("T069")); if (_persistSettings) LanguagePreference.Save(Text.Language); SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status(Text.T("T135") + " " + ex.Message); }
        finally { _changingLanguage = false; }
    }
    private void Busy(bool value)
    {
        _busy = value; Tabs.IsEnabled = !value; ScanButton.IsEnabled = !value; AlbumList.IsEnabled = !value;
        Recursive.IsEnabled = ReadTagsBox.IsEnabled = LanguageBox.IsEnabled = SearchBox.IsEnabled = !value;
        SelectAllButton.IsEnabled = SelectNoneButton.IsEnabled = !value; CancelButton.IsEnabled = value;
        OperationProgress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task<T?> Work<T>(string key, Func<CancellationToken, IProgress<WorkProgress>, T> operation) where T : class
    {
        if (_busy) return null;
        _refreshTimer.Stop();
        var cts = new CancellationTokenSource(); _scanCancellation = cts;
        Busy(true); OperationProgress.IsIndeterminate = true; Status(Text.T(key));
        var progress = new Progress<WorkProgress>(p =>
        {
            if (!_busy || !ReferenceEquals(_scanCancellation, cts)) return;
            OperationProgress.IsIndeterminate = p.Total == 0;
            if (p.Total > 0) OperationProgress.Value = p.Completed * 100.0 / p.Total;
            Status(Text.T(key) + $" {p.Completed}" + (p.Total > 0 ? $"/{p.Total}" : "") + " · " + p.Detail);
        });
        try { return await Task.Run(() => operation(cts.Token, progress)); }
        catch (OperationCanceledException) { Status(Text.T("OperationCancelled")); return null; }
        catch (Exception ex) { Error(ex); return null; }
        finally { _scanCancellation = null; cts.Dispose(); Busy(false); }
    }
    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFolderDialog { Title = Text.T("T032"), Multiselect = false };
        if (_root != null && Directory.Exists(_root)) dialog.InitialDirectory = _root;
        if (dialog.ShowDialog(this) != true) return;
        _root = dialog.FolderName; _selectedAlbums = null; await ScanLibrary();
    }
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_root == null) { Browse_Click(sender, e); return; }
        _selectedAlbums = _library?.Albums.Where(a => a.IsSelected).Select(a => a.Directory).ToArray() ?? _selectedAlbums;
        await ScanLibrary();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _scanCancellation?.Cancel();
    private async Task ScanLibrary()
    {
        if (_root == null) return;
        if (_library != null && string.Equals(_library.Root, _root, StringComparison.OrdinalIgnoreCase))
            _selectedAlbums = _library.Albums.Where(a => a.IsSelected).Select(a => a.Directory).ToArray();
        var root = _root; var recursive = Recursive.IsChecked == true; var tags = ReadTagsBox.IsChecked == true;
        var snapshot = await Work("T074", (token, progress) => LibraryScanner.Scan(root, recursive, token, progress, tags));
        if (snapshot == null) { _root = _library?.Root; return; }
        _library = snapshot; _batchAlbums = true;
        foreach (var album in snapshot.Albums)
        {
            if (_selectedAlbums != null) album.IsSelected = _selectedAlbums.Contains(album.Directory, StringComparer.OrdinalIgnoreCase);
            album.PropertyChanged += (_, _) => { if (!_batchAlbums) { RefreshPlan(); SaveSettings(); } };
        }
        LibraryLabel.Text = Path.GetFileName(snapshot.Root.TrimEnd(Path.DirectorySeparatorChar)); LibraryPath.Text = snapshot.Root;
        TrackCount.Text = snapshot.Tracks.Count.ToString("N0"); AlbumCount.Text = snapshot.Albums.Count.ToString("N0");
        AlbumList.ItemsSource = snapshot.Albums; SavedGrid.ItemsSource = snapshot.Playlists; _batchAlbums = false;
        RefreshPlan(); ApplyFilters(); SaveSettings();
        _report = snapshot.Warnings.Count == 0 ? Text.T("T076") : string.Join(Environment.NewLine, snapshot.Warnings);
        if (_planWarnings.Count > 0) _report += "\n" + string.Join("\n", _planWarnings);
        Status(Text.T("T075", snapshot.Tracks.Count, snapshot.Albums.Count, snapshot.Playlists.Count, snapshot.Warnings.Count + _planWarnings.Count));
    }
    private void RefreshSavedEntry(string path)
    {
        if (_library == null) return;
        var relative = Path.GetRelativePath(_library.Root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) return;
        if (Recursive.IsChecked != true && !string.Equals(Path.GetDirectoryName(path), _library.Root, StringComparison.OrdinalIgnoreCase)) return;
        var files = _library.Playlists.Where(p => !string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)).Append(new PlaylistFile(path))
            .OrderBy(p => p.Path, NaturalPathComparer.Instance).ToArray();
        _library = _library with { Playlists = files };
        SavedGrid.ItemsSource = files; ApplyFilters();
    }
    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _busy) return;
        _refreshTimer.Stop(); _refreshTimer.Start();
    }
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (_ready && !_busy) ApplyFilters(); }
    private void ApplyFilters()
    {
        if (!_ready) return;
        var query = SearchBox.Text;
        if (_library != null)
        {
            CollectionViewSource.GetDefaultView(AlbumList.ItemsSource).Filter = o => o is Album a &&
                (string.IsNullOrWhiteSpace(query) || a.Directory.Contains(query, StringComparison.CurrentCultureIgnoreCase) || a.Tracks.Any(t => t.Matches(query)));
            CollectionViewSource.GetDefaultView(SavedGrid.ItemsSource).Filter = o => o is PlaylistFile p &&
                (string.IsNullOrWhiteSpace(query) || p.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
                (IssuesOnly.IsChecked != true || p.HasIssues);
        }
        CollectionViewSource.GetDefaultView(_tracks).Refresh();
    }
    private void SelectAlbums(bool selected)
    {
        if (_library == null || _busy) return;
        _batchAlbums = true; foreach (var album in AlbumList.Items.OfType<Album>()) album.IsSelected = selected;
        _batchAlbums = false; RefreshPlan(); SaveSettings();
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e) => SelectAlbums(true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => SelectAlbums(false);
    private void RefreshPlan()
    {
        if (!_ready) return;
        SubfolderBox.IsEnabled = ModeBox.SelectedIndex == 2;
        NameBox.Visibility = ModeBox.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        AlbumNameHint.Visibility = ModeBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            var extensions = ExtensionPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = _library == null ? new PlanningResult([], []) : PlaylistPlanner.CreateDetailed(_library, extensions,
                (OutputMode)ModeBox.SelectedIndex, (PlaylistFormat)FormatBox.SelectedIndex, NameBox.Text, SubfolderBox.Text);
            var policy = (ExistingPlaylistPolicy)Math.Max(0, PolicyBox.SelectedIndex);
            var order = (TrackOrder)Math.Max(0, AutoSortBox.SelectedIndex);
            _plans = result.Plans.Select(p => p with { Policy = policy, Tracks = PlaylistTools.Sort(p.Tracks, order) }).ToArray(); _planWarnings = result.Warnings;
            PlanGrid.ItemsSource = _plans; PlanEmpty.Visibility = _plans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PlanEmpty.Text = _library == null ? Text.T("T037") : _planWarnings.Count > 0 ? string.Join("\n", _planWarnings) : Text.T("T078");
            var actionable = _plans.Count(p => policy != ExistingPlaylistPolicy.Skip || !File.Exists(p.Path));
            PlanSummary.Text = Text.T("PlanStatus", _plans.Count, _plans.Sum(p => p.Count), actionable, _planWarnings.Count); GenerateButton.IsEnabled = actionable > 0;
        }
        catch (Exception ex) { _plans = []; PlanGrid.ItemsSource = _plans; PlanEmpty.Visibility = Visibility.Visible; PlanEmpty.Text = ex.Message; PlanSummary.Text = Text.T("T080"); GenerateButton.IsEnabled = false; }
    }
    private sealed record BatchResult(int Written, int Skipped, int Errors, IReadOnlyList<string> Log);
    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _refreshTimer.Stop(); RefreshPlan(); if (_plans.Count == 0) return;
        var plans = _plans.ToArray(); var relative = RelativeBox.IsChecked == true; var format = (PlaylistFormat)FormatBox.SelectedIndex;
        var policy = (ExistingPlaylistPolicy)PolicyBox.SelectedIndex;
        var result = await Work("T081", (token, progress) =>
        {
            var log = new List<string>(_planWarnings); int written = 0, skipped = 0, errors = 0;
            foreach (var plan in plans)
            {
                if (token.IsCancellationRequested) { log.Add(Text.T("T082")); break; }
                try
                {
                    if (Directory.Exists(plan.Folder) && (File.GetAttributes(plan.Folder) & FileAttributes.ReparsePoint) != 0) throw new IOException(Text.T("T084"));
                    var output = PlaylistWriter.Write(plan.Path, plan.Tracks, format, relative, policy, token: token);
                    if (output.Written) { written++; log.Add(Text.T("T085") + output.Path + (output.BackupPath == null ? "" : " · " + output.BackupPath)); }
                    else { skipped++; log.Add(Text.T("T083") + output.Path); }
                }
                catch (OperationCanceledException) { log.Add(Text.T("T082")); break; }
                catch (Exception ex) { errors++; log.Add(Text.T("T086", plan.Path, ex.Message)); }
                progress.Report(new(written + skipped + errors, plans.Length, plan.Name));
            }
            return new BatchResult(written, skipped, errors, log);
        });
        if (result == null) return;
        _selectedAlbums = _library?.Albums.Where(a => a.IsSelected).Select(a => a.Directory).ToArray();
        await ScanLibrary(); _report = string.Join(Environment.NewLine, result.Log); Status(Text.T("T087", result.Written, result.Skipped, result.Errors));
    }
    private void Plan_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && PlanGrid.SelectedItem is PlaylistPlan p) Status(Text.T("T088", p.Path, p.Count)); }
    private async void PlanPreview_Click(object sender, RoutedEventArgs e)
    {
        if (PlanGrid.SelectedItem is not PlaylistPlan plan) { Status(Text.T("T089")); return; }
        if (!await CanReplaceEditor()) return;
        SetEditor(plan.Tracks, plan.Path, (PlaylistFormat)FormatBox.SelectedIndex, plan.Name, null);
        _loading = true; EditorRelative.IsChecked = RelativeBox.IsChecked; _loading = false; MarkDirty(); Tabs.SelectedIndex = 2;
    }
    private void Plan_DoubleClick(object sender, MouseButtonEventArgs e) => PlanPreview_Click(sender, e);
    private async void Album_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_busy || _changingLanguage || AlbumList.SelectedItem is not Album album || !await CanReplaceEditor()) return;
        SetEditor(album.Tracks, null, PlaylistFormat.M3U8, album.Name, null); MarkDirty(); Tabs.SelectedIndex = 2;
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) { SaveSettings(); return; }
        if (_busy) { _scanCancellation?.Cancel(); e.Cancel = true; Status(Text.T("T115")); return; }
        if (!_dirty) { SaveSettings(); return; }
        e.Cancel = true; if (await CanReplaceEditor()) { _allowClose = true; Close(); }
    }
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_busy) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.S) { e.Handled = true; await SaveEditor(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); }
        else if (ctrl && e.Key == Key.O) { e.Handled = true; OpenPlaylist_Click(sender, e); }
        else if (ctrl && e.Key == Key.Z && e.OriginalSource is not TextBox) { e.Handled = true; Undo_Click(sender, e); }
        else if (ctrl && e.Key == Key.Y && e.OriginalSource is not TextBox) { e.Handled = true; Redo_Click(sender, e); }
        else if (e.Key == Key.Delete && TracksGrid.IsKeyboardFocusWithin) { e.Handled = true; RemoveTracks_Click(sender, e); }
    }
}
