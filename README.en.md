# Listy — playlist studio

A local Windows app for automatic and manual playlist creation. Available in **Polish and English**.

[Download Listy.exe — Windows x64](https://github.com/zetmar-collab/Listy/releases/latest/download/Listy.exe) · [Polska dokumentacja](README.md) · [MIT license](LICENSE)

![Listy playlist studio](docs/images/listy.png)

## Getting started

Download and run **Listy.exe** from Releases. This portable Windows x64 build includes .NET and requires no installer, account or server. The executable is unsigned.

Select **English** from the language selector at the bottom of the left sidebar. The interface changes immediately, and the app remembers your choice in `%LOCALAPPDATA%\Listy\language.txt`. File names, album names, existing playlists and unsaved track order are preserved. Existing operation reports retain their original language. Native file-picker controls and operating-system errors follow your Windows language.

## Automatic playlists

1. Choose a music folder. Enable **Include subfolders** to scan nested folders, then use **Refresh** after changing this option.
2. Choose one playlist per album folder, one playlist in the root folder, or one combined playlist in a named subfolder.
3. Select the music extensions and output format: **M3U8, M3U, PLS, XSPF or WPL**.
4. Check the preview and click **Create playlists**. Existing playlists are skipped.

Per-album playlists use their containing folder's name. For example, `Jazz\Jazz.m3u8`. Albums are identified by folder structure, without reading audio tags. Relative paths let you move playlists together with their music folders.

## Manual playlists and management

Use **Editor & preview → New playlist** to create a playlist. Add or drop music files, select tracks with Ctrl/Shift, and use **Move up / Move down** to reorder them. **Remove selected** removes entries from the playlist only. Choose a format and click **Save playlist**.

**Open** loads an existing playlist. The preview shows missing files and allows you to inspect the playlist text. Saving rebuilds the playlist from track locations and order; custom metadata is not preserved. This app does not play audio.

The **Saved playlists** tab provides preview, renaming, and moving selected playlists to the Recycle Bin after confirmation. Recycle Bin availability depends on the drive and Windows settings; cancel any Windows prompt offering permanent deletion if you want to keep the file.

The sample music folder contains short synthetic WAV tones for testing.

## Build from source

Requires Windows and .NET SDK 8 or newer:

```powershell
dotnet build src/Listy.App/Listy.App.csproj -c Release
dotnet run --project tests/Listy.Tests -c Release
.\scripts\Build.ps1 -Portable
```

The portable executable is written to `Program\Listy.exe`. Application strings are maintained in `src/Listy.Core/Translations/pl.json` and `en.json`; both catalogs use the same keys and format parameters.
