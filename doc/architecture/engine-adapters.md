# Archive Engine Adapters

`IArchiveEngine` owns all command-line invocation. The orchestration layer only sees typed recognition, validation, and extraction results. Recognition distinguishes a readable archive, a password-protected archive, a definite non-archive, and an unsupported or corrupt input. Adapter output and exit-code parsing remain private to the adapter boundary.

The application discovers supported adapters before scanning selected paths. Built-in signatures locate known file-head formats and exact embedded ZIP/ZIP64 ranges. Only a directly selected file that remains unknown is offered to adapters in preferred-engine order; recursive folder and nested-output scans never launch a process for every unknown file. This preserves predictable scan cost while allowing engines to recognize formats that are not in the built-in signature catalog.

Every candidate is recognized and fully validated again immediately before extraction. Embedded ranges are materialized once in the extraction job's staging directory, and that same canonical temporary file is reused for recognition, password validation, and extraction across fallback adapters. A generic engine-detected format is always treated as a single volume. Source archives remain untouched until extraction output has been published successfully.

Working adapters are `SevenZipEngine` (used for 7-Zip and NanaZip), `WinRarEngine`, and `BandizipEngine`. Each uses `ProcessStartInfo.ArgumentList` so archive paths cannot change command parsing. These tools require their documented password command-line switches; those arguments are never logged or surfaced in the UI.

When a split 7-Zip archive has been renamed, the volume resolver creates temporary hard-link aliases under the job staging directory. This handles both `name.7z.001.jpg` and `name.001.jpg` while preserving the downloaded source names and contents. The aliases are removed with the staging directory after the job completes or fails.

Discovery recognizes 7-Zip, NanaZip, WinRAR, Bandizip, PeaZip, WinZip, HaoZip, and 360压缩 from configured environment variables, `PATH`, and common installation directories. When PeaZip contains its usual bundled 7-Zip backend at `res\bin\7z\7z.exe`, that backend is selected through the verified 7-Zip adapter. A discovered tool is not selected for extraction until it has a complete `IArchiveEngine` adapter.

The 7-Zip adapter always closes standard input for password-free probes so an encrypted archive cannot leave a hidden console prompt waiting indefinitely. Password-bearing commands are never included in application logs or surfaced through task status.
All adapters use the shared process runner, which closes standard input and terminates the complete child process tree on cancellation. Lightweight recognition has a 30-second deadline; full validation and extraction remain governed by the job cancellation token because large archives may legitimately take longer.
WinZip, HaoZip, and 360 compression are currently detection-only because no complete, tested command-line adapter is registered for them. Automatic extraction selects only a verified adapter.

ZIP and ZIP64 payloads embedded near the end of another file are detected from their end-of-central-directory, locator, and central-directory structure. ZIP64 recognition uses checked 64-bit offsets, accepts only a single-disk layout, and verifies the first local file header before reporting a payload. The payload byte range remains core metadata; before an adapter is invoked, orchestration copies that range into the job staging directory under a canonical `.zip` name. This temporary normalization never modifies the selected source, and the source remains eligible for recycling only after extraction output has been published successfully.
