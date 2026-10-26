using System.Text;
using Listy.Core;

var root = Path.Combine(Path.GetTempPath(), "Listy-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
var failed = new List<string>();
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + name + " — " + ex); }
}
void Assert(bool condition, string message = "Warunek nie został spełniony") { if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Oczekiwano wyjątku " + typeof(T).Name);
}
string Fixture(string relative)
{
    var path = Path.GetFullPath(Path.Combine(root, relative));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "fixture");
    return path;
}
try
{
    var first = Fixture("Album A/02 Żółć # &.mp3");
    var second = Fixture("Album A/10 Koniec.flac");
    Fixture("Album A/01 Start.mp3");
    Fixture("Album A/okładka.jpg");
    Fixture("Album B/CD 1/01 Inny.wav");
    Fixture("Album B/stara.m3u8");
    var top = Fixture("samotny.ogg");
    var snapshot = LibraryScanner.Scan(root, true);
    Test("Skanowanie rekurencyjne i filtrowanie", () => { Assert(snapshot.Tracks.Count == 5); Assert(snapshot.Albums.Count == 3); Assert(snapshot.Playlists.Count == 1); });
    Test("Skanowanie bez podkatalogów", () => Assert(LibraryScanner.Scan(root, false).Tracks.Single().Location == top));
    Test("Naturalna kolejność", () => Assert(snapshot.Albums.Single(a => a.Name == "Album A").Tracks.Select(t => Path.GetFileName(t.Location)).SequenceEqual(new[] { "01 Start.mp3", "02 Żółć # &.mp3", "10 Koniec.flac" })));
    Test("Anulowanie skanowania", () => Throws<OperationCanceledException>(() => LibraryScanner.Scan(root, true, new CancellationToken(true))));
    Test("Brak katalogu", () => Throws<DirectoryNotFoundException>(() => LibraryScanner.Scan(Path.Combine(root, "nieistniejący"), true)));
    var tracks = new Track[] { new(first), new(second), new("https://example.org/live?x=1&y=2"), new(Path.Combine(root, "brak pliku.mp3")) };
    foreach (var format in Enum.GetValues<PlaylistFormat>())
    foreach (var relative in new[] { true, false })
    {
        var path = Path.Combine(root, $"roundtrip-{format}-{relative}" + PlaylistCodec.Extension(format));
        Test($"{format} zapis/odczyt, względne={relative}", () =>
        {
            PlaylistCodec.Save(path, tracks, format, relative);
            var loaded = PlaylistCodec.Read(path);
            Assert(loaded.Select(t => t.Location).SequenceEqual(tracks.Select(t => t.Location)), File.ReadAllText(path));
        });
    }
    Test("Ochrona istniejącej playlisty", () =>
    {
        var path = Path.Combine(root, "chroniona.m3u8");
        PlaylistCodec.Save(path, tracks, PlaylistFormat.M3U8, true);
        var before = File.ReadAllBytes(path);
        Throws<IOException>(() => PlaylistCodec.Save(path, [], PlaylistFormat.M3U8, true));
        Assert(before.SequenceEqual(File.ReadAllBytes(path)));
        Assert(Directory.GetFiles(root, ".listy-*.tmp").Length == 0);
    });
    Test("Jawne nadpisanie i pusta lista", () =>
    {
        var path = Path.Combine(root, "chroniona.m3u8");
        PlaylistCodec.Save(path, [], PlaylistFormat.M3U8, true, true);
        Assert(PlaylistCodec.Read(path).Count == 0);
    });
    Test("Niezgodność rozszerzenia", () => Throws<InvalidDataException>(() => PlaylistCodec.Save(Path.Combine(root, "zla.pls"), tracks, PlaylistFormat.M3U8, true)));
    Test("Polskie nazwy w M3U Windows-1250", () =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(root, "legacy.m3u");
        File.WriteAllBytes(path, Encoding.GetEncoding(1250).GetBytes(first + "\r\n"));
        Assert(PlaylistCodec.Read(path).Single().Location == first);
    });
    Test("Odczyt numerowanej listy PLS", () =>
    {
        var path = Path.Combine(root, "numerowana.pls");
        File.WriteAllText(path, $"[playlist]\nFile10={second}\nfile2={first}\n");
        Assert(PlaylistCodec.Read(path).Select(t => t.Location).SequenceEqual(new[] { first, second }));
    });
    Test("XML z DTD jest odrzucany", () =>
    {
        var path = Path.Combine(root, "unsafe.xspf");
        File.WriteAllText(path, "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><playlist><location>&e;</location></playlist>");
        Throws<System.Xml.XmlException>(() => PlaylistCodec.Read(path));
    });
    var onlyMp3 = new HashSet<string>([".mp3"], StringComparer.OrdinalIgnoreCase);
    Test("Plan osobno w albumach", () =>
    {
        var plans = PlaylistPlanner.Create(snapshot, onlyMp3, OutputMode.PerAlbum, PlaylistFormat.M3U8, "Muzyka", "Listy");
        Assert(plans.Count == 1 && plans[0].Count == 2);
        Assert(plans[0].Folder == Path.GetDirectoryName(first));
        Assert(plans[0].Name == "Album A.m3u8");
    });
    Test("Każda lista przyjmuje nazwę własnego folderu we wszystkich formatach", () =>
    {
        foreach (var format in Enum.GetValues<PlaylistFormat>())
        {
            var plans = PlaylistPlanner.Create(snapshot, LibraryScanner.AudioExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase), OutputMode.PerAlbum, format, "", "Listy");
            Assert(plans.Count == 3);
            Assert(plans.All(p => p.Name == Path.GetFileName(p.Folder) + PlaylistCodec.Extension(format)));
            Assert(plans.Any(p => p.Name == "CD 1" + PlaylistCodec.Extension(format)));
        }
    });
    Test("Istniejąca lista z nazwą albumu jest oznaczona do pominięcia", () =>
    {
        var plan = PlaylistPlanner.Create(snapshot, onlyMp3, OutputMode.PerAlbum, PlaylistFormat.M3U8, "", "Listy").Single();
        PlaylistCodec.Save(plan.Path, plan.Tracks, PlaylistFormat.M3U8, true);
        Assert(plan.State == "Istnieje — zostanie pominięta");
    });
    Test("Plan zbiorczy w katalogu głównym", () =>
    {
        var plans = PlaylistPlanner.Create(snapshot, onlyMp3, OutputMode.Root, PlaylistFormat.PLS, "Muzyka", "Listy");
        Assert(plans.Single().Path == Path.Combine(root, "Muzyka.pls"));
    });
    Test("Plan zbiorczy w podkatalogu", () =>
    {
        var plans = PlaylistPlanner.Create(snapshot, onlyMp3, OutputMode.Subfolder, PlaylistFormat.XSPF, "Muzyka", "Listy");
        Assert(plans.Single().Path == Path.Combine(root, "Listy", "Muzyka.xspf"));
    });
    Test("Brak dopasowań nie tworzy list", () => Assert(PlaylistPlanner.Create(snapshot, new HashSet<string>(), OutputMode.Root, PlaylistFormat.M3U8, "Muzyka", "Listy").Count == 0));
    foreach (var name in new[] { "../escape", "", "A:B", "CON", "NUL.txt", "LPT1", "kropka.", "spacja ", "..", "COM¹" })
        Test("Odrzucenie nazwy: " + name, () => Throws<ArgumentException>(() => PlaylistPlanner.ValidateName(name)));
    Test("Poprawna polska nazwa", () => PlaylistPlanner.ValidateName("Żółta łódź — lato 2026"));
    Test("Zmiana nazwy bez zmiany zawartości", () =>
    {
        var path = Path.Combine(root, "przed.m3u8");
        PlaylistCodec.Save(path, tracks, PlaylistFormat.M3U8, true);
        var before = File.ReadAllBytes(path);
        var target = PlaylistPlanner.Rename(path, "Nowa nazwa");
        Assert(!File.Exists(path) && before.SequenceEqual(File.ReadAllBytes(target)));
    });
    Test("Zmiana nazwy nie nadpisuje pliku", () =>
    {
        var a = Fixture("a.m3u8"); var b = Fixture("b.m3u8");
        Throws<IOException>(() => PlaylistPlanner.Rename(a, "b"));
        Assert(File.Exists(a) && File.ReadAllText(b) == "fixture");
    });
    Test("Zapis do podkatalogu i ponowny skan", () =>
    {
        var plan = PlaylistPlanner.Create(snapshot, onlyMp3, OutputMode.Subfolder, PlaylistFormat.WPL, "Lista", "Zestawy").Single();
        Directory.CreateDirectory(plan.Folder);
        PlaylistCodec.Save(plan.Path, plan.Tracks, PlaylistFormat.WPL, true);
        Assert(LibraryScanner.Scan(root, true).Playlists.Any(p => p.Path == plan.Path));
        Assert(PlaylistCodec.Read(plan.Path).Count == 2);
    });
    Test("Brak pliku jest widoczny", () => Assert(tracks[^1].State == "Brak pliku"));
    Test("M3U8 nazwa zaczynająca się od #", () =>
    {
        var song = Fixture("#piosenka.mp3");
        var list = Path.Combine(root, "hash.m3u8");
        PlaylistCodec.Save(list, [new(song)], PlaylistFormat.M3U8, true);
        Assert(PlaylistCodec.Read(list).Single().Location == song);
    });
    Test("Odczyt XML UTF-16 z BOM", () =>
    {
        var path = Path.Combine(root, "unicode.wpl");
        File.WriteAllText(path, $"<?xml version=\"1.0\" encoding=\"utf-16\"?><smil><body><seq><media src=\"{top}\"/></seq></body></smil>", Encoding.Unicode);
        Assert(PlaylistCodec.Read(path).Single().Location == top);
    });
    Test("Strumień nie jest traktowany jako lokalny plik", () => Assert(tracks[2].IsRemote && tracks[2].State == "Strumień"));
    Test("Kompletność tłumaczeń i zgodność parametrów", () =>
    {
        var pl = Text.Catalog("pl"); var en = Text.Catalog("en");
        Assert(pl.Keys.Order().SequenceEqual(en.Keys.Order()));
        foreach (var key in pl.Keys)
        {
            Assert(!string.IsNullOrWhiteSpace(en[key]), key);
            var pattern = @"\{\d+\}";
            var plArgs = System.Text.RegularExpressions.Regex.Matches(pl[key], pattern).Select(m => m.Value).Order();
            var enArgs = System.Text.RegularExpressions.Regex.Matches(en[key], pattern).Select(m => m.Value).Order();
            Assert(plArgs.SequenceEqual(enArgs), key);
            foreach (var language in new[] { "pl", "en" })
            {
                Text.SetLanguage(language);
                Assert(!Text.T(key, 1, 2, 3, 4).Contains("{0}"), key);
            }
        }
        Text.SetLanguage("pl");
    });
    Test("Angielskie statusy, błędy i podsumowania", () =>
    {
        try
        {
            Text.SetLanguage("en");
            Assert(tracks[0].State == "Available");
            Assert(tracks[2].State == "Stream" && tracks[2].Folder == "Internet stream");
            Assert(tracks[^1].State == "Missing file");
            Assert(Text.T("T075", 5, 2, 1, 0) == "Loaded tracks: 5, folders: 2, playlists: 1. Skipped folders: 0.");
            try { PlaylistPlanner.ValidateName("CON"); throw new Exception("Expected validation error"); }
            catch (ArgumentException ex) { Assert(ex.Message == "This name is reserved by Windows."); }
            try { PlaylistCodec.Detect("unknown.xyz"); throw new Exception("Expected format error"); }
            catch (InvalidDataException ex) { Assert(ex.Message == "Unsupported playlist format."); }
        }
        finally { Text.SetLanguage("pl"); }
    });
    Test("Zmiana języka zachowuje nazwy i zapis playlist", () =>
    {
        try
        {
            var plPlans = PlaylistPlanner.Create(snapshot, onlyMp3, OutputMode.PerAlbum, PlaylistFormat.M3U8, "", "");
            var plText = PlaylistCodec.Serialize(tracks, Path.Combine(root, "język.m3u8"), PlaylistFormat.M3U8, true);
            Text.SetLanguage("en");
            var enPlans = PlaylistPlanner.Create(snapshot, onlyMp3, OutputMode.PerAlbum, PlaylistFormat.M3U8, "", "");
            Assert(plPlans.Select(p => p.Path).SequenceEqual(enPlans.Select(p => p.Path)));
            Assert(tracks[0].Name == Path.GetFileNameWithoutExtension(first));
            Assert(plText == PlaylistCodec.Serialize(tracks, Path.Combine(root, "język.m3u8"), PlaylistFormat.M3U8, true));
            Assert(enPlans[0].State == "Exists — will be skipped");
        }
        finally { Text.SetLanguage("pl"); }
    });
    Test("Zapamiętanie języka i bezpieczny domyślny język", () =>
    {
        var path = Path.Combine(root, "settings", "language.txt");
        Assert(LanguagePreference.Load(path) == "pl");
        LanguagePreference.Save("en", path);
        Assert(LanguagePreference.Load(path) == "en");
        LanguagePreference.Save("pl", path);
        Assert(LanguagePreference.Load(path) == "pl");
        File.WriteAllText(path, "invalid");
        Assert(LanguagePreference.Load(path) == "pl");
        Text.SetLanguage("invalid");
        Assert(Text.Language == "pl");
    });
}
finally
{
    if (!Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "Listy-tests-"), StringComparison.OrdinalIgnoreCase)) throw new Exception("Nieprawidłowy katalog testowy");
    Directory.Delete(root, true);
}
Console.WriteLine($"\nWynik: {passed} zaliczonych, {failed.Count} błędów.");
Environment.ExitCode = failed.Count == 0 ? 0 : 1;
