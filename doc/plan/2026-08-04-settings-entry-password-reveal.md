# Settings Entry And Password Reveal Fix

## Implementation

- Add an EasyUnpack Start Menu shortcut that launches the existing no-argument settings window.
- Add a password-vault list-wide reveal toggle that is masked by default and resets when the window is reopened.
- Preserve the existing selected-entry editor reveal control, password protection, persistence format, and shell extension behavior.
- Update the password-vault security and user documentation without exposing passwords.

## Tests And Verification

- Add WPF coverage for masked, revealed, re-masked, selected-entry, and protected-vault behavior without writing secrets to test output.
- Run Release tests and build, compile the installer, overwrite the installed 1.0.3 application, and verify the Start Menu shortcut and installed shell registration.
- Remove only temporary UI test directories, installer logs, and diagnostics created by this work.

## Git

- Keep version 1.0.3 and the existing v1.0.3 tag and GitHub Release unchanged.
- Commit the implementation and completion record, then push main.
- Preserve the unrelated user-owned untracked plan file.

