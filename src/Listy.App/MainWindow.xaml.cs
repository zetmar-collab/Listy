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
    private bool _ready, _dirty, _busy, _loading;
    private string? _editorPath, _originalText;
    private string _report = "Brak operacji do wyświetlenia.";
    private const string PlaylistFilter = "M3U8 (UTF-8)|*.m3u8|M3U (UTF-8)|*.m3u|PLS|*.pls|XSPF|*.xspf|WPL|*.wpl";
    private static string AudioFilter => "Pliki muzyczne|" + string.Join(";", LibraryScanner.AudioExtensions.Select(e => "*" + e));

    public MainWindow()
    {
        InitializeComponent();
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
    private void Error(Exception ex)
    {
        _report = ex.ToString();
        Status(ex.Message);
        MessageBox.Show(this, ex.Message, "Listy — nie udało się wykonać operacji", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void Busy(bool value)
    {
        _busy = value;
        Tabs.IsEnabled = !value;
        ScanButton.IsEnabled = !value;
        AlbumList.IsEnabled = !value;
        Recursive.IsEnabled = !value;
        CancelButton.IsEnabled = value;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFolderDialog { Title = "Wybierz katalog z muzyką", Multiselect = false };
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
        Status("Skanowanie katalogów…");
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
            Status($"Wczytano {snapshot.Tracks.Count} utworów, {snapshot.Albums.Count} folderów i {snapshot.Playlists.Count} playlist. Pominięte katalogi: {snapshot.Warnings.Count}.");
            _report = snapshot.Warnings.Count == 0 ? "Skanowanie zakończone bez błędów." : string.Join(Environment.NewLine, snapshot.Warnings);
        }
        catch (OperationCanceledException) { _root = _library?.Root; Status("Skanowanie anulowane. Poprzedni wynik pozostaje dostępny."); }
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
            PlanEmpty.Text = _library == null ? "Wybierz katalog. Tutaj pojawią się playlisty gotowe do utworzenia."
                : "Brak utworów dla wybranych formatów. Zmień filtr lub wybierz inny katalog.";
            var newCount = _plans.Count(p => !File.Exists(p.Path));
            PlanSummary.Text = $"{_plans.Count} list · {_plans.Sum(p => p.Count)} utworów · {newCount} nowych";
            GenerateButton.IsEnabled = newCount > 0;
        }
        catch (Exception ex)
        {
            _plans = [];
            PlanGrid.ItemsSource = _plans;
            PlanEmpty.Visibility = Visibility.Visible;
            PlanEmpty.Text = ex.Message;
            PlanSummary.Text = "Popraw ustawienia";
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
        Status("Zapisywanie playlist…");
        var result = await Task.Run(() =>
        {
            var log = new List<string>();
            int written = 0, skipped = 0, errors = 0;
            foreach (var plan in plans)
            {
                if (cancellation.IsCancellationRequested) { log.Add("Anulowano dalszy zapis."); break; }
                try
                {
                    if (File.Exists(plan.Path)) { skipped++; log.Add("Pominięto istniejącą: " + plan.Path); continue; }
                    Directory.CreateDirectory(plan.Folder);
                    if ((File.GetAttributes(plan.Folder) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Folder docelowy jest dowiązaniem. Wybierz bezpośredni katalog.");
                    PlaylistCodec.Save(plan.Path, plan.Tracks, format, relative);
                    written++; log.Add("Zapisano: " + plan.Path);
                }
                catch (Exception ex) { errors++; log.Add($"Błąd: {plan.Path} — {ex.Message}"); }
            }
            return (written, skipped, errors, log);
        });
        _scanCancellation.Dispose(); _scanCancellation = null;
        Busy(false);
        await ScanLibrary();
        _report = string.Join(Environment.NewLine, result.log);
        Status($"Zapisano: {result.written}. Pominięto: {result.skipped}. Błędy: {result.errors}. Szczegóły w raporcie.");
    }

    private void Plan_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && PlanGrid.SelectedItem is PlaylistPlan plan)
            Status($"{plan.Path} · {plan.Count} utworów. Kliknij dwukrotnie, aby zobaczyć utwory.");
    }
    private void PlanPreview_Click(object sender, RoutedEventArgs e)
    {
        if (PlanGrid.SelectedItem is not PlaylistPlan plan) { Status("Zaznacz playlistę w tabeli podglądu."); return; }
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
        if (!_ready || AlbumList.SelectedItem is not Album album || !CanReplaceEditor()) return;
        SetEditor(album.Tracks, null, PlaylistFormat.M3U8, album.Name, null);
        MarkDirty();
        Tabs.SelectedIndex = 2;
    }
    private void Saved_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && SavedGrid.SelectedItem is PlaylistFile file) Status(file.Path + " — wybierz „Podgląd / edycja”.");
    }
    private void EditSaved_Click(object sender, RoutedEventArgs e)
    {
        if (SavedGrid.SelectedItem is PlaylistFile file) OpenEditor(file.Path);
        else Status("Zaznacz playlistę do podglądu.");
    }
    private void OpenPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Otwórz playlistę", Filter = "Playlisty|*.m3u8;*.m3u;*.pls;*.xspf;*.wpl|Wszystkie pliki|*.*" };
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
            Status($"Wczytano {tracks.Count} utworów. Niedostępne pliki: {tracks.Count(t => !t.IsRemote && !File.Exists(t.Location))}.");
        }
        catch (Exception ex) { Error(ex); }
    }
    private bool CanReplaceEditor() => !_dirty || MessageBox.Show(this,
        "Bieżąca playlista ma niezapisane zmiany. Odrzucić zmiany i kontynuować?", "Niezapisana playlista",
        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void SetEditor(IEnumerable<Track> tracks, string? path, PlaylistFormat format, string title, string? raw)
    {
        _loading = true;
        _tracks.Clear();
        foreach (var track in tracks) _tracks.Add(track);
        _editorPath = path; _originalText = raw; _dirty = false;
        EditorFormat.SelectedIndex = (int)format;
        EditorTitle.Text = title;
        EditorSubtitle.Text = path ?? "Lista w pamięci — wybierz miejsce przy zapisie.";
        _loading = false;
        UpdateEditor();
    }
    private void MarkDirty() { _dirty = true; _originalText = null; UpdateEditor(); }
    private void UpdateEditor()
    {
        if (!_ready) return;
        DirtyLabel.Visibility = _dirty ? Visibility.Visible : Visibility.Collapsed;
        EditorCount.Text = $"{_tracks.Count} utworów";
        TracksEmpty.Visibility = _tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateRaw();
    }
    private void UpdateRaw()
    {
        if (!_ready || _loading) return;
        try
        {
            var format = (PlaylistFormat)EditorFormat.SelectedIndex;
            var target = _editorPath == null ? Path.Combine(_root ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Playlista" + PlaylistCodec.Extension(format))
                : Path.ChangeExtension(_editorPath, PlaylistCodec.Extension(format));
            RawPreview.Text = _originalText ?? PlaylistCodec.Serialize(_tracks, target, format, EditorRelative.IsChecked == true);
        }
        catch (Exception ex) { RawPreview.Text = ex.Message; }
    }
    private void PreviewTab_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && e.Source == PreviewTabs) UpdateRaw(); }
    private void EditorOptions_Changed(object sender, RoutedEventArgs e) { if (_ready && !_loading) MarkDirty(); }
    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (CanReplaceEditor()) SetEditor([], null, PlaylistFormat.M3U8, "Nowa playlista", null);
    }
    private void AddTracks_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Dodaj utwory do playlisty", Filter = AudioFilter, Multiselect = true };
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
        Status($"Dodano {added} utworów. Duplikaty i pliki innych typów pominięto.");
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
        if (_tracks.Count == 0) { Status("Dodaj co najmniej jeden utwór do playlisty."); return; }
        var format = (PlaylistFormat)EditorFormat.SelectedIndex;
        var dialog = new SaveFileDialog
        {
            Title = "Zapisz playlistę", Filter = PlaylistFilter, FilterIndex = (int)format + 1,
            DefaultExt = PlaylistCodec.Extension(format), AddExtension = true, OverwritePrompt = true,
            FileName = Path.GetFileNameWithoutExtension(_editorPath ?? "Playlista") + PlaylistCodec.Extension(format)
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
            _loading = true; EditorFormat.SelectedIndex = (int)actualFormat; _loading = false;
            _dirty = false; _originalText = File.ReadAllText(dialog.FileName);
            EditorTitle.Text = Path.GetFileName(dialog.FileName); EditorSubtitle.Text = dialog.FileName;
            UpdateEditor();
            if (_root != null) await ScanLibrary();
            Status("Zapisano playlistę: " + dialog.FileName);
        }
        catch (Exception ex) { Error(ex); }
    }
    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SavedGrid.SelectedItems.Count != 1 || SavedGrid.SelectedItem is not PlaylistFile file)
        { Status("Wybierz dokładnie jedną playlistę do zmiany nazwy."); return; }
        var name = AskName(Path.GetFileNameWithoutExtension(file.Path));
        if (name == null) return;
        try
        {
            var target = PlaylistPlanner.Rename(file.Path, name);
            if (string.Equals(_editorPath, file.Path, StringComparison.OrdinalIgnoreCase))
            { _editorPath = target; EditorTitle.Text = Path.GetFileName(target); EditorSubtitle.Text = target; }
            await ScanLibrary();
            Status("Zmieniono nazwę: " + Path.GetFileName(target));
        }
        catch (Exception ex) { Error(ex); }
    }
    private string? AskName(string current)
    {
        var input = new TextBox { Text = current, Margin = new Thickness(0, 12, 0, 18) };
        var ok = new Button { Content = "Zmień nazwę", IsDefault = true, Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Nowa nazwa playlisty (bez rozszerzenia)", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(input); panel.Children.Add(ok);
        var dialog = new Window { Owner = this, Title = "Zmień nazwę", Content = panel, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => { try { PlaylistPlanner.ValidateName(input.Text); dialog.DialogResult = true; } catch (Exception ex) { MessageBox.Show(dialog, ex.Message); } };
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text : null;
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var files = SavedGrid.SelectedItems.Cast<PlaylistFile>().ToArray();
        if (files.Length == 0) { Status("Zaznacz playlisty do przeniesienia do Kosza."); return; }
        var names = string.Join(Environment.NewLine, files.Take(10).Select(f => f.Path));
        if (files.Length > 10) names += $"\n… oraz {files.Length - 10} innych.";
        if (MessageBox.Show(this, $"Przenieść {files.Length} playlist do Kosza?\nPliki muzyczne pozostaną na dysku.\n\n{names}",
            "Usuwanie playlist", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
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
        _report = $"Przeniesiono do Kosza: {count}.\n" + string.Join(Environment.NewLine, errors);
        Status($"Przeniesiono do Kosza: {count} playlist. Błędy: {errors.Count}.");
    }
    private void Report_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextBox { Text = _report, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18) };
        new Window { Owner = this, Title = "Listy — raport operacji", Content = text, Width = 760, Height = 470, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy) { _scanCancellation?.Cancel(); e.Cancel = true; Status("Kończenie operacji… Zamknij okno ponownie po jej zakończeniu."); return; }
        if (!CanReplaceEditor()) e.Cancel = true;
    }
}
