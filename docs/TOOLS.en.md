# Studio playlist tools — Windows 1.3 / Android 1.1

Open **Tools** in the Windows sidebar or the Android footer. Every dialog supports Polish and English.

- **Backups:** open a library playlist, choose a `.bak` version, preview and restore it. The current file is backed up first. Backups contain playlists, not music.
- **Differences:** compare editor entries with the saved playlist. Added/removed entries, duplicate counts and order changes are shown before updates. A changed location appears as one removal and one addition.
- **Player profile:** applies to the current tab. VLC/foobar2000 uses M3U8 UTF-8 and relative paths; Legacy uses M3U Windows-1250; PLS uses UTF-8; XSPF uses absolute references. Unsupported Legacy characters cause an error without damaging the original. Changing to any format other than M3U forces UTF-8. Other players may not support Android `content://` references.
- **Smart playlist:** save named rules for artist, album, extension and duration in seconds. Conditions are combined with AND. Enable tag reading and refresh first. Unknown durations do not satisfy duration limits. Results open in the editor for further changes.
- **Metadata cache:** the folder tree is enumerated on each scan; unchanged files reuse tags based on identifier/path, size and modification time. Providers without a reliable fingerprint are read again. Use **Read all tags again** to force a refresh. No background watcher or music copies are created.
- **Identical audio files:** optional whole-file SHA-256 comparison reports identical copies without deleting anything. Different tags or encodings produce different hashes even when the recording sounds the same. The operation can be cancelled.
- **ZIP with music:** exports editor tracks into `Music/` plus a relative `Playlist.m3u8`. Numbered names prevent collisions. Extract the entire archive before playing. Accessible audio files/documents are required; network links are rejected. Android first stages the archive in private storage. Local free space is checked; the destination provider can still reject a write. A cancelled final Android transfer can leave an incomplete destination file; retry using a new name.
- **Audio preview:** select an editor track, then open preview. Play/Pause/Stop controls provide a maximum 30-second preview. Closing it releases the player; Android also releases it when the app goes into the background. Supported codecs depend on the device.

Android keeps generator and editor names, formats, encoding and relative-path options separate. Draft writes and preparation of pending exports use atomic private files. After abrupt process termination, the last completed draft write can be restored. Clearing app data also clears drafts, preferences and pending exports.

Playlist reading is limited to 32 MB, 200,000 entries and XML depth 64. Invalid XML structure, DTD and entity declarations are rejected. Cancellation and resource limits are checked before constructing the complete XML document.

The Android APK remains a development build signed with the same local key as the previous version. No private key is committed. Production signing requires a separate signing and installation migration plan. CI builds use another temporary test key.
