# Pakiety MSIX — Microsoft Store i testy

Budowanie z katalogu repozytorium (PowerShell, Windows SDK i .NET 8 SDK):

```powershell
.\scripts\Build-Msix.ps1
```

Skrypt uruchamia 72 testy logiki i 6 testów WPF, publikuje aplikację razem z .NET dla x64, generuje grafiki z istniejącej ikony i pakuje ją narzędziem MakeAppx z walidacją manifestu. Używa świeżego katalogu roboczego w `.build`. Wymagany jest ważny certyfikat podpisywania kodu z kluczem prywatnym w `Cert:\CurrentUser\My`, którego Subject jest równy Publisher manifestu. Domyślnie wybierany jest pasujący certyfikat o najdłuższej ważności; można podać `-CertificateThumbprint`. Klucz prywatny nie jest eksportowany. SignTool weryfikuje podpis i zaufanie certyfikatu.

Wyniki trafiają do `Packages\1.2.0.0\` (katalog wyłączony z Git):

| Plik | Przeznaczenie |
| --- | --- |
| `Listy_1.2.0.0_x64_Store.msix` | Niepodpisany pakiet do przesłania w Partner Center |
| `Listy_1.2.0.0_x64_Test.msix` | Podpisany pakiet do instalacji testowej |
| `Listy-Test.cer` | Wyłącznie publiczna część certyfikatu testowego |
| `SHA256SUMS.txt` | Sumy kontrolne obu paczek i publicznego certyfikatu |

## Tożsamość i zgodność

- Name: `MarekZettel-zetmar.Studioplaylist`
- Publisher: `CN=15A53D32-C868-48EE-B700-5DBB5449CA1B`
- PublisherDisplayName: `Marek Zettel - zetmar`
- PFN: `MarekZettel-zetmar.Studioplaylist_411qrz2m02jw4`
- Store ID: `9ND04MW6N5BP`
- Wersja: `1.2.0.0`, x64, Windows 10 2004 (19041) lub nowszy, interfejs PL/EN.

Końcowe spacje i encje `&#x20;` z danych skopiowanych z Partner Center nie są częścią tożsamości. PFN oraz Package SID wylicza Windows; nie wpisuje się ich do manifestu. Store ID nie jest deklaracją manifestu. Nazwa w menu Start: **Studio playlist**. Aplikacja WPF deklaruje `runFullTrust`, aby działać jako zwykła aplikacja desktopowa i obsługiwać katalogi muzyczne wybrane przez użytkownika. Nie wymaga zewnętrznej instalacji .NET.

## Instalacja testowa

Na tym komputerze certyfikat jest już zaufany. Instalacja przez dwuklik podpisanego pliku lub:

```powershell
Add-AppxPackage -Path .\Packages\1.2.0.0\Listy_1.2.0.0_x64_Test.msix
```

Na innym komputerze najpierw należy zaufać publicznemu certyfikatowi (PowerShell administratora):

```powershell
Import-Certificate -FilePath .\Listy-Test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path .\Listy_1.2.0.0_x64_Test.msix
```

Podpis testowy nie ma znacznika czasu. Certyfikat użyty do pierwszego wydania jest ważny do 20.08.2029, odcisk SHA-1: `5047763DCF5DB77675453898888D521F3FED424D`. Certyfikat `.cer` nie zawiera klucza prywatnego. Paczki Store i Test mają tę samą tożsamość, więc zajmują tę samą pozycję aplikacji w systemie.

## Przesłanie do Microsoft Store

W Partner Center otwórz produkt `9ND04MW6N5BP`, utwórz zgłoszenie i w sekcji pakietów prześlij plik **Store.msix**. Microsoft podpisuje pakiet podczas publikacji. W zgłoszeniu opisz zastosowanie `runFullTrust`: tworzenie, odczyt, zmiana nazw i zarządzanie lokalnymi playlistami w folderach wskazanych przez użytkownika. Pakiet ma pełny dostęp typowy dla desktopowej aplikacji uruchamianej przez użytkownika, bez żądania uprawnień administratora.

Przy kolejnym zgłoszeniu zwiększ wersję projektu lub podaj `-PackageVersion 1.2.1.0`; ostatni składnik wersji sklepowej musi wynosić `0`. Publikacja i certyfikacja Microsoft Store pozostają osobnym etapem. Samo przejście MakeAppx, testów i weryfikacji podpisu nie oznacza przejścia Windows App Certification Kit ani zatwierdzenia przez Store.

Dokumentacja Microsoft: [pakowanie aplikacji desktopowej](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion), [podpisywanie i dystrybucja przez Store](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide).
