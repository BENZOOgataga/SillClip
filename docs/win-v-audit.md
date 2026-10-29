# Win+V behavior audit

Reviewed for version 0.10.3 on Windows 11 with WindowSill 1.0.3. This compares the fork with documented Windows clipboard behavior and records the limits of the comparison.

## Native behavior and evidence

Microsoft documents Win+V as a picker: selecting an entry pastes it into the current application. Entries can be pinned or deleted, and Clear all preserves pins. Unpinned Windows history is cleared at restart. Native history accepts text, HTML, and bitmaps, with a 4 MB per-item limit and a 25-entry count limit. These are Windows storage rules, not a contract for popup placement or animation. [Microsoft clipboard guide](https://support.microsoft.com/en-us/windows/apps/using-the-clipboard)

Microsoft Q&A contains reports of the native picker appearing beside the text caret, plus a suggested combination of GetGUIThreadInfo and MSAA caret geometry. Other reports describe fixed-corner placement or movement changes after Windows updates. Those reports do not establish a universal rule that the popup follows the mouse or remembers a dragged position. [Caret discussion](https://learn.microsoft.com/en-us/answers/questions/818647/how-to-get-caret-position-in-any-application-from), [corner placement report](https://learn.microsoft.com/en-us/answers/questions/4166566/windows-clipboard-history-gets-stuck-on-the-bottom), [movement report](https://learn.microsoft.com/en-us/answers/questions/3867145/my-clipboard-history-window-moved-and-i-cant-drag)

## Findings addressed

| Finding | Change in this release |
| --- | --- |
| Reopening after many copies could show an old scroll position. | Each opening clears search, updates the virtualized layout, aligns the first item, and resets the scroll offset without animation. Stale opening callbacks are ignored. |
| Successive openings could switch monitors when caret lookup failed or focus returned to WindowSill. | The pointer snapshot selects the monitor. A caret on another display is ignored, and missing caret geometry falls back to the pointer. |
| The popup opened at the WindowSill bar, then jumped to saved coordinates. | Initial native placement is corrected while the popup is hidden. Saved coordinates are no longer used. |
| Opening waited for the Windows key to be released. | The first Win+V keydown queues opening. Repeats and the matching V keyup are suppressed; ordinary keys pass through. |
| The popup looked like a dense bar list. | The 360 by 400 logical pixel popup uses multiline cards, a search field, Clear all, and a close button. |
| Search and selection had incomplete keyboard navigation. | Escape closes; a second Win+V toggles; Up and Down select; Enter pastes; Ctrl+F focuses search. Action buttons retain their own keyboard behavior. |
| Bar-item behavior was unclear. | The context menu now has Paste and Copy. Copy changes the clipboard without sending Ctrl+V. |
| Focus could move while an asynchronous paste was pending. | Popup paste checks the original foreground window and process before sending the paste command. If focus goes elsewhere, it leaves the item copied. |
| A missing live entry could leave the previous clipboard content in place. | Copy checks the Windows history operation status and stops on failure. |
| Each completed search index could trigger another filter pass. | Index updates queue one filter pass and suppress intermediate empty-state notifications. |
| Image bindings read native clipboard content during layout. | Included from unlimited.2: bindings return cached previews; reads and decoding run outside layout. |
| The keyboard hook shared the UI thread. | Included from unlimited.2: a dedicated hook thread runs the message loop, separate from layout and clipboard refresh. |

## Placement policy

The fork captures the foreground window and mouse position before queuing UI work. The mouse position selects the monitor for the whole invocation. For an external foreground window, it asks GetGUIThreadInfo for the caret rectangle, then tries MSAA caret geometry with a 40 ms wait budget. WindowSill's own windows are excluded from caret lookup. A provider that has not returned blocks further MSAA probes, rather than accumulating worker threads. Only geometry is requested.

When a caret is on the pointer's monitor, the popup opens below it, or above it when space is insufficient. Without a suitable caret, it opens near the captured pointer position on that same display. Coordinates are clamped to the work area and dimensions are scaled for the target display. Dragging is available for the current opening; the next invocation computes a new position. This is a fork policy, rather than a claim about the Shell's private algorithm. [GetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcursorpos) supplies screen coordinates and [MonitorFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfrompoint) resolves the display containing them.

The WindowSill SDK owns the popup window and its capture-protection registration. A same-thread native subclass adjusts the SDK's initial move before display. A hidden-window fallback handles hosts that skip WM_WINDOWPOSCHANGING. The callback is removed on closing. If installation fails, the popup uses the host's placement. [GetGUIThreadInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguithreadinfo), [SetWindowSubclass](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass), [WM_WINDOWPOSCHANGING](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-windowposchanging)

## Differences and limits

- Search, persistent unpinned history, and unlimited archive retention are fork features. Emoji and GIF tabs are omitted.
- The fork imports Windows-accepted history entries. It does not replace the Windows capture backend or guarantee capture of every format and oversized copy.
- The native popup's overflow menu, including Paste as text, is not reproduced. This popup exposes pin and delete buttons directly.
- Custom editors may not expose a usable caret. If the pointer and caret are on different displays, this fork follows the pointer's display. Native focus routing and animation are not public contracts and have not been reproduced exactly.
- Search takes keyboard focus. Outside-click closing and focus restoration use WindowSill's popup lifecycle, including its deactivation delay.
- Elevated or protected applications may reject simulated paste. The extension cannot bypass Windows input restrictions.
- Screen recording protection comes from WindowSill. No Discord stream-state detection is implemented, and protection depends on the capture method.
- Password detection is heuristic. Encryption at rest does not make an unlocked session's clipboard safe from other processes running as the same user.
- Clear all in this extension clears its unpinned archive. Clearing only Windows history does not erase that archive.
- GitHub updates are checked automatically but are not downloaded or installed automatically. The marketplace update service is separate.

## Validation

The release has 70 passing regression tests, covering retention, version channels, clipboard signatures, privacy, refresh coalescing, shortcut state, popup navigation, placement bounds, and intermittent or out-of-monitor caret reports.

Release builds map source paths to `/_/`. The packaging script checks required resources, local build paths, diagnostic files, and common secret markers before accepting a package.

A separate native probe exercised the production placement callback on 80 hidden-window cases at 100%, 125%, 150%, and 200% scale, with and without WM_WINDOWPOSCHANGING. It includes repeated openings on the same native window, alternating valid, absent, and out-of-monitor caret reports on positive and negative display coordinates. It checked final position, dimensions, hidden state, and callback removal. No window was shown and no keyboard input was generated.

Run it with `dotnet run --project tests/NativePlacementProbe -c Release` on Windows. It also runs in CI before packaging.

The 0.10.3 scroll reset builds against WinUI 3. The existing unit and native placement tests do not provide an end-to-end test of the popup's scroll position.

The earlier hook probe passed 32 concurrent startup and shutdown cycles without generating input. These checks do not establish full visual parity or cover an extended gameplay session, every editor, IME input, every multi-monitor transition, or every capture mode. Those remain manual compatibility checks.
