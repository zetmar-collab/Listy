package pl.zetmar.studioplaylist;

import static com.google.common.truth.Truth.assertThat;
import android.content.Context;
import android.net.Uri;
import android.provider.DocumentsContract;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.*;
import org.junit.runner.RunWith;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.CancellationException;
import java.util.concurrent.atomic.AtomicBoolean;

@RunWith(AndroidJUnit4.class)
public class StorageInstrumentedTest {
    private Storage storage;private Storage.Entry folder;private String tree;private final AtomicBoolean stop=new AtomicBoolean();
    @Before public void setup() throws Exception {
        // Temporary test-process identity, dropped in cleanup. Production requests user SAF grants.
        InstrumentationRegistry.getInstrumentation().getUiAutomation().adoptShellPermissionIdentity("android.permission.MANAGE_DOCUMENTS");
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();storage=new Storage(context);
        Uri root=DocumentsContract.buildTreeDocumentUri("pl.zetmar.studioplaylist.test.documents","root");
        Storage.Entry providerRoot=storage.describe(DocumentsContract.buildDocumentUriUsingTree(root,"root"));
        folder=storage.create(providerRoot,"run-"+UUID.randomUUID(),true);
        tree=DocumentsContract.buildTreeDocumentUri("pl.zetmar.studioplaylist.test.documents",DocumentsContract.getDocumentId(Uri.parse(folder.uri))).toString();
    }
    @After public void cleanup() throws Exception {try{if(folder!=null)clean(folder);}finally{InstrumentationRegistry.getInstrumentation().getUiAutomation().dropShellPermissionIdentity();}}
    private void clean(Storage.Entry e) throws Exception {if(e.directory)for(Storage.Entry child:storage.children(e,stop))clean(child);DocumentsContract.deleteDocument(InstrumentationRegistry.getInstrumentation().getTargetContext().getContentResolver(),Uri.parse(e.uri));}
    @Test public void recursiveScanSeesAlbumAudioAndPlaylists() throws Exception {
        Storage.Entry album=storage.create(folder,"Zażółć Album",true);storage.create(album,"02 utwór.MP3",false);storage.create(album,"Album.m3u8",false);storage.create(album,"not-music.txt",false);
        Storage.Library flat=storage.scan(tree,false,false,stop,s->{});assertThat(flat.tracks).isEmpty();
        Storage.Library recursive=storage.scan(tree,true,false,stop,s->{});assertThat(recursive.tracks).hasSize(1);assertThat(recursive.playlists).hasSize(1);assertThat(recursive.tracks.get(0).path).isEqualTo("Zażółć Album/02 utwór.MP3");
    }
    @Test public void updateCreatesExactBackupAndVersioningDoesNotOverwrite() throws Exception {
        byte[] first="#EXTM3U\nold.mp3\n".getBytes(StandardCharsets.UTF_8),second="#EXTM3U\nnew.mp3\n".getBytes(StandardCharsets.UTF_8);
        storage.save(folder,"Album.m3u8",first,0,stop);storage.save(folder,"Album.m3u8",second,0,stop);
        assertThat(storage.read(storage.find(folder,"Album.m3u8",stop).uri)).isEqualTo(first);
        storage.save(folder,"Album.m3u8",second,1,stop);List<Storage.Entry> files=storage.children(folder,stop);assertThat(files).hasSize(2);
        Storage.Entry backup=files.stream().filter(e->e.name.endsWith(".bak")).findFirst().orElseThrow();assertThat(storage.read(backup.uri)).isEqualTo(first);
        storage.save(folder,"Album.m3u8",first,2,stop);assertThat(storage.find(folder,"Album (2).m3u8",stop)).isNotNull();assertThat(storage.read(storage.find(folder,"Album.m3u8",stop).uri)).isEqualTo(second);
    }
    @Test public void renameAndDeleteOnlyAffectPlaylists() throws Exception {
        Storage.Entry audio=storage.create(folder,"01.mp3",false);storage.write(audio.uri,new byte[]{1,2,3});storage.save(folder,"Album.pls","[playlist]".getBytes(StandardCharsets.UTF_8),0,stop);
        Storage.Entry renamed=storage.rename(storage.find(folder,"Album.pls",stop),"Nowa nazwa",stop);assertThat(renamed.name).isEqualTo("Nowa nazwa.pls");storage.deletePlaylist(renamed);assertThat(storage.read(audio.uri)).isEqualTo(new byte[]{1,2,3});
        boolean refused=false;try{storage.deletePlaylist(audio);}catch(Exception ex){refused=true;}assertThat(refused).isTrue();
    }
    @Test public void relativeReferencesResolveAgainstActualPlaylistFolder() throws Exception {
        Storage.Entry album=storage.create(folder,"Album",true);Storage.Entry song=storage.create(album,"01.mp3",false);storage.save(album,"Album.m3u8","#EXTM3U\n01.mp3\n".getBytes(StandardCharsets.UTF_8),0,stop);
        Storage.Library library=storage.scan(tree,true,false,stop,s->{});Storage.Entry playlist=library.playlists.get(0);List<Track> rows=storage.resolve(PlaylistCodec.parse(PlaylistCodec.decode(storage.read(playlist.uri)),"M3U8"),playlist,library);assertThat(DocumentsContract.getDocumentId(Uri.parse(rows.get(0).uri))).isEqualTo(DocumentsContract.getDocumentId(Uri.parse(song.uri)));assertThat(rows.get(0).missing).isFalse();
    }
    @Test public void allFormatsParseOnAndroidRuntime() throws Exception {Track t=new Track("/music/Zażółć & #1.mp3","Zażółć & #1");for(String format:PlaylistCodec.FORMATS){String text=PlaylistCodec.serialize(List.of(t),format,"Test","",false);assertThat(PlaylistCodec.parse(text,format)).hasSize(1);}}
    @Test(expected=CancellationException.class) public void cancelledScanStopsBeforeAnyTraversal() throws Exception {storage.scan(tree,true,false,new AtomicBoolean(true),s->{});}
    @Test public void restoringBackupPreservesCurrentContents() throws Exception {
        byte[] original="#EXTM3U\nold.mp3\n".getBytes(StandardCharsets.UTF_8),changed="#EXTM3U\nnew.mp3\n".getBytes(StandardCharsets.UTF_8);
        storage.save(folder,"Album.m3u8",original,0,stop);storage.save(folder,"Album.m3u8",changed,1,stop);
        Storage.Entry backup=storage.children(folder,stop).stream().filter(e->e.name.endsWith(".bak")).findFirst().orElseThrow();storage.save(folder,"Album.m3u8",storage.read(backup.uri),1,stop);
        assertThat(storage.read(storage.find(folder,"Album.m3u8",stop).uri)).isEqualTo(original);
        boolean preserved=false;for(Storage.Entry e:storage.children(folder,stop))if(e.name.endsWith(".bak") && Arrays.equals(storage.read(e.uri),changed))preserved=true;assertThat(preserved).isTrue();
    }
    @Test public void portableZipAndHashingDoNotChangeSources() throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();StudioStorage extra=new StudioStorage(context);List<Track> tracks=new ArrayList<>();
        for(String name:List.of("a.mp3","b.mp3")){Storage.Entry e=storage.create(folder,name,false);storage.write(e.uri,new byte[]{1,2,3,4});tracks.add(storage.track(e));}
        assertThat(extra.duplicates(tracks,stop)).hasSize(1);java.io.File file=extra.portable(tracks,stop);
        try(java.util.zip.ZipFile zip=new java.util.zip.ZipFile(file)){assertThat(zip.size()).isEqualTo(3);String text=new String(zip.getInputStream(zip.getEntry("Playlist.m3u8")).readAllBytes(),StandardCharsets.UTF_8);assertThat(text).contains("Music/00001-a.mp3");assertThat(text).contains("Music/00002-b.mp3");}
        for(Track t:tracks)assertThat(storage.read(t.uri)).isEqualTo(new byte[]{1,2,3,4});
    }
    @Test public void unknownProviderFingerprintNeverCachesStaleTags() throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();Storage.Entry e=storage.create(folder,"unknown-broken.mp3",false);storage.write(e.uri,new byte[]{1,2});Track t=storage.track(e);MetadataCache cache=new MetadataCache(context);cache.read(storage,new StudioStorage(context),t);cache.save();assertThat(t.metadataWarning).isNotEmpty();assertThat(cache.hits).isEqualTo(0);
    }
    @Test public void failedOverwriteKeepsOriginalAndReadableBackup() throws Exception {
        byte[] original="#EXTM3U\noriginal.mp3\n".getBytes(StandardCharsets.UTF_8);storage.save(folder,"editable.m3u8",original,0,stop);Storage.Entry readOnly=storage.rename(storage.find(folder,"editable.m3u8",stop),"readonly",stop);boolean failed=false;
        try{storage.save(folder,"readonly.m3u8","changed".getBytes(StandardCharsets.UTF_8),1,stop);}catch(java.io.IOException ex){failed=true;assertThat(ex.getMessage()).contains("Backup");}assertThat(failed).isTrue();assertThat(storage.read(readOnly.uri)).isEqualTo(original);
        Storage.Entry backup=storage.children(folder,stop).stream().filter(e->e.name.endsWith(".bak")).findFirst().orElseThrow();assertThat(storage.read(backup.uri)).isEqualTo(original);
    }
    @Test public void missingDocumentAndRevokedPermissionAreReported() throws Exception {
        Storage.Entry e=storage.create(folder,"removed.m3u8",false);storage.deletePlaylist(e);boolean unavailable=false;try{storage.read(e.uri);}catch(Exception ex){unavailable=true;}assertThat(unavailable).isTrue();
        InstrumentationRegistry.getInstrumentation().getUiAutomation().dropShellPermissionIdentity();boolean denied=false;try{storage.children(folder,stop);}catch(SecurityException ex){denied=true;}finally{InstrumentationRegistry.getInstrumentation().getUiAutomation().adoptShellPermissionIdentity("android.permission.MANAGE_DOCUMENTS");}assertThat(denied).isTrue();
    }
    private byte[] wav(int dataSize){java.nio.ByteBuffer b=java.nio.ByteBuffer.allocate(44+dataSize).order(java.nio.ByteOrder.LITTLE_ENDIAN);b.put("RIFF".getBytes(StandardCharsets.US_ASCII)).putInt(36+dataSize).put("WAVEfmt ".getBytes(StandardCharsets.US_ASCII)).putInt(16).putShort((short)1).putShort((short)1).putInt(8000).putInt(16000).putShort((short)2).putShort((short)16).put("data".getBytes(StandardCharsets.US_ASCII)).putInt(dataSize);return b.array();}
    @Test public void metadataCacheReusesTagsAndInvalidatesChangedAudio() throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();Storage.Entry song=storage.create(folder,"cache.wav",false);storage.write(song.uri,wav(16000));StudioStorage extra=new StudioStorage(context);MetadataCache cache=new MetadataCache(context);Track first=storage.track(song);cache.read(storage,extra,first);assertThat(first.metadataWarning).isEmpty();assertThat(first.duration).isGreaterThan(0);cache.save();
        MetadataCache restored=new MetadataCache(context);restored.read(storage,extra,storage.track(song));assertThat(restored.hits).isEqualTo(1);storage.write(song.uri,wav(32000));restored.read(storage,extra,storage.track(song));assertThat(restored.reads).isEqualTo(1);
    }
}
