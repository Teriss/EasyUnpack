# Extraction Progress Reporting

Archive engines are isolated behind adapters and commonly provide no reliable total size or file count. Extraction reports retained typed operation updates for recognition, input preparation, validation, password handling, extraction, nested scanning, normalization, and publication. Each nested archive has its own archive ID and parent archive ID, so its progress never replaces a completed outer operation.

7-Zip and NanaZip report native progress through their progress stream; that is an exact percentage. Bandizip supplies an uncompressed listing total when available, so observed output bytes are displayed as an explicitly marked estimate. With no reliable total, the operation remains indeterminate while still showing file count, bytes written, and elapsed time. Active percentages are capped at 99%; only a successful operation reports 100%.

The directory monitor is cancellation-aware, tolerates files that are still being written, and stops before output normalization and publication. It reports aggregate counts and durations, never command output or passwords.

The task-row progress bindings are explicitly one-way. `ProgressPercent` remains a read-only display model property, so no WPF control can attempt to write a value back during layout.
