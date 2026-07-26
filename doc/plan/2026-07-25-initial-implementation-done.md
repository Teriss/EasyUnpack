# Initial EasyUnpack Implementation

## Goal

Build a Windows 10 automatic extraction application launched from a first-level Explorer context-menu command. It detects installed archive tools, handles disguised archive names, nested archives, password retries, naming cleanup, and transactional source cleanup.

## Work Items

1. Establish repository rules, documentation, solution, and tests.
2. Implement managed core models, archive signature probing, output naming, engine discovery, and a 7-Zip adapter.
3. Implement the WPF task and settings entry points.
4. Add a native `IExplorerCommand` shell-extension project that follows the PowerToys process-isolation pattern and transfers selection through a named pipe.
5. Add installer registration, remaining engine adapters, integration tests, and end-to-end verification.

## Completion Rule

Rename this file with the `-done` suffix only after every work item is implemented and verified on Windows 10.

## Final Verification

All five work items are implemented and verified:

- `dotnet test EasyUnpack.slnx --no-restore`: 46 passed, 0 failed.
- `dotnet build EasyUnpack.slnx --no-restore`: 0 warnings, 0 errors.
- Native Shell Extension Release x64 build: 0 warnings, 0 errors.
- Inno Setup installer generated at `artifacts/installer/EasyUnpack-Setup.exe`.
- Installer smoke test confirmed the application, shell DLL, CLSID, first-level `*` and `Directory` registrations, and clean uninstall.
- COM activation smoke test succeeded.
- Installed runtime smoke test processed a ZIP renamed to `.jpg`, published the `payload` directory, moved the source archive to the recycle bin, and reported completion through WPF UI Automation.

During runtime smoke testing, the recycle-bin P/Invoke entry point was corrected from `ShFileOperation` to the Windows export `SHFileOperation`. The recycler now verifies that each source path disappeared and uses `Microsoft.VisualBasic.FileIO` as a fallback when Explorer reports an incomplete move.

The supported execution adapters are 7-Zip/NanaZip, WinRAR, and Bandizip; PeaZip is supported through its bundled 7-Zip backend. WinZip, HaoZip, and 360压缩 are detected for visibility and manual configuration, but are not selected for automatic extraction until a complete, tested command-line adapter is available. When no executable adapter is found, the application prompts the user to install one or configure its path/environment variable.
