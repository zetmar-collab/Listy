package pl.zetmar.studioplaylist;

public class Track {
    public String uri = "", path = "", location = "", title = "", artist = "", album = "", metadataWarning="";
    public double duration = -1;
    public boolean missing;
    public Track() { }
    public Track(String location, String title) { this.location = location; this.title = title; }
    public Track copy() {
        Track t = new Track(location, title);
        t.uri=uri; t.path=path; t.artist=artist; t.album=album; t.duration=duration; t.missing=missing;t.metadataWarning=metadataWarning;
        return t;
    }
    public String label() { return title.isEmpty() ? PlaylistCodec.basename(location) : title; }
}
