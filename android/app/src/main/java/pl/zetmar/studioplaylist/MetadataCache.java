package pl.zetmar.studioplaylist;

import android.content.Context;
import android.util.AtomicFile;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;

/** Only providers with a reliable size and modification time are cached. */
public final class MetadataCache {
    private JSONObject items=new JSONObject();
    private final AtomicFile file;
    public int hits,reads;
    public MetadataCache(Context context){file=new AtomicFile(new File(context.getFilesDir(),"metadata-cache.json"));try{if(file.getBaseFile().length()<16*1024*1024)items=new JSONObject(new String(file.readFully(),StandardCharsets.UTF_8));}catch(Exception ignored){}}
    public void read(Storage storage,StudioStorage extra,Track t){
        long[] key=extra.fingerprint(t.uri);JSONObject old=items.optJSONObject(t.uri);
        if(key[0]>=0 && key[1]>0 && old!=null && old.optLong("size",-1)==key[0] && old.optLong("modified",0)==key[1]){t.title=old.optString("title");t.artist=old.optString("artist");t.album=old.optString("album");t.duration=old.optDouble("duration",-1);hits++;return;}
        storage.readTags(t);reads++;
        if(key[0]>=0 && key[1]>0 && t.metadataWarning.isEmpty())try{JSONObject v=new JSONObject();v.put("size",key[0]);v.put("modified",key[1]);v.put("title",t.title);v.put("artist",t.artist);v.put("album",t.album);v.put("duration",t.duration);items.put(t.uri,v);}catch(JSONException ignored){}
    }
    public void save() throws IOException {byte[] bytes=items.toString().getBytes(StandardCharsets.UTF_8);if(bytes.length>16*1024*1024)return;FileOutputStream out=file.startWrite();try{out.write(bytes);file.finishWrite(out);}catch(Exception ex){file.failWrite(out);throw ex;}}
}
