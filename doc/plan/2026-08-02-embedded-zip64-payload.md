# Embedded ZIP64 Payload Support

## Problem

Large ZIP payloads appended to media files use ZIP64 metadata when a central-directory offset or size exceeds the classic 32-bit ZIP limit. The current embedded-ZIP probe only reads classic end-of-central-directory fields, so valid ZIP64 payloads are reported as unknown.

## Implementation

- Detect a ZIP64 locator immediately before the classic end-of-central-directory record when classic fields are saturated.
- Parse and validate the ZIP64 end record using checked 64-bit offsets and single-disk invariants.
- Resolve ZIP64 entry metadata needed to verify the first local file header.
- Preserve the existing rejection of unstructured or multi-disk ZIP markers.
- Document ZIP64 payload handling at the archive-engine boundary.

## Tests And Verification

- Add focused tests for a prefixed ZIP64 payload whose saturated classic offset represents the greater-than-4-GiB metadata path without allocating a multi-gigabyte test file.
- Add malformed ZIP64 and classic ZIP regression coverage where useful.
- Verify the supplied MP4 reports the validated payload offset and length.
- Run `dotnet test EasyUnpack.slnx` and `dotnet build EasyUnpack.slnx --configuration Release`.
- Update managed application, core library, installer, and README metadata for version 1.0.3.
- Build and install the final package from the release commit, then verify the installed version and shell registration.
- Commit and push `main`, create tag `v1.0.3`, publish the installer in a GitHub Release, and verify it anonymously.

## Safety

- Do not modify, move, recycle, or fully extract the supplied source file during recognition verification.
- Use checked arithmetic for every untrusted on-disk offset and length.
- Do not invoke archive tools outside an engine adapter.
- Do not expose GitHub credentials in command output, logs, release notes, or repository files.

## Verification Progress

- The supplied 6,031,102,583-byte file is recognized as ZIP with payload offset `915459573` and length `5115624562` without extraction or source modification.
- `dotnet test EasyUnpack.slnx --configuration Release`: 64 passed, 0 failed.
- `dotnet build EasyUnpack.slnx --configuration Release`: 0 warnings, 0 errors.
- Test-created temporary directories and the one-time real-file probe runner are absent.
