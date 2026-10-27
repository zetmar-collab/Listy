using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Listy.Core;

namespace Listy.App;

public partial class MainWindow
{
    private async void OpenPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = Text.T("T092"), Filter = Text.T("T093") };
        if (dialog.ShowDialog(this) == true) await OpenEditor(dialog.FileName);
    }
    private async Task OpenEditor(string path)
    {
        if (!await CanReplaceEditor()) return;
        var document = await Work("ReadingPlaylist", (token, _) => PlaylistCodec.ReadDocument(path, token));
        if (document == null) return;
        SetEditor(document.Tracks, path, document.Format, Path.GetFileName(path), document); Tabs.SelectedIndex = 2;
        _report = Text.T("Encoding", document.EncodingName) + "\n" + string.Join("\n", document.Warnings);
        Status(Text.T("T094", document.Tracks.Count, document.Tracks.Count(t => t.IsMissing)));
    }
    private async Task<bool> CanReplaceEditor()
    {
        if (_busy) return false;
        if (!_dirty) return true;
        var choice = Choose(Text.T("UnsavedPrompt"), Text.T("T096"), [Text.T("Save"), Text.T("Discard"), Text.T("T009")]);
        return choice == 1 || choice == 0 && await SaveEditor(false);
    }
    private int Choose(string message, string title, string[] labels)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new ScrollViewer { MaxHeight = 390, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } });
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var dialog = new Window { Owner = this, Title = title, Icon = Icon, Content = panel, Width = 620, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var result = -1;
        for (var i = 0; i < labels.Length; i++)
        {
            var index = i;
            var button = new Button { Content = labels[i], MinWidth = 95, IsDefault = i == labels.Length - 1, IsCancel = i == labels.Length - 1 };
            button.Click += (_, _) => { result = index; if (!button.IsCancel) dialog.DialogResult = true; };
            buttons.Children.Add(button);
        }
        panel.Children.Add(buttons); dialog.ShowDialog(); return result;
    }
    private bool Confirm(string message, string title) => Choose(message, title, [Text.T("T130"), Text.T("T131")]) == 0;
    private void SetEditor(IEnumerable<Track> tracks, string? path, PlaylistFormat format, string title, PlaylistDocument? document)
    {
        _loading = true; _editorOpened = true; _editorTitleKey = null;
        _tracks.Clear(); foreach (var track in tracks) _tracks.Add(track.Copy());
        _editorPath = path; _document = document; _originalText = document?.RawText; _dirty = false;
        _history.Clear(); EditorFormat.SelectedIndex = (int)format;
        EditorTitle.Text = title; EditorSubtitle.Text = path ?? Text.T("T097"); _loading = false; UpdateEditor();
    }
    private void Remember() => _history.Remember(_tracks);
    private void MarkDirty() { _dirty = true; _originalText = null; UpdateEditor(); }
    private void UpdateEditor()
    {
        if (!_ready) return;
        DirtyLabel.Visibility = _dirty ? Visibility.Visible : Visibility.Collapsed;
        var known = _tracks.Where(t => t.DurationSeconds.HasValue).ToArray();
        var total = TimeSpan.FromSeconds(Math.Min(known.Sum(t => t.DurationSeconds!.Value), TimeSpan.MaxValue.TotalSeconds - 1));
        EditorCount.Text = Text.T("EditorSummary", _tracks.Count, $"{(int)total.TotalHours}:{total.Minutes:00}:{total.Seconds:00}", known.Length);
        TracksEmpty.Visibility = _tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.IsEnabled = _history.CanUndo; RedoButton.IsEnabled = _history.CanRedo;
        ApplyFilters(); if (PreviewTabs.SelectedIndex == 1) _ = UpdateRaw();
    }
    private async Task UpdateRaw()
    {
        if (!_ready || _loading || PreviewTabs.SelectedIndex != 1) return;
        var revision = ++_previewRevision; var format = (PlaylistFormat)EditorFormat.SelectedIndex;
        var target = _editorPath == null ? Path.Combine(_root ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Text.T("T133") + PlaylistCodec.Extension(format))
            : Path.ChangeExtension(_editorPath, PlaylistCodec.Extension(format));
        var tracks = _tracks.ToArray(); var document = _document; var original = _originalText; var relative = EditorRelative.IsChecked == true;
        try
        {
            var text = original ?? await Task.Run(() => PlaylistCodec.Serialize(tracks, target, format, relative, document));
            if (revision == _previewRevision) RawPreview.Text = text;
        }
        catch (Exception ex) { if (revision == _previewRevision) RawPreview.Text = ex.Message; }
    }
    private async void PreviewTab_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && e.Source == PreviewTabs) await UpdateRaw(); }
    private void EditorOptions_Changed(object sender, RoutedEventArgs e) { if (_ready && !_loading) MarkDirty(); }
    private async void New_Click(object sender, RoutedEventArgs e)
    {
        if (await CanReplaceEditor()) { SetEditor([], null, PlaylistFormat.M3U8, Text.T("T099"), null); _editorTitleKey = "T099"; }
    }
    private async void AddTracks_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = Text.T("T100"), Filter = AudioFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) await AddTracks(dialog.FileNames);
    }
    private async Task AddTracks(IEnumerable<string> paths)
    {
        var files = paths.ToArray(); var tags = ReadTagsBox.IsChecked == true;
        var existing = _tracks.Select(PlaylistTools.Identity).ToHashSet(StringComparer.Ordinal);
        var result = await Work("AddingTracks", (token, progress) =>
        {
            var tracks = new List<Track>(); int i = 0;
            foreach (var path in files)
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(path) && LibraryScanner.AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) && existing.Add(Path.GetFullPath(path).ToUpperInvariant()))
                { var track = new Track(Path.GetFullPath(path)); if (tags) AudioMetadata.Read(track); tracks.Add(track); }
                if (++i % 20 == 0) progress.Report(new(i, files.Length, Path.GetFileName(path)));
            }
            return tracks;
        });
        if (result == null || result.Count == 0) return;
        Remember(); foreach (var t in result) _tracks.Add(t); MarkDirty(); Status(Text.T("T101", result.Count));
    }
    private async void Tracks_Drop(object sender, DragEventArgs e)
    {
        if (_busy) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { await AddTracks(paths); return; }
        if (e.Data.GetData("ListyTracks") is not Track[] selected) return;
        var target = FindRow(e.OriginalSource as DependencyObject)?.Item as Track;
        if (target == null || selected.Contains(target)) return;
        Remember(); foreach (var t in selected) _tracks.Remove(t);
        var index = _tracks.IndexOf(target); foreach (var t in selected) _tracks.Insert(index++, t); MarkDirty();
    }
    private static DataGridRow? FindRow(DependencyObject? item)
    {
        while (item != null)
        {
            if (item is DataGridRow row) return row;
            item = System.Windows.Media.VisualTreeHelper.GetParent(item);
        }
        return null;
    }
    private void Tracks_MouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(TracksGrid);
    private void Tracks_MouseMove(object sender, MouseEventArgs e)
    {
        if (_busy || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(TracksGrid);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var selected = _tracks.Where(t => TracksGrid.SelectedItems.Contains(t)).ToArray();
        if (selected.Length > 0) DragDrop.DoDragDrop(TracksGrid, new DataObject("ListyTracks", selected), DragDropEffects.Move);
    }
    private void RemoveTracks_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var selected = TracksGrid.SelectedItems.Cast<Track>().ToArray(); if (selected.Length == 0) return;
        Remember(); foreach (var track in selected) _tracks.Remove(track); MarkDirty();
    }
    private void Up_Click(object sender, RoutedEventArgs e) => MoveTracks(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => MoveTracks(1);
    private void MoveTracks(int direction)
    {
        if (_busy) return;
        var selection = TracksGrid.SelectedItems.Cast<Track>().ToHashSet(); if (selection.Count == 0) return;
        Remember(); var indices = Enumerable.Range(0, _tracks.Count).Where(i => selection.Contains(_tracks[i]));
        if (direction > 0) indices = indices.Reverse();
        foreach (var index in indices.ToArray())
        {
            var target = index + direction;
            if (target >= 0 && target < _tracks.Count && !selection.Contains(_tracks[target])) _tracks.Move(index, target);
        }
        TracksGrid.SelectedItems.Clear(); foreach (var t in selection) TracksGrid.SelectedItems.Add(t); MarkDirty();
    }
    private void RestoreHistory(Track[]? state)
    {
        if (state == null) return;
        _tracks.Clear(); foreach (var t in state) _tracks.Add(t); MarkDirty();
    }
    private void Undo_Click(object sender, RoutedEventArgs e) { if (!_busy) RestoreHistory(_history.Undo(_tracks)); }
    private void Redo_Click(object sender, RoutedEventArgs e) { if (!_busy) RestoreHistory(_history.Redo(_tracks)); }
    private async void Tags_Click(object sender, RoutedEventArgs e)
    {
        var source = _tracks.Select(t => t.Copy()).ToArray();
        var result = await Work("ReadingTags", (token, progress) =>
        {
            for (var i = 0; i < source.Length; i++) { token.ThrowIfCancellationRequested(); AudioMetadata.Read(source[i]); if (i % 20 == 0) progress.Report(new(i + 1, source.Length, source[i].FileName)); }
            return source;
        });
        if (result == null) return;
        Remember(); _tracks.Clear(); foreach (var t in result) _tracks.Add(t); MarkDirty();
        _report = string.Join("\n", result.Where(t => t.MetadataWarning != null).Select(t => t.Location + ": " + t.MetadataWarning));
        Status(Text.T("TagsRead", result.Count(t => t.MetadataWarning == null && !t.IsRemote && !t.IsMissing), result.Count(t => t.MetadataWarning != null)));
    }
    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Remember();
        var array = PlaylistTools.Sort(_tracks, (TrackOrder)SortBox.SelectedIndex);
        _tracks.Clear(); foreach (var t in array) _tracks.Add(t); MarkDirty();
    }
    private sealed record SaveResult(WriteResult Output, PlaylistDocument Document);
    private async Task<bool> SaveEditor(bool saveAs)
    {
        if (_busy) return false;
        var format = (PlaylistFormat)EditorFormat.SelectedIndex;
        var target = _editorPath;
        if (saveAs || target == null || !string.Equals(Path.GetExtension(target), PlaylistCodec.Extension(format), StringComparison.OrdinalIgnoreCase))
        {
            var dialog = new SaveFileDialog { Title = Text.T("T103"), Filter = PlaylistFilter, FilterIndex = (int)format + 1, DefaultExt = PlaylistCodec.Extension(format),
                AddExtension = true, OverwritePrompt = true, FileName = Path.GetFileNameWithoutExtension(target ?? Text.T("T133")) + PlaylistCodec.Extension(format) };
            var initial = target == null ? _root : Path.GetDirectoryName(target);
            if (initial != null && Directory.Exists(initial)) dialog.InitialDirectory = initial;
            if (dialog.ShowDialog(this) != true) return false;
            target = dialog.FileName; format = (PlaylistFormat)(dialog.FilterIndex - 1);
        }
        if (_document != null && format != _document.Format &&
            !(format is PlaylistFormat.M3U or PlaylistFormat.M3U8 && _document.Format is PlaylistFormat.M3U or PlaylistFormat.M3U8) &&
            !Confirm(Text.T("ConversionWarning"), Text.T("T103"))) return false;
        var tracks = _tracks.ToArray(); var relative = EditorRelative.IsChecked == true; var document = _document;
        var result = await Work("T081", (token, _) =>
        {
            PlaylistPlanner.ValidateName(Path.GetFileNameWithoutExtension(target));
            var output = PlaylistWriter.Write(target, tracks, format, relative, ExistingPlaylistPolicy.UpdateWithBackup, document, token);
            return new SaveResult(output, PlaylistCodec.ReadDocument(output.Path));
        });
        if (result == null) return false;
        _editorPath = result.Output.Path; _editorTitleKey = null; _document = result.Document; _originalText = result.Document.RawText;
        _dirty = false; _loading = true; EditorFormat.SelectedIndex = (int)format; _loading = false;
        EditorTitle.Text = Path.GetFileName(_editorPath); EditorSubtitle.Text = _editorPath; UpdateEditor();
        RefreshSavedEntry(_editorPath);
        _report = Text.T("T104") + _editorPath + (result.Output.BackupPath == null ? "" : "\n" + result.Output.BackupPath);
        Status(_report.Replace('\n', ' ')); return true;
    }
    private async void SaveEditor_Click(object sender, RoutedEventArgs e) => await SaveEditor(false);
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await SaveEditor(true);
}
