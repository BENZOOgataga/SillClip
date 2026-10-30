# Clipboard library

Version 1.0 adds library tools to the Win+V popup.

Open the popup and select Manage clipboard library, next to the search field. Back returns to the compact clipboard picker.

## Search and selection

Search accepts words, quoted phrases, and filters. Filters can be combined with text:

```text
type:image after:2026-01-01
pinned:true
collection:"Project samples" invoice
```

`before:` and `after:` include the specified date, in local time. `type:text` includes HTML and RTF entries. The filter button offers common searches without typing their syntax.

Browse supports multiple selection. Copy and Paste require one selected entry. Compare requires two text entries. Paste format offers original content, plain text, a single line, formatted JSON, or an HTTP/HTTPS URL with common tracking parameters removed. Transformed output does not replace the saved entry.

## Collections and paste queue

Select entries in Browse, then add them to a collection. Entries can belong to several collections. Deleting a collection keeps its clipboard entries. Show collection returns to Browse with its filter applied.

Add a selection to Paste queue to arrange a sequence. Paste next removes one entry after a successful paste. Paste together combines text entries using a newline, tab, or blank line. A changed target window prevents automatic paste. The queue is kept only for the current WindowSill session.

## Privacy

Pause capture for 15 minutes, one hour, or until resumed. The pause is saved, including its time interval, so copies made during that interval are not collected later from Windows history. Pausing SillClip does not stop Windows from recording its own history.

Presentation mode hides clipboard previews in the bar and popup until explicitly revealed. It does not detect a Discord stream or stop capture. These controls are also available in the extension settings.

## Storage and backup

Storage lists saved payloads by size. Copies in both the pin and history stores share one row, with their combined size. Pinned entries are excluded from cleanup unless Include pinned items in deletion is enabled. Date boundaries are inclusive. Deletion requires confirmation.

Backup exports a local `.sillclip` file encrypted with a passphrase. Export requires at least 12 characters and confirmation. Keep the passphrase separately: it cannot be recovered. Import verifies the complete archive before adding entries and keeps existing payloads.

File references are excluded by default. If included, local drive paths are restored as text; file contents are never exported. Remote and device paths are rejected. Import only backups you trust, since text, links, and rich text can contain sensitive or unwanted content.

There is no archive item-count limit. Each encrypted record has a 32 MiB safety limit. The storage total covers saved clipboard payloads, excluding library metadata and temporary transfers. See [storage security](storage-security.md) for encryption details and limitations.
