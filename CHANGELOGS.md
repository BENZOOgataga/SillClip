# 0.10.1-unlimited.2
- Stopped reading native clipboard image content during XAML layout.
- Moved thumbnail reads and decoding to background work.
- Moved the Win+V keyboard hook to a dedicated message loop.
- Kept ordinary keys and injected input outside shortcut handling.
- Coalesced clipboard notifications and retained existing bar controls during refresh.
- Isolated unavailable history entries so other items can still refresh.

# 0.10.1-unlimited.1
- Removed network requests for link icons.
- Applied password masking to previews and search indexing.
- Fixed retention ordering when Windows history is imported repeatedly.
- Included rich text payloads in duplicate detection.
- Deferred archived image reads and limited thumbnail decoding.
- Kept live clipboard access available when archive loading fails.
- Added popup dragging, saved placement, and monitor bounds checks.
- Set explicit search icon sizes.
- Isolated the fork from official marketplace updates with a separate package ID.
- Migrated encrypted history and pins without overwriting existing files.
- Added semantic version checks and a six-hour update check interval.
- Added regression tests and a Windows build workflow.

# 0.10.0-unlimited.1
- Set the clipboard popup to 360 by 400 logical pixels.
- Fixed Start opening when the Windows key is released after Win+V.
- Fixed Escape closing the history when the search field has focus.
- Added DPAPI-encrypted persistent clipboard history.
- Added live search in compact mode and a search popup in expanded mode.
- Added automatic update checks through GitHub Releases.
- Added an optional Win+V override that opens the searchable WindowSill popup.
- Changed `0` and `-1` to mean no extension-side item limit.
- Kept pinned items outside the retention limit.

# 0.9.26
- You can now pin clipboard items. Pinned items stay at the top and are kept across restarts, even after Windows clears its clipboard history.
- Fixed a crash happening when a tooltip contains a large text.

# 0.9.25
- Added missing screenshots to the extension package.

# 0.9.24
- Fixed missing translation in the onboarding page.
- Updated dependencies.
- Added screenshots.

# 0.9.23
- Clipboard text and image items can now be pasted directly into the File Explorer.

# 0.9.22
- Fixed crash when copying some URL.

# 0.9.21
- Updated icons to be more consistent with the new Windows 11 design.

# 0.9.20
- Added ARM64 support.

# 0.9.18
- Changed icon of the Clear History button in compact mode.

# 0.9.17
- The clipboard can now be compacted into a popup window instead of having all clipboard items displayed in the WindowSill bar.
- Improved many translations.

# 0.9.13
- Updated dependencies.

# 0.8.0
- Added multiple display support.

# 0.7.9
- Improved performance.
- Updated dependencies.

# 0.7.8
- Fixed double treatment of clipboard items.

# 0.7.7
- Improved Portuguese translation.

# 0.7.6
- Added translation for Portuguese.
- Improved other translations.

# 0.7.5
- Fixed an internal error preventing to retrieve clipboard item containing images.
- Updated dependencies.

# 0.7.4
- Improved Spanish translation.

# 0.7.3
- Fixed an issue where the text indicated an empty or disabled clipboard had an inaccurate size.

# 0.7.2
- Improved various translations

# 0.7.1
- Fixed an issue where the app would crash when copying certain data.

# 0.7.0
- Improved some translation.
- Fixed the layout of the "Enable clipboard history" message in Small and Medium mode.
- Updated dependencies.

# 0.6.5
- Improved password detection.
- Improved some translation.

# 0.6.4
- Fixed an issue where unsupported clipboard format would crash the app.
- Added translation for Chinese (Simplified & Traditional), German, Hindi, Japanese, Spanish, Ukrainian and Vietnamese.

# 0.6.3
- Added an option to hide copied text that looks like a password.
- Added translation for French

# 0.6.2
- Fixed and issue where text from a web page would appear with version numbers in the clipboard.

# 0.6.1
- You can now delete an item from the history, or clear the history.
