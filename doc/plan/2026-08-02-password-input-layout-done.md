# Password Input Layout Fix

## Problem

The extraction password dialog uses a fixed 240-pixel window with a star-sized password row. A long archive name wraps onto multiple lines and consumes the available grid height, collapsing the password box and clipping the action buttons.

## Implementation

- Keep archive names to one ellipsized line while preserving the full name in the tooltip.
- Give the password row an auto-sized, visible input and increase the dialog height enough for themed controls at common display scaling factors.
- Focus the password box when the dialog opens.
- Apply the same non-collapsing password-row layout to the related master-password dialogs where needed.

## Tests And Verification

- Add focused UI layout coverage that measures the rendered password controls and action buttons with a long archive name.
- Run `dotnet test EasyUnpack.slnx`.
- Run `dotnet build EasyUnpack.slnx --configuration Release`.
- Update application, core library, installer, and README metadata for version 1.0.2.
- Rebuild and install the application, then verify the installed version, dialog layout, and shell registration.
- Remove confirmed test temporary files without touching user archives or unrelated worktree files.
- Commit and push `main`, create tag `v1.0.2`, publish the installer in a GitHub Release, and verify the public asset.

## Safety

- Do not expose passwords or GitHub credentials in logs, test output, release notes, or repository files.
- Do not remove source archives or unrelated user-owned files.
- Keep this plan open until installation and public release verification have completed successfully.

## Verification Progress

- `dotnet test EasyUnpack.slnx --configuration Release`: 62 passed, 0 failed.
- `dotnet build EasyUnpack.slnx --configuration Release`: 0 warnings, 0 errors.
- Final installer version: 1.0.2; size: 46,452,238 bytes; SHA-256: `65768DDC7AE235374C6854AC91AE97082D12E07079AF239BBAFC42425FFA6ED0`.
- Installed application version: `1.0.2+a7514e4594a23a00e0795913ed79a2e892cabdcf`; the installed DLL matches the release build.
- Explorer was restarted successfully; file and directory menu text and the registered shell-extension path were verified.
- Test-created temporary directories and installer logs are absent.
- Commit `a7514e4`, remote `main`, and annotated tag `v1.0.2` point to the release source.
- GitHub Release `EasyUnpack 1.0.2` is public with `EasyUnpack-Setup.exe` in the `uploaded` state.
- Anonymous verification confirmed the UTF-8 Chinese release text, tag target, asset metadata, and a 1,024-byte HTTP 206 range download.
