# Extraction Progress Reporting

Archive engines are isolated behind adapters and commonly provide no reliable total size or file count. EasyUnpack therefore monitors the temporary extraction directory at a low frequency while the engine process runs. The task row reports observed file count, bytes written, and elapsed time; the shared activity bar is indeterminate and animated. A percentage is never fabricated from an archive format guess.

The monitor is cancellation-aware, tolerates files that are still being written, and stops before output normalization and publication. It reports only aggregate counts and durations, never paths, archive contents, command output, or passwords. Nested archive extraction reuses the same progress channel so the UI continues to show activity after the outer archive has been opened.
