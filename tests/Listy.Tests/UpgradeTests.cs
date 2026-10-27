using System.Text;
using System.Xml.Linq;
using Listy.Core;

public static class UpgradeTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
        string List(string name, string text, Encoding? encoding = null)
        {
            var path = Path.Combine(root, name); File.WriteAllText(path, text, encoding ?? new UTF8Encoding(false)); return path;
        }
        test("Wspólny odczyt kodowania i tekstu podglądu", () =>
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var path = List("kodowanie.m3u", "#EXTM3U\n#EXTINF:123,Żółć\nŻółć.mp3\n", Encoding.GetEncoding(1250));
            var document = PlaylistCodec.ReadDocument(path);
            Check(document.RawText.Contains("Żółć")); Check(document.EncodingName == "windows-1250");
            Check(document.Tracks.Single().Title == "Żółć" && document.Tracks[0].DurationSeconds == 123);
        });
        test("Błędny album nie blokuje pozostałych", () =>
        {
            var snapshot = new LibrarySnapshot(root,
                [new(Path.Combine(root, "Dobry"), [new(Path.Combine(root, "a.mp3"))]),
                 new(Path.Combine(root, new string('A', 181)), [new(Path.Combine(root, "b.mp3"))])], [], []);
            var result = PlaylistPlanner.CreateDetailed(snapshot, new HashSet<string> { ".mp3" }, OutputMode.PerAlbum, PlaylistFormat.M3U8, "", "");
            Check(result.Plans.Count == 1 && result.Warnings.Count == 1);
        });
        test("Zaznaczenie albumów steruje również listą zbiorczą", () =>
        {
            var a = new Album(Path.Combine(root, "Wybrany"), [new(Path.Combine(root, "a.mp3"))]);
            var b = new Album(Path.Combine(root, "Pominięty"), [new(Path.Combine(root, "b.mp3"))]) { IsSelected = false };
            var snapshot = new LibrarySnapshot(root, [a, b], [], []);
            foreach (var mode in Enum.GetValues<OutputMode>())
            {
                var plans = PlaylistPlanner.Create(snapshot, new HashSet<string> { ".mp3" }, mode, PlaylistFormat.M3U8, "Test", "Podfolder");
                Check(plans.Sum(p => p.Count) == 1);
            }
        });
        test("Aktualizacja tworzy kopię dokładnej poprzedniej zawartości", () =>
        {
            var path = List("aktualizacja.m3u8", "#EXTM3U\nstary.mp3\n");
            var before = File.ReadAllBytes(path);
            var result = PlaylistWriter.Write(path, [new(Path.Combine(root, "nowy.mp3"))], PlaylistFormat.M3U8, true, ExistingPlaylistPolicy.UpdateWithBackup);
            Check(result.Written && result.BackupPath != null && before.SequenceEqual(File.ReadAllBytes(result.BackupPath)));
            Check(PlaylistCodec.Read(path).Single().FileName == "nowy.mp3");
        });
        test("Nowa wersja nie nadpisuje poprzednich wersji", () =>
        {
            var path = List("wersja.m3u8", "#EXTM3U\npierwszy.mp3\n");
            var second = PlaylistWriter.Write(path, [], PlaylistFormat.M3U8, true, ExistingPlaylistPolicy.NewVersion);
            var third = PlaylistWriter.Write(path, [], PlaylistFormat.M3U8, true, ExistingPlaylistPolicy.NewVersion);
            Check(second.Path.EndsWith("wersja (2).m3u8") && third.Path.EndsWith("wersja (3).m3u8"));
            Check(File.ReadAllText(path).Contains("pierwszy"));
        });
        test("Tryb pomijania i anulowanie nie zmieniają danych", () =>
        {
            var path = List("pomiń.m3u8", "original");
            Check(!PlaylistWriter.Write(path, [], PlaylistFormat.M3U8, true, ExistingPlaylistPolicy.Skip).Written);
            try { PlaylistWriter.Write(path, [], PlaylistFormat.M3U8, true, ExistingPlaylistPolicy.UpdateWithBackup, token: new CancellationToken(true)); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { Check(File.ReadAllText(path) == "original"); }
        });
        test("M3U zachowuje tytuły, czasy, atrybuty i komentarze po zmianie kolejności", () =>
        {
            var path = List("metadata.m3u8", "#EXTM3U\n#EXTVLCOPT:network-caching=1000\n#EXTINF:12 tvg-id=\"id1\",Pierwszy\none.mp3\n#EXTINF:25,Drugi\ntwo.mp3\n");
            var doc = PlaylistCodec.ReadDocument(path);
            var output = PlaylistCodec.Serialize(doc.Tracks.Reverse(), path, doc.Format, true, doc);
            Check(output.Contains("#EXTINF:12 tvg-id=\"id1\",Pierwszy"));
            Check(output.Contains("#EXTVLCOPT:network-caching=1000"));
            Check(output.IndexOf("Drugi", StringComparison.Ordinal) < output.IndexOf("Pierwszy", StringComparison.Ordinal));
        });
        test("PLS zachowuje pola niestandardowe podczas renumeracji", () =>
        {
            var path = List("metadata.pls", "[playlist]\nCustom=Root\nFile1=one.mp3\nTitle1=Jeden\nLength1=15\nCustom1=Value\nFile2=two.mp3\nTitle2=Dwa\nLength2=30\n");
            var doc = PlaylistCodec.ReadDocument(path);
            var output = PlaylistCodec.Serialize(doc.Tracks.Reverse(), path, doc.Format, true, doc);
            Check(output.Contains("Custom=Root") && output.Contains("Custom2=Value") && output.Contains("Title2=Jeden") && output.Contains("Length2=15"));
        });
        test("XSPF zachowuje metadane globalne i rozszerzenia utworów", () =>
        {
            var path = List("metadata.xspf", """
                <playlist xmlns="http://xspf.org/ns/0/" version="1"><title>Zestaw</title><creator>Autor</creator><extension application="test"><custom>root</custom></extension>
                <trackList><track><location>one.mp3</location><title>Jeden</title><creator>Artysta</creator><album>Album</album><trackNum>3</trackNum><duration>12500</duration><extension application="test"><custom>track</custom></extension></track></trackList></playlist>
                """);
            var doc = PlaylistCodec.ReadDocument(path);
            var track = doc.Tracks.Single();
            Check(track.Artist == "Artysta" && track.AlbumTitle == "Album" && track.TrackNumber == 3 && track.DurationSeconds == 12.5);
            var xml = XDocument.Parse(PlaylistCodec.Serialize(doc.Tracks, path, doc.Format, true, doc));
            Check(xml.Descendants().Count(e => e.Name.LocalName == "extension") == 2);
            Check(xml.Root!.Elements().Any(e => e.Name.LocalName == "creator" && e.Value == "Autor"));
        });
        test("WPL zachowuje metadane nagłówka i atrybuty media", () =>
        {
            var path = List("metadata.wpl", "<?wpl version=\"1.0\"?><smil><head><meta name=\"custom\" content=\"value\"/></head><body><seq><media src=\"one.mp3\" cid=\"keep\"/></seq></body></smil>");
            var doc = PlaylistCodec.ReadDocument(path);
            var output = PlaylistCodec.Serialize(doc.Tracks, path, doc.Format, true, doc);
            Check(output.Contains("cid=\"keep\"") && output.Contains("content=\"value\""));
        });
        test("XSPF uwzględnia xml:base", () =>
        {
            var location = new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri;
            var path = List("base.xspf", $"<playlist xmlns=\"http://xspf.org/ns/0/\" version=\"1\" xml:base=\"{location}\"><trackList><track><location>Album%20A/01%20%23.mp3</location></track></trackList></playlist>");
            Check(PlaylistCodec.Read(path).Single().Location == Path.Combine(root, "Album A", "01 #.mp3"));
            var document = PlaylistCodec.ReadDocument(path);
            var output = Path.Combine(root, "base-out.xspf");
            PlaylistCodec.Save(output, document.Tracks, PlaylistFormat.XSPF, true, document: document);
            Check(PlaylistCodec.Read(output).Single().Location == document.Tracks[0].Location);
        });
        test("Względne XSPF nie dekoduje procentów podwójnie", () =>
        {
            var track = new Track(Path.Combine(root, "100% # %23 &.mp3"));
            var path = Path.Combine(root, "percent.xspf");
            PlaylistCodec.Save(path, [track], PlaylistFormat.XSPF, true);
            Check(PlaylistCodec.Read(path).Single().Location == track.Location);
        });
        test("Kontrola wykrywa brakujące, powtórzone i puste listy", () =>
        {
            var path = List("exists.mp3", "fixture");
            var health = PlaylistTools.Validate([new(path), new(path), new(Path.Combine(root, "missing.mp3")), new("https://example.org/live")]);
            Check(health.Missing == 1 && health.Duplicates == 1 && !health.Empty);
            Check(PlaylistTools.Validate([]).Empty);
        });
        test("Naprawa ścieżek zachowuje podkatalogi i nie obejmuje sąsiednich folderów", () =>
        {
            var old = Path.Combine(root, "old"); var replacement = Path.Combine(root, "new");
            var tracks = new[] { new Track(Path.Combine(old, "Album", "song.mp3")) { Title = "Zachowany tytuł" },
                new Track(Path.Combine(root, "old-other", "song.mp3")), new Track("https://example.org/live") };
            var result = PlaylistTools.Relocate(tracks, old, replacement);
            Check(result[0].Location == Path.Combine(replacement, "Album", "song.mp3") && result[0].Title == "Zachowany tytuł");
            Check(ReferenceEquals(result[1], tracks[1]) && ReferenceEquals(result[2], tracks[2]));
        });
        test("Historia cofa i ponawia zmiany bez współdzielenia metadanych", () =>
        {
            var track = new Track(Path.Combine(root, "history.mp3")) { Title = "Przed" };
            var history = new EditorHistory(); history.Remember([track]); track.Title = "Po";
            var undo = history.Undo([track])!; Check(undo.Single().Title == "Przed" && history.CanRedo);
            var redo = history.Redo(undo)!; Check(redo.Single().Title == "Po");
            history.Remember(redo); Check(!history.CanRedo);
        });
        test("Zapamiętanie wszystkich ustawień i odporność na błędny JSON", () =>
        {
            var path = Path.Combine(root, "settings-upgrade.json");
            new AppSettings { Root = root, Format = 3, Mode = 2, Policy = 1, Relative = false, ReadMetadata = true, Width = 1500, Extensions = [".flac"], SelectedAlbums = [root] }.Save(path);
            var loaded = AppSettings.Load(path);
            Check(loaded.Root == root && loaded.Format == 3 && loaded.Policy == 1 && loaded.ReadMetadata && loaded.Extensions.Single() == ".flac" && loaded.SelectedAlbums!.Single() == root);
            File.WriteAllText(path, "{invalid"); Check(AppSettings.Load(path).Recursive);
        });
        test("Odczyt tagów i czasu z WAV bez modyfikacji pliku", () =>
        {
            var path = Path.Combine(root, "tagged.wav");
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                var samples = 16000;
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2); writer.Write(new byte[samples * 2]);
            }
            var tags = new ATL.Track(path) { Title = "Żółty utwór", Artist = "Artysta", Album = "Album", TrackNumber = 3 };
            tags.Save();
            var before = File.ReadAllBytes(path);
            var track = new Track(path); AudioMetadata.Read(track);
            Check(track.Title == "Żółty utwór" && track.Artist == "Artysta" && track.AlbumTitle == "Album" && track.TrackNumber == 3);
            Check(track.DurationSeconds is > 0.9 and < 1.1 && before.SequenceEqual(File.ReadAllBytes(path)));
            Check(track.Matches("artysta") && track.Matches("żółty"));
        });
        test("Nieprawidłowy czas i duży indeks PLS nie przerywają odczytu", () =>
        {
            var path = List("hostile.pls", "[playlist]\nFile9999999999999999999=skip.mp3\nFile1=ok.mp3\nLength1=1e100\n");
            var doc = PlaylistCodec.ReadDocument(path);
            Check(doc.Tracks.Count == 1 && doc.Warnings.Count == 1 && doc.Tracks[0].DurationSeconds == null);
        });
        test("Anulowanie odczytu i kontroli listy", () =>
        {
            var token = new CancellationToken(true); var path = List("cancel.m3u8", "#EXTM3U\nsong.mp3\n");
            try { PlaylistCodec.ReadDocument(path, token); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            try { PlaylistTools.Validate([new(path)], token); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
        });
        test("Aktualizacja automatyczna zachowuje metadane poprzednich wpisów", () =>
        {
            var path = List("update-metadata.m3u8", "#EXTM3U\n#CUSTOM:keep\n#EXTINF:42,Oryginalny tytuł\nold.mp3\n");
            PlaylistWriter.Write(path, [new(Path.Combine(root, "old.mp3")), new(Path.Combine(root, "new.mp3"))], PlaylistFormat.M3U8, true, ExistingPlaylistPolicy.UpdateWithBackup);
            var doc = PlaylistCodec.ReadDocument(path);
            Check(doc.Tracks.Count == 2 && doc.Tracks[0].Title == "Oryginalny tytuł" && doc.Tracks[0].DurationSeconds == 42);
            Check(doc.RawText.Contains("#CUSTOM:keep"));
            PlaylistWriter.Write(path, [new(Path.Combine(root, "old.mp3")) { DurationSeconds = 99 }], PlaylistFormat.M3U8, true, ExistingPlaylistPolicy.UpdateWithBackup);
            Check(PlaylistCodec.Read(path).Single().DurationSeconds == 99);
        });
        test("Sortowanie według tagów i naturalnych numerów", () =>
        {
            var tracks = new Track[] { new("C:\\10.mp3") { TrackNumber = 10, AlbumTitle = "Album" }, new("C:\\2.mp3") { TrackNumber = 2, AlbumTitle = "Album" } };
            Check(PlaylistTools.Sort(tracks, TrackOrder.FileName)[0].FileName == "2.mp3");
            Check(PlaylistTools.Sort(tracks, TrackOrder.Album)[0].TrackNumber == 2);
        });
        test("Duplikaty URL uwzględniają wielkość liter w ścieżce", () =>
        {
            var unique = PlaylistTools.Distinct([new("https://example.org/Live"), new("https://example.org/live"), new("https://example.org/live")]);
            Check(unique.Count == 2);
            Check(PlaylistTools.Distinct([new("C:\\Music\\Song.mp3"), new("c:\\music\\song.MP3")]).Count == 1);
        });
        test("Naprawa odświeża dostępność plików po przeniesieniu", () =>
        {
            var old = Path.Combine(root, "move-old"); var target = Path.Combine(root, "move-new");
            Directory.CreateDirectory(old); Directory.CreateDirectory(target);
            var path = Path.Combine(old, "song.mp3"); File.WriteAllText(path, "fixture");
            var track = new Track(path); Check(!track.IsMissing);
            File.Move(path, Path.Combine(target, "song.mp3"));
            var replacement = PlaylistTools.Relocate([track], old, target).Single();
            Check(replacement.Location == Path.Combine(target, "song.mp3") && !replacement.IsMissing);
        });
    }
}
