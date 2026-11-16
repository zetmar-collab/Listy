package pl.zetmar.studioplaylist;

import java.util.*;
import java.nio.charset.*;

/** Platform independent rules, compatibility profiles and playlist differences. */
public final class StudioTools {
    private StudioTools() { }
    public static class Difference {
        public final List<String> added=new ArrayList<>(),removed=new ArrayList<>();
        public boolean reordered;
        public String summary(boolean english){return (english?"Added: ":"Dodane: ")+added.size()+(english?"\nRemoved: ":"\nUsunięte: ")+removed.size()+(english?"\nOrder changed: ":"\nZmiana kolejności: ")+(reordered?(english?"yes":"tak"):(english?"no":"nie"))+"\n\n+ "+String.join("\n+ ",added.subList(0,Math.min(30,added.size())))+"\n− "+String.join("\n− ",removed.subList(0,Math.min(30,removed.size())));}
    }
    public static Difference compare(List<Track> before,List<Track> after) {
        Difference d=new Difference();Map<String,Integer> a=counts(before),b=counts(after);Map<String,Integer> old=new HashMap<>(a),fresh=new HashMap<>(b);
        List<String> commonA=new ArrayList<>(),commonB=new ArrayList<>();
        for(Track t:after){int n=old.getOrDefault(t.location,0);if(n==0)d.added.add(t.location);else{old.put(t.location,n-1);commonB.add(t.location);}}
        for(Track t:before){int n=fresh.getOrDefault(t.location,0);if(n==0)d.removed.add(t.location);else{fresh.put(t.location,n-1);commonA.add(t.location);}}
        d.reordered=!commonA.equals(commonB);return d;
    }
    private static Map<String,Integer> counts(List<Track> rows){Map<String,Integer> m=new HashMap<>();for(Track t:rows)m.put(t.location,m.getOrDefault(t.location,0)+1);return m;}
    public static final String[] PROFILES={"VLC / foobar2000 · M3U8 · UTF-8", "Legacy · M3U · Windows-1250", "PLS · UTF-8", "XSPF · URI"};
    public static int format(int profile){return new int[]{0,1,2,3}[profile];}
    public static byte[] encode(String text,String encoding) throws CharacterCodingException {
        java.nio.ByteBuffer b=Charset.forName(encoding).newEncoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).encode(java.nio.CharBuffer.wrap(text));byte[] result=new byte[b.remaining()];b.get(result);return result;
    }
    public static class Rule {
        public String name="",artist="",album="",extension="";
        public double min=-1,max=-1;
        public boolean matches(Track t){return t.artist.toLowerCase(Locale.ROOT).contains(artist.toLowerCase(Locale.ROOT)) && t.album.toLowerCase(Locale.ROOT).contains(album.toLowerCase(Locale.ROOT)) && (extension.isEmpty() || PlaylistCodec.ext(t.location).equalsIgnoreCase(extension.replaceFirst("^\\.",""))) && (min<0 || t.duration>=min) && (max<0 || t.duration>=0 && t.duration<=max);}
    }
}
