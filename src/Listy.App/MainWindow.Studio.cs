using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Listy.Core;

namespace Listy.App;

public partial class MainWindow
{
    private string _editorEncoding = "utf-8", _autoEncoding = "utf-8";
    private static string L(string pl, string en) => Text.Language == "pl" ? pl : en;
    private void Studio_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var actions = new[] { L("Kopie zapasowe", "Backups"), L("Porównaj z zapisanym plikiem", "Compare with saved file"),
            L("Profil odtwarzacza", "Player profile"), L("Inteligentna playlista", "Smart playlist"),
            L("Znajdź identyczne pliki audio", "Find identical audio files"), L("Eksport ZIP z muzyką", "Export ZIP with music"), L("Odsłuch zaznaczonego utworu", "Preview selected track"), L("Odczytaj wszystkie tagi od nowa", "Read all tags again") };
        var panel = new StackPanel { Margin = new Thickness(20) };
        var window = new Window { Owner = this, Title = L("Narzędzia", "Tools"), Content = panel, Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        for (int i = 0; i < actions.Length; i++)
        {
            int action = i; var button = new Button { Content = actions[i], Margin = new Thickness(0, 4, 0, 4) };
            button.Click += async (_, _) => { window.Close(); try { switch (action) {
                case 0: await Revisions(); break; case 1: await CompareSaved(); break; case 2: Profile(); break;
                case 3: await SmartPlaylist(); break; case 4: await DuplicateAudio(); break; case 5: await ExportZip(); break; case 6: PreviewAudio(); break;
                case 7: if (_root != null) { var cleared = await Work("ReadingTags", (_, _) => { new MetadataCache().Clear(); return "OK"; }); if (cleared != null) { ReadTagsBox.IsChecked = true; await ScanLibrary(); } } break;
            } } catch (Exception ex) { Error(ex); } }; panel.Children.Add(button);
        }
        window.ShowDialog();
    }
    private string? CurrentPlaylist => Tabs.SelectedIndex == 1 && SavedGrid.SelectedItem is PlaylistFile f ? f.Path : _editorPath;
    private static string DifferenceText(PlaylistDifference d) => L($"Dodane: {d.Added.Count}\nUsunięte: {d.Removed.Count}\nZmiana kolejności: {(d.Reordered ? "tak" : "nie")}",
        $"Added: {d.Added.Count}\nRemoved: {d.Removed.Count}\nOrder changed: {(d.Reordered ? "yes" : "no")}") + "\n\n+ " + string.Join("\n+ ", d.Added.Take(30)) + "\n− " + string.Join("\n− ", d.Removed.Take(30));
    private async Task<bool> ConfirmChanges(string path, IReadOnlyList<Track> tracks)
    {
        var diff = await Work("Validating", (token, _) => PlaylistDifference.Compare(File.Exists(path) ? PlaylistCodec.ReadDocument(path, token).Tracks : [], tracks));
        return diff != null && Confirm(Path.GetFileName(path) + "\n\n" + DifferenceText(diff) + "\n\n" + L("Zapisać? Poprzednia wersja otrzyma kopię zapasową.", "Save? A backup will preserve the previous version."), L("Zmiany przed zapisem", "Changes before saving"));
    }
    private async Task CompareSaved()
    {
        if (CurrentPlaylist is not string path) { Status(L("Najpierw otwórz playlistę.", "Open a playlist first.")); return; }
        var rows = _tracks.ToArray(); var diff = await Work("Validating", (token, _) => PlaylistDifference.Compare(PlaylistCodec.ReadDocument(path, token).Tracks, rows));
        if (diff != null) { _report = DifferenceText(diff); Report_Click(this, new()); }
    }
    private async Task Revisions()
    {
        if (CurrentPlaylist is not string path || !await CanReplaceEditor()) return;
        var backups = await Work("Validating", (_, _) => PlaylistRevisions.List(path)); if (backups == null || backups.Length == 0) { Status(L("Brak kopii tej playlisty.", "No backups for this playlist.")); return; }
        var list = new ListBox { ItemsSource = backups, SelectedIndex = 0, MaxHeight = 320 };
        var panel = new StackPanel { Margin = new Thickness(20) }; panel.Children.Add(list);
        var preview = new Button { Content = L("Podgląd kopii", "Preview backup") }; var restore = new Button { Content = L("Przywróć", "Restore") }; panel.Children.Add(preview); panel.Children.Add(restore);
        var window = new Window { Owner = this, Title = L("Kopie zapasowe", "Backups"), Content = panel, Width = 760, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        preview.Click += async (_, _) => { if (list.SelectedItem is string b) { var value = await Work("ReadingPlaylist", (token, _) => PlaylistCodec.ReadDocument(b, token, PlaylistCodec.Detect(path)).RawText); if (value != null) { _report = value; Report_Click(this, new()); } } };
        string? chosen = null; restore.Click += (_, _) => { if (list.SelectedItem is string b) { chosen = b; window.Close(); } }; window.ShowDialog();
        if (chosen == null || !Confirm(L("Przywrócić kopię? Aktualna wersja także zostanie zachowana jako kopia.", "Restore backup? The current version will also be preserved as a backup."), L("Przywróć", "Restore"))) return;
        var restored = await Work("T081", (_, _) => PlaylistRevisions.Restore(chosen, path)); if (restored != null) await OpenEditor(restored);
    }
    private void Profile()
    {
        var profiles = PlayerProfile.All; var index = Choose(L("Profil ustawia format, kodowanie i rodzaj ścieżek dla bieżącej zakładki. Adresy content:// mogą być niezgodne z innymi odtwarzaczami.", "A profile sets format, encoding and paths for the current tab. content:// addresses may be incompatible with other players."), L("Profil odtwarzacza", "Player profile"), profiles.Select(p => p.Name).Append(Text.T("T009")).ToArray());
        if (index < 0 || index >= profiles.Count) return; var p = profiles[index];
        if (Tabs.SelectedIndex == 0) { _autoEncoding = p.Encoding; FormatBox.SelectedIndex = (int)p.Format; RelativeBox.IsChecked = p.Relative; }
        else { _editorEncoding = p.Encoding; EditorFormat.SelectedIndex = (int)p.Format; EditorRelative.IsChecked = p.Relative; MarkDirty(); }
    }
    private async Task SmartPlaylist()
    {
        var rules = SmartRule.Load().ToList(); var panel = new StackPanel { Margin = new Thickness(20) };
        var saved = new ComboBox { ItemsSource = rules, DisplayMemberPath = "Name" }; panel.Children.Add(saved);
        var fields = new[] { L("Nazwa reguły", "Rule name"), L("Wykonawca zawiera", "Artist contains"), L("Album zawiera", "Album contains"), L("Rozszerzenie (np. mp3)", "Extension (e.g. mp3)"), L("Minimum sekund", "Minimum seconds"), L("Maksimum sekund", "Maximum seconds") };
        var inputs = fields.Select(label => { panel.Children.Add(new TextBlock { Text = label }); var input = new TextBox(); panel.Children.Add(input); return input; }).ToArray();
        saved.SelectionChanged += (_, _) => { if (saved.SelectedItem is SmartRule r) { string[] values = [r.Name, r.Artist, r.Album, r.Extension, r.MinSeconds?.ToString() ?? "", r.MaxSeconds?.ToString() ?? ""]; for (int i = 0; i < inputs.Length; i++) inputs[i].Text = values[i]; } };
        var apply = new Button { Content = L("Zapisz regułę i utwórz listę", "Save rule and create playlist") }; panel.Children.Add(apply);
        var window = new Window { Owner = this, Title = L("Inteligentna playlista", "Smart playlist"), Content = panel, Width = 500, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        SmartRule? rule = null; apply.Click += (_, _) => { try { double? Number(int i) => string.IsNullOrWhiteSpace(inputs[i].Text) ? null : double.Parse(inputs[i].Text); var min = Number(4); var max = Number(5); if (min < 0 || max < 0 || min > max || min.HasValue && !double.IsFinite(min.Value) || max.HasValue && !double.IsFinite(max.Value)) throw new ArgumentException(L("Nieprawidłowy zakres czasu.", "Invalid duration range.")); PlaylistPlanner.ValidateName(inputs[0].Text); rule = new(inputs[0].Text, inputs[1].Text, inputs[2].Text, inputs[3].Text, min, max); window.DialogResult = true; } catch (Exception ex) { MessageBox.Show(window, ex.Message); } }; if (window.ShowDialog() != true || rule == null || !await CanReplaceEditor()) return;
        rules.RemoveAll(r => r.Name.Equals(rule.Name, StringComparison.OrdinalIgnoreCase)); rules.Add(rule); SmartRule.Save(rules);
        SetEditor((_library?.Tracks ?? _tracks.ToArray()).Where(rule.Matches).ToArray(), null, PlaylistFormat.M3U8, rule.Name, null); MarkDirty(); Tabs.SelectedIndex = 2;
    }
    private async Task DuplicateAudio()
    {
        var tracks = _library?.Tracks ?? _tracks.ToArray(); var groups = await Work("Validating", (token, progress) => AudioDuplicates.Find(tracks, token, progress).ToArray());
        if (groups == null) return; _report = L("Grupy identycznych plików: ", "Identical file groups: ") + groups.Length + "\n" + L("Porównanie SHA-256 całych plików. Różne tagi mogą dawać różne wyniki. Nic nie usunięto.", "Whole-file SHA-256 comparison. Different tags may produce different results. No files deleted.") + "\n\n" + string.Join("\n\n", groups.Select(g => string.Join("\n", g))); Report_Click(this, new());
    }
    private async Task ExportZip()
    {
        var dialog = new SaveFileDialog { Filter = "ZIP|*.zip", FileName = "Playlist.zip", AddExtension = true }; if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName)) { Status(L("Wybierz nową nazwę archiwum.", "Choose a new archive name.")); return; }
        var tracks = _tracks.ToArray(); var result = await Work("T081", (token, _) => { PortablePlaylist.Export(dialog.FileName, tracks, token); return dialog.FileName; }); if (result != null) Status(result);
    }
    private void PreviewAudio()
    {
        if (TracksGrid.SelectedItem is not Track track || track.IsRemote) { Status(L("Zaznacz lokalny utwór w edytorze.", "Select a local track in the editor.")); return; }
        var player = new MediaPlayer(); var panel = new StackPanel { Margin = new Thickness(20) }; var label = new TextBlock { Text = track.Name, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(label);
        var window = new Window { Owner = this, Title = L("Odsłuch — 30 sekund", "Preview — 30 seconds"), Content = panel, Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; timer.Tick += (_, _) => { if (player.Position.TotalSeconds >= 30) { player.Stop(); timer.Stop(); } };
        foreach (var name in new[] { L("Odtwórz", "Play"), L("Pauza", "Pause"), L("Stop", "Stop") }) { var b = new Button { Content = name }; int action = panel.Children.Count - 1; b.Click += (_, _) => { if (action == 0) { player.Play(); timer.Start(); } else if (action == 1) player.Pause(); else { player.Stop(); timer.Stop(); } }; panel.Children.Add(b); }
        player.MediaFailed += (_, e) => label.Text = e.ErrorException.Message; player.Open(new Uri(track.Location)); window.Closed += (_, _) => { timer.Stop(); player.Close(); }; window.ShowDialog();
    }
}
