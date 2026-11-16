package pl.zetmar.studioplaylist;

import static com.google.common.truth.Truth.assertThat;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.junit.runners.JUnit4;
import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;
import java.util.*;

@RunWith(JUnit4.class)
public class PlaylistCodecTest {
    private Track track(String path) {Track t=new Track("/storage/emulated/0/Music/"+path,"Zażółć & <świat>");t.path=path;t.duration=123;t.artist="Marek";return t;}
    @Test public void allFiveFormatsRoundTripPolishNames() throws Exception {
        for(String format:PlaylistCodec.FORMATS){String raw=PlaylistCodec.serialize(List.of(track("Album/01 Żółć & #.mp3")),format,"Album","Album",true);List<Track> result=PlaylistCodec.parse(raw,format);assertThat(result).hasSize(1);assertThat(result.get(0).location).isEqualTo("01 Żółć & #.mp3");if(!format.equals("WPL")){assertThat(result.get(0).title).isEqualTo("Zażółć & <świat>");assertThat(result.get(0).duration).isEqualTo(123);}}
    }
    @Test public void siblingPathsAreRebased() {assertThat(PlaylistCodec.relative("Playlisty","Jazz/CD1/01.mp3")).isEqualTo("../Jazz/CD1/01.mp3");assertThat(PlaylistCodec.relative("Jazz/CD1","Jazz/CD2/02.mp3")).isEqualTo("../CD2/02.mp3");}
    @Test public void rootDoesNotAddParentSegments(){assertThat(PlaylistCodec.relative("","Album/01.wav")).isEqualTo("Album/01.wav");}
    @Test public void leadingHashIsNotAComment() throws Exception {Track t=track("#intro.mp3");String raw=PlaylistCodec.serialize(List.of(t),"M3U8","Test","",true);assertThat(PlaylistCodec.parse(raw,"M3U8").get(0).location).isEqualTo("./#intro.mp3");}
    @Test public void relativeNormalizationKeepsUnresolvedParents(){assertThat(PlaylistCodec.normalize("Jazz/CD1/../../Pop/01.mp3")).isEqualTo("Pop/01.mp3");assertThat(PlaylistCodec.normalize("../../01.mp3")).isEqualTo("../../01.mp3");assertThat(PlaylistCodec.normalize("/../../01.mp3")).isEqualTo("/01.mp3");}
    @Test public void naturalOrderHandlesLongNumbers(){List<String> values=new ArrayList<>(List.of("10.mp3","2.mp3","01.mp3","9999999999999999999999.mp3"));values.sort(PlaylistCodec.NATURAL);assertThat(values).containsExactly("01.mp3","2.mp3","10.mp3","9999999999999999999999.mp3").inOrder();}
    @Test public void utf8AndWindows1250AreDecoded(){String text="Żółć Łódź";assertThat(PlaylistCodec.decode(text.getBytes(StandardCharsets.UTF_8))).isEqualTo(text);assertThat(PlaylistCodec.decode(text.getBytes(Charset.forName("windows-1250")))).isEqualTo(text);}
    @Test public void utf16BomIsDecoded(){byte[] bytes="\ufeffZażółć".getBytes(StandardCharsets.UTF_16LE);assertThat(PlaylistCodec.decode(bytes)).isEqualTo("Zażółć");}
    @Test public void percentLiteralIsNotDecodedTwice() throws Exception {Track t=track("Album/100%23.mp3");String raw=PlaylistCodec.serialize(List.of(t),"XSPF","Album","Album",true);assertThat(PlaylistCodec.parse(raw,"XSPF").get(0).location).isEqualTo("100%23.mp3");}
    @Test public void plsOrderAndTitlesAreIndependentOfLineOrder() throws Exception {List<Track> rows=PlaylistCodec.parse("[playlist]\nTitle10=Ten\nFile10=10.mp3\nFile2=2.mp3\nLength2=NaN\nFile999999999999999999=ignore", "PLS");assertThat(rows).hasSize(2);assertThat(rows.get(0).location).isEqualTo("2.mp3");assertThat(rows.get(1).title).isEqualTo("Ten");assertThat(rows.get(0).duration).isEqualTo(-1);}
    @Test public void emptyPlaylistsAreValid() throws Exception {for(String format:PlaylistCodec.FORMATS)assertThat(PlaylistCodec.parse(PlaylistCodec.serialize(List.of(),format,"Empty","",true),format)).isEmpty();}
    @Test public void contentReferencesSurviveRoundTrip() throws Exception {Track t=new Track("content://provider/document/42","Test");for(String f:PlaylistCodec.FORMATS)assertThat(PlaylistCodec.parse(PlaylistCodec.serialize(List.of(t),f,"Test","",false),f).get(0).location).isEqualTo(t.location);}
    @Test public void decodedXmlMayDeclareOriginalUtf16Encoding() throws Exception {String text="<?xml version=\"1.0\" encoding=\"UTF-16\"?><playlist xmlns=\"http://xspf.org/ns/0/\"><trackList><track><location>Zażółć.mp3</location></track></trackList></playlist>";assertThat(PlaylistCodec.parse(text,"XSPF").get(0).location).isEqualTo("Zażółć.mp3");}
    @Test public void xmlBaseIsResolvedThroughAncestorsWithoutNetworkAccess() throws Exception {String xml="<playlist xmlns=\"http://xspf.org/ns/0/\" xml:base=\"file:///music/\"><trackList xml:base=\"Album%20A/\"><track><location>100%2523.mp3</location></track></trackList></playlist>";assertThat(PlaylistCodec.parse(xml,"XSPF").get(0).location).isEqualTo("/music/Album A/100%23.mp3");}
    @Test public void relativeXmlBaseIsAppliedBeforeThePlaylistFolder() throws Exception {String xml="<playlist xmlns=\"http://xspf.org/ns/0/\" xml:base=\"../Album/\"><trackList><track><location>01.mp3</location></track></trackList></playlist>";assertThat(PlaylistCodec.parse(xml,"XSPF").get(0).location).isEqualTo("../Album/01.mp3");}
    @Test public void filesAndPlaylistExtensionsAreCaseInsensitive(){assertThat(PlaylistCodec.isPlaylist("Test.M3U8")).isTrue();assertThat(PlaylistCodec.isPlaylist("Test.mp3")).isFalse();assertThat(PlaylistCodec.ext("song.FLAC")).isEqualTo("flac");}
    @Test(expected=Exception.class) public void xmlEntitiesAreRejected() throws Exception {PlaylistCodec.parse("<!DOCTYPE playlist [<!ENTITY x SYSTEM 'file:///secret'>]><playlist>&x;</playlist>","XSPF");}
    @Test(expected=IllegalArgumentException.class) public void newlineCannotInjectPlaylistEntries(){Track t=new Track("track.mp3\nsecond.mp3","Title");PlaylistCodec.serialize(List.of(t),"M3U8","Test","",false);}
    @Test(expected=IllegalArgumentException.class) public void parentTraversalIsNotAFileName(){PlaylistCodec.name("../escape");}
    @Test(expected=IllegalArgumentException.class) public void emptyNameIsRejected(){PlaylistCodec.name(" ");}
    @Test(expected=IllegalArgumentException.class) public void tooLongNameIsRejected(){PlaylistCodec.name("x".repeat(181));}
 @Test public void locationElementBaseIsResolved() throws Exception {String xml="<playlist xmlns='http://xspf.org/ns/0/'><trackList><track><location xml:base='Other/'>song.mp3</location></track></trackList></playlist>";assertThat(PlaylistCodec.parse(xml,"XSPF").get(0).location).isEqualTo("Other/song.mp3");}
 @Test public void comparatorHasConsistentEquivalence(){assertThat(PlaylistCodec.NATURAL.compare("1a","01")).isGreaterThan(0);assertThat(PlaylistCodec.NATURAL.compare("01","1b")).isLessThan(0);assertThat(PlaylistCodec.NATURAL.compare("1a","01a")).isEqualTo(0);}
 @Test(expected=Exception.class) public void wrongXmlRootIsRejected() throws Exception {PlaylistCodec.parse("<wrong/>","XSPF");}
 @Test(expected=Exception.class) public void deepXmlIsRejected() throws Exception {PlaylistCodec.parse("<a>".repeat(70)+"</a>".repeat(70),"XSPF");}
 @Test(expected=java.util.concurrent.CancellationException.class) public void parseCancellationIsChecked() throws Exception {PlaylistCodec.parse("track.mp3", "M3U8",new java.util.concurrent.atomic.AtomicBoolean(true));}
}
