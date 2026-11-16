package pl.zetmar.studioplaylist;

import android.content.*;
import android.database.Cursor;
import android.media.MediaMetadataRetriever;
import android.net.Uri;
import android.os.CancellationSignal;
import android.os.Environment;
import android.provider.DocumentsContract;
import android.provider.DocumentsContract.Document;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.CancellationException;
import java.util.concurrent.atomic.AtomicBoolean;

/** All shared storage access is mediated by the user's document-provider grant. */
public final class Storage {
    public static class Entry {
        public String uri, name, path, parentUri, parentPath;
        public boolean directory;
        public int flags;
        public Entry(String uri,String name,String path,String parentUri,String parentPath,boolean directory,int flags) {
            this.uri=uri;this.name=name;this.path=path;this.parentUri=parentUri;this.parentPath=parentPath;this.directory=directory;this.flags=flags;
        }
    }
    public static class Library {
        public Entry root;
        public List<Entry> folders=new ArrayList<>(),playlists=new ArrayList<>();
        public List<Track> tracks=new ArrayList<>();
        public List<String> warnings=new ArrayList<>();
        public Map<String,Track> byPath=new HashMap<>(),byLocation=new HashMap<>();
    }
    public interface Progress { void update(String text); }
    private final Context context;
    private final ContentResolver resolver;
    public Storage(Context context) { this.context=context.getApplicationContext();resolver=context.getContentResolver(); }
    public static void check(AtomicBoolean cancel) { if(cancel.get()) throw new CancellationException(); }
    public Entry describe(Uri uri) throws IOException {
        try(Cursor c=resolver.query(uri,new String[]{Document.COLUMN_DISPLAY_NAME,Document.COLUMN_MIME_TYPE,Document.COLUMN_FLAGS},null,null,null)) {
            if(c==null || !c.moveToFirst()) throw new IOException("Document unavailable / Plik niedostÄ™pny");
            return new Entry(uri.toString(),c.getString(0),"","","",Document.MIME_TYPE_DIR.equals(c.getString(1)),c.getInt(2));
        }
    }
    public List<Entry> children(Entry parent,AtomicBoolean cancel) throws IOException {
        check(cancel); Uri uri=Uri.parse(parent.uri); Uri query=DocumentsContract.buildChildDocumentsUriUsingTree(uri,DocumentsContract.getDocumentId(uri));
        List<Entry> result=new ArrayList<>();
        CancellationSignal signal=new CancellationSignal();
        try(Cursor c=resolver.query(query,new String[]{Document.COLUMN_DOCUMENT_ID,Document.COLUMN_DISPLAY_NAME,Document.COLUMN_MIME_TYPE,Document.COLUMN_FLAGS},null,null,null,signal)) {
            if(c==null) throw new IOException("Folder unavailable / Katalog niedostÄ™pny");
            while(c.moveToNext()) { check(cancel); String name=c.getString(1); if(name==null)continue;
                result.add(new Entry(DocumentsContract.buildDocumentUriUsingTree(uri,c.getString(0)).toString(),name,join(parent.path,name),parent.uri,parent.path,Document.MIME_TYPE_DIR.equals(c.getString(2)),c.getInt(3)));
                if(result.size()>200000) throw new IOException("Too many files / Zbyt wiele plikĂłw");
            }
        }
        result.sort((a,b)->PlaylistCodec.NATURAL.compare(a.name,b.name)); return result;
    }
    public Library scan(String tree,boolean recursive,boolean metadata,AtomicBoolean cancel,Progress progress) throws Exception {
        Uri u=Uri.parse(tree); Uri rootUri=DocumentsContract.buildDocumentUriUsingTree(u,DocumentsContract.getTreeDocumentId(u));
        Library library=new Library(); library.root=describe(rootUri);
        MetadataCache cache=metadata?new MetadataCache(context):null;StudioStorage extra=new StudioStorage(context);
        ArrayDeque<Entry> queue=new ArrayDeque<>();queue.add(library.root); Set<String> visited=new HashSet<>(); int count=0;
        while(!queue.isEmpty()) { check(cancel); Entry folder=queue.remove(); if(!visited.add(folder.uri)) continue;
            if(visited.size()>10000) throw new IOException("Too many folders / Zbyt wiele katalogĂłw");
            library.folders.add(folder);progress.update(folder.path.isEmpty()?folder.name:folder.path);
            List<Entry> list;
            try { list=children(folder,cancel); } catch(CancellationException ex) { throw ex; } catch(Exception ex) {
                if(folder==library.root)throw ex; library.warnings.add(folder.path+": "+ex.getMessage());continue;
            }
            for(Entry e:list) { check(cancel); if(++count>200000)throw new IOException("Library exceeds 200000 entries");
                if(e.directory) { if(recursive && e.path.split("/").length<100) queue.add(e); }
                else if(PlaylistCodec.isPlaylist(e.name)) library.playlists.add(e);
                else if(PlaylistCodec.AUDIO.contains(PlaylistCodec.ext(e.name))) {
                    Track t=track(e); if(cache!=null)cache.read(this,extra,t);if(!t.metadataWarning.isEmpty())library.warnings.add(e.path+": "+t.metadataWarning);library.tracks.add(t);library.byPath.put(t.path,t);library.byLocation.put(t.location,t);library.byLocation.put(t.uri,t);
                }
            }
        }
        if(cache!=null){try{cache.save();}catch(IOException ex){library.warnings.add(ex.getMessage());}progress.update("Cache: "+cache.hits+"; read / odczyt: "+cache.reads);}
        return library;
    }
    public Track track(Entry e) { Track t=new Track(localPath(e.uri),PlaylistCodec.stem(e.name)); t.uri=e.uri;t.path=e.path;return t; }
    public static String localPath(String raw) {
        Uri u=Uri.parse(raw);
        if("com.android.externalstorage.documents".equals(u.getAuthority())) {
            try { String id=DocumentsContract.getDocumentId(u); int colon=id.indexOf(':'); if(colon>0) {
                String volume=id.substring(0,colon),path=id.substring(colon+1);
                return (volume.equalsIgnoreCase("primary")?Environment.getExternalStorageDirectory().getAbsolutePath():"/storage/"+volume)+"/"+path;
            } } catch(Exception ignored) { }
        }
        return raw;
    }
    public void readTags(Track t) {
        if(t.uri.isEmpty())return;
        MediaMetadataRetriever r=new MediaMetadataRetriever();
        try { r.setDataSource(context,Uri.parse(t.uri));String title=r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_TITLE);if(title!=null && !title.trim().isEmpty())t.title=title;
            String artist=r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ARTIST),album=r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ALBUM);
            t.artist=artist==null?"":artist;t.album=album==null?"":album;String ms=r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION);if(ms!=null)t.duration=Double.parseDouble(ms)/1000;
        } catch(Exception ex) {t.metadataWarning=ex.getMessage()==null?ex.toString():ex.getMessage();} finally { try { r.release(); } catch(Exception ignored) { } }
    }
    public byte[] read(String raw) throws IOException {
        try(InputStream in=resolver.openInputStream(Uri.parse(raw)); ByteArrayOutputStream out=new ByteArrayOutputStream()) {
            if(in==null)throw new IOException("Cannot open file");byte[] buffer=new byte[8192];int n,total=0;
            while((n=in.read(buffer))!=-1) {total+=n;if(total>32*1024*1024)throw new IOException("Playlist exceeds 32 MB");out.write(buffer,0,n);}return out.toByteArray();
        }
    }
    public void write(String uri,byte[] bytes) throws IOException {
        try(OutputStream out=resolver.openOutputStream(Uri.parse(uri),"wt")) {if(out==null)throw new IOException("Cannot write file / Nie moĹĽna zapisaÄ‡ pliku");out.write(bytes);out.flush();}
    }
    public List<Track> resolve(List<Track> tracks,Entry playlist,Library library) {
        String folder=playlist.parentPath;String source=localPath(playlist.uri);int slash=source.lastIndexOf('/');String absoluteParent=slash<0?"":source.substring(0,slash);
        for(Track t:tracks) {
            String loc=t.location;boolean scheme=loc.matches("^[a-zA-Z][a-zA-Z0-9+.-]*:.*"),absolute=loc.startsWith("/");
            if(!scheme && !absolute) { t.path=playlist.parentUri.isEmpty()?"":PlaylistCodec.normalize(join(folder,loc)); t.location=absoluteParent.startsWith("/")?PlaylistCodec.normalize(join(absoluteParent,loc)):loc; }
            else if(loc.startsWith("file:")) { try { t.location=Uri.parse(loc).getPath(); } catch(Exception ignored) { } }
            Track found=library==null?null:library.byLocation.get(t.location);
            if(found==null && library!=null && !playlist.parentUri.isEmpty() && !t.path.isEmpty()) found=library.byPath.get(t.path);
            if(found!=null) { t.uri=found.uri;t.path=found.path;t.location=found.location; }
            else if(loc.startsWith("content:")) {t.uri=loc;try{describe(Uri.parse(loc));}catch(Exception ex){t.missing=true;}}
            else t.missing=!(loc.startsWith("http://") || loc.startsWith("https://"));
        }return tracks;
    }
    public static String join(String a,String b) { return a.isEmpty()?b:a+"/"+b; }
    public Entry find(Entry folder,String name,AtomicBoolean cancel) throws IOException {
        for(Entry e:children(folder,cancel))if(e.name.equals(name))return e;return null;
    }
    public Entry create(Entry folder,String name,boolean directory) throws IOException {
        PlaylistCodec.name(name);
        Uri u=DocumentsContract.createDocument(resolver,Uri.parse(folder.uri),directory?Document.MIME_TYPE_DIR:"application/octet-stream",name);
        if(u==null)throw new IOException("Provider refused creation / Brak moĹĽliwoĹ›ci zapisu");Entry e=describe(u);e.path=join(folder.path,e.name);e.parentUri=folder.uri;e.parentPath=folder.path;
        if(!e.name.equals(name)) {try{DocumentsContract.deleteDocument(resolver,u);}catch(Exception ignored){}throw new IOException("Name conflict / Konflikt nazwy: "+name);}return e;
    }
    public Entry subfolder(Entry parent,String name,AtomicBoolean cancel) throws IOException {
        name=PlaylistCodec.name(name);Entry e=find(parent,name,cancel);if(e!=null && !e.directory)throw new IOException("File already uses folder name");return e==null?create(parent,name,true):e;
    }
    public String save(Entry folder,String name,byte[] data,int policy,AtomicBoolean cancel) throws IOException {
        // policy: 0 skip, 1 update with backup, 2 numbered new version.
        PlaylistCodec.name(name);check(cancel);Map<String,Entry> names=new HashMap<>();for(Entry e:children(folder,cancel))names.put(e.name,e);Entry existing=names.get(name);
        if(existing!=null && existing.directory)throw new IOException("Destination is a folder");
        if(existing!=null && policy==0)return "SKIP "+name;
        if(existing!=null && policy==2) {String extension=PlaylistCodec.ext(name),base=PlaylistCodec.stem(name);int i=2;do{name=base+" ("+i+++")."+extension;}while(names.containsKey(name));existing=null;}
        if(existing!=null) {
            byte[] original=read(existing.uri);String backupName=existing.name+"."+System.currentTimeMillis()+"-"+UUID.randomUUID().toString().substring(0,8)+".bak";
            Entry backup=create(folder,backupName,false);write(backup.uri,original);check(cancel);
            try{write(existing.uri,data);}catch(IOException ex){try{write(existing.uri,original);}catch(IOException restore){ex.addSuppressed(restore);}throw new IOException("Backup / Kopia: "+backupName+"; "+ex.getMessage(),ex);}
            return "UPDATE "+name+" â†’ "+backupName;
        }
        check(cancel);Entry created=create(folder,name,false);
        try{write(created.uri,data);}catch(IOException ex){try{DocumentsContract.deleteDocument(resolver,Uri.parse(created.uri));}catch(Exception ignored){}throw ex;}
        return "OK "+name;
    }
    public Entry rename(Entry entry,String base,AtomicBoolean cancel) throws IOException {
        if(!PlaylistCodec.isPlaylist(entry.name))throw new IOException("Not a playlist");String name=PlaylistCodec.name(base)+"."+PlaylistCodec.ext(entry.name);
        if(name.equals(entry.name))return entry;
        Entry parent=new Entry(entry.parentUri,"",entry.parentPath,"","",true,0);if(find(parent,name,cancel)!=null)throw new IOException("Name already exists / Nazwa juĹĽ istnieje");
        Uri uri=DocumentsContract.renameDocument(resolver,Uri.parse(entry.uri),name);if(uri==null)throw new IOException("Rename refused");Entry e=describe(uri);e.path=join(entry.parentPath,e.name);e.parentUri=entry.parentUri;e.parentPath=entry.parentPath;return e;
    }
    public void deletePlaylist(Entry entry) throws IOException {
        if(!PlaylistCodec.isPlaylist(entry.name))throw new IOException("Not a playlist");
        if(!DocumentsContract.deleteDocument(resolver,Uri.parse(entry.uri)))throw new IOException("Deletion refused / Nie udaĹ‚o siÄ™ usunÄ…Ä‡");
    }
}
