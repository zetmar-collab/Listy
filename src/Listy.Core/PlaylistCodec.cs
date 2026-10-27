using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Listy.Core;

public enum PlaylistFormat { M3U8, M3U, PLS, XSPF, WPL }

public sealed record PlaylistDocument(PlaylistFormat Format, string RawText, string EncodingName, IReadOnlyList<Track> Tracks)
{
    public string? SourcePath { get; init; }
    public XDocument? XmlTemplate { get; init; }
    public IReadOnlyList<string> Preamble { get; init; } = [];
    public IReadOnlyDictionary<string, string> Globals { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public static class PlaylistCodec
{
    public static string Extension(PlaylistFormat format) => "." + format.ToString().ToLowerInvariant();
    public static PlaylistFormat Detect(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".m3u8" => PlaylistFormat.M3U8, ".m3u" => PlaylistFormat.M3U, ".pls" => PlaylistFormat.PLS,
        ".xspf" => PlaylistFormat.XSPF, ".wpl" => PlaylistFormat.WPL,
        _ => throw new InvalidDataException(Text.T("T126"))
    };
    private static string Number(double? seconds, double scale = 1) => seconds is >= 0
        ? (seconds.Value * scale).ToString("0.###", CultureInfo.InvariantCulture) : "-1";
    private static string Clean(string value)
    {
        if (value.Contains('\r') || value.Contains('\n')) throw new InvalidDataException(Text.T("T127"));
        return value;
    }

    public static string Serialize(IEnumerable<Track> source, string destination, PlaylistFormat format, bool relative,
        PlaylistDocument? document = null, CancellationToken token = default)
    {
        var tracks = source.ToArray();
        var sameFormat = document?.Format == format ||
            (document?.Format is PlaylistFormat.M3U or PlaylistFormat.M3U8 && format is PlaylistFormat.M3U or PlaylistFormat.M3U8);
        string Ref(Track track)
        {
            if (track.IsRemote) return track.Location;
            var path = Path.GetFullPath(track.Location);
            var reference = relative ? Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(destination))!, path) : path;
            return reference.StartsWith('#') ? "." + Path.DirectorySeparatorChar + reference : reference;
        }
        if (format is PlaylistFormat.M3U or PlaylistFormat.M3U8)
        {
            var output = new StringBuilder("#EXTM3U\r\n");
            if (sameFormat && document != null)
                foreach (var line in document.Preamble) output.AppendLine(Clean(line));
            foreach (var track in tracks)
            {
                token.ThrowIfCancellationRequested();
                if (sameFormat) foreach (var line in track.ExtraLines) output.AppendLine(Clean(line));
                var info = sameFormat && track.ExtendedInfo != null ? track.ExtendedInfo : Number(track.DurationSeconds);
                if (track.DurationSeconds is >= 0 && track.ExtendedInfo != null) info = Regex.Replace(info, @"^\S+", Number(track.DurationSeconds));
                output.Append("#EXTINF:").Append(Clean(info))
                    .Append(',').Append(Clean(track.Name)).Append("\r\n").Append(Clean(Ref(track))).Append("\r\n");
            }
            return output.ToString();
        }
        if (format == PlaylistFormat.PLS)
        {
            var output = new StringBuilder("[playlist]\r\n");
            if (sameFormat && document != null)
                foreach (var field in document.Globals) output.Append(Clean(field.Key)).Append('=').Append(Clean(field.Value)).Append("\r\n");
            for (var i = 0; i < tracks.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var t = tracks[i];
                output.Append($"File{i + 1}={Clean(Ref(t))}\r\nTitle{i + 1}={Clean(t.Name)}\r\nLength{i + 1}={Number(t.DurationSeconds)}\r\n");
                if (sameFormat) foreach (var field in t.ExtraFields)
                    output.Append(Clean(field.Key)).Append(i + 1).Append('=').Append(Clean(field.Value)).Append("\r\n");
            }
            return output.Append($"NumberOfEntries={tracks.Length}\r\nVersion=2\r\n").ToString();
        }
        XNamespace ns = "http://xspf.org/ns/0/";
        var xml = sameFormat && document?.XmlTemplate != null ? new XDocument(document.XmlTemplate)
            : format == PlaylistFormat.XSPF
                ? new XDocument(new XElement(ns + "playlist", new XAttribute("version", "1"),
                    new XElement(ns + "title", Path.GetFileNameWithoutExtension(destination)), new XElement(ns + "trackList")))
                : new XDocument(new XProcessingInstruction("wpl", "version=\"1.0\""), new XElement("smil",
                    new XElement("head", new XElement("title", Path.GetFileNameWithoutExtension(destination))),
                    new XElement("body", new XElement("seq"))));
        var containerName = format == PlaylistFormat.XSPF ? "trackList" : "seq";
        var container = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == containerName)
            ?? throw new InvalidDataException(Text.T("XmlStructure"));
        container.Elements().Where(e => e.Name.LocalName == (format == PlaylistFormat.XSPF ? "track" : "media")).Remove();
        var preserveBase = format == PlaylistFormat.XSPF && sameFormat && document?.SourcePath != null &&
            (xml.Descendants().Attributes(XNamespace.Xml + "base").Any() ||
             !string.Equals(Path.GetDirectoryName(document.SourcePath), Path.GetDirectoryName(Path.GetFullPath(destination)), StringComparison.OrdinalIgnoreCase));
        if (preserveBase)
        {
            var originalBase = new Uri(new Uri(document!.SourcePath!).AbsoluteUri);
            if (xml.Root!.Attribute(XNamespace.Xml + "base") == null) xml.Root.SetAttributeValue(XNamespace.Xml + "base", originalBase.AbsoluteUri);
            foreach (var element in xml.Root.DescendantsAndSelf())
            {
                if (element.Attribute(XNamespace.Xml + "base") is not { } attr) continue;
                var parentBase = element.Parent?.AncestorsAndSelf().Select(e => e.Attribute(XNamespace.Xml + "base")).FirstOrDefault(a => a != null)?.Value;
                attr.Value = new Uri(parentBase == null ? originalBase : new Uri(parentBase), attr.Value).AbsoluteUri;
            }
        }
        foreach (var t in tracks)
        {
            token.ThrowIfCancellationRequested();
            if (format == PlaylistFormat.WPL)
            {
                var media = sameFormat && t.SourceXml != null ? new XElement(t.SourceXml) : new XElement("media");
                media.SetAttributeValue("src", Ref(t)); container.Add(media); continue;
            }
            var item = sameFormat && t.SourceXml != null ? new XElement(t.SourceXml) : new XElement(ns + "track");
            void Set(string name, object? value)
            {
                if (value == null) return;
                var existing = item.Elements().FirstOrDefault(e => e.Name.LocalName == name);
                if (existing == null) item.Add(new XElement(ns + name, value)); else existing.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
            var reference = Ref(t);
            var uri = t.IsRemote ? t.Location : relative && !preserveBase && !Path.IsPathRooted(reference)
                ? string.Join("/", reference.Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString))
                : new Uri(Path.GetFullPath(t.Location)).AbsoluteUri;
            // Keep alternate locations, but update the primary one used by this application.
            Set("location", uri); Set("title", t.Name); Set("creator", t.Artist); Set("album", t.AlbumTitle);
            Set("trackNum", t.TrackNumber); if (t.DurationSeconds is >= 0) Set("duration", Number(t.DurationSeconds, 1000));
            container.Add(item);
        }
        return xml.ToString();
    }

    public static IReadOnlyList<Track> Read(string path) => ReadDocument(path).Tracks;

    public static PlaylistDocument ReadDocument(string path, CancellationToken token = default)
    {
        path = Path.GetFullPath(path);
        var format = Detect(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException(Text.T("T128"));
        var bytes = new byte[checked((int)stream.Length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = stream.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
            if (count == 0) throw new EndOfStreamException();
            offset += count;
        }
        string text, encoding;
        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true);
            text = reader.ReadToEnd(); encoding = reader.CurrentEncoding.WebName;
        }
        catch (DecoderFallbackException) when (format is PlaylistFormat.M3U or PlaylistFormat.PLS)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Encoding.GetEncoding(1250).GetString(bytes); encoding = "windows-1250";
        }
        var tracks = new List<Track>(); var warnings = new List<string>(); var preamble = new List<string>();
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Track? Resolve(string location, XElement? element = null)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var decoded = format == PlaylistFormat.XSPF ? Uri.UnescapeDataString(location) : location;
                if (Uri.TryCreate(location, UriKind.Absolute, out var absolute))
                    return new Track(absolute.IsFile ? absolute.LocalPath : absolute.AbsoluteUri);
                if (format == PlaylistFormat.XSPF && element != null)
                {
                    var baseUri = new Uri(new Uri(path).AbsoluteUri, UriKind.Absolute);
                    foreach (var ancestor in element.AncestorsAndSelf().Reverse())
                        if (ancestor.Attribute(XNamespace.Xml + "base") is { } attr) baseUri = new Uri(baseUri, attr.Value);
                    var resolved = new Uri(baseUri, location);
                    return new Track(resolved.IsFile ? resolved.LocalPath : resolved.AbsoluteUri);
                }
                return new Track(Path.GetFullPath(decoded.Replace('/', Path.DirectorySeparatorChar), Path.GetDirectoryName(path)!));
            }
            catch (Exception ex) when (ex is ArgumentException or UriFormatException or NotSupportedException)
            { warnings.Add(location + ": " + ex.Message); return null; }
        }
        static double? Duration(string? value, double scale = 1) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) && d >= 0 && d / scale < TimeSpan.MaxValue.TotalSeconds ? d / scale : null;
        XDocument? xml = null;
        if (format is PlaylistFormat.M3U or PlaylistFormat.M3U8)
        {
            var pending = new List<string>(); string? info = null, title = null; double? duration = null;
            foreach (var raw in text.Split('\n'))
            {
                token.ThrowIfCancellationRequested();
                var line = raw.Trim();
                if (line.Length == 0 || line.Equals("#EXTM3U", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = line.IndexOf(',');
                    info = comma < 0 ? line[8..] : line[8..comma];
                    title = comma < 0 ? null : line[(comma + 1)..];
                    duration = Duration(info.Split(' ')[0]); continue;
                }
                if (line.StartsWith('#')) { pending.Add(line); continue; }
                if (Resolve(line) is { } track)
                {
                    track.Title = title; track.DurationSeconds = duration; track.ExtendedInfo = info;
                    track.ExtraLines = [.. pending]; tracks.Add(track);
                }
                pending.Clear(); title = info = null; duration = null;
            }
            preamble.AddRange(pending);
        }
        else if (format == PlaylistFormat.PLS)
        {
            var entries = new SortedDictionary<int, Dictionary<string, string>>();
            foreach (var raw in text.Split('\n'))
            {
                token.ThrowIfCancellationRequested();
                var equal = raw.IndexOf('='); if (equal < 1) continue;
                var key = raw[..equal].Trim(); var value = raw[(equal + 1)..].TrimEnd('\r');
                if (key.Equals("NumberOfEntries", StringComparison.OrdinalIgnoreCase) || key.Equals("Version", StringComparison.OrdinalIgnoreCase)) continue;
                var match = Regex.Match(key, @"^(.+?)([0-9]+)$");
                if (!match.Success) { globals[key] = value; continue; }
                if (!int.TryParse(match.Groups[2].Value, out var number)) { warnings.Add(key); continue; }
                if (!entries.TryGetValue(number, out var fields)) entries[number] = fields = new(StringComparer.OrdinalIgnoreCase);
                fields[match.Groups[1].Value] = value;
            }
            foreach (var fields in entries.Values)
                if (fields.TryGetValue("File", out var location) && Resolve(location) is { } track)
                {
                    track.Title = fields.GetValueOrDefault("Title"); track.DurationSeconds = Duration(fields.GetValueOrDefault("Length"));
                    track.ExtraFields = fields.Where(p => !new[] { "File", "Title", "Length" }.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
                        .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                    tracks.Add(track);
                }
        }
        else
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
            xml = XDocument.Load(reader);
            var entryName = format == PlaylistFormat.XSPF ? "track" : "media";
            foreach (var item in xml.Descendants().Where(e => e.Name.LocalName == entryName))
            {
                token.ThrowIfCancellationRequested();
                string? Value(string name) => item.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
                var location = format == PlaylistFormat.XSPF ? Value("location") : (string?)item.Attribute("src");
                if (string.IsNullOrWhiteSpace(location) || Resolve(location, item) is not { } track) continue;
                track.SourceXml = new(item);
                track.Title = Value("title"); track.Artist = Value("creator"); track.AlbumTitle = Value("album");
                if (int.TryParse(Value("trackNum"), out var num)) track.TrackNumber = num;
                track.DurationSeconds = Duration(Value("duration"), 1000);
                tracks.Add(track);
            }
        }
        return new(format, text, encoding, tracks) { SourcePath = path, XmlTemplate = xml, Globals = globals, Preamble = preamble, Warnings = warnings };
    }

    public static void Save(string path, IEnumerable<Track> tracks, PlaylistFormat format, bool relative, bool overwrite = false,
        PlaylistDocument? document = null, CancellationToken token = default, string? backupPath = null)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), Extension(format), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Text.T("T129"));
        var text = Serialize(tracks, path, format, relative, document, token);
        token.ThrowIfCancellationRequested();
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".listy-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            token.ThrowIfCancellationRequested();
            if (overwrite && backupPath != null && File.Exists(path)) File.Replace(temp, path, backupPath);
            else File.Move(temp, path, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
