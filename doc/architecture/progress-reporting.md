# Extraction Progress Reporting

Archive engines are isolated behind adapters and commonly provide no reliable total size or file count. Extraction reports retained typed operation updates for recognition, input preparation, validation, password handling, extraction, nested scanning, normalization, and publication. Each nested archive has its own archive ID and parent archive ID, so its progress never replaces a completed outer operation.

7-Zip and NanaZip report native progress through their progress stream; that is an exact percentage. Bandizip supplies an uncompressed listing total when available, so observed output bytes are displayed as an explicitly marked estimate. With no reliable total, the operation remains indeterminate while still showing file count, bytes written, and elapsed time. Active percentages are capped at 99%; only a successful operation reports 100%.

Each operation scope merges concurrent reports using `Exact > Estimated > Indeterminate`. Once a higher precision has been observed, later directory-monitor reports may update bytes, file count, and elapsed time but cannot downgrade the progress mode or move the active percentage backwards. Terminal states stop animation; only completion emits 100%.

The directory monitor is cancellation-aware, tolerates files that are still being written, and stops before output normalization and publication. It reports aggregate counts and durations, never command output or passwords.

The application uses the typed operation stream as its only UI progress source; the legacy top-level callback is retained for core compatibility but is not bound by the window. Data-grid rows and progress controls are retained and only their properties are updated. The task-row progress bindings are explicitly one-way. `ProgressPercent` remains a read-only display model property, so no WPF control can attempt to write a value back during layout.
