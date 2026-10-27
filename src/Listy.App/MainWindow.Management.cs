using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;
using Listy.Core;

namespace Listy.App;

public partial class MainWindow
{
    private void About_Click(object sender, RoutedEventArgs e)
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Listy.ThirdPartyNotices")!;
        using var reader = new StreamReader(stream);
        var text = new TextBox { Text = "Listy 1.2.0\n\n" + reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18) };
        new Window { Owner = this, Title = Text.T("About"), Content = text, Width = 760, Height = 540, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }
    private void Saved_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && SavedGrid.SelectedItem is PlaylistFile f) Status(f.Path + Text.T("T090")); }
    private async void EditSaved_Click(object sender, RoutedEventArgs e)
    {
        if (SavedGrid.SelectedItem is PlaylistFile f) await OpenEditor(f.Path); else Status(Text.T("T091"));
    }
    private async void ValidateEditor_Click(object sender, RoutedEventArgs e)
    {
        var tracks = _tracks.ToArray();
        var health = await Work("Validating", (token, _) => PlaylistTools.Validate(tracks, token));
        if (health == null) return;
        TracksGrid.Items.Refresh(); ApplyFilters();
        _report = Text.T("HealthSummary", health.Missing, health.Duplicates, health.Empty ? Text.T("T130") : Text.T("T131")); Status(_report);
    }
    private async void ValidateLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null) return;
        var files = _library.Playlists.ToArray();
        var result = await Work("Validating", (token, progress) =>
        {
            var log = new List<string>();
            for (var i = 0; i < files.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                try { files[i].Health = PlaylistTools.Validate(PlaylistCodec.ReadDocument(files[i].Path, token).Tracks, token); files[i].CheckError = null; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { files[i].CheckError = ex.Message; }
                log.Add(files[i].Path + ": " + files[i].CheckSummary); progress.Report(new(i + 1, files.Length, files[i].Name));
            }
            return log;
        });
        SavedGrid.Items.Refresh(); ApplyFilters();
        if (result != null) { _report = string.Join("\n", result); Status(Text.T("CheckedPlaylists", files.Length, files.Count(f => f.HasIssues))); }
    }
    private void RemoveDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var unique = PlaylistTools.Distinct(_tracks); var count = _tracks.Count - unique.Count;
        if (count == 0) { Status(Text.T("DuplicatesRemoved", 0)); return; }
        Remember(); _tracks.Clear(); foreach (var t in unique) _tracks.Add(t); MarkDirty(); Status(Text.T("DuplicatesRemoved", count));
    }
    private void RemoveMissing_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var missing = _tracks.Where(t => t.IsMissing).ToArray(); if (missing.Length == 0) return;
        Remember(); foreach (var t in missing) _tracks.Remove(t); MarkDirty();
    }
    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var panel = new StackPanel { Margin = new Thickness(24) };
        var oldPath = new TextBox { Text = _tracks.FirstOrDefault(t => t.IsMissing)?.Folder ?? _root ?? "" };
        var newPath = new TextBox { Margin = new Thickness(0, 10, 0, 10) };
        var browse = new Button { Content = Text.T("T006") };
        var apply = new Button { Content = Text.T("Repair"), Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(new TextBlock { Text = Text.T("RelocateHelp"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = Text.T("OldRoot"), Margin = new Thickness(0, 12, 0, 4) }); panel.Children.Add(oldPath);
        panel.Children.Add(new TextBlock { Text = Text.T("NewRoot"), Margin = new Thickness(0, 12, 0, 4) }); panel.Children.Add(newPath); panel.Children.Add(browse); panel.Children.Add(apply);
        var dialog = new Window { Owner = this, Title = Text.T("Repair"), Content = panel, Width = 640, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        browse.Click += (_, _) => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(dialog) == true) newPath.Text = picker.FolderName; };
        apply.Click += (_, _) => { if (Path.IsPathFullyQualified(oldPath.Text) && Directory.Exists(newPath.Text)) dialog.DialogResult = true; };
        if (dialog.ShowDialog() != true) return;
        var source = _tracks.ToArray(); var oldRoot = oldPath.Text; var newRoot = newPath.Text;
        var replacement = await Work("Repair", (token, _) => PlaylistTools.Relocate(source, oldRoot, newRoot, token: token).ToArray());
        if (replacement == null) return;
        Remember(); _tracks.Clear(); foreach (var t in replacement) _tracks.Add(t); MarkDirty(); Status(Text.T("RepairDone", _tracks.Count(t => t.IsMissing)));
    }
    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || SavedGrid.SelectedItems.Count != 1 || SavedGrid.SelectedItem is not PlaylistFile file) { Status(Text.T("T105")); return; }
        var name = AskName(Path.GetFileNameWithoutExtension(file.Path)); if (name == null) return;
        var target = await Work("Renaming", (_, _) => PlaylistPlanner.Rename(file.Path, name));
        if (target == null) return;
        if (string.Equals(_editorPath, file.Path, StringComparison.OrdinalIgnoreCase)) { _editorPath = target; EditorTitle.Text = Path.GetFileName(target); EditorSubtitle.Text = target; }
        await ScanLibrary(); Status(Text.T("T106") + Path.GetFileName(target));
    }
    private string? AskName(string current)
    {
        var input = new TextBox { Text = current, Margin = new Thickness(0, 12, 0, 18) };
        var ok = new Button { Content = Text.T("T047"), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = Text.T("T107") }); panel.Children.Add(input); panel.Children.Add(ok);
        var dialog = new Window { Owner = this, Title = Text.T("T047"), Content = panel, Width = 460, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => { try { PlaylistPlanner.ValidateName(input.Text); dialog.DialogResult = true; } catch (Exception ex) { MessageBox.Show(dialog, ex.Message); } };
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text : null;
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var files = SavedGrid.SelectedItems.Cast<PlaylistFile>().ToArray();
        if (_busy || files.Length == 0) { Status(Text.T("T108")); return; }
        var names = string.Join(Environment.NewLine, files.Take(10).Select(f => f.Path));
        if (files.Length > 10) names += Text.T("T109", files.Length - 10);
        if (!Confirm(Text.T("T110", files.Length, names), Text.T("T111"))) return;
        var result = await Work("Deleting", (token, progress) =>
        {
            var log = new List<string>(); int count = 0;
            foreach (var file in files)
            {
                if (token.IsCancellationRequested) { log.Add(Text.T("OperationCancelled")); break; }
                try { _ = PlaylistCodec.Detect(file.Path); FileSystem.DeleteFile(file.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException); count++; }
                catch (Exception ex) { log.Add(file.Path + ": " + ex.Message); }
                progress.Report(new(count + log.Count, files.Length, file.Name));
            }
            return new BatchResult(count, 0, log.Count, log);
        });
        if (result == null) return;
        if (_editorPath != null && files.Any(f => string.Equals(f.Path, _editorPath, StringComparison.OrdinalIgnoreCase)) && !File.Exists(_editorPath))
        { _editorPath = null; _dirty = true; _originalText = null; EditorSubtitle.Text = Text.T("DeletedEditor"); UpdateEditor(); }
        await ScanLibrary(); _report = string.Join("\n", result.Log); Status(Text.T("T113", result.Written, result.Errors));
    }
    private void Report_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextBox { Text = _report + (_planWarnings.Count == 0 ? "" : "\n\n" + string.Join("\n", _planWarnings)),
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18) };
        new Window { Owner = this, Title = Text.T("T114"), Content = text, Width = 800, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }
}
