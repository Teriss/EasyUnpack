# Progress Indicator and Application Icon Fix

## Goal

Make the packaged application icon match the archive icon shown in the main window, and make extraction activity observable so a running task cannot look frozen.

## Scope

- Regenerate the transparent multi-resolution application icon from the main-window archive mark and keep WPF, Start Menu, and Explorer command icon sources consistent.
- Expose extraction progress when an engine reports it, including a determinate percentage and an indeterminate activity state when only output activity is available.
- Keep the existing task status, cancellation, publication, password, and source-retention behavior unchanged.
- Never write passwords or archive contents to logs or UI history.

## Verification

- Run focused tests for progress parsing and task property notifications, then `dotnet test EasyUnpack.slnx --configuration Release` and `dotnet build EasyUnpack.slnx --configuration Release`.
- Inspect the generated icon at 1024px and verify ICO frames and application version resources.
- Build the installer for local verification only. Do not push, move the `v1.0.3` tag, or modify the GitHub Release until explicitly requested.

## Completed Verification

- The 1024px source now uses the exact archive glyph shown in the main window, with transparent corners; the ICO contains 16, 24, 32, 48, 64, 128, and 256 pixel frames.
- A shared WPF Window style loads that ICO for the main window and all dialogs; the application executable and Explorer command continue to use the same resource.
- Extraction progress monitors the temporary output directory and reports file count, bytes written, and elapsed time without exposing paths, contents, or secrets. The status glyph rotates and the activity bar is indeterminate while the engine is running.
- Added a focused extraction progress test. `dotnet test EasyUnpack.slnx --configuration Release` passes 77 tests; `dotnet build EasyUnpack.slnx --configuration Release` passes with 0 warnings and 0 errors.
- The Release installer was built and a local overwrite install completed with exit code 0. The installed app launched without arguments and remained running, confirming the packaged icon resource loads.
