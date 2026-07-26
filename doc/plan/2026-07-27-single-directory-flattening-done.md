# Single-directory output flattening

## Goal

Remove redundant directory chains from extracted output so users reach real content without opening multiple folders, while retaining the archive-named published root.

## Work Items

1. Normalize outer and nested extraction staging trees before publication.
2. Collapse a directory only when it contains no files and exactly one ordinary child directory.
3. Repeat collapsing at the root and recursively inside meaningful branches; do not traverse or collapse reparse points.
4. Keep directories that contain files, multiple child directories, or mixed content.
5. Add focused tests for deep wrappers, recursive branch normalization, valid-directory preservation, and nested archive output.
6. Run the full managed test and build gates, then document the behavior.

## Completion Rule

Rename this file to `2026-07-27-single-directory-flattening-done.md` only after implementation and all verification pass.

## Verification

- Focused extraction and nested-archive tests: 7 passed.
- Full `dotnet test EasyUnpack.slnx --no-restore`: 56 passed.
- Release `dotnet build EasyUnpack.slnx --no-restore --configuration Release`: 0 warnings, 0 errors.
- Real disguised archive smoke test converted `A/B/C/payload.txt` to `<archive name>/payload.txt` and recycled the source.
- Installer rebuilt and installed successfully after restarting Explorer to release the shell-extension DLL.
- The installed app repeated the disguised `.mp4` deep-directory smoke test successfully.
