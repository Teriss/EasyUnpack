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
- Initial installer and local installation checks succeeded; the final release artifact will be rebuilt from the release commit so its informational version contains the matching commit hash.
