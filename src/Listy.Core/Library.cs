using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Xml.Linq;

namespace Listy.Core;

public sealed class Track(string location)
{
    public string Location { get; private set; } = location;
    private bool _exists = File.Exists(location);
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? AlbumTitle { get; set; }
    public int? TrackNumber { get; set; }
    public double? DurationSeconds { get; set; }
    public string? MetadataWarning { get; set; }
    public XElement? SourceXml { get; set; }
    public List<string> ExtraLines { get; set; } = [];
    public Dictionary<string, string> ExtraFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ExtendedInfo { get; set; }
    public bool IsRemote => Uri.TryCreate(Location, UriKind.Absolute, out var uri) && !uri.IsFile;
    public string FileName => IsRemote ? Location : Path.GetFileName(Location);
    public string Name => string.IsNullOrWhiteSpace(Title) ? IsRemote ? Location : Path.GetFileNameWithoutExtension(Location) : Title;
    public string DurationLabel => DurationSeconds is >= 0 && DurationSeconds <= TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(DurationSeconds.Value).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "—";
    public string Format => IsRemote ? "URL" : Path.GetExtension(Location).TrimStart('.').ToUpperInvariant();
    public string Folder => IsRemote ? Text.T("T116") : Path.GetDirectoryName(Location) ?? "";
    public bool IsMissing => !IsRemote && !_exists;
    public string State => IsRemote ? Text.T("T117") : _exists ? Text.T("T118") : Text.T("T119");
    public void RefreshAvailability() => _exists = File.Exists(Location);
    public Track Copy(string? location = null)
    {
        var copy = (Track)MemberwiseClone();
        copy.ExtraLines = [.. ExtraLines];
        copy.ExtraFields = new(ExtraFields, StringComparer.OrdinalIgnoreCase);
        copy.SourceXml = SourceXml == null ? null : new(SourceXml);
        if (location != null) { copy.Location = location; copy.RefreshAvailability(); }
        return copy;
    }
    public bool Matches(string query) => string.IsNullOrWhiteSpace(query) ||
        $"{Name} {Artist} {AlbumTitle} {Location}".Contains(query, StringComparison.CurrentCultureIgnoreCase);
}

public record Album(string Directory, IReadOnlyList<Track> Tracks) : INotifyPropertyChanged
{
    private bool _selected = true;
    public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name => Path.GetFileName(Directory.TrimEnd(Path.DirectorySeparatorChar));
    public string Description => Text.T("T120", Tracks.Count, Directory);
    public string TrackCountLabel => Text.T("T098", Tracks.Count);
    public override string ToString() => Name;
}

public record PlaylistFile(string Path)
{
    public PlaylistHealth? Health { get; set; }
    public string? CheckError { get; set; }
    public bool HasIssues => CheckError != null || Health is { Missing: > 0 } or { Duplicates: > 0 } or { Empty: true };
    public string CheckSummary => CheckError ?? (Health == null ? "—" : Text.T("HealthSummary", Health.Missing, Health.Duplicates, Health.Empty ? Text.T("T130") : Text.T("T131")));
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public override string ToString() => Name;
}

public record LibrarySnapshot(string Root, IReadOnlyList<Album> Albums, IReadOnlyList<PlaylistFile> Playlists,
    IReadOnlyList<string> Warnings)
{
    private IReadOnlyList<Track>? _allTracks;
    public IReadOnlyList<Track> Tracks => _allTracks ??= Albums.SelectMany(a => a.Tracks).ToArray();
}

public static class LibraryScanner
{
    public static readonly string[] AudioExtensions = [".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".aiff", ".aif", ".ape", ".alac"];
    public static readonly string[] PlaylistExtensions = [".m3u8", ".m3u", ".pls", ".xspf", ".wpl"];

    public static LibrarySnapshot Scan(string root, bool recursive, CancellationToken token = default,
        IProgress<WorkProgress>? progress = null, bool readMetadata = false)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(Text.T("T121"));
        var albums = new List<Album>();
        var playlists = new List<PlaylistFile>();
        var warnings = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        var folders = 0;
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new(++folders, 0, folder));
            try
            {
                var tracks = new List<Track>();
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    token.ThrowIfCancellationRequested();
                    var ext = Path.GetExtension(file);
                    if (AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        var track = new Track(file);
                        if (readMetadata) AudioMetadata.Read(track);
                        if (track.MetadataWarning != null) warnings.Add(file + ": " + track.MetadataWarning);
                        tracks.Add(track);
                    }
                    else if (PlaylistExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) playlists.Add(new(file));
                }
                if (tracks.Count > 0) albums.Add(new(folder, tracks.OrderBy(t => t.Location, NaturalPathComparer.Instance).ToArray()));
                if (recursive)
                    foreach (var child in Directory.EnumerateDirectories(folder))
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{folder}: {ex.Message}");
            }
        }
        return new(root, albums.OrderBy(a => a.Directory, NaturalPathComparer.Instance).ToArray(),
            playlists.OrderBy(p => p.Path, NaturalPathComparer.Instance).ToArray(), warnings);
    }
}

public sealed class NaturalPathComparer : IComparer<string>
{
    public static NaturalPathComparer Instance { get; } = new();
    public int Compare(string? x, string? y)
    {
        var left = Regex.Split(x ?? "", "([0-9]+)");
        var right = Regex.Split(y ?? "", "([0-9]+)");
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var a = left[i]; var b = right[i];
            int cmp;
            if (a.Length > 0 && b.Length > 0 && char.IsAsciiDigit(a[0]) && char.IsAsciiDigit(b[0]))
            {
                a = a.TrimStart('0'); b = b.TrimStart('0');
                cmp = a.Length.CompareTo(b.Length);
                if (cmp == 0) cmp = string.Compare(a, b, StringComparison.Ordinal);
            }
            else cmp = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;
        }
        return left.Length.CompareTo(right.Length);
    }
}
