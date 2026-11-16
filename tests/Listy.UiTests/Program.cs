using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Listy.App;
using Listy.Core;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Listy;component/Styles.xaml") });
        Text.SetLanguage("pl");
        var window = new MainWindow(new AppSettings { Root = null }, persistSettings: false);
        var passed = 0; var failures = new List<string>();
        T Find<T>(string name) where T : class => (T)window.FindName(name);
        void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
        void Call(string method, params object?[] args) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        void Test(string name, Action action)
        {
            try { action(); Pump(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures.Add(name); Console.WriteLine("FAIL " + name + ": " + ex); }
        }
        void Load(Track[] tracks, PlaylistDocument? doc = null) =>
            Call("SetEditor", tracks, null, PlaylistFormat.M3U8, "Test", doc);
        Track[] Items() => ((IEnumerable)Find<DataGrid>("TracksGrid").ItemsSource).Cast<Track>().ToArray();
        try
        {
            Test("Przełączanie PL/EN aktualizuje zakładki i nagłówki", () =>
            {
                Find<ComboBox>("LanguageBox").SelectedIndex = 1; Pump();
                Check(Text.Language == "en" && ((TabItem)Find<TabControl>("Tabs").Items[0]).Header.ToString() == "Automatic playlists");
                Check(Find<DataGrid>("TracksGrid").Columns[0].Header.ToString() == "TRACK");
                Find<ComboBox>("LanguageBox").SelectedIndex = 0; Pump();
                Check(Text.Language == "pl");
            });
            Test("Usuwanie duplikatów, cofanie i ponawianie w edytorze", () =>
            {
                Load([new("C:\\ListyTests\\one.mp3"), new("C:\\ListyTests\\one.mp3"), new("C:\\ListyTests\\two.mp3")]);
                Call("RemoveDuplicates_Click", window, new RoutedEventArgs()); Check(Items().Length == 2);
                Find<Button>("UndoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(Items().Length == 3);
                Find<Button>("RedoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(Items().Length == 2);
            });
            Test("Wyszukiwanie i filtr brakujących nie usuwają utworów", () =>
            {
                Load([new("C:\\ListyTests\\one.mp3") { Title = "Pierwszy", Artist = "Artysta" }, new("https://example.org/live")]);
                Find<TextBox>("SearchBox").Text = "Artysta"; Pump();
                Check(Find<DataGrid>("TracksGrid").Items.Count == 1 && Items().Length == 2);
                Find<TextBox>("SearchBox").Text = ""; Find<CheckBox>("MissingOnly").IsChecked = true; Pump();
                Check(Find<DataGrid>("TracksGrid").Items.Count == 1 && Items().Length == 2);
                Find<CheckBox>("MissingOnly").IsChecked = false;
            });
            Test("Zmiana kolejności jest odwracalna", () =>
            {
                Load([new("C:\\ListyTests\\one.mp3"), new("C:\\ListyTests\\two.mp3"), new("C:\\ListyTests\\three.mp3")]);
                Find<DataGrid>("TracksGrid").SelectedItems.Add(Items()[2]); Call("MoveTracks", -1);
                Check(Items()[1].FileName == "three.mp3");
                Call("Undo_Click", window, new RoutedEventArgs()); Check(Items()[1].FileName == "two.mp3");
            });
            Test("Podgląd używa zdekodowanego tekstu dokumentu", () =>
            {
                var tracks = new Track[] { new("C:\\ListyTests\\Żółć.mp3") };
                Load(tracks, new(PlaylistFormat.M3U8, "#EXTM3U\nŻółć.mp3\n", "windows-1250", tracks));
                Find<TabControl>("PreviewTabs").SelectedIndex = 1; Pump();
                Check(Find<TextBox>("RawPreview").Text.Contains("Żółć"));
                Find<TabControl>("PreviewTabs").SelectedIndex = 0;
            });
            Test("Zmiana języka zachowuje niezapisane dane i nazwę playlisty", () =>
            {
                Load([new("C:\\ListyTests\\utwór.mp3") { Title = "Mój utwór" }]);
                Call("MarkDirty"); Find<ComboBox>("LanguageBox").SelectedIndex = 1; Pump();
                Check(Items().Single().Name == "Mój utwór" && Find<TextBlock>("EditorTitle").Text == "Test");
                Check(Find<TextBlock>("DirtyLabel").Visibility == Visibility.Visible);
                Check(Find<TextBlock>("EditorCount").Text.Contains("Tracks: 1"));
            });
            foreach (var label in new[] { "Anuluj", "Odrzuć", "Zapisz" }) Test("Niezapisany edytor: " + label, () =>
            {
                Text.SetLanguage("pl"); new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Listy-ui-" + Guid.NewGuid()); System.IO.Directory.CreateDirectory(folder);
                var path = System.IO.Path.Combine(folder, "Test.m3u8"); var before = new Track("https://example.org/old"); var after = new Track("https://example.org/new");
                PlaylistCodec.Save(path, [before], PlaylistFormat.M3U8, true);
                Call("SetEditor", new[] { after }, path, PlaylistFormat.M3U8, "Test", PlaylistCodec.ReadDocument(path)); Call("MarkDirty");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) }; var deadline = DateTime.UtcNow.AddSeconds(15); bool timedOut = false;
                IEnumerable<Button> Buttons(DependencyObject parent) { for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++) { var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i); if (child is Button button) yield return button; foreach (var nested in Buttons(child)) yield return nested; } }
                timer.Tick += (_, _) => { foreach (Window dialog in window.OwnedWindows.Cast<Window>().ToArray()) { if (DateTime.UtcNow > deadline) { timedOut = true; dialog.Close(); continue; } var button = Buttons(dialog).FirstOrDefault(b => b.Content?.ToString() == label || b.Content?.ToString() == "Tak"); if (button != null) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (dialog.IsVisible) dialog.Close(); } } };
                timer.Start(); try {
                    var pending = (Task<bool>)typeof(MainWindow).GetMethod("CanReplaceEditor", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null)!;
                    var frame = new DispatcherFrame(); _ = pending.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false)); if (!pending.IsCompleted) Dispatcher.PushFrame(frame);
                    Check(!timedOut && pending.Result == (label != "Anuluj"));
                    var saved = PlaylistCodec.ReadDocument(path).Tracks.Single().Location; Check(saved == (label == "Zapisz" ? after.Location : before.Location));
                    if (label == "Zapisz") Check(PlaylistRevisions.List(path).Length == 1);
                } finally { timer.Stop(); System.IO.Directory.Delete(folder, true); }
            });
        }
        finally
        {
            typeof(MainWindow).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
            window.Close(); app.Shutdown(); Text.SetLanguage("pl");
        }
        Console.WriteLine($"UI: {passed} passed, {failures.Count} failed.");
        return failures.Count == 0 ? 0 : 1;
    }
}
