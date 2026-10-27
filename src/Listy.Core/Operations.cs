using System.Text.Json;

namespace Listy.Core;

public record WorkProgress(int Completed, int Total, string Detail);
public enum ExistingPlaylistPolicy { Skip, UpdateWithBackup, NewVersion }
public enum TrackOrder { FileName, Artist, Album, TrackNumber }
public record WriteResult(string Path, bool Written, string? BackupPath);

public static class PlaylistWriter
{
    public static WriteResult Write(string path, IEnumerable<Track> tracks, PlaylistFormat format, bool relative,
        ExistingPlaylistPolicy policy, PlaylistDocument? document = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string? backup = null;
        if (File.Exists(path))
        {
            if (policy == ExistingPlaylistPolicy.Skip) return new(path, false, null);
            if (policy == ExistingPlaylistPolicy.NewVersion)
            {
                var basePath = path;
                for (var i = 2; File.Exists(path); i++)
                    path = Path.Combine(Path.GetDirectoryName(basePath)!, Path.GetFileNameWithoutExtension(basePath) + $" ({i})" + Path.GetExtension(basePath));
            }
            else
            {
                backup = path + $".{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak";
                if (document == null)
                {
                    try
                    {
                        document = PlaylistCodec.ReadDocument(path, token);
                        var previous = document.Tracks.GroupBy(PlaylistTools.Identity, StringComparer.Ordinal)
                            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                        tracks = tracks.Select(t =>
                        {
                            if (!previous.TryGetValue(PlaylistTools.Identity(t), out var old)) return t;
                            var merged = old.Copy(t.Location);
                            merged.Title = t.Title ?? old.Title; merged.Artist = t.Artist ?? old.Artist;
                            merged.AlbumTitle = t.AlbumTitle ?? old.AlbumTitle; merged.TrackNumber = t.TrackNumber ?? old.TrackNumber;
                            merged.DurationSeconds = t.DurationSeconds ?? old.DurationSeconds;
                            return merged;
                        }).ToArray();
                    }
                    catch (Exception ex) when (ex is IOException or System.Xml.XmlException or ArgumentException)
                    { document = null; }
                }
            }
        }
        PlaylistCodec.Save(path, tracks, format, relative, policy == ExistingPlaylistPolicy.UpdateWithBackup, document, token, backup);
        return new(path, true, backup != null && File.Exists(backup) ? backup : null);
    }
}

public static class AudioMetadata
{
    public static void Read(Track track)
    {
        if (track.IsRemote || track.IsMissing) return;
        try
        {
            var data = new ATL.Track(track.Location);
            track.Title = string.IsNullOrWhiteSpace(data.Title) ? track.Title : data.Title;
            track.Artist = string.IsNullOrWhiteSpace(data.Artist) ? track.Artist : data.Artist;
            track.AlbumTitle = string.IsNullOrWhiteSpace(data.Album) ? track.AlbumTitle : data.Album;
            if (int.TryParse(Convert.ToString(data.TrackNumber)?.Split('/')[0], out var number) && number > 0) track.TrackNumber = number;
            if (double.IsFinite(data.DurationMs) && data.DurationMs > 0 && data.DurationMs / 1000.0 < TimeSpan.MaxValue.TotalSeconds) track.DurationSeconds = data.DurationMs / 1000.0;
            track.MetadataWarning = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { track.MetadataWarning = ex.Message; }
    }
}

public record PlaylistHealth(int Missing, int Duplicates, bool Empty);
public static class PlaylistTools
{
    public static IReadOnlyList<Track> Sort(IEnumerable<Track> source, TrackOrder order) => (order switch
    {
        TrackOrder.Artist => source.OrderBy(t => t.Artist ?? "", NaturalPathComparer.Instance).ThenBy(t => t.AlbumTitle ?? "", NaturalPathComparer.Instance).ThenBy(t => t.TrackNumber ?? int.MaxValue).ThenBy(t => t.Name, NaturalPathComparer.Instance),
        TrackOrder.Album => source.OrderBy(t => t.AlbumTitle ?? "", NaturalPathComparer.Instance).ThenBy(t => t.TrackNumber ?? int.MaxValue).ThenBy(t => t.Name, NaturalPathComparer.Instance),
        TrackOrder.TrackNumber => source.OrderBy(t => t.TrackNumber ?? int.MaxValue).ThenBy(t => t.Name, NaturalPathComparer.Instance),
        _ => source.OrderBy(t => t.FileName, NaturalPathComparer.Instance)
    }).ToArray();
    public static string Identity(Track track) => track.IsRemote ? new Uri(track.Location).AbsoluteUri : Path.GetFullPath(track.Location).ToUpperInvariant();
    public static PlaylistHealth Validate(IEnumerable<Track> source, CancellationToken token = default)
    {
        var tracks = source.ToArray();
        foreach (var track in tracks) { token.ThrowIfCancellationRequested(); track.RefreshAvailability(); }
        return new(tracks.Count(t => t.IsMissing), tracks.Length - Distinct(tracks).Count, tracks.Length == 0);
    }
    public static IReadOnlyList<Track> Distinct(IEnumerable<Track> source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return source.Where(t => seen.Add(Identity(t))).ToArray();
    }
    public static IReadOnlyList<Track> Relocate(IEnumerable<Track> source, string oldRoot, string newRoot, bool onlyMissing = true, CancellationToken token = default)
    {
        oldRoot = Path.GetFullPath(oldRoot);
        newRoot = Path.GetFullPath(newRoot);
        return source.Select(t =>
        {
            token.ThrowIfCancellationRequested();
            t.RefreshAvailability();
            if (t.IsRemote || (onlyMissing && !t.IsMissing)) return t;
            var relative = Path.GetRelativePath(oldRoot, t.Location);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) return t;
            return t.Copy(Path.GetFullPath(relative, newRoot));
        }).ToArray();
    }
}

public sealed class EditorHistory
{
    private readonly Stack<Track[]> _undo = new(), _redo = new();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Clear() { _undo.Clear(); _redo.Clear(); }
    public void Remember(IEnumerable<Track> tracks)
    {
        _undo.Push(tracks.Select(t => t.Copy()).ToArray()); _redo.Clear();
        if (_undo.Count <= 30) return;
        var recent = _undo.Take(30).Reverse().ToArray(); _undo.Clear();
        foreach (var entry in recent) _undo.Push(entry);
    }
    public Track[]? Undo(IEnumerable<Track> current)
    {
        if (!_undo.TryPop(out var result)) return null;
        _redo.Push(current.Select(t => t.Copy()).ToArray()); return result;
    }
    public Track[]? Redo(IEnumerable<Track> current)
    {
        if (!_redo.TryPop(out var result)) return null;
        _undo.Push(current.Select(t => t.Copy()).ToArray()); return result;
    }
}

public sealed record AppSettings
{
    public string? Root { get; init; }
    public string Language { get; init; } = "pl";
    public bool Recursive { get; init; } = true;
    public bool Relative { get; init; } = true;
    public bool ReadMetadata { get; init; }
    public int Format { get; init; }
    public int Mode { get; init; }
    public int Policy { get; init; }
    public string Name { get; init; } = Text.T("T133");
    public string Subfolder { get; init; } = Text.T("T134");
    public int SortMode { get; init; }
    public string[] Extensions { get; init; } = LibraryScanner.AudioExtensions;
    public string[]? SelectedAlbums { get; init; }
    public double Width { get; init; } = 1360;
    public double Height { get; init; } = 900;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Listy", "settings.json");
    public static AppSettings Load(string? path = null)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path ?? DefaultPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string? path = null)
    {
        var target = Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this)); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
