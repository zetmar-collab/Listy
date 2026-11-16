package pl.zetmar.studioplaylist;

import android.app.*;
import android.content.*;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.*;
import android.provider.DocumentsContract;
import android.text.*;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Native phone/tablet UI; file operations are confined to a single background worker. */
@SuppressWarnings("deprecation")
public class MainActivity extends Activity {
    private static final int TREE=10,ADD=11,OPEN=12,EXPORT=13,ZIP=14;
    private static final int INK=0xff172f36,MUTED=0xff52716e,GREEN=0xff246b60,BG=0xfff2f6f4;
    private final ExecutorService io=Executors.newSingleThreadExecutor();
    private static final ExecutorService persistence=Executors.newSingleThreadExecutor();
    private boolean draftReady;
    private volatile int draftRevision;
    private final Handler ui=new Handler(Looper.getMainLooper());
    private AtomicBoolean cancel=new AtomicBoolean();
    private Storage storage;
    private Storage.Library library;
    private SharedPreferences prefs;
    private boolean english,busy,recursive=true,relative=true,editorRelative=true,tags,dirty;
    private String tree="",playlistName="Moja playlista",subfolder="Playlisty",query="",report="",progress="";
    private int tab,format,mode,policy,sort,autoFormat;
    private String autoName="",smartArtist="",smartAlbum="";
    private Set<String> extensions=new LinkedHashSet<>(PlaylistCodec.AUDIO),selected=new HashSet<>();
    private List<Track> editor=new ArrayList<>();
    private final Deque<List<Track>> undo=new ArrayDeque<>(),redo=new ArrayDeque<>();
    private Storage.Entry opened;
    private String importedRaw="";
    private int focused=-1;
    private LinearLayout screen,body,contentRows;
    private ListView virtualRows;
    private ScrollView mainScroll;
    private int renderedTab=-1;
    private Runnable pendingSearch;
    private TextView progressLabel;
    private String editorEncoding="UTF-8",autoEncoding="UTF-8";
    private android.media.MediaPlayer previewPlayer;
    private AlertDialog editorPrompt;
    private final Runnable stopPreview=()->{if(previewPlayer!=null){previewPlayer.release();previewPlayer=null;}};
    private String exportName;

