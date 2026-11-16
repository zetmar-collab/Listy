using Listy.Core;
using System.IO.Compression;
using System.Text;

public static class StudioTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        var folder = Path.Combine(root, "studio"); Directory.CreateDirectory(folder);
        void Check(bool condition) { if (!condition) throw new Exception("Studio assertion failed"); }
        test("Backup restoration preserves current version and refuses unrelated file", () => {
            var path = Path.Combine(folder, "Album.m3u8"); var first = new Track(Path.Combine(folder, "a.mp3")); var second = new Track(Path.Combine(folder, "b.mp3"));
            PlaylistWriter.Write(path, [first], PlaylistFormat.M3U8, true, ExistingPlaylistPolicy.Skip);
            PlaylistWriter.Write(path, [second], PlaylistFormat.M3U8, true, ExistingPlaylistPolicy.UpdateWithBackup);
            var backup = PlaylistRevisions.List(path).Single(); PlaylistRevisions.Restore(backup, path);
            Check(PlaylistCodec.ReadDocument(path).Tracks.Single().Location == first.Location);
            Check(PlaylistRevisions.List(path).Any(b => File.ReadAllText(b).Contains("b.mp3")));
            try { PlaylistRevisions.Restore(Path.Combine(folder, "other.bak"), path); throw new Exception("unrelated backup accepted"); } catch (IOException) { }
        });
        test("Differences count repeated entries and detect order", () => {
            var a = new Track("https://music/a"); var b = new Track("https://music/b");
            var d = PlaylistDifference.Compare([a, b, a], [b, a]); Check(d.Removed.Count == 1 && d.Added.Count == 0 && d.Reordered);
            Check(PlaylistDifference.Compare([a], [a, b]).Added.Count == 1);
        });
        test("Legacy profile writes Polish CP1250 and rejects unsupported text", () => {
            var path = Path.Combine(folder, "legacy.m3u"); PlaylistCodec.Save(path, [new Track(Path.Combine(folder, "Żółć.mp3"))], PlaylistFormat.M3U, true, encodingName: "windows-1250");
            Check(PlaylistCodec.ReadDocument(path).Tracks.Single().Location.EndsWith("Żółć.mp3")); var before = File.ReadAllBytes(path);
            try { PlaylistCodec.Save(path, [new Track(Path.Combine(folder, "😀.mp3"))], PlaylistFormat.M3U, true, overwrite: true, encodingName: "windows-1250"); throw new Exception("lossy encoding accepted"); } catch (EncoderFallbackException) { }
            Check(before.SequenceEqual(File.ReadAllBytes(path)));
        });
        test("Saved rules filter tags extension and duration", () => {
            var rule = new SmartRule("Jazz", "Marek", "Live", "mp3", 60, 180); var path = Path.Combine(folder, "rules.json"); SmartRule.Save([rule], path); var loaded = SmartRule.Load(path).Single();
            var t = new Track("a.mp3") { Artist = "MAREK Z", AlbumTitle = "Live", DurationSeconds = 90 }; Check(loaded.Matches(t)); t.DurationSeconds = null; Check(!loaded.Matches(t)); t.DurationSeconds = 200; Check(!loaded.Matches(t));
        });
        test("Metadata cache persists hits and invalidates changed files", () => {
            var audio = Path.Combine(folder, "cache.wav"); var path = Path.Combine(folder, "cache.json"); File.WriteAllText(audio, "one"); var reads = 0;
            void Reader(Track t) { reads++; t.Title = "cached"; }
            var cache = new MetadataCache(path); cache.Read(new Track(audio), Reader); cache.Save(); cache = new MetadataCache(path); var hit = new Track(audio); cache.Read(hit, Reader); Check(reads == 1 && cache.Hits == 1 && hit.Title == "cached");
            File.AppendAllText(audio, "changed"); cache.Read(new Track(audio), Reader); Check(reads == 2);
            cache.Clear(); new MetadataCache(path).Read(new Track(audio), Reader); Check(reads == 3);
        });
        test("Content duplicates ignore names and support cancellation", () => {
            var a = Path.Combine(folder, "same-a.mp3"); var b = Path.Combine(folder, "same-b.mp3"); var c = Path.Combine(folder, "different.mp3"); File.WriteAllText(a, "equal"); File.WriteAllText(b, "equal"); File.WriteAllText(c, "other"); var tracks = new[] { new Track(a), new Track(b), new Track(c) };
            Check(AudioDuplicates.Find(tracks).Single().Length == 2);
            try { AudioDuplicates.Find(tracks, new CancellationToken(true)); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        });
        test("Portable ZIP includes unique names and relative playlist without overwriting", () => {
            var a = Path.Combine(folder, "same-a.mp3"); var destination = Path.Combine(folder, "music.zip"); PortablePlaylist.Export(destination, [new Track(a), new Track(a)]);
            using (var zip = ZipFile.OpenRead(destination)) { Check(zip.Entries.Count == 3); using var reader = new StreamReader(zip.GetEntry("Playlist.m3u8")!.Open()); var text = reader.ReadToEnd(); Check(text.Contains("Music/00001-same-a.mp3") && text.Contains("Music/00002-same-a.mp3")); }
            var before = File.ReadAllBytes(destination); try { PortablePlaylist.Export(destination, [new Track(a)]); throw new Exception("Overwrite accepted"); } catch (IOException) { } Check(before.SequenceEqual(File.ReadAllBytes(destination)));
            var cancelled = Path.Combine(folder, "cancelled.zip"); try { PortablePlaylist.Export(cancelled, [new Track(a)], new CancellationToken(true)); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { } Check(!File.Exists(cancelled));
        });
        test("Playlist parser stops at 200000 entries", () => {
            var path = Path.Combine(folder, "oversized.m3u8"); File.WriteAllText(path, string.Concat(Enumerable.Repeat("a.mp3\n", 200001)));
            try { PlaylistCodec.ReadDocument(path); throw new Exception("Track limit ignored"); } catch (InvalidDataException) { }
        });
    }
}
