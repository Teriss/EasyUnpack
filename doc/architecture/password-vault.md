# Password Vault

EasyUnpack stores only passwords that have been supplied successfully or explicitly entered in the password-vault window. The ordered entry list is the exact extraction attempt order: manual additions are inserted first, drag moves are saved atomically, and a successfully used password is promoted to the first position. Duplicate values are rejected without changing existing statistics or order.

The list shows masked values by default. The vault window has a list-wide reveal toggle for temporary in-memory viewing; closing the window resets the list to masked values. A selected value can also be revealed temporarily in the editor at the user's request. Passwords are never written to logs, exceptions, task status, screenshots, clipboard contents, or test snapshots. Extraction jobs use the candidate snapshot captured when that job starts; later vault edits only affect subsequent jobs.

The default vault format is a JSON envelope under `%AppData%\EasyUnpack\passwords.json`. Users may choose the existing master-password protection path; this uses PBKDF2-SHA256 with 600,000 iterations and AES-256-GCM. A protected vault must be unlocked before it can be changed, so a failed unlock cannot overwrite protected entries with a plaintext vault.

Payload version 2 stores the ordered entries in a versioned object while retaining the existing outer encryption envelope. Legacy array payloads are loaded once using the former last-success and success-count ordering; the next save upgrades them to version 2.

The Start Menu EasyUnpack entry opens the settings window without arguments, which is the supported entry point for engine configuration and the password vault when no archive is being extracted. List-wide reveal is an in-memory window state only: it starts masked, can be toggled for the current window, and is masked again when that window closes. The selected-entry eye control remains independent. The bundled icon has transparent corners and its ICO contains 16, 24, 32, 48, 64, 128, and 256 pixel images.
