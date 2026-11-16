# Audyt Studio playlist / Listy — 15 listopada 2026

## 1. Podsumowanie

Zakres: wersja Windows 1.2.0 (C#/.NET 8, WPF), biblioteka formatów i operacji plikowych, testy, skrypty budowania i MSIX oraz samodzielny Android 1.0.0 (Java 17, natywne API Androida, SAF). Przejrzano oba interfejsy, import i zapis pięciu formatów, historię edycji, konfigurację, uprawnienia, dokumentację i CI. Aplikacje nie mają serwera ani bazy danych; ustawienia i szkice są lokalne.

Android w `Listy/android` i `C:/Users/Marek/Codex/Listy-Android` ma identyczne cztery główne pliki Java. Samodzielne repozytorium Android jest na commicie `dafad32c4bc6326ec28dfdcdbfa0c913b728d104`; katalog roboczy jest czysty. W projekcie Windows są już wcześniejsze niezacommitowane zmiany dokumentacji i pliki Android/MSIX. Nie zostały zmienione przez ten audyt.

Ogólny stan: podstawowe operacje działają i są testowane. Najważniejsze problemy dotyczą zgodności ścieżek XSPF, stanu edytora Androida, wiarygodności usuwania niedostępnych wpisów i przerwanego eksportu. Nie potwierdzono błędu krytycznego pozwalającego wykonywać obcy kod lub samoczynnie usuwać pliki muzyczne. Poniższa lista nie jest gwarancją braku innych błędów.

Weryfikacja wykonana podczas audytu:

| Sprawdzenie | Wynik |
| --- | --- |
| Windows `scripts/Build.ps1` | 72 testy logiki/integracji + 6 testów WPF; kompilacja bez błędów i ostrzeżeń |
| Android `testDebugUnitTest --rerun` | 21 testów, 0 błędów |
| Android `lintDebug` | Bez błędów; ostrzeżenia o wersji target API i zależnościach testowych |
| NuGet `--vulnerable --include-transitive` | Brak zgłoszonych podatnych pakietów w wyniku dla `Listy.Core`, według użytego źródła NuGet |
| Dodatkowe, izolowane próby | Potwierdzone B1, B2, B6 i B7; wyłącznie syntetyczne pliki w `.build/audit-repro` |
| Dystrybucja Android | Repozytorium publiczne, wcześniejszy CI zakończony sukcesem, wydanie `v1.0.0` nadal draft; publiczny link APK zwraca HTTP 404 |

Osiem testów instrumentacyjnych Androida przejrzano, ale nie uruchamiano ponownie emulatora ani testów na fizycznym telefonie. Nie przeprowadzano ponownej certyfikacji Microsoft Store ani analizy całego natywnego kodu bibliotek zewnętrznych. Kod aplikacji pozostał bez zmian; nowe pliki to raport i lokalne materiały diagnostyczne.

## 2. Błędy krytyczne i funkcjonalne

Brak potwierdzonych błędów o ważności „Krytyczny”. Błędy funkcjonalne oraz blokada dystrybucji:

### B1. `xml:base` na elemencie `location` jest pomijane — Średni, potwierdzony wykonaniem na obu platformach

**Lokalizacja:** [Windows PlaylistCodec.cs:248](C:/Users/Marek/Codex/Listy/src/Listy.Core/PlaylistCodec.cs:248), [rozwiązywanie bazy:175](C:/Users/Marek/Codex/Listy/src/Listy.Core/PlaylistCodec.cs:175); [Android PlaylistCodec.java:77](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/PlaylistCodec.java:77), [withXmlBase:87](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/PlaylistCodec.java:87).

**Problem:** rozwiązywanie adresu zaczyna się od `track`, zamiast od zawierającego URI elementu `location`. Baza zadeklarowana bezpośrednio na `location` nie jest brana pod uwagę.

**Przykład:** `<location xml:base="Other/">song.mp3</location>` daje `song.mp3` zamiast `Other/song.mp3`. Próba Windows wskazała plik obok playlisty, a próba Java zwróciła samą nazwę bez `Other/`.

**Wpływ:** wpis może wyglądać na brakujący albo wskazać inny utwór o tej samej nazwie. Późniejszy zapis utrwala niewłaściwą lokalizację.

**Minimalna poprawka:** przekazywać do rozwiązywania URI sam element `location`. Przy ponownym zapisie uwzględniać bazy zachowane w `SourceXml` i nie łączyć nowej względnej lokalizacji ze starą bazą; w takim przypadku można zapisać jednoznaczne URI bezwzględne. [Specyfikacja XML Base](https://www.w3.org/TR/xmlbase/).

### B2. Windows usuwa wpis przywróconego już pliku — Średni, potwierdzony wykonaniem

**Lokalizacja:** [MainWindow.Management.cs:63](C:/Users/Marek/Codex/Listy/src/Listy.App/MainWindow.Management.cs:63), [Library.cs:10](C:/Users/Marek/Codex/Listy/src/Listy.Core/Library.cs:10), [Library.cs:27](C:/Users/Marek/Codex/Listy/src/Listy.Core/Library.cs:27).

**Problem:** „Usuń brakujące” używa zapamiętanego stanu `IsMissing`, bez odświeżenia dostępności plików.

**Dowód:** utworzono wpis nieistniejącego pliku, następnie przywrócono plik i wywołano rzeczywisty handler okna WPF. Plik istniał, lecz liczba wpisów spadła do zera.

**Wpływ:** można usunąć prawidłowy wpis z playlisty. Muzyka nie jest kasowana, a zmianę da się cofnąć, ale po zapisie lista będzie niekompletna.

**Minimalna poprawka:** przed filtrowaniem odświeżyć dostępność w operacji w tle, a następnie pokazać liczbę wpisów do usunięcia. Nie uzależniać poprawności od wcześniejszego kliknięcia „Sprawdź”.

### B3. Generator Androida zmienia stan otwartego edytora — Średni, potwierdzony analizą przepływu danych

**Lokalizacja:** [MainActivity.java:34](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:34), [ustawienia generatora:127](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:127), [zapis edytora:252](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:252).

**Problem:** `playlistName` i `format` są wspólne dla generatora i edytora. Ustawienia generatora zmieniają je bez oznaczenia edytora jako zmodyfikowanego.

**Scenariusz:** otworzyć `Jazz.m3u8`, przejść do tworzenia jednej listy, wpisać `Trening` i wybrać PLS, wrócić do edytora. Utwory Jazzu pozostają, ale nazwa i format pochodzą już z generatora; zmiana nie ma prawidłowego stanu `dirty`.

**Wpływ:** niezamierzona zmiana nazwy lub konwersja przy następnym zapisie i niespójne ostrzeganie o niezapisanych zmianach.

**Minimalna poprawka:** osobne `GenerationSettings` i `EditorState`, z niezależną nazwą, formatem i flagą zmian. Wspólna może pozostać jedynie domyślna preferencja dla nowej listy. Scenariusza nie odtwarzano na urządzeniu podczas tego audytu.

### B4. Ponowny wybór tego samego folderu odznacza wszystkie albumy — Średni, potwierdzony analizą przepływu

**Lokalizacja:** [MainActivity.java:292](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:292), [scan:208](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:208).

**Problem:** wynik wyboru folderu zawsze wykonuje `selected.clear()`. Jednocześnie `scan()` uznaje folder zapisany w `scannedTree` za wymagający zachowania poprzednich zaznaczeń. Przejmuje więc pusty zbiór jako świadomy wybór użytkownika.

**Wpływ:** po ponownym wybraniu tej samej biblioteki generowanie nie znajduje utworów, mimo że skanowanie je wykryło.

**Minimalna poprawka:** zachować zaznaczenia dla tej samej URI; przy faktycznej zmianie biblioteki jawnie zainicjować nowy stan wyboru. Dodać rozróżnienie „brak zapamiętanego wyboru” i „użytkownik odznaczył wszystko”.

### B5. Eksport Androida nie przeżywa śmierci procesu — Średni, potwierdzony analizą kodu; nie symulowano zabicia procesu

**Lokalizacja:** [MainActivity.java:44](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:44), [utrwalanie szkicu:72](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:72), [eksport:262](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:262), [wynik pickera:296](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:296).

**Problem:** `exportBytes` i `exportName` są wyłącznie polami instancji. Nie trafiają do utrwalanego szkicu. Android może usunąć proces, kiedy na wierzchu jest systemowe okno zapisu. Nowa instancja odbiera rezultat z `exportBytes == null` i przerywa eksport.

**Wpływ:** po wybraniu miejsca użytkownik dostaje błąd, a utworzony przez picker plik może pozostać pusty. Nie jest to dowód utraty muzyki ani całego szkicu.

**Minimalna poprawka:** przed otwarciem pickera utrwalić gotowy eksport w prywatnym pliku tymczasowym i zapamiętać identyfikator oczekującej operacji. Po odtworzeniu procesu wznowić zapis. Dużych bajtów playlisty nie umieszczać w Bundle. [Dokumentacja stanu Androida](https://developer.android.com/topic/libraries/architecture/views/saving-states-views).

### B6. Naturalny komparator Androida narusza kontrakt porównania — Średni, potwierdzony wykonaniem

**Lokalizacja:** [PlaylistCodec.java:119](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/PlaylistCodec.java:119), [użycie podczas skanowania:Storage.java:56](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/Storage.java:56).

**Dowód:** `compare("1a", "01") == 0`, `compare("01", "1b") == 0`, ale `compare("1a", "1b") < 0`. Dotyczy również legalnych nazw folderów albumów.

**Wpływ:** sortowanie nie ma spójnego porządku. Możliwość wyjątku TimSort dla większego zestawu pozostaje hipotezą — nie wywołano takiego wyjątku w tej próbie.

**Minimalna poprawka:** po pętli porównywać pozostałe części tekstów, nie długości całych nazw, np. pozostałe długości `a.length()-i` oraz `b.length()-j`. Sprawdzić kontrakt na losowych zbiorach i z zerami wiodącymi. [Kontrakt Comparator](https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Comparator.html).

### B7. Niepoprawna struktura XML staje się pustą playlistą Androida — Średni, potwierdzony wykonaniem

**Lokalizacja:** [PlaylistCodec.java:68](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/PlaylistCodec.java:68).

**Problem:** parser sprawdza poprawność składni XML, ale nie sprawdza wymaganej struktury XSPF/WPL. `parse("<not-a-playlist/>", "XSPF")` zwraca pustą listę bez błędu.

**Wpływ:** uszkodzony lub niewłaściwy plik może wyglądać jak prawidłowa pusta playlista i zostać odbudowany przy zapisie. Kopia `.bak` chroni oryginał przy aktualizacji w bibliotece, ale nie naprawia błędnej informacji w interfejsie.

**Minimalna poprawka:** sprawdzać korzeń i wymagany kontener `trackList`/`body/seq`. Zachować możliwość otwierania prawidłowej, świadomie pustej playlisty.

### B8. Limit liczby wpisów jest egzekwowany zbyt późno — Średni, potwierdzone miejsce kontroli; skutku OOM nie wymuszano

**Lokalizacja:** [Android PlaylistCodec.java:55](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/PlaylistCodec.java:55), [PLS:62](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/PlaylistCodec.java:62), [kontrola:84](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/PlaylistCodec.java:84); [Windows PlaylistCodec.cs:191](C:/Users/Marek/Codex/Listy/src/Listy.Core/PlaylistCodec.cs:191).

**Problem:** M3U/PLS Androida najpierw tworzą obiekty wszystkich pozycji, a dopiero potem sprawdzają 200 000 wpisów. XML najpierw tworzy DOM. Windows ma limit bajtów, lecz nie liczby utworów. Niewielkie powtarzalne linie pozwalają zmieścić bardzo dużo obiektów w 32 MB wejścia.

**Wpływ:** ryzyko zużycia pamięci i zamknięcia aplikacji przy otwieraniu specjalnie przygotowanego lub bardzo dużego pliku. Próg zależy od urządzenia i nie był mierzony przez doprowadzenie aplikacji do braku pamięci.

**Minimalna poprawka:** limity sprawdzać przed każdym dodaniem utworu lub nowego rekordu PLS; do XML wprowadzić limit głębokości/liczby węzłów i rozważyć parser strumieniowy. Dodać anulowanie parsowania Androida, nie tylko operacji przed odczytem.

### B9. Publiczny odnośnik do APK jest nieaktywny — Wysoki dla dystrybucji, potwierdzony usługą i HTTP

**Lokalizacja:** [Android README.md:5](C:/Users/Marek/Codex/Listy-Android/README.md:5).

**Problem:** README kieruje do `releases/latest/download/Listy-Android-1.0.0.apk`, lecz `gh release view v1.0.0` zwraca `isDraft: true`. Anonimowe żądanie HEAD pod publicznym adresem zwróciło HTTP 404. Wcześniejszy [CI projektu Android](https://github.com/zetmar-collab/Listy-Android/actions/runs/42335320324) zakończył się sukcesem.

**Wpływ:** osoba odwiedzająca publiczne repozytorium nie może pobrać APK przez główny przycisk.

**Minimalna poprawka:** dokończyć publikację istniejącego wydania i sprawdzić publiczny link. Docelowo utrzymywać stałą nazwę pobieranego pliku albo aktualizować README wraz z nazwą pliku kolejnego wydania. W ramach audytu nie publikowano ani nie zmieniano wydania.

## 3. Bezpieczeństwo

Pozytywne ustalenia: Windows blokuje DTD i zewnętrzny resolver XML; Android odrzuca deklaracje DTD/ENTITY. Nazwy plików są walidowane, Windows zapisuje przez plik tymczasowy i używa `File.Replace` dla aktualizacji z kopią. Android korzysta z grantów SAF i nie deklaruje szerokiego dostępu do pamięci, Internetu ani aparatu. Uprawnienie `MANAGE_DOCUMENTS` znajduje się tylko w kodzie testowym. MSIX eksportuje wyłącznie publiczny certyfikat `.cer`.

Najistotniejsze ryzyko wejścia niezaufanego to B8. Nie wykazano wykonania kodu z playlisty. Żaden odnośnik odczytany z playlisty nie jest przez parser automatycznie pobierany z sieci.

Znane ograniczenie wydania: APK używa podpisu debug, co jest jawnie opisane. Nie zgłaszam tego jako ukrytej podatności. Dla regularnych aktualizacji potrzebny jest kontrolowany klucz wydania; obecny [skrypt Build.ps1:6](C:/Users/Marek/Codex/Listy-Android/scripts/Build.ps1:6) buduje `assembleDebug`, a [app/build.gradle:16](C:/Users/Marek/Codex/Listy-Android/app/build.gradle:16) nie definiuje podpisu release. Buildy z innych maszyn mogą mieć inne klucze debug i nie zastąpią zainstalowanego APK bez odinstalowania.

Nie raportuję kluczy, tokenów ani haseł. Kontrola NuGet obejmuje zgłoszenia znane wykorzystanemu źródłu, nie pełny audyt ATL.NET. CORS, CSRF i sesje serwerowe nie mają zastosowania do obecnej architektury.

## 4. Wydajność i jakość

| Obszar | Lokalizacja i obserwacja | Rekomendacja |
| --- | --- | --- |
| Android — praca na głównym wątku | [MainActivity.java:74](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:74): cała lista jest serializowana do JSON przed przekazaniem zapisu do executora; [restoreDraft:81](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:81) odczytuje i parsuje szkic podczas startu Activity. | Migawka modelu, odroczone zapisy i odczyt w tle. Dla dużych list ryzyko zauważalnych przerw; czasu ANR nie mierzono. |
| Android — utrwalanie w tej samej kolejce co skan | [MainActivity.java:76](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:76): zapis szkicu czeka za długą operacją plikową. Błędy zapisu są ignorowane. | Osobna krótka kolejka utrwalania, kontrola ostatniej zapisanej rewizji i widoczny błąd. Po ubiciu procesu w tym oknie może zostać stary szkic; scenariusz wymaga testu cyklu życia. |
| Android — budowanie całych widoków | [MainActivity.java:116](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:116), [drawEditor:169](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:169): przebudowa `setContentView` oraz wszystkich widocznych wierszy przy zmianach; wyszukiwarka działa na każde naciśnięcie. | RecyclerView/ListAdapter, stały układ, odświeżanie różnicowe i opóźnienie wyszukiwarki. Zachować pozycję przewinięcia i fokus. |
| Windows — istnienie plików na UI | [MainWindow.xaml.cs:222](C:/Users/Marek/Codex/Listy/src/Listy.App/MainWindow.xaml.cs:222), [Planning.cs:10](C:/Users/Marek/Codex/Listy/src/Listy.Core/Planning.cs:10): `File.Exists` dla planów i stanów wierszy. | Migawka dostępności w tle; szczególnie dla SMB/NAS. Zawieszania na sieci nie odtwarzano — jest to ryzyko wydajnościowe. |
| Android — ponowne listowanie folderów przy zapisie | [Storage.java:121](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/Storage.java:121), [wersjonowanie:138](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/Storage.java:138): każde szukanie nazwy pobiera wszystkie dzieci, a każda próba kolejnego numeru ponawia listowanie. | Jeden indeks nazw na folder w ramach operacji, z kontrolą konfliktu przy tworzeniu. |
| Architektura Androida | [MainActivity.java:24](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:24): ekran, stan, historia, JSON, operacje i nawigacja w jednej klasie; B3 jest skutkiem współdzielonego modelu. | Oddzielić stan edytora, konfigurację generatora i usługę operacji. Unikać refaktoryzacji całego projektu przed testami regresji. |
| Typy i diagnostyka Androida | [MainActivity.java:203](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/MainActivity.java:203): wyniki jako `Object[]`, callback sukcesu poza obsługą błędów; [Storage.java:96](C:/Users/Marek/Codex/Listy-Android/app/src/main/java/pl/zetmar/studioplaylist/Storage.java:96) ignoruje błędy tagów. | Nazwane klasy wyników, jedno miejsce obsługi błędów UI oraz ostrzeżenia o pominiętych metadanych. To dług jakościowy, a nie dowód konkretnego crasha. |

## 5. Brakujące testy

1. `xml:base` bezpośrednio na `location`, na kilku poziomach, oraz odczyt → przeniesienie → zapis → odczyt tego samego utworu.
2. Plik przywrócony po otwarciu listy i przed „Usuń brakujące” w Windows — wynik nie może usuwać prawidłowego wpisu.
3. Android: otwarty edytor → zmiana ustawień generatora → powrót → zapis/anulowanie/zamknięcie. Nazwa, format i `dirty` muszą być niezależne.
4. Ponowne wskazanie identycznej URI drzewa; odznaczenie wszystkiego; pierwszy skan nowego drzewa; restart z zapisanym wyborem.
5. Android: usunięcie procesu przy otwartym pickerze eksportu i wznowienie oczekującego zapisu; odtworzenie szkicu po błędzie utrwalania.
6. Własności komparatora: antysymetria, przechodniość i równoważność wobec trzeciego elementu. Nazwy liczbowe, z zerami wiodącymi i sufiksami.
7. Niepoprawny korzeń XML, brak kontenera, prawidłowa pusta lista, duża liczba krótkich wpisów, głęboki XML i anulowanie parsowania.
8. Błędy dostawców SAF: opóźnione pojawienie się nowego pliku, brak zapisu/zmiany nazwy, odwołanie grantu, brak miejsca, nieudane odtworzenie kopii.
9. Testy całych dialogów zapisu i zamknięcia, szczególnie Save/Discard/Cancel. Obecne testy UI Windows i Androida obejmują głównie zmiany widoku i podstawowe polecenia.
10. Publiczne pobranie wydanego APK i zgodność SHA-256; obecny CI sprawdza budowanie, ale nie dostępność release.

## 6. Propozycje funkcji

Szacunki zakresu są względne: S — kilka ograniczonych zmian, M — kilka modułów i ekran/dialog, L — nowy przepływ z integracją systemową. Nie są to deklaracje czasu realizacji. „Logika” oznacza lokalną warstwę aplikacji; nie proponuję serwera dla tych funkcji.

| Funkcja | Problem użytkownika i działanie | Priorytet | Zakres zmian |
| --- | --- | --- | --- |
| Menedżer kopii i przywracanie wersji | Pliki `.bak` istnieją, lecz trudno przywrócić właściwy. Lista wersji, data, podgląd i jawne przywrócenie wybranego pliku. | MVP | M: UI + logika plikowa + testy awarii; bez bazy |
| Różnice przed aktualizacją | Liczba utworów nie pokazuje skutków nadpisania. Pokaż dodane/usunięte/przeniesione wpisy i zmianę kolejności przed zapisem. | MVP | M: UI + porównanie dokumentów + testy; bez bazy |
| Profile zgodności z odtwarzaczami | Ta sama playlista bywa różnie odczytywana. Profil określa format, kodowanie i rodzaj ścieżek; ostrzega o nieprzenośnym `content://`. | Ważne | M: UI + konfiguracja JSON + kodeki + testowe pliki zgodności |
| Inteligentne listy i zapisane reguły | Ręczne wybieranie wielu albumów jest czasochłonne. Reguły po wykonawcy, albumie, czasie i rozszerzeniu, zapisane jako szablon. | Ważne | M: UI + model reguł + ustawienia JSON + testy; dodatkowe tagi wymagają rozszerzenia modelu |
| Przyrostowe skanowanie z pamięcią metadanych | Ponowne czytanie całej biblioteki/NAS jest wolne. Buforuj metadane według identyfikatora, rozmiaru i daty, przetwarzaj zmiany. | Ważne | L: UI postępu + warstwa skanowania + lokalny cache + testy unieważniania; format cache dobrać po pomiarach |
| Wykrywanie kopii samych nagrań | Obecne duplikaty dotyczą wpisów/lokalizacji. Opcjonalne haszowanie wykrywa identyczne pliki pod różnymi nazwami, przedstawiając raport bez automatycznego kasowania. | Później | M/L: UI raportu + logika haszowania i cache + testy anulowania; koszt zależy od wielkości muzyki |
| Przenośny pakiet playlisty | Użytkownik chce przenieść wybrany zestaw na inne urządzenie. Eksport folderu/ZIP z kopiami wybranych utworów i względną listą, po sprawdzeniu wolnego miejsca. | Później | L: UI wyboru + kopiowanie/archiwum + raport konfliktów + testy awarii; bez serwera |
| Krótki odsłuch utworu | Trudno rozpoznać pliki wyłącznie po nazwie. Opcjonalny odsłuch z edytora, kontrola dostępności formatu i wyraźny stan odtwarzania. | Później | L: UI + integracja odtwarzania na obu platformach + testy cyklu życia; bez bazy |

## 7. Plan działania

1. **Dystrybucja:** dokończyć publikację istniejącego wydania Androida (B9) i sprawdzić pobieranie bez logowania. Nie wymaga zmiany kodu aplikacji.
2. **Integralność playlist:** naprawić B1 i B2, dopisując najpierw odtwarzające je testy. To zapobiega wskazywaniu złego utworu i usuwaniu prawidłowych wpisów.
3. **Spójność Androida:** odseparować modele generatora i edytora (B3), poprawić ponowny wybór folderu (B4), utrwalić oczekujący eksport (B5).
4. **Odporność parserów:** naprawić komparator B6, walidację struktury B7 i limity B8. Uruchomić testy na granicach limitów oraz urządzeniu o mniejszej pamięci.
5. **Testy integracyjne:** pokryć Save/Discard/Cancel, śmierć procesu i awarie SAF. Dopiero później wydzielać duże fragmenty MainActivity i zmieniać architekturę widoków.
6. **Pierwsze nowe funkcje:** menedżer kopii oraz różnice przed aktualizacją; następnie profile zgodności i zapisane reguły. Cache skanowania wdrożyć po pomiarach dużej biblioteki.
7. **Wydania regularne:** przygotować kontrolowany podpis Android release i procedurę aktualizacji. Nie zmieniać klucza pod istniejącą instalacją bez zaplanowania migracji.

Nie wykonywano napraw, publikacji, usuwania plików użytkownika ani migracji danych. Materiały diagnostyczne znajdują się w `.build/audit-repro`, a logi poleceń w `.build/audit-*.log`.
