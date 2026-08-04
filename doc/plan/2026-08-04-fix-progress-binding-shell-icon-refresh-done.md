# Fix Progress Binding and Explorer Icon Refresh

## Goal

Prevent the extraction window from crashing when a progress task row is laid out, and make the Explorer context-menu icon use the same current archive icon as the application.

## Work Items

1. Make all progress-row bindings explicitly one-way and cover the layout with an STA WPF regression test.
2. Install `EasyUnpack.ico` as an independent application file, return it from the Explorer command, and notify Explorer about association changes after installation.
3. Build, overwrite-install, and verify the installed application, icon source, and a copied archive sample without modifying the user source archive.

## Constraints

- Keep version `1.0.3`.
- Do not push `main`, change `v1.0.3`, or modify the GitHub Release.
- Do not write passwords or archive contents to diagnostics.

## Verification Record

- Passed: the STA WPF regression test lays out the progress row and verifies the `ProgressPercent`, `IsProgressIndeterminate`, and `ProgressText` bindings are explicitly one-way. The full Release test suite passed (77 tests), and the Release solution build passed with no warnings or errors.
- Passed: the local `1.0.3+d72e0bd` installer was built and installed over the existing application. The installed ICO hash matches the source asset, the installed shell-extension DLL matches the package, and its registered `IExplorerCommand::GetIcon` returns `C:\Program Files\EasyUnpack\EasyUnpack.ico` after Explorer restart.
- Passed: the retained nested ZIP was subsequently verified read only with 7-Zip, which immediately requested a password. The replacement password flow treats that state as confirmed, does not launch Bandizip validation, and pauses the existing job for input. A disposable hard-link run of the installed application confirmed no Bandizip process and no residual engine process after shutdown. The original sample and retained staging directory were not modified.
