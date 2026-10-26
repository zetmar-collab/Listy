namespace Listy.Core;

public enum OutputMode { PerAlbum, Root, Subfolder }
public record PlaylistPlan(string Path, IReadOnlyList<Track> Tracks)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public int Count => Tracks.Count;
    public string State => File.Exists(Path) ? "Istnieje — zostanie pominięta" : "Nowa playlista";
}

public static class PlaylistPlanner
{
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.EndsWith('.') || name.EndsWith(' ') || name.Length > 180)
            throw new ArgumentException("Podaj nazwę do 180 znaków, bez znaków: \\ / : * ? \" < > | i bez kropki lub spacji na końcu.");
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem)
            || System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(COM|LPT)[1-9¹²³]$"))
            throw new ArgumentException("Ta nazwa jest zarezerwowana przez Windows.");
    }

    public static IReadOnlyList<PlaylistPlan> Create(LibrarySnapshot library, ISet<string> extensions,
        OutputMode mode, PlaylistFormat format, string name, string subfolder)
    {
        if (mode != OutputMode.PerAlbum) ValidateName(name);
        if (mode == OutputMode.Subfolder) ValidateName(subfolder);
        var filtered = library.Albums.Select(a => new Album(a.Directory,
            a.Tracks.Where(t => extensions.Contains(Path.GetExtension(t.Location).ToLowerInvariant())).ToArray())).Where(a => a.Tracks.Count > 0).ToArray();
        if (mode == OutputMode.PerAlbum)
            return filtered.Select(a =>
            {
                ValidateName(a.Name);
                return new PlaylistPlan(Path.Combine(a.Directory, a.Name + PlaylistCodec.Extension(format)), a.Tracks);
            }).ToArray();
        var tracks = filtered.SelectMany(a => a.Tracks).ToArray();
        if (tracks.Length == 0) return [];
        var folder = mode == OutputMode.Subfolder ? Path.Combine(library.Root, subfolder) : library.Root;
        return [new(Path.Combine(folder, name + PlaylistCodec.Extension(format)), tracks)];
    }

    public static string Rename(string path, string name)
    {
        ValidateName(name);
        _ = PlaylistCodec.Detect(path);
        var target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, name + Path.GetExtension(path));
        if (string.Equals(path, target, StringComparison.Ordinal)) return path;
        if (File.Exists(target) && !string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Plik o tej nazwie już istnieje.");
        File.Move(path, target);
        return target;
    }
}
