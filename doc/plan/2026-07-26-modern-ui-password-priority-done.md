# Modern UI and Password Priority

## Goal

Modernize the WPF application with the built-in .NET 10 Fluent theme, expand the task surface, show extraction output paths, and add an editable drag-ordered password vault.

## Work Items

1. Upgrade the password-vault payload to a versioned ordered format while preserving legacy data.
2. Add password add/edit/move semantics and success-driven promotion.
3. Redesign the main, settings, password-vault, and prompt windows with a shared Fluent visual system and Chinese UI text.
4. Show output paths and recycle warnings in the task grid.
5. Generate and wire a multi-resolution application icon into the app, installer, and Explorer command.
6. Run managed tests/builds, UI smoke checks, installer build, and installed runtime verification.

## Completion Rule

Rename this plan with the `-done` suffix only when every implemented work item is verified. If image generation remains externally blocked, keep this filename without the suffix and record the blocker.

## Implementation Status

Completed:

- .NET 10 system Fluent theme and shared balanced-density design resources.
- Modern Chinese main, engine-settings, password-vault, password-prompt, master-password setup, and unlock windows.
- Full-width task grid with source path, output directory, live property notifications, completed/warning/failure states, and path tooltips.
- Ordered password-vault payload version 2 with legacy migration, atomic save, add/edit/delete, duplicate rejection, drag priority, success promotion, and encrypted round trips.
- Password list automation names are masked; plaintext is exposed only after the user activates the reveal control.
- 7-Zip encrypted-archive probing no longer waits for interactive console input.
- Windows source recycling preflights every volume for delete sharing so locked sources immediately produce a warning and are not partly recycled.
- Release installer rebuilt and installed; the Windows 10 first-level file and directory Explorer commands remain registered in Chinese.

Verified on 2026-07-27:

- `dotnet test EasyUnpack.slnx --no-restore`: 53 passed.
- `dotnet build EasyUnpack.slnx --no-restore --configuration Release`: 0 warnings, 0 errors.
- UI Automation at the active Windows 150% scale covered password-vault open/add/edit/reveal/hide/duplicate validation/drag/reopen persistence, master-password setup/unlock, and encrypted-archive password prompting.
- Main and password-vault windows were checked at default and minimum dimensions with no overlap; screenshots are under `artifacts/ui-*.png`.
- Real disguised ZIP extraction displayed the published output path and completed status.
- A locked disguised ZIP displayed the published output path with `已完成，源文件未回收`, retained the source, and returned without hanging.
- The installed app extracted a disguised `.mp4` archive successfully, and both Explorer registry commands resolve to `使用 EasyUnpack 自动解压`.
- `App.xaml` retains `ThemeMode="System"`; the current host is in light mode. A visual dark-mode pass was not forced because that would modify the user's Windows theme setting.

Verified on 2026-08-04:

- Generated `src/EasyUnpack.App/Assets/EasyUnpack-1024.png` with transparent corners and a high-contrast archive/checkmark mark; visual inspection confirmed centered geometry and clean edges.
- Generated `src/EasyUnpack.App/Assets/EasyUnpack.ico` with 16, 24, 32, 48, 64, 128, and 256 pixel PNG frames.
- Wired the ICO into the WPF application and returned the installed application icon from `IExplorerCommand::GetIcon`.
- Updated README and password-vault/shell-extension architecture notes with the settings entry, reveal boundary, and icon behavior.
