# EasyUnpack Engineering Rules

## Workflow

- Read the relevant code and documentation before editing.
- Preserve user changes. Do not use destructive Git operations.
- Write each implementation plan to `doc/plan/YYYY-MM-DD-name.md` before its code changes begin.
- Rename a plan to `YYYY-MM-DD-name-done.md` only after all listed implementation, tests, and verification are complete.
- Update architecture or security decisions in `doc/` when behavior changes.

## Product Safety

- Never write passwords to logs, exceptions, test snapshots, or UI history.
- Do not delete or recycle source archives until the complete extraction job has been published successfully.
- Keep shell-extension work minimal. It may only transfer selected paths to the app; it must not inspect archives, read configuration, or extract files in Explorer.
- All archive tools must be accessed through an engine adapter. Tool-specific commands must not leak into orchestration code.

## Quality Gates

- Add focused tests for core behavior changes.
- Run `dotnet test` and `dotnet build` before handing off managed-code changes.
- Use UTF-8 text files and keep user-facing strings localized through the application resource layer once it exists.
