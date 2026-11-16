using Listy.Core;
using System.Text;

public static class AuditFixTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        test("XSPF location xml:base survives read and Save As", () =>
        {
            var original = Path.Combine(root, "location-base.xspf");
            File.WriteAllText(original, "<playlist xmlns='http://xspf.org/ns/0/'><trackList><track><location xml:base='Other/'>song.mp3</location></track></trackList></playlist>");
            var doc = PlaylistCodec.ReadDocument(original);
            var expected = Path.Combine(root, "Other", "song.mp3");
            if (doc.Tracks.Single().Location != expected) throw new Exception("Wrong location base");
            var target = Path.Combine(root, "saved-base.xspf");
            PlaylistCodec.Save(target, doc.Tracks, PlaylistFormat.XSPF, true, document: doc);
            if (PlaylistCodec.ReadDocument(target).Tracks.Single().Location != expected) throw new Exception("Save As changed location");
        });
        test("Remove missing refreshes availability before filtering", () =>
        {
            var path = Path.Combine(root, "restored.mp3"); var track = new Track(path); File.WriteAllText(path, "fixture");
            if (PlaylistTools.RemoveMissing([track]).Count != 1) throw new Exception("Restored file was removed");
        });
        test("Malformed XML and excessive nesting are rejected", () =>
        {
            foreach (var xml in new[] { "<wrong/>", new string(' ', 1) + string.Concat(Enumerable.Repeat("<a>", 70)) + string.Concat(Enumerable.Repeat("</a>", 70)) })
            {
                var path = Path.Combine(root, "bad-structure.xspf"); File.WriteAllText(path, xml);
                try { PlaylistCodec.ReadDocument(path); throw new Exception("Accepted malformed structure"); }
                catch (InvalidDataException) { }
            }
        });
    }
}
