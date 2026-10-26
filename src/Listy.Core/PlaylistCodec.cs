using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Listy.Core;

public enum PlaylistFormat { M3U8, M3U, PLS, XSPF, WPL }

public static class PlaylistCodec
{
    public static string Extension(PlaylistFormat format) => "." + format.ToString().ToLowerInvariant();
    public static PlaylistFormat Detect(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".m3u8" => PlaylistFormat.M3U8, ".m3u" => PlaylistFormat.M3U, ".pls" => PlaylistFormat.PLS,
        ".xspf" => PlaylistFormat.XSPF, ".wpl" => PlaylistFormat.WPL,
        _ => throw new InvalidDataException("Nieobsługiwany format playlisty.")
    };

    public static string Serialize(IEnumerable<Track> source, string destination, PlaylistFormat format, bool relative)
    {
        var tracks = source.ToArray();
        string Ref(Track track)
        {
            if (track.IsRemote) return track.Location;
            var path = Path.GetFullPath(track.Location);
            var reference = relative ? Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(destination))!, path) : path;
            return reference.StartsWith('#') ? "." + Path.DirectorySeparatorChar + reference : reference;
        }
        string Clean(string value)
        {
            if (value.Contains('\r') || value.Contains('\n')) throw new InvalidDataException("Ścieżka zawiera znak nowej linii.");
            return value;
        }
        if (format is PlaylistFormat.M3U or PlaylistFormat.M3U8)
            return "#EXTM3U\r\n" + string.Join("", tracks.Select(t => $"#EXTINF:-1,{Clean(t.Name)}\r\n{Clean(Ref(t))}\r\n"));
        if (format == PlaylistFormat.PLS)
        {
            var text = new StringBuilder("[playlist]\r\n");
            for (var i = 0; i < tracks.Length; i++)
                text.Append($"File{i + 1}={Clean(Ref(tracks[i]))}\r\nTitle{i + 1}={Clean(tracks[i].Name)}\r\nLength{i + 1}=-1\r\n");
            return text.Append($"NumberOfEntries={tracks.Length}\r\nVersion=2\r\n").ToString();
        }
        if (format == PlaylistFormat.XSPF)
        {
            XNamespace ns = "http://xspf.org/ns/0/";
            return new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement(ns + "playlist", new XAttribute("version", "1"),
                    new XElement(ns + "title", Path.GetFileNameWithoutExtension(destination)),
                    new XElement(ns + "trackList", tracks.Select(t => new XElement(ns + "track",
                        new XElement(ns + "location", t.IsRemote ? t.Location : relative && !Path.IsPathRooted(Ref(t))
                            ? string.Join("/", Ref(t).Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString))
                            : new Uri(Path.GetFullPath(t.Location)).AbsoluteUri),
                        new XElement(ns + "title", t.Name)))))).ToString();
        }
        return new XDocument(new XProcessingInstruction("wpl", "version=\"1.0\""), new XElement("smil",
            new XElement("head", new XElement("title", Path.GetFileNameWithoutExtension(destination))),
            new XElement("body", new XElement("seq", tracks.Select(t => new XElement("media", new XAttribute("src", Ref(t)))))))).ToString();
    }

    public static IReadOnlyList<Track> Read(string path)
    {
        var format = Detect(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("Playlista jest zbyt duża (limit 32 MB).");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        string text;
        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true);
            text = reader.ReadToEnd();
        }
        catch (DecoderFallbackException) when (format is PlaylistFormat.M3U or PlaylistFormat.PLS)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Encoding.GetEncoding(1250).GetString(bytes);
        }
        IEnumerable<string> locations;
        if (format is PlaylistFormat.M3U or PlaylistFormat.M3U8)
            locations = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));
        else if (format == PlaylistFormat.PLS)
            locations = text.Split('\n').Select(l => Regex.Match(l.Trim(), @"^File(\d+)=(.*)$", RegexOptions.IgnoreCase))
                .Where(m => m.Success).OrderBy(m => int.Parse(m.Groups[1].Value)).Select(m => m.Groups[2].Value);
        else
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
            var xml = XDocument.Load(reader);
            locations = format == PlaylistFormat.XSPF
                ? xml.Descendants().Where(e => e.Name.LocalName == "location").Select(e => e.Value).ToArray()
                : xml.Descendants().Where(e => e.Name.LocalName == "media").Select(e => (string?)e.Attribute("src") ?? "").Where(s => s.Length > 0).ToArray();
        }
        return locations.Select(location =>
        {
            if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
                return new Track(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri);
            var decoded = format == PlaylistFormat.XSPF ? Uri.UnescapeDataString(location) : location;
            return new Track(Path.GetFullPath(decoded.Replace('/', Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(path))!));
        }).ToArray();
    }

    public static void Save(string path, IEnumerable<Track> tracks, PlaylistFormat format, bool relative, bool overwrite = false)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), Extension(format), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Rozszerzenie pliku nie odpowiada formatowi playlisty.");
        var text = Serialize(tracks, path, format, relative);
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".listy-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
