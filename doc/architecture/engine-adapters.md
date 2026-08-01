# Archive Engine Adapters

`IArchiveEngine` owns all command-line invocation. The orchestration layer only sees validation and extraction results.

Working adapters are `SevenZipEngine` (used for 7-Zip and NanaZip), `WinRarEngine`, and `BandizipEngine`. Each uses `ProcessStartInfo.ArgumentList` so archive paths cannot change command parsing. These tools require their documented password command-line switches; those arguments are never logged or surfaced in the UI.

When a split 7-Zip archive has been renamed, the volume resolver creates temporary hard-link aliases under the job staging directory. This handles both `name.7z.001.jpg` and `name.001.jpg` while preserving the downloaded source names and contents. The aliases are removed with the staging directory after the job completes or fails.

Discovery recognizes 7-Zip, NanaZip, WinRAR, Bandizip, PeaZip, WinZip, HaoZip, and 360压缩 from configured environment variables, `PATH`, and common installation directories. When PeaZip contains its usual bundled 7-Zip backend at `res\bin\7z\7z.exe`, that backend is selected through the verified 7-Zip adapter. A discovered tool is not selected for extraction until it has a complete `IArchiveEngine` adapter.

The 7-Zip adapter always closes standard input for password-free probes so an encrypted archive cannot leave a hidden console prompt waiting indefinitely. Password-bearing commands are never included in application logs or surfaced through task status.
WinZip, HaoZip, and 360 compression are currently detection-only because no complete, tested command-line adapter is registered for them. Automatic extraction selects only a verified adapter.

ZIP payloads embedded near the end of another file are detected from their end-of-central-directory and central-directory structure. The payload byte range remains core metadata; before an adapter is invoked, orchestration copies that range into the job staging directory under a canonical `.zip` name. This temporary normalization never modifies the selected source, and the source remains eligible for recycling only after extraction output has been published successfully.
