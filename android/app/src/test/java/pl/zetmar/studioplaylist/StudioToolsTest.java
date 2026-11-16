package pl.zetmar.studioplaylist;

import org.junit.Test;
import static com.google.common.truth.Truth.assertThat;
import java.util.*;
import java.nio.charset.*;

public class StudioToolsTest {
    private Track t(String location){return new Track(location,location);}
    @Test public void differenceCountsDuplicateEntries(){StudioTools.Difference d=StudioTools.compare(List.of(t("a"),t("b"),t("a")),List.of(t("b"),t("a")));assertThat(d.removed).containsExactly("a");assertThat(d.reordered).isTrue();assertThat(d.added).isEmpty();}
    @Test public void insertionDoesNotClaimReordering(){StudioTools.Difference d=StudioTools.compare(List.of(t("a"),t("b")),List.of(t("a"),t("new"),t("b")));assertThat(d.added).containsExactly("new");assertThat(d.reordered).isFalse();}
    @Test public void profileKeepsPolishCharacters() throws Exception {String value="Żółć Łódź";assertThat(new String(StudioTools.encode(value,"windows-1250"),Charset.forName("windows-1250"))).isEqualTo(value);}
    @Test(expected=CharacterCodingException.class) public void legacyEncodingRejectsLossyConversion() throws Exception {StudioTools.encode("😀","windows-1250");}
    @Test public void savedRuleFiltersAllCriteria(){StudioTools.Rule rule=new StudioTools.Rule();rule.artist="Marek";rule.album="Live";rule.extension=".mp3";rule.min=60;rule.max=180;Track t=t("/music/a.MP3");t.artist="MAREK Z";t.album="Live 2026";t.duration=90;assertThat(rule.matches(t)).isTrue();t.duration=-1;assertThat(rule.matches(t)).isFalse();t.duration=200;assertThat(rule.matches(t)).isFalse();}
    @Test(expected=java.io.IOException.class) public void parserRejectsBeyondTrackLimit() throws Exception {PlaylistCodec.parse("a.mp3\n".repeat(200001),"M3U8");}
    @Test public void comparatorIsTransitiveForPrefixes(){List<String> names=List.of("1a","01","1b","0001a","2","10","");for(String a:names)for(String b:names)for(String c:names){int ab=PlaylistCodec.NATURAL.compare(a,b),bc=PlaylistCodec.NATURAL.compare(b,c);if(ab<=0&&bc<=0)assertThat(PlaylistCodec.NATURAL.compare(a,c)).isAtMost(0);}}
}
