# Hybrid Engine Recognition And v1.0.3 Release

## Implementation

- Add typed archive recognition to every supported engine adapter, including bounded process execution and adapter-owned result classification.
- Discover supported engines before candidates, probe only directly selected unknown files, and retain built-in-only recursive and nested discovery.
- Validate every candidate through an ordered engine set before extraction, reuse one staged embedded payload, and preserve source archives on every failure.
- Add generic engine-detected format handling without applying known split-volume rules.
- Update the archive-engine architecture documentation.

## Tests And Verification

- Cover recognition states, engine ordering and fallback, direct-selection scope, password handling, cancellation, and malformed input preservation.
- Preserve existing embedded ZIP/ZIP64, split-volume, nested extraction, publication, recycling, and password regressions.
- Run `dotnet test EasyUnpack.slnx --configuration Release` and `dotnet build EasyUnpack.slnx --configuration Release`.
- Probe the supplied large sample read-only, build and install the final installer, and verify installed version and shell registration.
- Remove only temporary files created by this work.

## Release

- Keep version 1.0.3, commit and push the final implementation, replace the existing local and remote `v1.0.3` tag, and publish the installer in a public GitHub Release.
- Verify the public tag, UTF-8 release metadata, asset state, size, hash, and anonymous download.
- Never expose archive passwords or GitHub credentials, and never modify, extract, move, or recycle the supplied source sample.

## Verification Results

- Release tests passed: 75 passed, 0 failed; Release build completed with 0 warnings and 0 errors.
- The supplied 6,031,102,583-byte sample was probed read-only as ZIP at offset `915459573` with length `5115624562`.
- Installed application version: `1.0.3+60c8dd6a37b89dd7cd3a08cfe7e8b45f0978cd61`; shell registration and installed UI startup were verified.
- Installer size: 46,476,913 bytes; SHA-256: `39DEF414A401FB66AE57E05D02B8BE19B757513E0F7B1524DB7B194B56C01756`.
- Commit `60c8dd6` and the recreated `v1.0.3` tag were pushed. The public `EasyUnpack 1.0.3` Release contains the uploaded installer, valid UTF-8 Chinese text, and an anonymously verified HTTP 206 byte range.
- Test diagnostics, the isolated UI data directory, and installer logs created by this work were removed. The unrelated user-owned untracked plan file was not modified or committed.
