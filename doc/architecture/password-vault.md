# Password Vault

EasyUnpack stores only passwords that have been supplied successfully or explicitly entered in the password-vault window. The ordered entry list is the exact extraction attempt order: manual additions are inserted first, drag moves are saved atomically, and a successfully used password is promoted to the first position. Duplicate values are rejected without changing existing statistics or order.

The list shows masked values only. A selected value can be revealed temporarily in the editor at the user's request. Passwords are never written to logs, exceptions, task status, screenshots, or test snapshots. Extraction jobs use the candidate snapshot captured when that job starts; later vault edits only affect subsequent jobs.

The default vault format is a JSON envelope under `%AppData%\EasyUnpack\passwords.json`. Users may choose the existing master-password protection path; this uses PBKDF2-SHA256 with 600,000 iterations and AES-256-GCM. A protected vault must be unlocked before it can be changed, so a failed unlock cannot overwrite protected entries with a plaintext vault.

Payload version 2 stores the ordered entries in a versioned object while retaining the existing outer encryption envelope. Legacy array payloads are loaded once using the former last-success and success-count ordering; the next save upgrades them to version 2.