    private static final class ReadResult {final String text;final List<Track> tracks;ReadResult(String text,List<Track> tracks){this.text=text;this.tracks=tracks;}}
    private static final class SaveResult {final String message;final Storage.Entry entry;SaveResult(String message,Storage.Entry entry){this.message=message;this.entry=entry;}}
    private static final class HealthResult {final List<Track> tracks;final int duplicates,missing;HealthResult(List<Track> tracks,int duplicates,int missing){this.tracks=tracks;this.duplicates=duplicates;this.missing=missing;}}
    private static final class RepairResult {final List<Track> tracks;final int count;RepairResult(List<Track> tracks,int count){this.tracks=tracks;this.count=count;}}
    private String l(String pl,String en) {return english?en:pl;}
    private int dp(float value) {return (int)(value*getResources().getDisplayMetrics().density+.5f);}
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);storage=new Storage(this);prefs=getSharedPreferences("listy",MODE_PRIVATE);
        english=prefs.getBoolean("english",!Locale.getDefault().getLanguage().equals("pl"));tree=prefs.getString("tree","");
        recursive=prefs.getBoolean("recursive",true);relative=prefs.getBoolean("relative",true);editorRelative=prefs.getBoolean("editorRelative",true);tags=prefs.getBoolean("tags",false);
        format=prefs.getInt("format",0);mode=prefs.getInt("mode",0);policy=prefs.getInt("policy",0);sort=prefs.getInt("sort",0);
        playlistName=prefs.getString("name",l("Moja playlista","My playlist"));subfolder=prefs.getString("subfolder","Playlisty");
        extensions=new LinkedHashSet<>(prefs.getStringSet("extensions",PlaylistCodec.AUDIO));
        selected=new HashSet<>(prefs.getStringSet("selected",Collections.emptySet()));
        if(Build.VERSION.SDK_INT>=33)getOnBackInvokedDispatcher().registerOnBackInvokedCallback(android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT,this::handleBack);
        editorEncoding=prefs.getString("editorEncoding","UTF-8");autoEncoding=prefs.getString("autoEncoding","UTF-8");autoName=prefs.getString("autoName",playlistName);autoFormat=prefs.getInt("autoFormat",format);busy=true;render();persistence.execute(()->{restoreDraft();ui.post(()->{if(isDestroyed())return;draftReady=true;busy=false;render();if(!tree.isEmpty())scan();});});
    }
    @Override public void onConfigurationChanged(Configuration c) {super.onConfigurationChanged(c);render();}
    @Override protected void onPause() {super.onPause();stopPreview.run();ui.removeCallbacks(stopPreview);savePreferences();persistDraft();}
    @Override protected void onDestroy() {cancel.set(true);io.shutdown();super.onDestroy();}
    private void savePreferences() {
        if(prefs==null)return;
        prefs.edit().putBoolean("english",english).putString("tree",tree).putBoolean("recursive",recursive).putBoolean("relative",relative).putBoolean("editorRelative",editorRelative)
            .putBoolean("tags",tags).putInt("format",format).putInt("mode",mode).putInt("policy",policy).putInt("sort",sort)
            .putString("editorEncoding",editorEncoding).putString("autoEncoding",autoEncoding).putString("autoName",autoName).putInt("autoFormat",autoFormat).putString("name",playlistName).putString("subfolder",subfolder).putStringSet("extensions",new HashSet<>(extensions)).putStringSet("selected",new HashSet<>(selected)).apply();
    }
    private static List<Track> copy(List<Track> source) {List<Track> r=new ArrayList<>();for(Track t:source)r.add(t.copy());return r;}
    private void persistDraft() {
        if(!draftReady)return;
        final int revision=++draftRevision;final String draftName=playlistName;final boolean draftDirty=dirty;final int draftFormat=format;final boolean imported=!importedRaw.isEmpty();final Storage.Entry draftOpened=opened;final List<Track> draftTracks=copy(editor);
        persistence.execute(()->{if(revision!=draftRevision)return;try {
            JSONObject data=new JSONObject();data.put("name",draftName);data.put("dirty",draftDirty);data.put("format",draftFormat);data.put("imported",imported);
            if(draftOpened!=null) {JSONObject e=new JSONObject();e.put("uri",draftOpened.uri);e.put("name",draftOpened.name);e.put("path",draftOpened.path);e.put("parentUri",draftOpened.parentUri);e.put("parentPath",draftOpened.parentPath);data.put("opened",e);}
            JSONArray rows=new JSONArray();for(Track t:draftTracks) {JSONObject o=new JSONObject();o.put("uri",t.uri);o.put("path",t.path);o.put("location",t.location);o.put("title",t.title);o.put("artist",t.artist);o.put("album",t.album);o.put("duration",Double.isFinite(t.duration)?t.duration:-1);o.put("missing",t.missing);rows.put(o);}data.put("tracks",rows);
            byte[] bytes=data.toString().getBytes(StandardCharsets.UTF_8);if(bytes.length>32*1024*1024)throw new IOException("Draft exceeds 32 MB / Szkic przekracza 32 MB");
            {android.util.AtomicFile f=new android.util.AtomicFile(new File(getFilesDir(),"draft.json"));FileOutputStream out=null;try{out=f.startWrite();out.write(bytes);f.finishWrite(out);}catch(Exception ex){if(out!=null)f.failWrite(out);ui.post(()->toast(l("Błąd zapisu szkicu: ","Draft save failed: ")+ex.getMessage()));}}
        } catch(Exception ex) {ui.post(()->{if(!isDestroyed())toast(l("Błąd szkicu: ","Draft failed: ")+ex.getMessage());});}
        });
    }
    private void restoreDraft() {
        File file=new File(getFilesDir(),"draft.json");if(file.length()>32*1024*1024)return;
        try(InputStream in=new android.util.AtomicFile(file).openRead();ByteArrayOutputStream buffer=new ByteArrayOutputStream()) {byte[] b=new byte[8192];int n;while((n=in.read(b))!=-1)buffer.write(b,0,n);
            JSONObject o=new JSONObject(buffer.toString("UTF-8"));playlistName=o.optString("name",playlistName);dirty=o.optBoolean("dirty");format=o.optInt("format",format);importedRaw=o.optBoolean("imported")?"restored":"";
            JSONObject e=o.optJSONObject("opened");if(e!=null)opened=new Storage.Entry(e.getString("uri"),e.getString("name"),e.getString("path"),e.getString("parentUri"),e.getString("parentPath"),false,0);
            JSONArray a=o.getJSONArray("tracks");for(int i=0;i<a.length();i++){JSONObject t=a.getJSONObject(i);Track row=new Track(t.getString("location"),t.getString("title"));row.uri=t.optString("uri");row.path=t.optString("path");row.artist=t.optString("artist");row.album=t.optString("album");row.duration=t.optDouble("duration",-1);row.missing=t.optBoolean("missing");editor.add(row);}
            if(dirty || !editor.isEmpty())tab=2;
        }catch(FileNotFoundException ignored){}catch(Exception ex){report=l("Nie udało się przywrócić szkicu: ","Could not restore draft: ")+ex.getMessage();}
    }
    private GradientDrawable shape(int color,int radius) {GradientDrawable d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(radius));return d;}
    private LinearLayout column() {LinearLayout v=new LinearLayout(this);v.setOrientation(LinearLayout.VERTICAL);return v;}
    private LinearLayout row() {LinearLayout v=new LinearLayout(this);v.setGravity(Gravity.CENTER_VERTICAL);return v;}
    private TextView text(String value,int size,int color) {TextView t=new TextView(this);t.setText(value);t.setTextSize(size);t.setTextColor(color);t.setPadding(0,dp(4),0,dp(4));return t;}
    private TextView heading(String value) {TextView t=text(value,20,INK);t.setTypeface(null,Typeface.BOLD);return t;}
    private Button button(String label,Runnable action) {Button b=new Button(this);b.setText(label);b.setAllCaps(false);b.setTextSize(14);b.setTextColor(GREEN);b.setMinHeight(dp(48));b.setEnabled(!busy);if(label.equals("↑"))b.setContentDescription(l("Przesuń utwór w górę","Move track up"));if(label.equals("↓"))b.setContentDescription(l("Przesuń utwór w dół","Move track down"));b.setOnClickListener(v->{if(!busy)action.run();});return b;}
    private void buttons(LinearLayout parent,Button... buttons) {LinearLayout r=row();for(Button b:buttons)r.addView(b,new LinearLayout.LayoutParams(0,ViewGroup.LayoutParams.WRAP_CONTENT,1));parent.addView(r);}
    private LinearLayout card() {LinearLayout c=column();c.setPadding(dp(16),dp(12),dp(16),dp(12));c.setBackground(shape(Color.WHITE,18));LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.setMargins(0,dp(8),0,dp(8));body.addView(c,p);return c;}
    private void watch(EditText e,java.util.function.Consumer<String> changed) {e.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int a,int c,int f){}public void onTextChanged(CharSequence s,int start,int before,int count){changed.accept(s.toString());}public void afterTextChanged(Editable x){}});}
    private EditText input(String hint,String value,java.util.function.Consumer<String> changed) {EditText e=new EditText(this);e.setSingleLine(true);e.setTextSize(15);e.setText(value);e.setHint(hint);e.setContentDescription(hint);e.setEnabled(!busy);watch(e,changed);return e;}
    private CheckBox check(String label,boolean checked,java.util.function.Consumer<Boolean> action) {CheckBox c=new CheckBox(this);c.setText(label);c.setTextColor(INK);c.setChecked(checked);c.setEnabled(!busy);c.setMinHeight(dp(48));c.setOnCheckedChangeListener((v,b)->action.accept(b));return c;}
    private void choice(LinearLayout parent,String label,String[] items,int current,java.util.function.IntConsumer changed) {
        parent.addView(text(label,13,MUTED));Spinner s=new Spinner(this);ArrayAdapter<String> a=new ArrayAdapter<>(this,android.R.layout.simple_spinner_dropdown_item,items);s.setAdapter(a);s.setSelection(Math.min(current,items.length-1));s.setContentDescription(label);s.setEnabled(!busy);s.setMinimumHeight(dp(48));parent.addView(s);
        s.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){boolean first=true;public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int i,long id){if(first){first=false;return;}changed.accept(i);savePreferences();}});
    }
    private void render() {
        int scrollPosition=mainScroll!=null && renderedTab==tab?mainScroll.getScrollY():0;renderedTab=tab;virtualRows=null;
        screen=column();screen.setBackgroundColor(INK);
        // Apply system bars and keyboard insets on Android's mandatory edge-to-edge layout.
        screen.setOnApplyWindowInsetsListener((v,insets)-> { if(Build.VERSION.SDK_INT>=30){android.graphics.Insets bars=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.ime());v.setPadding(bars.left,bars.top,bars.right,bars.bottom);}else v.setPadding(insets.getSystemWindowInsetLeft(),insets.getSystemWindowInsetTop(),insets.getSystemWindowInsetRight(),insets.getSystemWindowInsetBottom());return insets;});
        LinearLayout header=row();header.setPadding(dp(16),dp(10),dp(12),dp(10));header.setBackgroundColor(INK);
        LinearLayout brand=column();TextView title=text("Studio playlist",23,Color.WHITE);title.setTypeface(null,Typeface.BOLD);brand.addView(title);brand.addView(text(l("Twoja muzyka. Twoja kolejność.","Your music. Your order."),12,0xffa8e5c6));header.addView(brand,new LinearLayout.LayoutParams(0,-2,1));
        Button language=button(english?"PL":"EN",()->{english=!english;query="";savePreferences();render();});language.setTextColor(Color.WHITE);language.setBackground(shape(GREEN,10));header.addView(language,new LinearLayout.LayoutParams(dp(58),dp(52)));screen.addView(header);
        LinearLayout tabs=row();tabs.setBackgroundColor(BG);String[] names={l("Tworzenie","Create"),l("Zapisane","Saved"),l("Edytor","Editor")};for(int i=0;i<3;i++){final int n=i;Button b=button(names[i],()->{tab=n;query="";render();});if(i==tab)b.setBackground(shape(0xffd9eee3,8));tabs.addView(b,new LinearLayout.LayoutParams(0,dp(52),1));}screen.addView(tabs);
        LinearLayout status=column();status.setBackgroundColor(BG);status.setPadding(dp(16),0,dp(16),0);progressLabel=text(busy?progress:l("Lokalnie • bez konta i reklam","Local • no account or ads"),12,MUTED);status.addView(progressLabel);
        if(busy){ProgressBar p=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);p.setIndeterminate(true);status.addView(p);Button stop=new Button(this);stop.setText(l("Anuluj operację","Cancel operation"));stop.setOnClickListener(v->{cancel.set(true);stop.setEnabled(false);});status.addView(stop);}screen.addView(status);
        ScrollView scroll=new ScrollView(this);mainScroll=scroll;scroll.setBackgroundColor(BG);scroll.setFillViewport(true);body=column();body.setPadding(dp(16),dp(4),dp(16),dp(24));body.setGravity(Gravity.TOP|Gravity.CENTER_HORIZONTAL);scroll.addView(body);screen.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        if(tab==0)renderCreate();else if(tab==1)renderSaved();else renderEditor();
        LinearLayout footer=row();footer.setBackgroundColor(BG);footer.setPadding(dp(12),0,dp(12),0);footer.addView(button(l("Raport","Report"),()->showText(l("Raport","Report"),report.trim().isEmpty()?l("Brak operacji.","No operations yet."):report)),new LinearLayout.LayoutParams(0,-2,1));footer.addView(button(l("Narzędzia","Tools"),this::studio),new LinearLayout.LayoutParams(0,-2,1));footer.addView(button(l("O aplikacji","About"),this::about),new LinearLayout.LayoutParams(0,-2,1));screen.addView(footer);
        setContentView(screen);screen.requestApplyInsets();scroll.post(()->scroll.scrollTo(0,scrollPosition));
    }
    private void folderCard() {
        LinearLayout c=card();c.addView(heading(l("Biblioteka muzyki","Music library")));c.addView(text(library==null?l("Wskaż folder z muzyką","Choose a music folder"):library.root.name+" · "+library.tracks.size()+l(" utworów"," tracks"),15,MUTED));
        buttons(c,button(l("Wybierz folder","Choose folder"),()->guard(()->{Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION|Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);if(!tree.isEmpty())intent.putExtra(DocumentsContract.EXTRA_INITIAL_URI,Uri.parse(tree));startActivityForResult(intent,TREE);})),button(l("Odśwież","Refresh"),this::scan));
        c.addView(check(l("Wczytaj podkatalogi","Include subfolders"),recursive,b->{recursive=b;savePreferences();}));c.addView(check(l("Odczytaj tagi i czas (wolniejsze)","Read tags and duration (slower)"),tags,b->{tags=b;savePreferences();}));
        c.addView(text(l("Po zmianie opcji skanowania wybierz Odśwież.","Refresh after changing scan options."),12,MUTED));
    }
    private void renderCreate() {
        folderCard();LinearLayout c=card();c.addView(heading(l("Ustawienia playlist","Playlist settings")));
        choice(c,l("Miejsce zapisu","Save location"),new String[]{l("Osobno w albumach / folderach","One per album / folder"),l("Jedna w katalogu głównym","One in the root folder"),l("Jedna w podkatalogu","One in a subfolder")},mode,i->{mode=i;render();});
        if(mode==0)c.addView(text(l("Nazwa playlisty = nazwa folderu / albumu","Playlist name = folder / album name"),14,GREEN));else{c.addView(input(l("Nazwa playlisty","Playlist name"),autoName,s->{autoName=s;savePreferences();}));if(mode==2)c.addView(input(l("Podkatalog docelowy","Destination subfolder"),subfolder,s->{subfolder=s;savePreferences();}));}
        choice(c,l("Format zapisu","Playlist format"),PlaylistCodec.FORMATS,autoFormat,i->autoFormat=i);
        choice(c,l("Istniejące playlisty","Existing playlists"),new String[]{l("Pomiń istniejące","Skip existing"),l("Aktualizuj z kopią .bak","Update with .bak backup"),l("Nowa wersja (2), (3)…","New version (2), (3)…")},policy,i->policy=i);
        choice(c,l("Kolejność utworów","Track order"),new String[]{l("Nazwa pliku (naturalnie)","File name (natural)"),l("Wykonawca i tytuł","Artist and title")},sort,i->sort=i);
        c.addView(check(l("Ścieżki względne (przenośne)","Relative paths (portable)"),relative,b->{relative=b;savePreferences();}));
        c.addView(text(l("Formaty muzyki","Audio extensions"),13,MUTED));LinearLayout r=null;int n=0;for(String ext:PlaylistCodec.AUDIO){if(n++%3==0){r=row();c.addView(r);}CheckBox cb=check(ext.toUpperCase(Locale.ROOT),extensions.contains(ext),b->{if(b)extensions.add(ext);else extensions.remove(ext);savePreferences();});r.addView(cb,new LinearLayout.LayoutParams(0,-2,1));}
        LinearLayout albums=card();albums.addView(heading(l("Albumy i foldery","Albums and folders")));search(albums);buttons(albums,button(l("Wszystkie","Select all"),()->{if(library!=null)for(Storage.Entry e:library.folders)if(matches(e.path))selected.add(e.uri);drawAlbums();}),button(l("Żaden","Select none"),()->{if(library!=null)for(Storage.Entry e:library.folders)if(matches(e.path))selected.remove(e.uri);drawAlbums();}));contentRows=column();albums.addView(contentRows);drawAlbums();
        buttons(body,button(l("Podgląd przed zapisem","Preview before saving"),this::previewPlans));
    }
    private void search(LinearLayout parent) {EditText e=input(l("Szukaj…","Search…"),query,s->{query=s;if(pendingSearch!=null)ui.removeCallbacks(pendingSearch);pendingSearch=()->{if(tab==0)drawAlbums();else if(tab==1)drawSaved();else drawEditor();};ui.postDelayed(pendingSearch,250);});parent.addView(e);}
    private boolean matches(String value){return value.toLowerCase(english?Locale.ENGLISH:Locale.forLanguageTag("pl")).contains(query.toLowerCase(english?Locale.ENGLISH:Locale.forLanguageTag("pl")));}
    private void drawAlbums() {
        if(contentRows==null)return;
        Map<String,List<Track>> grouped=groups();List<Storage.Entry> rows=new ArrayList<>();
        if(library!=null)for(Storage.Entry e:library.folders)if(grouped.containsKey(e.path) && matches(e.path+" "+e.name))rows.add(e);
        virtual(rows,(box,e,index)->{LinearLayout r=row();r.addView(check(e.name+" · "+grouped.get(e.path).size(),selected.contains(e.uri),b->{if(b)selected.add(e.uri);else selected.remove(e.uri);savePreferences();}),new LinearLayout.LayoutParams(0,-2,1));r.addView(button(l("Zobacz","View"),()->showTracks(e.name,grouped.get(e.path))),new LinearLayout.LayoutParams(dp(80),dp(52)));box.addView(r);box.addView(text(e.path.isEmpty()?"/":e.path,12,MUTED));});
    }
    private Map<String,List<Track>> groups() {Map<String,List<Track>> map=new LinkedHashMap<>();if(library!=null)for(Track t:library.tracks){int i=t.path.lastIndexOf('/');String parent=i<0?"":t.path.substring(0,i);map.computeIfAbsent(parent,k->new ArrayList<>()).add(t);}return map;}
    private void renderSaved() {
        folderCard();LinearLayout c=card();c.addView(heading(l("Zapisane playlisty","Saved playlists")));c.addView(text(l("Zmiana nazwy i usuwanie dotyczą tylko plików playlist.","Rename and delete affect playlist files only."),13,MUTED));search(c);contentRows=column();c.addView(contentRows);drawSaved();
    }
    private void drawSaved() {
        if(contentRows==null)return;List<Storage.Entry> rows=new ArrayList<>();if(library!=null)for(Storage.Entry e:library.playlists)if(matches(e.path))rows.add(e);
        virtual(rows,(box,e,index)->{box.addView(text(e.name,17,INK));box.addView(text(e.parentPath.isEmpty()?"/":e.parentPath,12,MUTED));buttons(box,button(l("Otwórz","Open"),()->guard(()->open(e))),button(l("Zmień nazwę","Rename"),()->rename(e)),button(l("Usuń","Delete"),()->delete(e)));});
    }
    private void renderEditor() {
        LinearLayout c=card();c.addView(heading(l("Edytor i podgląd","Editor and preview")+(dirty?" *":"")));
        c.addView(text(editor.size()+l(" utworów • "," tracks • ")+(opened==null?l("nowa lista","new playlist"):opened.name),13,MUTED));
        buttons(c,button(l("Nowa","New"),()->guard(()->{editor.clear();opened=null;importedRaw="";undo.clear();redo.clear();dirty=false;focused=-1;playlistName=l("Moja playlista","My playlist");persistDraft();render();})),button(l("Otwórz plik","Open file"),()->guard(()->{Intent i=new Intent(Intent.ACTION_OPEN_DOCUMENT);i.setType("*/*");i.addCategory(Intent.CATEGORY_OPENABLE);i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);startActivityForResult(i,OPEN);})),button(l("Dodaj utwory","Add tracks"),()->{Intent i=new Intent(Intent.ACTION_OPEN_DOCUMENT);i.setType("*/*");i.addCategory(Intent.CATEGORY_OPENABLE);i.putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true);i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);startActivityForResult(i,ADD);}));
        c.addView(input(l("Nazwa playlisty","Playlist name"),playlistName,s->{if(!s.equals(playlistName)){playlistName=s;dirty=true;}}));
        choice(c,l("Format zapisu","Playlist format"),PlaylistCodec.FORMATS,format,i->{format=i;dirty=true;});c.addView(check(l("Ścieżki względne","Relative paths"),editorRelative,b->{editorRelative=b;dirty=true;}));
        buttons(c,button(l("Zapisz w bibliotece","Save in library"),()->saveEditor(null)),button(l("Eksportuj jako…","Export as…"),this::export));
        c.addView(text(l("Względne ścieżki najlepiej współpracują z odtwarzaczami. Zewnętrzne dokumenty mogą używać content:// — ich obsługa zależy od odtwarzacza.","Relative paths offer the best player compatibility. External documents may use content:// references; support depends on the player."),12,MUTED));
        buttons(c,button(l("Cofnij","Undo"),()->history(undo,redo)),button(l("Ponów","Redo"),()->history(redo,undo)),button(l("Podgląd","Preview"),this::previewEditor));
        buttons(c,button(l("Sprawdź","Check"),this::validateEditor),button(l("Sortuj","Sort"),()->{checkpoint();order(editor);changed();}),button(l("Bez duplikatów","Deduplicate"),()->{checkpoint();Set<String> seen=new HashSet<>();editor.removeIf(t->!seen.add(identity(t)));changed();}));
        buttons(c,button(l("Usuń brakujące wpisy","Remove missing entries"),this::removeMissing),button(l("Napraw ścieżki","Repair paths"),this::repairPaths));
        if(!importedRaw.isEmpty())c.addView(text(l("Import zachowuje utwory, tytuły i czasy. Zapis odbudowuje plik; komentarze i niestandardowe metadane mogą zostać pominięte. Aktualizacja tworzy kopię .bak.","Import keeps tracks, titles and durations. Saving rebuilds the file; comments and custom metadata may be omitted. Updating creates a .bak backup."),12,MUTED));
        LinearLayout rows=card();search(rows);contentRows=column();rows.addView(contentRows);drawEditor();
    }
    private interface BindRow<T>{void bind(LinearLayout box,T value,int index);}
    private <T> void virtual(List<T> values,BindRow<T> bind) {
        if(values.isEmpty()){contentRows.removeAllViews();virtualRows=null;contentRows.addView(text(l("Brak pasujących pozycji.","No matching items."),14,MUTED));return;}
        if(virtualRows==null || virtualRows.getParent()!=contentRows){contentRows.removeAllViews();virtualRows=new ListView(this);virtualRows.setNestedScrollingEnabled(true);contentRows.addView(virtualRows,new LinearLayout.LayoutParams(-1,dp(420)));}
        int first=virtualRows.getFirstVisiblePosition();View top=virtualRows.getChildAt(0);int offset=top==null?0:top.getTop();
        virtualRows.setAdapter(new BaseAdapter(){public int getCount(){return values.size();}public T getItem(int p){return values.get(p);}public long getItemId(int p){return p;}public View getView(int p,View recycled,ViewGroup parent){LinearLayout box=recycled instanceof LinearLayout?(LinearLayout)recycled:column();box.removeAllViews();box.setPadding(dp(8),dp(6),dp(8),dp(8));bind.bind(box,values.get(p),p);return box;}});
        if(first<values.size())virtualRows.setSelectionFromTop(first,offset);
    }
    private void drawEditor() {
        if(contentRows==null)return;List<Integer> rows=new ArrayList<>();for(int i=0;i<editor.size();i++){Track t=editor.get(i);if(matches(t.label()+" "+t.artist+" "+t.location))rows.add(i);}
        virtual(rows,(box,index,position)->{Track t=editor.get(index);box.setBackground(shape(index==focused?0xffe0eee6:Color.WHITE,10));TextView label=text((index+1)+". "+t.label()+(t.missing?l(" [brak]"," [missing]"):""),16,INK);label.setOnClickListener(v->{focused=index;drawEditor();});box.addView(label);TextView path=text(t.location,11,MUTED);path.setMaxLines(2);box.addView(path);
            if(!t.artist.isEmpty() || t.duration>=0)box.addView(text(t.artist+(t.duration>=0?" · "+(int)t.duration/60+":"+String.format(Locale.ROOT,"%02d",(int)t.duration%60):""),12,MUTED));
            if(index==focused)buttons(box,button("↑",()->move(index,-1)),button("↓",()->move(index,1)),button(l("Usuń wpis","Remove entry"),()->{checkpoint();editor.remove((int)index);focused=Math.min(index,editor.size()-1);changed();}));
        });
    }
    private void move(int i,int delta){int j=i+delta;if(j<0 || j>=editor.size())return;checkpoint();Collections.swap(editor,i,j);focused=j;changed();}
    private void checkpoint(){undo.addLast(copy(editor));while(undo.size()>30)undo.removeFirst();redo.clear();}
    private void changed(){dirty=true;persistDraft();render();}
    private void history(Deque<List<Track>> from,Deque<List<Track>> to){if(from.isEmpty())return;to.addLast(copy(editor));editor=from.removeLast();focused=-1;changed();}
    private void order(List<Track> tracks){tracks.sort((a,b)->PlaylistCodec.NATURAL.compare(sort==0?PlaylistCodec.basename(a.location):a.artist+" "+a.label(),sort==0?PlaylistCodec.basename(b.location):b.artist+" "+b.label()));}
    private String identity(Track t){return t.location;}
    private void guard(Runnable action) {
        if(!dirty){action.run();return;}
        editorPrompt=new AlertDialog.Builder(this).setTitle(l("Niezapisane zmiany","Unsaved changes")).setMessage(l("Zapisać zmiany w edytorze?","Save changes in the editor?"))
            .setPositiveButton(l("Zapisz","Save"),(d,w)->saveEditor(action)).setNegativeButton(l("Odrzuć","Discard"),(d,w)->{dirty=false;editor.clear();opened=null;importedRaw="";undo.clear();redo.clear();focused=-1;persistDraft();action.run();}).setNeutralButton(l("Anuluj","Cancel"),null).show();
    }
    // Android 13+ uses the platform back dispatcher registered in onCreate; older devices use this fallback.
    @android.annotation.SuppressLint("GestureBackNavigation")
    @Override public void onBackPressed(){handleBack();}
    private void handleBack(){if(busy){cancel.set(true);return;}guard(()->{persistDraft();finish();});}
    private void error(Throwable ex){new AlertDialog.Builder(this).setTitle(l("Nie udało się wykonać operacji","Operation failed")).setMessage(ex.getMessage()==null?ex.toString():ex.getMessage()).setPositiveButton("OK",null).show();}
    private void toast(String text){Toast.makeText(this,text,Toast.LENGTH_LONG).show();}
    private void showText(String title,String value){TextView t=text(value,14,INK);t.setPadding(dp(20),dp(12),dp(20),dp(12));t.setTextIsSelectable(true);ScrollView s=new ScrollView(this);s.addView(t);new AlertDialog.Builder(this).setTitle(title).setView(s).setPositiveButton("OK",null).show();}
    private void showTracks(String title,List<Track> tracks){StringBuilder b=new StringBuilder();int n=0;for(Track t:tracks){b.append(++n).append(". ").append(t.label()).append('\n');if(n>=1000){b.append("…");break;}}showText(title,b.toString());}
    private interface Work<T>{T run(AtomicBoolean stop) throws Exception;}
    private <T> void task(String label,Work<T> work,java.util.function.Consumer<T> done) {
        if(busy)return;busy=true;progress=label;cancel=new AtomicBoolean();AtomicBoolean stop=cancel;render();
        io.execute(()-> {T result=null;Exception failure=null;try{result=work.run(stop);}catch(Exception ex){failure=ex;}T r=result;Exception e=failure;
            ui.post(()->{if(isDestroyed())return;busy=false;progress="";if(e==null){try{done.accept(r);persistDraft();}catch(Exception ex){report=ex.toString();render();error(ex);}}else{report+="\n"+(e instanceof CancellationException?l("Anulowano. Zakończone zapisy pozostają na dysku.","Cancelled. Completed writes remain on disk."):e.toString());render();if(!(e instanceof CancellationException))error(e);} });
        });
    }
    private void setProgress(String value){ui.post(()->{if(progressLabel!=null)progressLabel.setText(value);});}
    private void scan() {
        if(tree.isEmpty()){toast(l("Najpierw wybierz folder.","Choose a folder first."));return;}
        final String root=tree;final boolean sub=recursive,meta=tags;Set<String> previous=new HashSet<>(selected);boolean keep=root.equals(prefs.getString("scannedTree",""));
        task(l("Skanowanie…","Scanning…"),stop->storage.scan(root,sub,meta,stop,this::setProgress),result->{library=result;selected.clear();for(Storage.Entry e:library.folders)if(!keep || previous.contains(e.uri))selected.add(e.uri);prefs.edit().putString("scannedTree",root).apply();String summary=l("Skanowanie: ","Scanned: ")+result.tracks.size()+l(" utworów, "," tracks, ")+result.playlists.size()+l(" playlist."," playlists.")+"\n"+String.join("\n",result.warnings);report=report+"\n"+summary;if(report.length()>200000)report=report.substring(report.length()-200000);render();});
    }
    private static class Plan {Storage.Entry folder;String name;List<Track> tracks;Plan(Storage.Entry f,String n,List<Track> t){folder=f;name=n;tracks=t;}}
    private List<Plan> plans() {
        if(library==null)throw new IllegalArgumentException(l("Wybierz i wczytaj folder.","Choose and scan a folder."));
        List<Plan> plans=new ArrayList<>();Map<String,List<Track>> groups=groups();List<Track> combined=new ArrayList<>();String extension="."+PlaylistCodec.FORMATS[autoFormat].toLowerCase(Locale.ROOT);
        for(Storage.Entry folder:library.folders)if(selected.contains(folder.uri)) {List<Track> tracks=new ArrayList<>();for(Track t:groups.getOrDefault(folder.path,Collections.emptyList()))if(extensions.contains(PlaylistCodec.ext(t.path)))tracks.add(t.copy());if(tracks.isEmpty())continue;order(tracks);if(mode==0){PlaylistCodec.name(folder.name);plans.add(new Plan(folder,folder.name+extension,tracks));}else combined.addAll(tracks);}
        if(mode!=0 && !combined.isEmpty()){String name=PlaylistCodec.name(autoName)+extension;Storage.Entry folder=library.root;if(mode==2){String dir=PlaylistCodec.name(subfolder);folder=new Storage.Entry("",dir,dir,library.root.uri,"",true,0);}order(combined);plans.add(new Plan(folder,name,combined));}
        return plans;
    }
    private void previewPlans() {
        try {List<Plan> plans=plans();if(plans.isEmpty()){toast(l("Brak utworów dla wybranych albumów i formatów.","No tracks match the selected albums and formats."));return;}
            StringBuilder b=new StringBuilder();int total=0;for(Plan p:plans){b.append(Storage.join(p.folder.path,p.name)).append(" · ").append(p.tracks.size()).append('\n');total+=p.tracks.size();}
            b.append('\n').append(total).append(l(" utworów.\nUkryte przez wyszukiwanie, zaznaczone albumy również są uwzględniane."," tracks.\nSelected albums hidden by search are also included."));
            String initial=b.toString();if(policy==1)task(l("Porównywanie…","Comparing…"),stop->{StringBuilder differences=new StringBuilder(initial);for(Plan plan:plans){Storage.check(stop);if(!plan.folder.uri.isEmpty()){Storage.Entry existing=storage.find(plan.folder,plan.name,stop);if(existing!=null)differences.append("\n\n").append(plan.name).append("\n").append(difference(plan.folder,plan.name,plan.tracks,stop).summary(english));}}return differences.toString();},summary->{render();showPlanConfirmation(plans,summary);});else showPlanConfirmation(plans,initial);
        }catch(Exception ex){error(ex);}
    }
    private void showPlanConfirmation(List<Plan> plans,String summary){new AlertDialog.Builder(this).setTitle(l("Podgląd przed zapisem","Preview before saving")).setMessage(summary).setPositiveButton(l("Utwórz playlisty","Create playlists"),(d,w)->generate(plans)).setNegativeButton(l("Anuluj","Cancel"),null).show();}
    private void generate(List<Plan> plans) {
        final String fmt=PlaylistCodec.FORMATS[autoFormat];final boolean rel=relative;final int existingPolicy=policy;final Storage.Library snapshot=library;
        report=l("Tworzenie playlist:\n","Creating playlists:\n");
        task(l("Zapisywanie…","Saving…"),stop->{List<String> lines=new ArrayList<>();for(Plan p:plans){Storage.check(stop);Storage.Entry parent=p.folder;if(parent.uri.isEmpty())parent=storage.subfolder(snapshot.root,parent.name,stop);setProgress(p.name);
            try {String content=PlaylistCodec.serialize(p.tracks,fmt,PlaylistCodec.stem(p.name),parent.path,rel);String line=storage.save(parent,p.name,StudioTools.encode(content,fmt.equals("M3U")?autoEncoding:"UTF-8"),existingPolicy,stop);lines.add(line);ui.post(()->report+=line+"\n");}catch(CancellationException ex){throw ex;}catch(Exception ex){String line="ERROR "+p.name+": "+ex.getMessage();lines.add(line);ui.post(()->report+=line+"\n");}}
            return String.join("\n",lines);},result->{report=result;render();showText(l("Wynik zapisu","Save results"),result);scan();});
    }
    private void open(Storage.Entry e) {
        final Storage.Library snapshot=library;
        task(l("Odczytywanie playlisty…","Reading playlist…"),stop->{Storage.check(stop);String text=PlaylistCodec.decode(storage.read(e.uri));List<Track> rows=storage.resolve(PlaylistCodec.parse(text,PlaylistCodec.ext(e.name),stop),e,snapshot);return new ReadResult(text,rows);},result->{
            importedRaw=result.text;editor=result.tracks;opened=e;playlistName=PlaylistCodec.stem(e.name);format=Arrays.asList(PlaylistCodec.FORMATS).indexOf(PlaylistCodec.ext(e.name).toUpperCase(Locale.ROOT));dirty=false;undo.clear();redo.clear();tab=2;focused=-1;query="";render();
        });
    }
    private void rename(Storage.Entry entry) {
        EditText name=input(l("Nowa nazwa bez rozszerzenia","New name without extension"),PlaylistCodec.stem(entry.name),s->{});
        new AlertDialog.Builder(this).setTitle(l("Zmień nazwę playlisty","Rename playlist")).setView(name).setPositiveButton(l("Zmień nazwę","Rename"),(d,w)->task(l("Zmiana nazwy…","Renaming…"),stop->storage.rename(entry,name.getText().toString(),stop),e->{if(opened!=null && opened.uri.equals(entry.uri)){opened=e;playlistName=PlaylistCodec.stem(e.name);}render();scan();})).setNegativeButton(l("Anuluj","Cancel"),null).show();
    }
    private void delete(Storage.Entry entry) {
        new AlertDialog.Builder(this).setTitle(l("Usunąć playlistę?","Delete playlist?"))
            .setMessage(entry.name+"\n\n"+l("Plik playlisty zostanie trwale usunięty. Muzyka pozostanie bez zmian.","The playlist file will be permanently deleted. Music files remain unchanged."))
            .setPositiveButton(l("Usuń playlistę","Delete playlist"),(d,w)->task(l("Usuwanie…","Deleting…"),stop->{Storage.check(stop);storage.deletePlaylist(entry);return entry;},e->{if(opened!=null && opened.uri.equals(e.uri)){opened=null;dirty=true;}render();scan();})).setNegativeButton(l("Anuluj","Cancel"),null).show();
    }
    private Storage.Entry editorFolder(){if(opened!=null && !opened.parentUri.isEmpty())return new Storage.Entry(opened.parentUri,"",opened.parentPath,"","",true,0);return library==null?null:library.root;}
    private void saveEditor(Runnable continuation) {
        if(library==null && editorFolder()==null){toast(l("Wybierz folder w zakładce Tworzenie lub użyj Eksportuj jako.","Choose a folder in Create or use Export as."));return;}
        Storage.Entry folder=editorFolder();String name;try{name=PlaylistCodec.name(playlistName)+"."+PlaylistCodec.FORMATS[format].toLowerCase(Locale.ROOT);}catch(Exception ex){error(ex);return;}
        String finalName=name;List<Track> rows=copy(editor);String fmt=PlaylistCodec.FORMATS[format];boolean rel=editorRelative;
        Runnable save=()->task(l("Zapisywanie playlisty…","Saving playlist…"),stop->{String value=PlaylistCodec.serialize(rows,fmt,PlaylistCodec.stem(finalName),folder.path,rel);String result=storage.save(folder,finalName,StudioTools.encode(value,fmt.equals("M3U")?editorEncoding:"UTF-8"),1,stop);Storage.Entry saved=storage.find(folder,finalName,new AtomicBoolean());return new SaveResult(result,saved);},result->{opened=result.entry;dirty=false;importedRaw="";report=result.message;if(library!=null && opened!=null && library.folders.stream().anyMatch(f->f.uri.equals(folder.uri))){Storage.Entry saved=opened;library.playlists.removeIf(e->e.uri.equals(saved.uri) || (e.parentUri.equals(saved.parentUri) && e.name.equals(saved.name)));library.playlists.add(saved);library.playlists.sort((a,b)->PlaylistCodec.NATURAL.compare(a.path,b.path));}render();toast(l("Zapisano playlistę.","Playlist saved."));if(continuation!=null)continuation.run();});
        showSaveDifference(folder,finalName,rows,save);
    }
    private void previewEditor(){Storage.Entry folder=editorFolder();List<Track> rows=copy(editor);String fmt=PlaylistCodec.FORMATS[format],name=playlistName;boolean rel=editorRelative;task(l("Podgląd…","Preview…"),stop->PlaylistCodec.serialize(rows,fmt,name,folder==null?"":folder.path,rel),value->{render();showText(l("Podgląd pliku","File preview"),value);});}
    private void export() {
        try{String base=PlaylistCodec.name(playlistName);String fmt=PlaylistCodec.FORMATS[format];List<Track> rows=copy(editor);exportName=base+"."+fmt.toLowerCase(Locale.ROOT);
            new AlertDialog.Builder(this).setTitle(l("Eksport playlisty","Export playlist")).setMessage(l("Eksport zapisuje nowy plik z bezwzględnymi ścieżkami lub adresami content://. Dla przenośnej listy użyj Zapisz w bibliotece. Komentarze i dodatkowe metadane importu nie są zachowywane.","Export creates a new file with absolute paths or content:// references. For portable relative paths, use Save in library. Imported comments and custom metadata are not preserved."))
                .setPositiveButton(l("Wybierz miejsce","Choose destination"),(d,w)->task(l("Przygotowanie eksportu…","Preparing export…"),stop->{
                    byte[] bytes=StudioTools.encode(PlaylistCodec.serialize(rows,fmt,base,"",false),fmt.equals("M3U")?editorEncoding:"UTF-8");if(bytes.length>32*1024*1024)throw new IOException("Export exceeds 32 MB");
                    android.util.AtomicFile pending=new android.util.AtomicFile(new File(getFilesDir(),"pending-export"));FileOutputStream out=pending.startWrite();try{out.write(bytes);pending.finishWrite(out);}catch(Exception ex){pending.failWrite(out);throw ex;}
                    prefs.edit().putString("pendingExportName",exportName).commit();return exportName;
                },name->{Intent i=new Intent(Intent.ACTION_CREATE_DOCUMENT);i.setType("application/octet-stream");i.addCategory(Intent.CATEGORY_OPENABLE);i.putExtra(Intent.EXTRA_TITLE,name);startActivityForResult(i,EXPORT);})).setNegativeButton(l("Anuluj","Cancel"),null).show();
        }catch(Exception ex){error(ex);}
    }
    private void validateEditor() {
        List<Track> rows=copy(editor);task(l("Sprawdzanie…","Checking…"),stop->{Set<String> seen=new HashSet<>();int duplicates=0,missing=0;for(Track t:rows){Storage.check(stop);if(!seen.add(identity(t)))duplicates++;if(!t.uri.isEmpty()){try{storage.describe(Uri.parse(t.uri));t.missing=false;}catch(Exception ex){t.missing=true;}}if(t.missing)missing++;}return new HealthResult(rows,duplicates,missing);},result->{checkpoint();editor=result.tracks;report=l("Utwory: ","Tracks: ")+editor.size()+l("\nDuplikaty: ","\nDuplicates: ")+result.duplicates+l("\nBrakujące lub poza dostępem: ","\nMissing or inaccessible: ")+result.missing;render();showText(l("Kontrola playlisty","Playlist check"),report);});
    }
    private void removeMissing() {
        List<Track> rows=copy(editor);
        task(l("Sprawdzanie dostępności…","Checking availability…"),stop->{for(Track t:rows){Storage.check(stop);if(!t.uri.isEmpty()){try{storage.describe(Uri.parse(t.uri));t.missing=false;}catch(Exception ex){t.missing=true;}}}return rows;},result->{
            int count=0;for(Track t:result)if(t.missing)count++;final int missing=count;
            new AlertDialog.Builder(this).setTitle(l("Usuń brakujące wpisy","Remove missing entries")).setMessage(missing+l(" wpisów niedostępnych. Usunąć je z edytora? Pliki muzyczne nie zostaną usunięte."," unavailable entries. Remove them from the editor? Music files will not be deleted."))
                .setPositiveButton(l("Usuń wpisy","Remove entries"),(d,w)->{checkpoint();editor=result;editor.removeIf(t->t.missing);focused=-1;changed();}).setNegativeButton(l("Anuluj","Cancel"),null).show();render();
        });
    }
    private void repairPaths() {
        if(library==null){toast(l("Najpierw wczytaj nowy folder z muzyką.","Scan the new music folder first."));return;}
        EditText old=input(l("Stary katalog, np. /storage/emulated/0/OldMusic","Old folder, e.g. /storage/emulated/0/OldMusic"),"",s->{});
        new AlertDialog.Builder(this).setTitle(l("Napraw ścieżki","Repair paths")).setMessage(l("Wpisz stary prefiks ścieżek. Nowym katalogiem jest wybrana biblioteka: ","Enter the old path prefix. The new folder is the selected library: ")+library.root.name).setView(old)
            .setPositiveButton(l("Napraw","Repair"),(d,w)->{String prefix=PlaylistCodec.normalize(old.getText().toString().trim());if(prefix.isEmpty() || prefix.equals("/")){toast(l("Podaj pełny stary katalog.","Enter the full old folder."));return;}
                List<Track> rows=copy(editor);Storage.Library snapshot=library;
                task(l("Naprawianie…","Repairing…"),stop->{int repaired=0;for(Track t:rows){Storage.check(stop);boolean available=false;if(!t.uri.isEmpty())try{storage.describe(Uri.parse(t.uri));available=true;}catch(Exception ignored){}
                    String path=PlaylistCodec.normalize(t.location);if(!available && path.startsWith(prefix+"/")){String suffix=path.substring(prefix.length()+1);Track replacement=snapshot.byPath.get(suffix);if(replacement!=null){t.uri=replacement.uri;t.location=replacement.location;t.path=replacement.path;t.missing=false;repaired++;}}}return new RepairResult(rows,repaired);},result->{checkpoint();editor=result.tracks;dirty=true;report=l("Naprawione ścieżki: ","Repaired paths: ")+result.count;render();showText(l("Naprawa ścieżek","Path repair"),report);});
            }).setNegativeButton(l("Anuluj","Cancel"),null).show();
    }
    private void studio() {
        String[] labels={l("Kopie zapasowe","Backups"),l("Porównaj z zapisanym plikiem","Compare with saved file"),l("Profil odtwarzacza","Player profile"),l("Inteligentna playlista","Smart playlist"),l("Identyczne pliki audio","Identical audio files"),l("ZIP z muzyką","ZIP with music"),l("Odsłuch zaznaczonego utworu","Preview selected track"),l("Odczytaj wszystkie tagi od nowa","Read all tags again")};
        new AlertDialog.Builder(this).setTitle(l("Narzędzia","Tools")).setItems(labels,(d,w)->{switch(w){case 0:backups();break;case 1:compareSaved();break;case 2:profile();break;case 3:smartRule();break;case 4:audioDuplicates();break;case 5:portable();break;case 6:audioPreview();break;case 7:task(l("Odświeżanie tagów…","Refreshing tags…"),stop->{android.util.AtomicFile file=new android.util.AtomicFile(new File(getFilesDir(),"metadata-cache.json"));FileOutputStream out=file.startWrite();try{out.write("{}".getBytes(StandardCharsets.UTF_8));file.finishWrite(out);}catch(Exception ex){file.failWrite(out);throw ex;}return true;},done->{tags=true;savePreferences();render();scan();});break;}}).setNegativeButton(l("Zamknij","Close"),null).show();
    }
    private StudioTools.Difference difference(Storage.Entry folder,String name,List<Track> rows,AtomicBoolean stop) throws Exception {
        Storage.Entry existing=storage.find(folder,name,stop);List<Track> before=existing==null?Collections.emptyList():storage.resolve(PlaylistCodec.parse(PlaylistCodec.decode(storage.read(existing.uri)),PlaylistCodec.ext(name),stop),existing,library);return StudioTools.compare(before,rows);
    }
    private void showSaveDifference(Storage.Entry folder,String name,List<Track> rows,Runnable save) {
        task(l("Porównywanie…","Comparing…"),stop->difference(folder,name,rows,stop),diff->{render();editorPrompt=new AlertDialog.Builder(this).setTitle(l("Zmiany przed zapisem","Changes before saving")).setMessage(name+"\n\n"+diff.summary(english)+"\n\n"+l("Poprzednia wersja zostanie zachowana jako .bak. Importowane dodatkowe metadane mogą zostać pominięte.","The previous version is preserved as .bak. Imported custom metadata may be omitted.")).setPositiveButton(l("Zapisz","Save"),(d,w)->save.run()).setNegativeButton(l("Anuluj","Cancel"),null).show();});
    }
    private void compareSaved(){if(opened==null || editorFolder()==null){toast(l("Najpierw otwórz playlistę.","Open a playlist first."));return;}List<Track> rows=copy(editor);Storage.Entry e=opened;task(l("Porównywanie…","Comparing…"),stop->difference(editorFolder(),e.name,rows,stop),diff->{render();showText(l("Różnice","Differences"),diff.summary(english));});}
    private void backups() {
        if(opened==null || opened.parentUri.isEmpty()){toast(l("Otwórz playlistę z biblioteki.","Open a playlist from the library."));return;}Storage.Entry target=opened,folder=editorFolder();
        task(l("Odczytywanie kopii…","Reading backups…"),stop->{List<Storage.Entry> result=new ArrayList<>();for(Storage.Entry e:storage.children(folder,stop))if(!e.directory && e.name.startsWith(target.name+".") && e.name.endsWith(".bak"))result.add(e);result.sort((a,b)->b.name.compareTo(a.name));return result;},entries->{render();if(entries.isEmpty()){toast(l("Brak kopii.","No backups."));return;}
            String[] names=entries.stream().map(e->e.name).toArray(String[]::new);new AlertDialog.Builder(this).setTitle(l("Kopie zapasowe","Backups")).setItems(names,(d,index)->{Storage.Entry chosen=entries.get(index);task(l("Podgląd kopii…","Previewing backup…"),stop->PlaylistCodec.decode(storage.read(chosen.uri)),value->{render();TextView text=text(value.length()>200000?value.substring(0,200000):value,14,INK);text.setPadding(dp(16),dp(8),dp(16),dp(8));ScrollView scroll=new ScrollView(this);scroll.addView(text);new AlertDialog.Builder(this).setTitle(chosen.name).setView(scroll).setPositiveButton(l("Przywróć","Restore"),(dialog,which)->guard(()->new AlertDialog.Builder(this).setTitle(l("Przywrócić wersję?","Restore version?")).setMessage(l("Aktualna wersja zostanie zachowana jako kolejna kopia.","The current version will be preserved as another backup.")).setPositiveButton(l("Przywróć","Restore"),(x,y)->task(l("Przywracanie…","Restoring…"),stop->{byte[] bytes=storage.read(chosen.uri);return storage.save(folder,target.name,bytes,1,stop);},result->{dirty=false;report=result;open(target);})).setNegativeButton(l("Anuluj","Cancel"),null).show())).setNegativeButton(l("Zamknij","Close"),null).show();});}).show();});
    }
    private void profile(){new AlertDialog.Builder(this).setTitle(l("Profil dla bieżącej zakładki","Profile for the current tab")).setMessage(l("Adresy content:// mogą nie działać w innych odtwarzaczach.","content:// references may not work in other players.")).setPositiveButton(l("Wybierz profil","Choose profile"),(d,w)->new AlertDialog.Builder(this).setItems(StudioTools.PROFILES,(x,i)->{if(tab==0){autoFormat=StudioTools.format(i);autoEncoding=i==1?"windows-1250":"UTF-8";relative=i!=3;}else{format=StudioTools.format(i);editorEncoding=i==1?"windows-1250":"UTF-8";editorRelative=i!=3;dirty=true;}savePreferences();persistDraft();render();}).show()).setNegativeButton(l("Anuluj","Cancel"),null).show();}
    private JSONArray rules(){try{return new JSONArray(prefs.getString("rules","[]"));}catch(JSONException ex){return new JSONArray();}}
    private void smartRule() {
        JSONArray saved=rules();List<String> names=new ArrayList<>();names.add(l("Nowa reguła","New rule"));for(int i=0;i<saved.length();i++)names.add(saved.optJSONObject(i).optString("name"));
        new AlertDialog.Builder(this).setTitle(l("Inteligentna playlista","Smart playlist")).setItems(names.toArray(new String[0]),(d,index)->editRule(index==0?new JSONObject():saved.optJSONObject(index-1))).show();
    }
    private void editRule(JSONObject previous){
        LinearLayout panel=column();panel.setPadding(dp(20),0,dp(20),0);String[] keys={"name","artist","album","extension","min","max"};String[] labels={l("Nazwa reguły","Rule name"),l("Wykonawca zawiera","Artist contains"),l("Album zawiera","Album contains"),l("Rozszerzenie, np. mp3","Extension, e.g. mp3"),l("Minimum sekund","Minimum seconds"),l("Maksimum sekund","Maximum seconds")};EditText[] inputs=new EditText[6];
        for(int i=0;i<6;i++){panel.addView(text(labels[i],12,MUTED));inputs[i]=input(labels[i],previous.optString(keys[i],""),s->{});panel.addView(inputs[i]);}ScrollView scroll=new ScrollView(this);scroll.addView(panel);
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(l("Reguła playlisty","Playlist rule")).setView(scroll).setPositiveButton(l("Zapisz i utwórz","Save and create"),null).setNegativeButton(l("Anuluj","Cancel"),null).create();dialog.setOnShowListener(d->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->{try{
            StudioTools.Rule rule=new StudioTools.Rule();rule.name=PlaylistCodec.name(inputs[0].getText().toString());rule.artist=inputs[1].getText().toString();rule.album=inputs[2].getText().toString();rule.extension=inputs[3].getText().toString();String min=inputs[4].getText().toString(),max=inputs[5].getText().toString();rule.min=min.isEmpty()?-1:Double.parseDouble(min.replace(',','.'));rule.max=max.isEmpty()?-1:Double.parseDouble(max.replace(',','.'));if(!Double.isFinite(rule.min)||!Double.isFinite(rule.max)||rule.min< -1||rule.max< -1||rule.min>=0&&rule.max>=0&&rule.min>rule.max)throw new IllegalArgumentException(l("Nieprawidłowy zakres czasu.","Invalid duration range."));
            JSONObject json=new JSONObject();for(int i=0;i<6;i++)json.put(keys[i],inputs[i].getText().toString());JSONArray next=new JSONArray(),all=rules();for(int i=0;i<all.length();i++)if(!all.getJSONObject(i).optString("name").equals(rule.name))next.put(all.getJSONObject(i));next.put(json);prefs.edit().putString("rules",next.toString()).apply();List<Track> source=copy(library==null?editor:library.tracks);dialog.dismiss();guard(()->{editor=new ArrayList<>();for(Track t:source)if(rule.matches(t))editor.add(t);opened=null;playlistName=rule.name;format=0;undo.clear();redo.clear();focused=-1;tab=2;changed();});
        }catch(Exception ex){error(ex);}}));dialog.show();
    }
    private void audioDuplicates(){List<Track> rows=copy(library==null?editor:library.tracks);task(l("Porównywanie zawartości…","Comparing content…"),stop->new StudioStorage(this).duplicates(rows,stop),groups->{StringBuilder b=new StringBuilder(l("Porównanie SHA-256 całych plików. Różne tagi mogą dawać różne wyniki. Nic nie usunięto.\nGrupy: ","Whole-file SHA-256 comparison. Different tags may produce different results. No files deleted.\nGroups: ")+groups.size()+"\n\n");for(List<String> group:groups)b.append(String.join("\n",group)).append("\n\n");report=b.toString();render();showText(l("Identyczne pliki","Identical files"),report);});}
    private void portable(){List<Track> rows=copy(editor);task(l("Przygotowanie ZIP…","Preparing ZIP…"),stop->new StudioStorage(this).portable(rows,stop),file->{render();Intent intent=new Intent(Intent.ACTION_CREATE_DOCUMENT);intent.setType("application/zip");intent.addCategory(Intent.CATEGORY_OPENABLE);intent.putExtra(Intent.EXTRA_TITLE,"Playlist.zip");startActivityForResult(intent,ZIP);});}
    private void audioPreview(){
        if(focused<0 || focused>=editor.size() || editor.get(focused).uri.isEmpty()){toast(l("Zaznacz dostępny utwór w edytorze.","Select an accessible track in the editor."));return;}stopPreview.run();Track t=editor.get(focused);TextView state=text(l("Przygotowanie…","Preparing…"),14,INK);LinearLayout panel=column();panel.setPadding(dp(20),dp(8),dp(20),dp(8));panel.addView(state);
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(t.label()+l(" — 30 sekund"," — 30 seconds")).setView(panel).setNegativeButton(l("Zamknij","Close"),null).create();dialog.setOnDismissListener(d->{stopPreview.run();ui.removeCallbacks(stopPreview);});
        try{previewPlayer=new android.media.MediaPlayer();android.media.MediaPlayer player=previewPlayer;player.setDataSource(this,Uri.parse(t.uri));player.setOnPreparedListener(p->{if(previewPlayer==p){state.setText(l("Gotowy","Ready"));}});player.setOnErrorListener((p,what,extra)->{state.setText(l("Nie można odtworzyć tego formatu.","This format cannot be played."));return true;});
            buttons(panel,button(l("Odtwórz","Play"),()->{try{if(previewPlayer!=null){previewPlayer.start();ui.postDelayed(stopPreview,30000);state.setText(l("Odtwarzanie","Playing"));}}catch(IllegalStateException ex){state.setText(l("Poczekaj na przygotowanie.","Wait until ready."));}}),button(l("Pauza","Pause"),()->{try{if(previewPlayer!=null){previewPlayer.pause();state.setText(l("Pauza","Paused"));}}catch(IllegalStateException ignored){}}),button("Stop",()->{stopPreview.run();state.setText(l("Zatrzymano. Otwórz odsłuch ponownie.","Stopped. Open preview again to restart."));}));player.prepareAsync();dialog.show();
        }catch(Exception ex){stopPreview.run();error(ex);}
    }
    private void retain(Intent result,Uri uri) {int flags=0;if((result.getFlags()&Intent.FLAG_GRANT_READ_URI_PERMISSION)!=0)flags|=Intent.FLAG_GRANT_READ_URI_PERMISSION;if((result.getFlags()&Intent.FLAG_GRANT_WRITE_URI_PERMISSION)!=0)flags|=Intent.FLAG_GRANT_WRITE_URI_PERMISSION;try{if(flags!=0)getContentResolver().takePersistableUriPermission(uri,flags);}catch(SecurityException ex){toast(l("Dostęp może być tymczasowy; wybierz plik ponownie po restarcie.","Access may be temporary; select the file again after restart."));}}
    @Override protected void onActivityResult(int request,int result,Intent data) {
        super.onActivityResult(request,result,data);if(result!=RESULT_OK || data==null)return;
        Uri uri=data.getData();
        if(request==TREE && uri!=null){retain(data,uri);if(!tree.equals(uri.toString())){for(Track t:editor)t.path="";opened=null;}boolean same=tree.equals(uri.toString());tree=uri.toString();if(!same){library=null;selected.clear();}savePreferences();scan();}
        else if(request==OPEN && uri!=null){retain(data,uri);task(l("Otwieranie…","Opening…"),stop->{Storage.Entry e=storage.describe(uri);if(!PlaylistCodec.isPlaylist(e.name))throw new IOException(l("Wybierz M3U8, M3U, PLS, XSPF lub WPL.","Choose M3U8, M3U, PLS, XSPF or WPL."));if(library!=null)for(Storage.Entry f:library.playlists)if(f.uri.equals(e.uri) || Storage.localPath(f.uri).equals(Storage.localPath(e.uri)))return f;return e;},this::open);}
        else if(request==ADD){List<Uri> uris=new ArrayList<>();if(data.getClipData()!=null)for(int i=0;i<data.getClipData().getItemCount();i++)uris.add(data.getClipData().getItemAt(i).getUri());else if(uri!=null)uris.add(uri);for(Uri u:uris)retain(data,u);
            final Storage.Library snapshot=library;final boolean readTags=tags;task(l("Dodawanie utworów…","Adding tracks…"),stop->{List<Track> rows=new ArrayList<>();for(Uri u:uris){Storage.check(stop);Storage.Entry e=storage.describe(u);if(!PlaylistCodec.AUDIO.contains(PlaylistCodec.ext(e.name)))continue;Track t=storage.track(e);if(snapshot!=null){Track known=snapshot.byLocation.get(t.location);if(known!=null)t=known.copy();}if(readTags)storage.readTags(t);rows.add(t);}return rows;},rows->{checkpoint();Set<String> seen=new HashSet<>();for(Track t:editor)seen.add(identity(t));for(Track t:rows)if(seen.add(identity(t)))editor.add(t);dirty=true;tab=2;query="";render();toast(rows.isEmpty()?l("Nie wybrano obsługiwanych plików audio.","No supported audio files selected."):l("Dodano utwory.","Tracks added."));});
        }else if(request==ZIP && uri!=null){task(l("Eksport ZIP…","Exporting ZIP…"),stop->{new StudioStorage(this).exportFile(new File(getFilesDir(),"pending-portable.zip"),uri.toString(),stop);return uri;},saved->{render();toast(l("Zapisano ZIP z muzyką.","ZIP with music saved."));});}else if(request==EXPORT && uri!=null){task(l("Eksportowanie…","Exporting…"),stop->{Storage.check(stop);byte[] bytes=new android.util.AtomicFile(new File(getFilesDir(),"pending-export")).readFully();storage.write(uri.toString(),bytes);return uri;},saved->{report=l("Wyeksportowano: ","Exported: ")+prefs.getString("pendingExportName","");render();toast(report);});}
    }
    private void about(){showText("Studio playlist · Android 1.1.0",l("Lokalny edytor i generator playlist.\n\nMuzyka: MP3, FLAC, WAV, M4A, AAC, OGG, OPUS, WMA, AIFF, AIF, APE, ALAC.\nPlaylisty: M3U8, M3U, PLS, XSPF, WPL.\n\nDostęp tylko do wybranych folderów i plików. Brak reklam, analityki i kont. Ustawienia oraz szkic są przechowywane lokalnie. Tagi muzyczne są tylko odczytywane. Usunięcie wpisu z listy nie usuwa muzyki. Usunięcie zapisanej playlisty jest trwałe i wymaga potwierdzenia.\n\nDomyślny zapis to UTF-8; profil Legacy używa M3U Windows-1250; odczyt starszych plików obsługuje Windows-1250. Import nie zachowuje wszystkich niestandardowych metadanych. Ścieżki content:// mogą nie działać w innych odtwarzaczach.\n\nMarek Zettel - zetmar · MIT\nhttps://github.com/zetmar-collab/Listy","Local playlist editor and generator.\n\nAudio: MP3, FLAC, WAV, M4A, AAC, OGG, OPUS, WMA, AIFF, AIF, APE, ALAC.\nPlaylists: M3U8, M3U, PLS, XSPF, WPL.\n\nAccess is limited to files and folders you choose. No advertising, analytics or accounts. Settings and draft stay on your device. Audio tags are read only. Removing a list entry does not delete music. Deleting a saved playlist is permanent and requires confirmation.\n\nDefault output is UTF-8; Legacy profile uses M3U Windows-1250; legacy reading supports Windows-1250. Import does not preserve all custom metadata. content:// references may not work in other players.\n\nMarek Zettel - zetmar · MIT\nhttps://github.com/zetmar-collab/Listy"));}
}
