namespace Listy.Core;

public enum OutputMode { PerAlbum, Root, Subfolder }
public record PlaylistPlan(string Path, IReadOnlyList<Track> Tracks)
{
    public ExistingPlaylistPolicy Policy { get; init; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public int Count => Tracks.Count;
    public string State => File.Exists(Path) ? Text.T(Policy switch { ExistingPlaylistPolicy.UpdateWithBackup => "UpdateBackup", ExistingPlaylistPolicy.NewVersion => "VersionNew", _ => "T122" }) : Text.T("T099");
}

public static class PlaylistPlanner
{
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.EndsWith('.') || name.EndsWith(' ') || name.Length > 180)
            throw new ArgumentException(Text.T("T123"));
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem)
            || System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(COM|LPT)[1-9¹²³]$"))
            throw new ArgumentException(Text.T("T124"));
    }

    public static IReadOnlyList<PlaylistPlan> Create(LibrarySnapshot library, ISet<string> extensions,
        OutputMode mode, PlaylistFormat format, string name, string subfolder)
        => CreateDetailed(library, extensions, mode, format, name, subfolder).Plans;

    public static PlanningResult CreateDetailed(LibrarySnapshot library, ISet<string> extensions,
        OutputMode mode, PlaylistFormat format, string name, string subfolder)
    {
        if (mode != OutputMode.PerAlbum) ValidateName(name);
        if (mode == OutputMode.Subfolder) ValidateName(subfolder);
        var filtered = library.Albums.Where(a => a.IsSelected).Select(a => new Album(a.Directory,
            a.Tracks.Where(t => extensions.Contains(Path.GetExtension(t.Location).ToLowerInvariant())).ToArray())).Where(a => a.Tracks.Count > 0).ToArray();
        if (mode == OutputMode.PerAlbum)
        {
            var plans = new List<PlaylistPlan>(); var warnings = new List<string>();
            foreach (var a in filtered)
            {
                try { ValidateName(a.Name); plans.Add(new(Path.Combine(a.Directory, a.Name + PlaylistCodec.Extension(format)), a.Tracks)); }
                catch (ArgumentException ex) { warnings.Add(a.Directory + ": " + ex.Message); }
            }
            return new(plans, warnings);
        }
        var tracks = filtered.SelectMany(a => a.Tracks).ToArray();
        if (tracks.Length == 0) return new([], []);
        var folder = mode == OutputMode.Subfolder ? Path.Combine(library.Root, subfolder) : library.Root;
        return new([new(Path.Combine(folder, name + PlaylistCodec.Extension(format)), tracks)], []);
    }

    public static string Rename(string path, string name)
    {
        ValidateName(name);
        _ = PlaylistCodec.Detect(path);
        var target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, name + Path.GetExtension(path));
        if (string.Equals(path, target, StringComparison.Ordinal)) return path;
        if (File.Exists(target) && !string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
            throw new IOException(Text.T("T125"));
        File.Move(path, target);
        return target;
    }
}
public record PlanningResult(IReadOnlyList<PlaylistPlan> Plans, IReadOnlyList<string> Warnings);
