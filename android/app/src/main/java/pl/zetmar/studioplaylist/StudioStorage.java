package pl.zetmar.studioplaylist;

import android.content.*;
import android.net.Uri;
import android.os.StatFs;
import android.provider.DocumentsContract.Document;
import android.database.Cursor;
import java.io.*;
import java.security.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.zip.*;

/** Streaming operations; never deletes or edits source audio. */
public final class StudioStorage {
    private final Context context;
    private final ContentResolver resolver;
    public StudioStorage(Context context){this.context=context.getApplicationContext();resolver=context.getContentResolver();}
    public long[] fingerprint(String uri) {
        try(Cursor c=resolver.query(Uri.parse(uri),new String[]{Document.COLUMN_SIZE,Document.COLUMN_LAST_MODIFIED},null,null,null)){
            if(c!=null && c.moveToFirst() && !c.isNull(0) && !c.isNull(1))return new long[]{c.getLong(0),c.getLong(1)};
        }catch(RuntimeException ignored){}return new long[]{-1,0};
    }
    public List<List<String>> duplicates(List<Track> tracks,AtomicBoolean stop) throws Exception {
        Map<Long,List<Track>> sizes=new HashMap<>();Set<String> seen=new HashSet<>();
        for(Track t:tracks){Storage.check(stop);if(!t.uri.isEmpty() && seen.add(t.uri)){long size=fingerprint(t.uri)[0];sizes.computeIfAbsent(size,k->new ArrayList<>()).add(t);}}
        Map<String,List<String>> hashes=new HashMap<>();byte[] buffer=new byte[65536];
        for(Map.Entry<Long,List<Track>> e:sizes.entrySet())if(e.getKey()<0 || e.getValue().size()>1)for(Track t:e.getValue()){
            Storage.check(stop);MessageDigest digest=MessageDigest.getInstance("SHA-256");
            try(InputStream in=resolver.openInputStream(Uri.parse(t.uri))){if(in==null)throw new IOException("Cannot open: "+t.label());int n;while((n=in.read(buffer))!=-1){Storage.check(stop);digest.update(buffer,0,n);}}
            String key=Base64.getEncoder().encodeToString(digest.digest());hashes.computeIfAbsent(key,k->new ArrayList<>()).add(t.path.isEmpty()?t.location:t.path);
        }
        List<List<String>> result=new ArrayList<>();for(List<String> group:hashes.values())if(group.size()>1)result.add(group);return result;
    }
    public File portable(List<Track> tracks,AtomicBoolean stop) throws Exception {
        long total=1024*1024;for(Track t:tracks){if(t.uri.isEmpty())throw new IOException("ZIP requires accessible music / ZIP wymaga dostępnych plików muzycznych");long size=fingerprint(t.uri)[0];if(size>0)total=Math.addExact(total,size);}
        if(new StatFs(context.getFilesDir().getPath()).getAvailableBytes()<total)throw new IOException("Insufficient free space / Brak wolnego miejsca");
        File file=new File(context.getFilesDir(),"pending-portable.zip"),temp=new File(context.getFilesDir(),"portable-"+UUID.randomUUID()+".tmp");
        try {
            try(ZipOutputStream zip=new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(temp)))){
                zip.setLevel(0);List<Track> exported=new ArrayList<>();int i=0;byte[] buffer=new byte[65536];
                for(Track t:tracks){Storage.check(stop);String base=PlaylistCodec.basename(t.path.isEmpty()?t.location:t.path).replaceAll("[\\\\/:*?\"<>|\\r\\n]","_");String name="Music/"+String.format(Locale.ROOT,"%05d",++i)+"-"+base;
                    zip.putNextEntry(new ZipEntry(name));try(InputStream in=resolver.openInputStream(Uri.parse(t.uri))){if(in==null)throw new IOException("Cannot open "+t.label());int n;while((n=in.read(buffer))!=-1){Storage.check(stop);zip.write(buffer,0,n);}}zip.closeEntry();Track row=t.copy();row.uri="";row.path=name;row.location=name;exported.add(row);
                }
                zip.putNextEntry(new ZipEntry("Playlist.m3u8"));zip.write(PlaylistCodec.serialize(exported,"M3U8","Playlist","",true).getBytes(java.nio.charset.StandardCharsets.UTF_8));zip.closeEntry();
            }
            Storage.check(stop);java.nio.file.Files.move(temp.toPath(),file.toPath(),java.nio.file.StandardCopyOption.REPLACE_EXISTING);return file;
        }finally{if(temp.exists() && !temp.delete())temp.deleteOnExit();}
    }
    public void exportFile(File file,String destination,AtomicBoolean stop) throws IOException {
        try(InputStream in=new FileInputStream(file);OutputStream out=resolver.openOutputStream(Uri.parse(destination),"wt")){
            if(out==null)throw new IOException("Cannot write / Nie można zapisać");byte[] buffer=new byte[65536];int n;while((n=in.read(buffer))!=-1){Storage.check(stop);out.write(buffer,0,n);}out.flush();
        }
    }
}
