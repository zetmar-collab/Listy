package pl.zetmar.studioplaylist;

import static com.google.common.truth.Truth.assertThat;
import android.app.Activity;
import android.app.Instrumentation;
import android.content.*;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.view.*;
import android.widget.*;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.*;
import org.junit.runner.RunWith;
import java.io.*;
import java.util.*;

@RunWith(AndroidJUnit4.class)
public class ActivityInstrumentedTest {
    private Instrumentation instrumentation;private MainActivity activity;
    @Before public void start() throws Exception {
        instrumentation=InstrumentationRegistry.getInstrumentation();Context context=instrumentation.getTargetContext();
        java.lang.reflect.Field queue=MainActivity.class.getDeclaredField("persistence");queue.setAccessible(true);((java.util.concurrent.ExecutorService)queue.get(null)).submit(()->{}).get(10,java.util.concurrent.TimeUnit.SECONDS);
        context.getSharedPreferences("listy",Context.MODE_PRIVATE).edit().clear().putBoolean("english",false).commit();
        new File(context.getFilesDir(),"draft.json").delete();
        Intent i=new Intent(context,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        activity=(MainActivity)instrumentation.startActivitySync(i);awaitReady();
    }
    @After public void finish(){if(activity!=null)instrumentation.runOnMainSync(()->activity.finish());instrumentation.waitForIdleSync();}
    private List<View> matching(String text) {ArrayList<View> views=new ArrayList<>();activity.getWindow().getDecorView().findViewsWithText(views,text,View.FIND_VIEWS_WITH_TEXT);return views;}
    private void tap(String label) {for(View v:matching(label))if(v instanceof Button && ((Button)v).getText().toString().equals(label)){v.performClick();return;}throw new AssertionError("Missing button: "+label);}
    private Object get(String name) throws Exception {java.lang.reflect.Field f=MainActivity.class.getDeclaredField(name);f.setAccessible(true);return f.get(activity);}
    private void set(String name,Object value) throws Exception {java.lang.reflect.Field f=MainActivity.class.getDeclaredField(name);f.setAccessible(true);f.set(activity,value);}
    private void invoke(String name) throws Exception {java.lang.reflect.Method m=MainActivity.class.getDeclaredMethod(name);m.setAccessible(true);m.invoke(activity);}
    private void awaitReady() throws Exception {for(int i=0;i<200;i++){instrumentation.waitForIdleSync();if(!(boolean)get("busy"))return;Thread.sleep(25);}throw new AssertionError("Activity did not become ready");}
    @Test public void startsInPolishAndSwitchesToEnglish() {
        instrumentation.runOnMainSync(()->{assertThat(matching("Wybierz folder")).isNotEmpty();tap("EN");assertThat(matching("Choose folder")).isNotEmpty();assertThat(matching("Include subfolders")).isNotEmpty();});
    }
    @Test public void editorAndSavedTabsOpenAndRenderOnPhone() throws Exception {
        instrumentation.runOnMainSync(()->{tap("Edytor");assertThat(matching("Dodaj utwory")).isNotEmpty();assertThat(matching("Zapisz w bibliotece")).isNotEmpty();tap("Zapisane");assertThat(matching("Zapisane playlisty")).isNotEmpty();tap("Tworzenie");});
        instrumentation.waitForIdleSync();
        instrumentation.runOnMainSync(()->{View view=activity.getWindow().getDecorView();Bitmap bitmap=Bitmap.createBitmap(view.getWidth(),view.getHeight(),Bitmap.Config.ARGB_8888);view.draw(new Canvas(bitmap));try(FileOutputStream out=new FileOutputStream(new File(activity.getExternalFilesDir(null),"android-create.png"))){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);}catch(IOException ex){throw new RuntimeException(ex);}finally{bitmap.recycle();}});
    }
    @Test public void generatorOptionsRemainIndependentOfEditor() throws Exception {
        instrumentation.runOnMainSync(()->{try{set("autoName","Collection");set("autoFormat",2);set("playlistName","Manual");set("format",1);Storage.Library library=new Storage.Library();library.root=new Storage.Entry("content://test/root","Root","","","",true,0);library.folders.add(library.root);Track t=new Track("/Music/song.mp3","song");t.path="song.mp3";library.tracks.add(t);set("library",library);set("selected",new HashSet<>(List.of(library.root.uri)));set("mode",1);java.lang.reflect.Method m=MainActivity.class.getDeclaredMethod("plans");m.setAccessible(true);List<?> plans=(List<?>)m.invoke(activity);java.lang.reflect.Field name=plans.get(0).getClass().getDeclaredField("name");name.setAccessible(true);assertThat(name.get(plans.get(0))).isEqualTo("Collection.pls");assertThat(get("playlistName")).isEqualTo("Manual");assertThat(get("format")).isEqualTo(1);}catch(Exception ex){throw new RuntimeException(ex);}});
    }
    @Test public void draftSurvivesActivityRecreation() throws Exception {
        instrumentation.runOnMainSync(()->{try{set("playlistName","Restored draft");set("dirty",true);set("editor",new ArrayList<>(List.of(new Track("/music/a.mp3","Song"))));invoke("persistDraft");}catch(Exception ex){throw new RuntimeException(ex);}});
        java.lang.reflect.Field field=MainActivity.class.getDeclaredField("persistence");field.setAccessible(true);((java.util.concurrent.ExecutorService)field.get(null)).submit(()->{}).get(10,java.util.concurrent.TimeUnit.SECONDS);
        instrumentation.runOnMainSync(()->activity.finish());instrumentation.waitForIdleSync();Context context=instrumentation.getTargetContext();activity=(MainActivity)instrumentation.startActivitySync(new Intent(context,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));awaitReady();
        assertThat(get("playlistName")).isEqualTo("Restored draft");assertThat((List<?>)get("editor")).hasSize(1);assertThat(get("dirty")).isEqualTo(true);
    }
    @Test public void previewIsReleasedWhenActivityPauses() throws Exception {
        instrumentation.runOnMainSync(()->{try{set("previewPlayer",new android.media.MediaPlayer());activity.onPause();assertThat(get("previewPlayer")).isNull();}catch(Exception ex){throw new RuntimeException(ex);}});
    }
    @Test public void sameFolderReselectionPreservesSelectedAlbums() throws Exception {
        instrumentation.getUiAutomation().adoptShellPermissionIdentity("android.permission.MANAGE_DOCUMENTS");Storage storage=new Storage(activity);Storage.Entry created=null;
        try{android.net.Uri root=android.provider.DocumentsContract.buildTreeDocumentUri("pl.zetmar.studioplaylist.test.documents","root");Storage.Entry provider=storage.describe(android.provider.DocumentsContract.buildDocumentUriUsingTree(root,"root"));created=storage.create(provider,"select-"+UUID.randomUUID(),true);Storage.Entry song=storage.create(created,"song.mp3",false);String tree=android.provider.DocumentsContract.buildTreeDocumentUri("pl.zetmar.studioplaylist.test.documents",android.provider.DocumentsContract.getDocumentId(android.net.Uri.parse(created.uri))).toString();Storage.Library library=storage.scan(tree,true,false,new java.util.concurrent.atomic.AtomicBoolean(),s->{});Set<String> selected=new HashSet<>(List.of(library.root.uri));
            instrumentation.runOnMainSync(()->{try{set("tree",tree);set("library",library);set("selected",selected);activity.getSharedPreferences("listy",Context.MODE_PRIVATE).edit().putString("scannedTree",tree).commit();activity.onActivityResult(10,Activity.RESULT_OK,new Intent().setData(android.net.Uri.parse(tree)));}catch(Exception ex){throw new RuntimeException(ex);}});awaitReady();assertThat((Set<?>)get("selected")).containsExactly(library.root.uri);android.provider.DocumentsContract.deleteDocument(activity.getContentResolver(),android.net.Uri.parse(song.uri));
        }finally{if(created!=null)android.provider.DocumentsContract.deleteDocument(activity.getContentResolver(),android.net.Uri.parse(created.uri));instrumentation.getUiAutomation().dropShellPermissionIdentity();}
    }
    @Test public void exportUsesDurablePayloadAfterRecreation() throws Exception {
        instrumentation.getUiAutomation().adoptShellPermissionIdentity("android.permission.MANAGE_DOCUMENTS");Storage storage=new Storage(activity);Storage.Entry target=null;
        try{android.net.Uri root=android.provider.DocumentsContract.buildTreeDocumentUri("pl.zetmar.studioplaylist.test.documents","root");Storage.Entry provider=storage.describe(android.provider.DocumentsContract.buildDocumentUriUsingTree(root,"root"));target=storage.create(provider,"export-"+UUID.randomUUID()+".m3u8",false);byte[] payload="#EXTM3U\nAlbum/song.mp3\n".getBytes(java.nio.charset.StandardCharsets.UTF_8);android.util.AtomicFile pending=new android.util.AtomicFile(new File(activity.getFilesDir(),"pending-export"));FileOutputStream stream=pending.startWrite();stream.write(payload);pending.finishWrite(stream);Storage.Entry destination=target;
            instrumentation.runOnMainSync(()->activity.finish());instrumentation.waitForIdleSync();activity=(MainActivity)instrumentation.startActivitySync(new Intent(instrumentation.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));awaitReady();
            instrumentation.runOnMainSync(()->activity.onActivityResult(13,Activity.RESULT_OK,new Intent().setData(android.net.Uri.parse(destination.uri))));awaitReady();assertThat(storage.read(target.uri)).isEqualTo(payload);
        }finally{if(target!=null)storage.deletePlaylist(target);instrumentation.getUiAutomation().dropShellPermissionIdentity();}
    }
    @Test public void unsavedEditorSupportsCancelDiscardAndSaveWithBackup() throws Exception {
        instrumentation.getUiAutomation().adoptShellPermissionIdentity("android.permission.MANAGE_DOCUMENTS");Storage storage=new Storage(activity);Storage.Entry folder=null;
        try{android.net.Uri root=android.provider.DocumentsContract.buildTreeDocumentUri("pl.zetmar.studioplaylist.test.documents","root");Storage.Entry provider=storage.describe(android.provider.DocumentsContract.buildDocumentUriUsingTree(root,"root"));folder=storage.create(provider,"guard-"+UUID.randomUUID(),true);storage.save(folder,"Album.m3u8","#EXTM3U\nhttps://example.org/old\n".getBytes(java.nio.charset.StandardCharsets.UTF_8),0,new java.util.concurrent.atomic.AtomicBoolean());Storage.Entry saved=storage.find(folder,"Album.m3u8",new java.util.concurrent.atomic.AtomicBoolean());
            for(int button:new int[]{android.app.AlertDialog.BUTTON_NEUTRAL,android.app.AlertDialog.BUTTON_NEGATIVE,android.app.AlertDialog.BUTTON_POSITIVE}){
                java.util.concurrent.atomic.AtomicInteger continued=new java.util.concurrent.atomic.AtomicInteger();
                instrumentation.runOnMainSync(()->{try{set("dirty",true);set("playlistName","Album");set("format",0);set("opened",saved);set("editor",new ArrayList<>(List.of(new Track("https://example.org/new","New"))));java.lang.reflect.Method guard=MainActivity.class.getDeclaredMethod("guard",Runnable.class);guard.setAccessible(true);guard.invoke(activity,(Runnable)continued::incrementAndGet);((android.app.AlertDialog)get("editorPrompt")).getButton(button).performClick();}catch(Exception ex){throw new RuntimeException(ex);}});
                awaitReady();if(button==android.app.AlertDialog.BUTTON_POSITIVE){instrumentation.runOnMainSync(()->{try{((android.app.AlertDialog)get("editorPrompt")).getButton(android.app.AlertDialog.BUTTON_POSITIVE).performClick();}catch(Exception ex){throw new RuntimeException(ex);}});awaitReady();}
                assertThat(continued.get()).isEqualTo(button==android.app.AlertDialog.BUTTON_NEUTRAL?0:1);if(button==android.app.AlertDialog.BUTTON_NEUTRAL)assertThat(get("dirty")).isEqualTo(true);
                String text=PlaylistCodec.decode(storage.read(saved.uri));assertThat(text).contains(button==android.app.AlertDialog.BUTTON_POSITIVE?"https://example.org/new":"https://example.org/old");
            }
            assertThat(storage.children(folder,new java.util.concurrent.atomic.AtomicBoolean()).stream().filter(e->e.name.endsWith(".bak")).count()).isEqualTo(1);
        }finally{if(folder!=null){for(Storage.Entry e:storage.children(folder,new java.util.concurrent.atomic.AtomicBoolean()))android.provider.DocumentsContract.deleteDocument(activity.getContentResolver(),android.net.Uri.parse(e.uri));android.provider.DocumentsContract.deleteDocument(activity.getContentResolver(),android.net.Uri.parse(folder.uri));}instrumentation.getUiAutomation().dropShellPermissionIdentity();}
    }
}
