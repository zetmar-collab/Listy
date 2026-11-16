using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Listy.Core;

public record PlaylistDifference(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, bool Reordered)
{
    public static PlaylistDifference Compare(IEnumerable<Track> before, IEnumerable<Track> after)
    {
        var a = before.ToArray(); var b = after.ToArray();
        var oldCounts = a.GroupBy(PlaylistTools.Identity).ToDictionary(g => g.Key, g => g.Count());
        var newCounts = b.GroupBy(PlaylistTools.Identity).ToDictionary(g => g.Key, g => g.Count());
        var added = new List<string>(); var removed = new List<string>(); var commonA = new List<string>(); var commonB = new List<string>();
        foreach (var t in b) { var key = PlaylistTools.Identity(t); if (oldCounts.GetValueOrDefault(key) == 0) added.Add(t.Location); else { oldCounts[key]--; commonB.Add(key); } }
        foreach (var t in a) { var key = PlaylistTools.Identity(t); if (newCounts.GetValueOrDefault(key) == 0) removed.Add(t.Location); else { newCounts[key]--; commonA.Add(key); } }
        return new(added, removed, !commonA.SequenceEqual(commonB));
    }
}
public record PlayerProfile(string Name, PlaylistFormat Format, string Encoding, bool Relative)
{
    public static IReadOnlyList<PlayerProfile> All { get; } = [
        new("VLC / foobar2000 — M3U8, UTF-8", PlaylistFormat.M3U8, "utf-8", true),
        new("Legacy — M3U, Windows-1250", PlaylistFormat.M3U, "windows-1250", true),
        new("PLS — UTF-8", PlaylistFormat.PLS, "utf-8", true),
        new("XSPF — URI", PlaylistFormat.XSPF, "utf-8", false)];
    public static Encoding GetEncoding(string name)
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return name == "windows-1250" ? System.Text.Encoding.GetEncoding(1250, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) : new UTF8Encoding(false, true);
    }
}
public record SmartRule(string Name = "", string Artist = "", string Album = "", string Extension = "", double? MinSeconds = null, double? MaxSeconds = null)
{
    public bool Matches(Track t) => (Artist.Length == 0 || (t.Artist ?? "").Contains(Artist, StringComparison.OrdinalIgnoreCase)) &&
        (Album.Length == 0 || (t.AlbumTitle ?? "").Contains(Album, StringComparison.OrdinalIgnoreCase)) &&
        (Extension.Length == 0 || t.Format.Equals(Extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) &&
        (!MinSeconds.HasValue || t.DurationSeconds >= MinSeconds) && (!MaxSeconds.HasValue || t.DurationSeconds <= MaxSeconds);
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath)!, "rules.json");
    public static SmartRule[] Load(string? path = null) { try { return JsonSerializer.Deserialize<SmartRule[]>(File.ReadAllText(path ?? DefaultPath)) ?? []; } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; } }
    public static void Save(IEnumerable<SmartRule> rules, string? path = null)
    {
        var target = path ?? DefaultPath; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var temp = target + "." + Guid.NewGuid() + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(rules)); File.Move(temp, target, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public static class PlaylistRevisions
{
    public static string[] List(string path) => Directory.Exists(Path.GetDirectoryName(path)) ? Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.bak").OrderByDescending(File.GetLastWriteTimeUtc).ToArray() : [];
    public static string Restore(string backup, string target)
    {
        target = Path.GetFullPath(target); backup = Path.GetFullPath(backup);
        if (!List(target).Contains(backup, StringComparer.OrdinalIgnoreCase)) throw new IOException("Backup does not belong to this playlist");
        if (new FileInfo(backup).Length > 32 * 1024 * 1024) throw new IOException("Backup exceeds 32 MB");
        var temp = target + "." + Guid.NewGuid() + ".tmp"; var previous = target + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid() + ".bak";
        try { File.Copy(backup, temp); if (File.Exists(target)) File.Replace(temp, target, previous); else File.Move(temp, target); return target; }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public static class AudioDuplicates
{
    public static IReadOnlyList<string[]> Find(IEnumerable<Track> tracks, CancellationToken token = default, IProgress<WorkProgress>? progress = null)
    {
        var paths = tracks.Where(t => !t.IsRemote && File.Exists(t.Location)).Select(t => Path.GetFullPath(t.Location)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hashes = new Dictionary<string, List<string>>(); int i = 0;
        foreach (var sameSize in paths.GroupBy(p => new FileInfo(p).Length).Where(g => g.Count() > 1)) foreach (var path in sameSize)
        {
            token.ThrowIfCancellationRequested(); using var stream = File.OpenRead(path); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[65536]; int read;
            while ((read = stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
            var key = Convert.ToHexString(hash.GetHashAndReset()); if (!hashes.TryGetValue(key, out var group)) hashes[key] = group = []; group.Add(path);
            progress?.Report(new(++i, paths.Length, Path.GetFileName(path)));
        }
        return hashes.Values.Where(g => g.Count > 1).Select(g => g.ToArray()).ToArray();
    }
}
public static class PortablePlaylist
{
    public static void Export(string destination, IEnumerable<Track> source, CancellationToken token = default)
    {
        var tracks = source.ToArray(); if (tracks.Any(t => t.IsRemote || !File.Exists(t.Location))) throw new IOException("ZIP requires accessible local music files");
        var estimate = tracks.Sum(t => new FileInfo(t.Location).Length); var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!);
        if (drive.IsReady && drive.AvailableFreeSpace < estimate + 1024 * 1024) throw new IOException("Insufficient free space");
        var temp = destination + "." + Guid.NewGuid() + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var playlist = new StringBuilder("#EXTM3U\n"); int i = 0;
                foreach (var t in tracks)
                {
                    token.ThrowIfCancellationRequested(); var name = "Music/" + (++i).ToString("D5") + "-" + Path.GetFileName(t.Location);
                    var entry = zip.CreateEntry(name, CompressionLevel.NoCompression); using var output = entry.Open(); using var input = File.OpenRead(t.Location); var buffer = new byte[65536]; int n;
                    while ((n = input.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, n); }
                    playlist.Append("#EXTINF:").Append(t.DurationSeconds is >= 0 ? ((long)t.DurationSeconds).ToString() : "-1").Append(',').Append(t.Name.Replace('\n', ' ').Replace('\r', ' ')).Append('\n').Append(name).Append('\n');
                }
                using var writer = new StreamWriter(zip.CreateEntry("Playlist.m3u8").Open(), new UTF8Encoding(false)); writer.Write(playlist);
            }
            token.ThrowIfCancellationRequested(); File.Move(temp, destination, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
