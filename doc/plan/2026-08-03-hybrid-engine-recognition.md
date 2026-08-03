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
