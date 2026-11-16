# Wdrożenie audytu — stan prac

Zakres: Windows i Android; poprawki B1–B9, testy regresji, wydajność i osiem nowych funkcji z raportu. Android jest rozwijany w samodzielnym `Listy-Android`, a kopia w `Listy/android` jest synchronizowana przed budowaniem.

- [x] B9: publikacja istniejącego APK 1.0.0; publiczny link HTTP 200.
- [x] B1–B8: poprawki parserów, dostępności plików, stanu i eksportu; testy regresji.
- [x] Przywracanie kopii i różnice przed zapisem.
- [x] Profile odtwarzaczy i zapisane reguły inteligentnych list.
- [x] Przyrostowy cache metadanych i poprawa responsywności.
- [x] Wykrywanie identycznych nagrań po zawartości.
- [x] Przenośny eksport ZIP z muzyką.
- [x] Odsłuch utworu na obu platformach.
- [x] Testy końcowe, dokumentacja i aktualne pakiety.

Nie wykonywać kasowania nagrań ani migracji bazy danych. Operacje na muzyce podczas testów ograniczone do syntetycznych plików testowych. Przywracanie plików w aplikacji zachowuje poprzednią wersję.

## Weryfikacja zakończona 16 listopada 2026

- Windows 1.3.0: 83 testy logiki/integracji i 9 testów WPF, 0 błędów; kompilacja bez ostrzeżeń.
- Android 1.1.0: 33 testy JVM i 20 testów na emulatorze API 37, 0 błędów. Android Lint: 0 błędów, 4 zalecenia dotyczące wersji narzędzi/zależności. Samodzielny projekt zbudowany własnym skryptem.
- Scenariusze: xml:base, przywrócone pliki, limit wpisów, nadmierna głębokość XML, porządek naturalny, osobny stan generatora, ponowny wybór folderu, trwały eksport, rekonstrukcja szkicu, Zapisz/Odrzuć/Anuluj, odmowa zapisu, cofnięcie uprawnienia, kopie, ZIP, reguły, kodowanie i unieważnianie cache.
- Wirtualizowane wiersze Androida przez natywny ListView, bez dodawania zależności produkcyjnych; opóźnienie wyszukiwarki i zachowanie przewinięcia. Serializacja szkicu i odczyt przy starcie działają w osobnej kolejce. Wyniki operacji mają nazwane typy zamiast Object[]. Wersjonowanie SAF korzysta z jednego indeksu nazw w operacji.
- Windows korzysta z migawki dostępności playlist zamiast sprawdzania dysku przy odczycie stanu wiersza. Zapis nadal weryfikuje faktyczny stan pliku.
- EXE x64, MSIX 1.3.0.0 Store bez podpisu i Test z poprawnie zweryfikowanym podpisem; zgodne Name i Publisher. APK 1.1.0 ma ten sam certyfikat deweloperski co 1.0.0.

Instrukcje: [polski](../TOOLS.md), [English](../TOOLS.en.md).

Ograniczenia: nie testowano wszystkich telefonów, fizycznych kart SD, NAS ani dostawców chmurowych. Android nie zachowuje wszystkich niestandardowych metadanych importu; kopia oryginału pozostaje dostępna. Haszowanie wykrywa identyczne bajty, nie podobieństwo nagrania. Odsłuch zależy od kodeków systemowych. APK jest testowy, a migracja do produkcyjnego klucza Androida pozostaje oddzielną decyzją; nie zmieniono klucza istniejących instalacji. MSIX nie został automatycznie ponownie wysłany do Partner Center.
