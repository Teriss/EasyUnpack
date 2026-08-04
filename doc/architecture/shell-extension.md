# Explorer Context Menu

EasyUnpack follows the PowerToys isolation boundary: the native Explorer command only obtains selected filesystem paths and starts the WPF process. It does not inspect files, load configuration, or invoke archive software.

The x64 command uses `IExplorerCommand` with CLSID `{A7B99305-3DA8-4EAB-965E-72070CDBA1A8}`. It sends a length-prefixed UTF-16 selection to `EasyUnpack.App.exe --pipe <unique-name>` through a one-use named pipe. The pipe ACL grants access only to its creating user. The installer registers it for files and directories under the first-level Windows 10 context menu.

`IExplorerCommand::GetIcon` returns the installed `EasyUnpack.ico` file. Keeping this ICO separate from the executable gives Explorer a stable, current icon source after upgrades. The installer copies the ICO with the application and sends the standard association-change notification. This method only resolves the module path; it does not read settings or archive data.
