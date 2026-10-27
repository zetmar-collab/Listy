# Listy — playlist studio

Local Windows playlist creation and editing. **Polski / English** · [Polska dokumentacja](README.md)

[Download Listy.exe — Windows x64](https://github.com/zetmar-collab/Listy/releases/latest/download/Listy.exe) · [Releases](https://github.com/zetmar-collab/Listy/releases) · [MIT license](LICENSE)

![Example Listy window](docs/images/listy.png)

## Getting started

Download and run **Listy.exe**. The portable Windows x64 build includes .NET and requires no installer, account or server. The executable is unsigned. Verify it against SHA256SUMS.txt using:

~~~powershell
Get-FileHash .\Listy.exe -Algorithm SHA256
~~~

Choose English at the bottom of the left sidebar. The language changes immediately and is remembered.

## Automatic playlists

1. Choose a music folder. Enable subfolder scanning and optional tag/duration reading, then click Refresh.
2. Select the album checkboxes to include. Select all / Select none act on currently visible albums. Double-click an album to preview its tracks.
3. Choose one playlist per album folder, one combined playlist in the root folder, or one combined playlist in a named subfolder. Per-album playlists use the folder name.
4. Select music extensions, playlist format and sorting order: filename, artist/album, album/track number or track number. Read tags first for metadata-based sorting.
5. Choose an existing-file policy:
   - **Skip existing**: default, no overwrite.
   - **Update with backup**: atomically replace the file and retain its previous bytes in a uniquely named .bak file.
   - **Save a new version**: create a name such as Album (2).m3u8.
6. Review the plan and create playlists. An invalid album name does not block valid albums; warnings appear in the report.

Album grouping follows folders, not tags. Names over the current 180-character limit are skipped with a warning. Search filters albums, tracks and saved playlists without changing album selection; selected hidden albums remain in the generation plan.

## Editor

Create a new playlist or open M3U8, M3U, PLS, XSPF or WPL. Add music files with the button or by dropping them from Explorer. Imported duplicates are retained until explicitly removed.

Reorder tracks with arrows or by dragging rows. Undo / Redo retain up to 30 in-memory states. They do not undo disk writes; those are protected by .bak copies. Reordering while filtering affects the complete playlist.

Read tags loads title, artist, album, track number and duration. The footer shows total known time and how many tracks have durations. Tag reading uses ATL.NET and never modifies music files.

Save updates the current playlist with a backup; Save as selects a different name, location or format. Empty playlists can be saved. Unsaved changes offer Save / Discard / Cancel.

| Shortcut | Action |
|---|---|
| Ctrl+S / Ctrl+Shift+S | Save / Save as |
| Ctrl+O | Open playlist |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Delete in the track table | Remove selected entries |

## Validation and repair

Check playlist detects missing files, duplicate locations and empty playlists. Missing files only filters the view. Removing missing or duplicate entries affects the in-memory playlist, never music files.

Repair paths replaces an old folder prefix with a new one for missing entries only, preserving subfolders. The old folder may no longer exist. Check the result before saving; repair can be undone.

Check library playlists scans saved playlists and fills their check-result column. Issues only filters the results. Saved playlists can also be previewed, renamed or moved to the Recycle Bin after confirmation.

Recycle Bin availability depends on the drive and Windows settings. Cancel any Windows prompt offering permanent deletion if you want to keep the file. An open deleted playlist becomes an unsaved in-memory list, rather than being silently recreated at its old path.

## Formats and metadata

Music extensions: MP3, FLAC, WAV, M4A, AAC, OGG, OPUS, WMA, AIFF, AIF, APE and ALAC. Tag availability depends on the actual audio format.

Playlist formats: M3U8, M3U, PLS, XSPF and WPL. Output is UTF-8. Input detects BOM/UTF-16, with Windows-1250 fallback for legacy M3U and PLS. The read limit is 32 MB. Text preview and track parsing use the same decoded document.

Same-format editing preserves titles, durations, supported M3U comments, PLS fields and XML structures/extensions. Conversion warns that unsupported metadata may be omitted. Rebuilding a damaged playlist through automatic update retains its original file in the backup.

Relative paths are recalculated for the output location. Different drives or preserved XML xml:base may require absolute paths.

## Settings and background work

Folder, album selection, format filters, playlist format, output policy, order, relative paths, metadata option and window size are saved in **%LOCALAPPDATA%/Listy/settings.json**. Language is also saved in language.txt. The last folder is rescanned at startup.

File operations run in the background with progress. Cancellation is checked between files and processing stages; an active operating-system call may take time to finish. Completed writes remain on disk. Existing reports retain their original language; native dialogs follow Windows language.

## Development

Requires Windows and .NET SDK 8 or newer. NuGet restores ATL.NET 7.19.0 (MIT).

~~~powershell
.\scripts\Build.ps1
.\scripts\Build.ps1 -Portable
~~~

The script runs 72 core/integration tests and 6 WPF window tests. Working output goes to .build, independently of a running development build. Portable output goes to **Program/Listy.exe**, with checksums and license notices. GitHub Actions runs the tests on pushes and pull requests.

The sample library contains synthetic WAV tones, not commercial recordings.

Source architecture: WPF UI; scanner and metadata reader; planner; collision-policy writer; playlist document/codec; validation, relocation, history and settings services. No database or server is required. Directory links are skipped during recursion; XML DTDs and external entities are blocked.

Listy is [MIT licensed](LICENSE). ATL.NET [source](https://github.com/Zeugma440/atldotnet) and [license notices](THIRD-PARTY-NOTICES.txt) are available here and in the About / licenses window.
