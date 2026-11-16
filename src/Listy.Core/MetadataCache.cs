using System.Text.Json;

namespace Listy.Core;

public sealed class MetadataCache
{
    public record Item(long Size, long Modified, string? Title, string? Artist, string? Album, int? Number, double? Duration);
    private readonly string _path;
    private Dictionary<string, Item> _items = new(StringComparer.OrdinalIgnoreCase);
    public int Hits { get; private set; }
    public int Reads { get; private set; }
    public MetadataCache(string? path = null)
    {
        _path = path ?? Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath)!, "metadata-cache.json");
        try { if (new FileInfo(_path).Length < 32 * 1024 * 1024) _items = new(JsonSerializer.Deserialize<Dictionary<string, Item>>(File.ReadAllText(_path)) ?? [], StringComparer.OrdinalIgnoreCase); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }
    public void Read(Track track, Action<Track>? reader = null)
    {
        var file = new FileInfo(track.Location); var key = file.FullName;
        if (_items.TryGetValue(key, out var item) && item.Size == file.Length && item.Modified == file.LastWriteTimeUtc.Ticks)
        {
            track.Title = item.Title; track.Artist = item.Artist; track.AlbumTitle = item.Album; track.TrackNumber = item.Number; track.DurationSeconds = item.Duration; Hits++; return;
        }
        (reader ?? AudioMetadata.Read)(track); Reads++;
        if (track.MetadataWarning == null) _items[key] = new(file.Length, file.LastWriteTimeUtc.Ticks, track.Title, track.Artist, track.AlbumTitle, track.TrackNumber, track.DurationSeconds);
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!); var temp = _path + "." + Guid.NewGuid() + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(_items.TakeLast(100000).ToDictionary())); File.Move(temp, _path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Clear() { _items.Clear(); Save(); }
}
