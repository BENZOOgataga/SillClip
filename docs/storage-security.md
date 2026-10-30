# Storage security

Local history, pins, library metadata, and import staging files use Windows DPAPI with `CurrentUser` scope. A program running as the same Windows user can decrypt them. This encryption does not protect clipboard contents from that user, malware running in their session, or applications receiving a paste.

Portable backups use AES-256-GCM with a 16-byte authentication tag. A random salt and PBKDF2-HMAC-SHA256 with 600,000 iterations derive the key from the passphrase. Each record authenticates the archive header, its position, and its length. An authenticated footer checks the entry count and detects truncation. Reordered records and trailing bytes are rejected.

Imports stage entries in DPAPI-encrypted files until every record and the footer authenticate. Imported IDs are regenerated and content signatures are recomputed. Existing payloads are not overwritten. If a commit fails, the importer attempts to remove newly added entries. An interrupted process can leave encrypted staging files or a partial import; reimport deduplicates existing content.

Custom application links and user activity data are restored as text. Optional file references are also restored as text, without resolving their paths. HTML and RTF are retained for an explicit paste into another application. Encryption authenticates the backup; it does not make its contents trustworthy.

Pause intervals and deletion markers are persisted separately from payloads. Deletion markers are saved before payload removal to prevent failed file deletion from restoring an entry to the visible library. If privacy metadata cannot be loaded, capture remains paused and previews remain hidden. A failed privacy-setting write displays a warning: session protection may not persist across a restart.

Passphrases are not saved. The backup view clears its passphrase fields after an operation or when closed. Temporary plaintext byte buffers are cleared where the cryptography and storage code owns them. Managed strings and Windows clipboard buffers cannot be reliably erased by this extension.

Presentation mode hides previews in SillClip. WindowSill capture protection depends on the recording tool and capture mode. Neither feature removes clipboard data from Windows history or from another application's memory.

Tests cover incorrect passphrases, modified headers and ciphertext, truncated and reordered records, invalid lengths, rejected file paths, metadata corruption, failed import commits, and persisted privacy state. These tests do not establish that the implementation has no vulnerabilities.
