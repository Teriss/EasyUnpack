# Hierarchical Operation Progress, Cancellation, and Nested Password Flow

## Goal

Show independent, retained progress for each archive operation and nested archive; stop engine processes reliably on cancellation; request nested passwords in place without retrying the outer archive or falling through to another engine.

## Constraints

- Keep version 1.0.3 and work local only.
- Preserve source archives and user-owned staging directories.
- Do not expose passwords or raw engine output.

## Implementation Record

- Added retained archive-operation events with parent archive identifiers and precision-aware progress.
- Added 7-Zip/NanaZip fragmented native-progress parsing, Bandizip listing-total parsing, and estimate/indeterminate display modes.
- Password-required validation now short-circuits fallback engines and uses an in-place asynchronous password provider.
- Added per-task cancellation, shutdown cancellation, process-tree termination, Windows Job Object protection, and preservation of incomplete staging output.
- Added focused operation, password-short-circuit, cancellation, nested hierarchy, and adapter parsing tests.

## Verification

- `dotnet test EasyUnpack.slnx --configuration Release` passed: 82 tests.
- `dotnet build EasyUnpack.slnx --configuration Release` passed with no warnings.
- A new local installer was built and elevated overwrite installation succeeded. The installed executable SHA-256 matches the published executable.
- The preserved `ntr14.zip` was read only: 7-Zip reached its password prompt immediately. A disposable hard-link run of the installed application remained open for password input, launched no Bandizip process, and left no engine process after shutdown. The temporary directory was moved to the recycle bin; the preserved staging directory was not modified.
