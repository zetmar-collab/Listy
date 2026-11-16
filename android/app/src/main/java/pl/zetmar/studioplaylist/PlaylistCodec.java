package pl.zetmar.studioplaylist;

import java.io.*;
import java.net.URI;
import java.nio.*;
import java.nio.charset.*;
import java.util.*;
import java.util.regex.*;
import javax.xml.parsers.*;
import org.w3c.dom.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.CancellationException;

/** Platform-independent playlist formats; storage grants stay in Storage. */
public final class PlaylistCodec {
    public static final String[] FORMATS = {"M3U8", "M3U", "PLS", "XSPF", "WPL"};
    public static final Set<String> AUDIO = new LinkedHashSet<>(Arrays.asList("mp3","flac","wav","m4a","aac","ogg","opus","wma","aiff","aif","ape","alac"));
    private PlaylistCodec() { }
    public static String ext(String name) { int i=name.lastIndexOf('.'); return i<0?"":name.substring(i+1).toLowerCase(Locale.ROOT); }
    public static boolean isPlaylist(String name) { return Arrays.asList(FORMATS).contains(ext(name).toUpperCase(Locale.ROOT)); }
    public static String basename(String name) { String s=name.replace('\\','/'); return s.substring(s.lastIndexOf('/')+1); }
    public static String stem(String name) { String n=basename(name); int i=n.lastIndexOf('.'); return i>0?n.substring(0,i):n; }
    public static String clean(String s) { if(s.indexOf('\n')>=0 || s.indexOf('\r')>=0 || s.indexOf('\0')>=0) throw new IllegalArgumentException("Invalid line break / niedozwolony znak"); return s; }
    public static String name(String s) {
        s=s.trim(); if(s.isEmpty() || s.length()>180 || s.equals(".") || s.equals("..") || s.matches(".*[\\\\/:*?\"<>|\\p{Cntrl}].*")) throw new IllegalArgumentException("Invalid name / Niepoprawna nazwa");
        return s;
    }
    public static String normalize(String path) {
        boolean absolute=path.startsWith("/"); List<String> parts=new ArrayList<>();
        for(String p:path.replace('\\','/').split("/")) {
            if(p.isEmpty() || p.equals(".")) continue;
            if(p.equals("..") && !parts.isEmpty() && !parts.get(parts.size()-1).equals("..")) parts.remove(parts.size()-1);
            else if(!p.equals("..") || !absolute) parts.add(p);
        }
        return (absolute?"/":"")+String.join("/",parts);
    }
    public static String relative(String parent,String path) {
        List<String> a=new ArrayList<>(Arrays.asList(normalize(parent).split("/"))), b=new ArrayList<>(Arrays.asList(normalize(path).split("/")));
        a.removeIf(String::isEmpty); b.removeIf(String::isEmpty); int i=0;
        while(i<a.size() && i<b.size() && a.get(i).equals(b.get(i))) i++;
        List<String> r=new ArrayList<>(); for(int j=i;j<a.size();j++) r.add(".."); r.addAll(b.subList(i,b.size()));
        String result=String.join("/",r); return result.startsWith("#")?"./"+result:result;
    }
    public static String decode(byte[] data) {
        if(data.length>32*1024*1024) throw new IllegalArgumentException("Playlist exceeds 32 MB");
        if(data.length>=2 && data[0]==(byte)0xff && data[1]==(byte)0xfe) return new String(data,2,data.length-2,StandardCharsets.UTF_16LE);
        if(data.length>=2 && data[0]==(byte)0xfe && data[1]==(byte)0xff) return new String(data,2,data.length-2,StandardCharsets.UTF_16BE);
        int offset=data.length>=3 && data[0]==(byte)0xef && data[1]==(byte)0xbb && data[2]==(byte)0xbf?3:0;
        try { return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(data,offset,data.length-offset)).toString(); }
        catch(CharacterCodingException ex) { return new String(data,Charset.forName("windows-1250")); }
    }
    private static double duration(String s) { try { double n=Double.parseDouble(s); return Double.isFinite(n)&&n>=0?n:-1; } catch(Exception ex) { return -1; } }
    public static List<Track> parse(String text,String format) throws Exception {
        return parse(text,format,new AtomicBoolean());
    }
    public static List<Track> parse(String text,String format,AtomicBoolean cancel) throws Exception {
        List<Track> tracks=new ArrayList<>(); format=format.toUpperCase(Locale.ROOT);
        if(format.equals("M3U") || format.equals("M3U8")) {
            String title=""; double seconds=-1;
            BufferedReader lines=new BufferedReader(new StringReader(text));String raw;
            while((raw=lines.readLine())!=null) { if(cancel.get())throw new CancellationException();String line=raw.trim(); if(line.isEmpty()) continue;
                if(line.startsWith("#EXTINF:")) { int comma=line.indexOf(','); if(comma>0) { seconds=duration(line.substring(8,comma).split(" ")[0]); title=line.substring(comma+1); } }
                else if(!line.startsWith("#")) { if(tracks.size()>=200000)throw new IOException("Too many tracks");Track t=new Track(line,title); t.duration=seconds; tracks.add(t); title=""; seconds=-1; }
            }
        } else if(format.equals("PLS")) {
            TreeMap<Integer,Track> map=new TreeMap<>(); Pattern p=Pattern.compile("(?i)^(File|Title|Length)([0-9]{1,8})=(.*)$");
            BufferedReader lines=new BufferedReader(new StringReader(text));String line;
            while((line=lines.readLine())!=null) { if(cancel.get())throw new CancellationException();Matcher m=p.matcher(line.trim()); if(!m.matches()) continue; int i=Integer.parseInt(m.group(2)); if(i<1) continue;
                if(!map.containsKey(i) && map.size()>=200000)throw new IOException("Too many tracks");
                Track t=map.computeIfAbsent(i,k->new Track()); switch(m.group(1).toLowerCase(Locale.ROOT)) { case "file":t.location=m.group(3);break; case "title":t.title=m.group(3);break; case "length":t.duration=duration(m.group(3));break; default:break; }
            }
            for(Track t:map.values()) if(!t.location.isEmpty()) tracks.add(t);
        } else if(format.equals("XSPF") || format.equals("WPL")) {
            String upper=text.toUpperCase(Locale.ROOT); if(upper.contains("<!DOCTYPE") || upper.contains("<!ENTITY")) throw new IOException("DTD / entities are not allowed");
            SAXParserFactory sax=SAXParserFactory.newInstance();sax.setNamespaceAware(true);
            sax.newSAXParser().parse(new org.xml.sax.InputSource(new StringReader(text)),new org.xml.sax.helpers.DefaultHandler(){int depth,nodes,entries;
                @Override public void startElement(String uri,String local,String q,org.xml.sax.Attributes a) throws org.xml.sax.SAXException {if(cancel.get())throw new CancellationException();if(++depth>64 || ++nodes>1000000 || ((local.equals("track") || local.equals("media")) && ++entries>200000))throw new org.xml.sax.SAXException("XML resource limit");}
                @Override public void endElement(String u,String l,String q){depth--;}
            });
            DocumentBuilderFactory f=DocumentBuilderFactory.newInstance(); f.setNamespaceAware(true); f.setExpandEntityReferences(false);
            Document doc=f.newDocumentBuilder().parse(new org.xml.sax.InputSource(new StringReader(text)));
            if(!doc.getDocumentElement().getLocalName().equals(format.equals("XSPF")?"playlist":"smil") || doc.getElementsByTagNameNS("*",format.equals("XSPF")?"trackList":"seq").getLength()==0)throw new IOException("Invalid playlist XML structure / Niepoprawna struktura XML");
            NodeList elements=doc.getElementsByTagNameNS("*",format.equals("XSPF")?"track":"media");
            if(elements.getLength()==0 && format.equals("WPL")) elements=doc.getElementsByTagName("media");
            if(elements.getLength()>200000) throw new IOException("Too many tracks");
            for(int i=0;i<elements.getLength();i++) {
                if(cancel.get())throw new CancellationException();
                Element e=(Element)elements.item(i); Track t=new Track();
                if(format.equals("WPL")) t.location=e.getAttribute("src");
                else { t.location=child(e,"location"); t.title=child(e,"title"); t.artist=child(e,"creator"); t.album=child(e,"album"); double d=duration(child(e,"duration")); t.duration=d<0?-1:d/1000; }
                if(format.equals("XSPF") && !t.location.isEmpty()) {
                    t.location=withXmlBase((Element)e.getElementsByTagNameNS("*","location").item(0),t.location);
                    // Decode only URI path escapes, leaving content:// and remote locations intact.
                    try { URI u=URI.create(t.location); if(u.getScheme()==null) t.location=u.getPath(); else if(u.getScheme().equals("file")) t.location=u.getPath(); } catch(IllegalArgumentException ignored) { }
                }
                if(!t.location.isEmpty()) tracks.add(t);
            }
        } else throw new IOException("Unsupported playlist format");
        if(tracks.size()>200000) throw new IOException("Too many tracks"); return tracks;
    }
    private static String child(Element e,String name) { NodeList n=e.getElementsByTagNameNS("*",name); return n.getLength()==0?"":n.item(0).getTextContent(); }
    private static String withXmlBase(Element element,String location) throws IOException {
        Deque<Element> ancestors=new ArrayDeque<>();Node node=element;boolean hasBase=false;
        while(node instanceof Element){Element e=(Element)node;ancestors.push(e);if(e.hasAttributeNS("http://www.w3.org/XML/1998/namespace","base"))hasBase=true;node=node.getParentNode();}
        if(!hasBase)return location;
        try{URI base=URI.create("");while(!ancestors.isEmpty()){String value=ancestors.pop().getAttributeNS("http://www.w3.org/XML/1998/namespace","base");if(!value.isEmpty())base=base.resolve(value.replace(" ","%20"));}return base.resolve(location.replace(" ","%20")).toASCIIString();}
        catch(IllegalArgumentException ex){throw new IOException("Invalid XSPF xml:base / Niepoprawna ścieżka bazowa XSPF",ex);}
    }
    private static String xml(String s) { return clean(s).replace("&","&amp;").replace("<","&lt;").replace(">","&gt;").replace("\"","&quot;").replace("'","&apos;"); }
    private static String number(double n) { return Double.isFinite(n)&&n>=0?Long.toString((long)n):"-1"; }
    public static String reference(Track t,String parent,boolean relative) {
        if(relative && !t.path.isEmpty()) return relative(parent,t.path);
        String s=!t.location.isEmpty()?t.location:t.uri; return s.startsWith("#")?"./"+s:s;
    }
    public static String serialize(List<Track> tracks,String format,String title,String parent,boolean relative) {
        StringBuilder s=new StringBuilder(); format=format.toUpperCase(Locale.ROOT);
        switch(format) {
            case "M3U": case "M3U8": s.append("#EXTM3U\n"); for(Track t:tracks) s.append("#EXTINF:").append(number(t.duration)).append(',').append(clean(t.label())).append('\n').append(clean(reference(t,parent,relative))).append('\n'); break;
            case "PLS": s.append("[playlist]\n"); int i=0; for(Track t:tracks) { i++; s.append("File").append(i).append('=').append(clean(reference(t,parent,relative))).append("\nTitle").append(i).append('=').append(clean(t.label())).append("\nLength").append(i).append('=').append(number(t.duration)).append('\n'); } s.append("NumberOfEntries=").append(i).append("\nVersion=2\n"); break;
            case "XSPF": s.append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<playlist version=\"1\" xmlns=\"http://xspf.org/ns/0/\"><title>").append(xml(title)).append("</title><trackList>");
                for(Track t:tracks) { String ref=reference(t,parent,relative); try { if(!ref.matches("^[a-zA-Z][a-zA-Z0-9+.-]*:.*")) ref=new URI(null,null,ref,null).toASCIIString(); } catch(Exception ex) { throw new IllegalArgumentException(ex); }
                    s.append("<track><location>").append(xml(ref)).append("</location><title>").append(xml(t.label())).append("</title>"); if(t.duration>=0) s.append("<duration>").append((long)(t.duration*1000)).append("</duration>"); if(!t.artist.isEmpty()) s.append("<creator>").append(xml(t.artist)).append("</creator>"); if(!t.album.isEmpty()) s.append("<album>").append(xml(t.album)).append("</album>"); s.append("</track>"); }
                s.append("</trackList></playlist>\n"); break;
            case "WPL": s.append("<?wpl version=\"1.0\"?>\n<smil><head><title>").append(xml(title)).append("</title></head><body><seq>"); for(Track t:tracks) s.append("<media src=\"").append(xml(reference(t,parent,relative))).append("\"/>"); s.append("</seq></body></smil>\n");break;
            default:throw new IllegalArgumentException("Unsupported format");
        }
        return s.toString();
    }
    public static final Comparator<String> NATURAL=(a,b)-> {
        int i=0,j=0; while(i<a.length() && j<b.length()) {
            char x=a.charAt(i),y=b.charAt(j);
            if(x>='0' && x<='9' && y>='0' && y<='9') { int si=i,sj=j; while(i<a.length() && Character.isDigit(a.charAt(i))) i++; while(j<b.length() && Character.isDigit(b.charAt(j))) j++; String na=a.substring(si,i).replaceFirst("^0+(?!$)",""),nb=b.substring(sj,j).replaceFirst("^0+(?!$)",""); int c=Integer.compare(na.length(),nb.length()); if(c==0)c=na.compareTo(nb); if(c!=0)return c; }
            else { int c=Character.compare(Character.toLowerCase(x),Character.toLowerCase(y)); if(c!=0)return c; i++; j++; }
        } return Integer.compare(a.length()-i,b.length()-j);
    };
}
