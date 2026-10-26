# Listy — studio playlist

Lokalna aplikacja Windows z polskim interfejsem do automatycznego i ręcznego tworzenia playlist.

**Języki aplikacji: Polski / English** · [English documentation](README.en.md)

[**Pobierz Listy.exe — Windows x64**](https://github.com/zetmar-collab/Listy/releases/latest/download/Listy.exe) · [Wszystkie wydania](https://github.com/zetmar-collab/Listy/releases) · [Licencja MIT](LICENSE)

![Listy — studio playlist: wybór formatów muzyki, nazwy albumów i podgląd playlist](docs/images/listy.png)

## Możliwości

- Wybór katalogu i skanowanie podkatalogów.
- Playlisty w folderach albumów, w katalogu głównym lub w wybranym podkatalogu.
- Automatyczne nazwy playlist na podstawie nazw folderów albumów.
- Filtr 12 rozszerzeń muzycznych oraz zapis M3U8, M3U, PLS, XSPF i WPL.
- Ręczne układanie list, przeciąganie plików i zmiana kolejności utworów.
- Podgląd zawartości, zmiana nazwy i przenoszenie starych playlist do Kosza.

## Uruchomienie

Pobierz **[Listy.exe z najnowszego wydania](https://github.com/zetmar-collab/Listy/releases/latest/download/Listy.exe)** i otwórz plik. Wersja przenośna dla Windows x64 zawiera środowisko .NET, nie wymaga instalacji, konta ani serwera. Można skopiować sam plik EXE na inny komputer. Pierwsze uruchomienie rozpakowuje biblioteki uruchomieniowe do katalogu tymczasowego .NET. Program nie ma podpisu cyfrowego.

Kod źródłowy nie zawiera gotowego EXE: plik jest dostępny w **Releases**. Przy budowaniu ze źródeł otrzymasz go w `Program\Listy.exe`; wówczas możesz użyć także skryptu `Uruchom Listy.cmd`.

Do każdego wydania dołączona jest suma kontrolna `SHA256SUMS.txt`. Aby sprawdzić pobrany plik w PowerShell, uruchom `Get-FileHash .\Listy.exe -Algorithm SHA256` i porównaj wynik.

## Tworzenie automatyczne

Język wybierzesz na dole lewego panelu: **Język → Polski / English**. Zmiana działa od razu i zostaje zapamiętana w `%LOCALAPPDATA%\Listy\language.txt`. Domyślnym językiem jest polski. Nazwy plików, albumów i istniejących playlist nie zmieniają się po przełączeniu. Treść wcześniejszych raportów pozostaje w języku, w którym wykonano operację. Przyciski i elementy natywnych okien wyboru plików oraz błędy systemowe mogą używać języka Windows.

1. Kliknij **Wybierz katalog**. Domyślnie program wczytuje także podkatalogi. Zmianę tego ustawienia zastosuj przyciskiem **Odśwież**.
2. Wybierz miejsce zapisu:
   - **Osobno w każdym folderze / albumie** — lista w każdym katalogu zawierającym bezpośrednio pliki muzyczne, z nazwą tego katalogu, np. `Spokojny poranek\Spokojny poranek.m3u8`. Zagnieżdżone foldery, np. `Album\CD 1`, dostają osobne listy, np. `CD 1.m3u8`. Lista nie obejmuje muzyki z innych folderów.
   - **Jedna lista w katalogu głównym** — wszystkie pasujące utwory z aktualnie wczytanej biblioteki.
   - **Jedna lista w podkatalogu** — zbiorcza lista w nazwanym podfolderze wybranego katalogu, np. `Muzyka\Playlisty`.
3. Wybierz format playlisty i zaznacz formaty muzyki. W trybie osobnych list nazwy są pobierane automatycznie z folderów. Dla jednej listy zbiorczej wpisz nazwę **bez rozszerzenia**.
4. Sprawdź tabelę wyników. Zaznacz listę i kliknij **Podgląd utworów** albo kliknij wiersz dwukrotnie, aby obejrzeć jej zawartość w edytorze.
5. Kliknij **Utwórz playlisty**. Istniejące pliki są pomijane; automatyczny zapis ich nie nadpisuje. Błędy poszczególnych plików znajdziesz pod przyciskiem **Raport**.

**Ścieżki względne** pozwalają przenosić folder z muzyką i playlistą razem. Przy przeniesieniu samej listy użyj ścieżek bezwzględnych albo zapisz ją ponownie w nowej lokalizacji w edytorze. Pliki na innym dysku mogą wymagać ścieżek bezwzględnych nawet przy zaznaczonej opcji względnej.

Foldery są traktowane jako albumy na podstawie struktury katalogów. Program nie odczytuje tagów albumu, tytułu ani czasu utworu. Nazwy w podglądzie pochodzą z nazw plików. Kolejność przy skanowaniu jest naturalna, np. `1`, `2`, `10`.

## Ręczne tworzenie i podgląd

Otwórz zakładkę **Edytor i podgląd** i kliknij **Nowa lista**. Dodaj pliki przyciskiem **Dodaj utwory** albo przeciągnij je z Eksploratora do tabeli. Nowo dodane duplikaty są pomijane. Wybieraj wiele utworów z Ctrl lub Shift. Przyciski **W górę / W dół** przesuwają zaznaczenie; **Usuń zaznaczone** usuwa tylko pozycje z listy w pamięci, bez usuwania muzyki z dysku.

Wybierz format i kliknij **Zapisz playlistę**. Systemowe okno pozwala podać nazwę i katalog. Ręczny zapis do istniejącego pliku wymaga potwierdzenia nadpisania. Rozszerzenie musi odpowiadać wybranemu formatowi. Zakładka **Treść pliku** pokazuje oryginalną zawartość wczytanej listy, a po edycji — tekst do zapisu. Dla nowej listy ścieżki w tym podglądzie są liczone względem katalogu biblioteki (lub katalogu Muzyka); ostateczny zapis przelicza je względem wybranego miejsca zapisu.

Przycisk **Otwórz** wczytuje istniejącą playlistę także spoza biblioteki. Brakujące pliki są oznaczane w tabeli i pozostają na liście. Program zachowuje lokalizacje i kolejność, również powtarzające się wpisy i adresy strumieni. Przy ponownym zapisie odtwarza strukturę playlisty; niestandardowe metadane i oryginalne tytuły z tagów nie są zachowywane. Nie jest odtwarzaczem audio.

## Stare playlisty i zmiana nazwy

Zakładka **Zapisane playlisty** pokazuje rozpoznane listy w wybranym katalogu i wczytanych podkatalogach. Zaznacz listę i kliknij **Podgląd / edycja**. **Zmień nazwę** zachowuje format i zawartość oraz nie nadpisuje istniejących plików.

Zaznacz jedną lub wiele playlist i kliknij **Przenieś do Kosza**. Program pokaże konkretne ścieżki i poprosi o potwierdzenie. Dotyczy to wyłącznie zaznaczonych playlist, nigdy utworów. Dostępność Kosza zależy od rodzaju dysku i ustawień Windows; na udziałach sieciowych i niektórych nośnikach Windows może wymagać dodatkowej decyzji. Jeśli Windows proponuje trwałe usunięcie, anuluj operację, aby zachować plik.

## Formaty

| Typ | Obsługiwane rozszerzenia |
|---|---|
| Muzyka | MP3, FLAC, WAV, M4A, AAC, OGG, OPUS, WMA, AIFF, AIF, APE, ALAC |
| Playlisty | M3U8, M3U, PLS, XSPF, WPL |

Nowe playlisty są zapisywane w UTF-8. Dla polskich znaków zalecany jest M3U8. Odczyt wykrywa BOM, w tym UTF-16; starsze M3U i PLS bez poprawnego UTF-8 są odczytywane jako Windows-1250. Limit odczytu playlisty wynosi 32 MB. Filtr rozpoznaje rozszerzenia, nie analizuje kodowania audio. M4A może zawierać m.in. AAC lub ALAC.

## Przykładowa biblioteka

Folder **Przykładowa muzyka** zawiera pięć krótkich syntetycznych plików WAV, dwa foldery z muzyką (jeden zagnieżdżony), starą playlistę oraz listy zapisane podczas kontroli programu. To dźwięki testowe, nie komercyjne nagrania. Odtworzysz je poleceniem `python scripts/CreateDemo.py`. Generator nie nadpisuje istniejących przykładów.

## Budowanie i testowanie ze źródeł

Wymagane: Windows i .NET SDK 8 lub nowszy. Brak zależności NuGet innych niż składniki .NET. Wersja przenośna może wymagać pobrania pakietów środowiska z NuGet podczas budowania.

```powershell
dotnet build src/Listy.App/Listy.App.csproj -c Release
dotnet run --project tests/Listy.Tests -c Release
dotnet run --project src/Listy.App/Listy.App.csproj
```

Budowanie samodzielnego EXE razem z testami:

```powershell
.\scripts\Build.ps1 -Portable
```

Gotowy plik: `Program\Listy.exe`. Publikacja publiczna wymaga odrębnego przygotowania certyfikatu i podpisu; skrypt tworzy lokalną wersję przenośną.

## Architektura

Ikona programu znajduje się w `src/Listy.App/Assets`: `Listy.ico` zawiera dziewięć rozmiarów (16–256 px), `Listy.png` służy jako duży podgląd, a `Listy.svg` jako źródło wektorowe. Generator `python scripts/CreateIcon.py` wymaga opcjonalnej biblioteki Pillow; zwykłe budowanie używa gotowego ICO i nie wymaga Pythona.

```text
WPF / MainWindow
  ├─ LibraryScanner → LibrarySnapshot → Album / Track / PlaylistFile
  ├─ PlaylistPlanner → PlaylistPlan → podgląd → zapis
  ├─ PlaylistCodec → M3U8 / M3U / PLS / XSPF / WPL
  └─ Windows → wybór katalogu / pliku / Kosz
```

`src/Listy.Core` zawiera skanowanie, walidację nazw, planowanie, formaty i atomowy zapis przez plik tymczasowy w katalogu docelowym. `src/Listy.App` zawiera interfejs i integrację z Windows. `tests/Listy.Tests` jest niezależnym programem testowym, który zwraca niezerowy kod przy błędzie. `scripts` zawiera budowanie i generator przykładów.

Skanowanie i zapis automatyczny działają poza wątkiem interfejsu i pozwalają anulować dalsze przetwarzanie. Zakończone zapisy pozostają na dysku po anulowaniu. Skaner pomija dowiązania katalogów, aby nie zapętlać rekursji. Błędy dostępu trafiają do raportu. XML jest czytany bez DTD i zewnętrznych encji. Dane nie są wysyłane przez sieć. Nie ma bazy danych ani serwera API; model danych pozostaje w pamięci, a źródłem prawdy są pliki.

Testy obejmują skanowanie z podkatalogami i bez nich, anulowanie, sortowanie naturalne, filtry, trzy tryby zapisu, zapis/odczyt pięciu formatów, ścieżki względne i bezwzględne, Unicode, strumienie, brakujące pliki, ochronę istniejących list, walidację nazw i blokowanie DTD. Kontrola ręczna obejmuje okno główne, wybór folderu, zapis automatyczny i podgląd listy.

Wybór WPF/.NET wykorzystuje natywne okna Windows i dostępne środowisko kompilacji bez Node, Rust ani przeglądarki. Kompromis: ta wersja działa wyłącznie w Windows. Możliwe dalsze rozszerzenia: tagi albumów, czasy utworów, odtwarzanie i zapamiętywanie ustawień.
