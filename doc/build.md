# Build and Install

Build the managed projects and run tests:

```powershell
dotnet test EasyUnpack.slnx
dotnet build EasyUnpack.slnx
```

The installer build is Windows x64 only. It requires the .NET SDK, Visual Studio C++ Build Tools with the Windows SDK, and Inno Setup 6. After installing those prerequisites, run:

```powershell
.\tools\build-installer.ps1 -Configuration Release
```

The script looks for `ISCC.exe` in `PATH` before checking the usual Inno Setup 6 directory. A nonstandard location can be supplied explicitly:

```powershell
.\tools\build-installer.ps1 -InnoCompilerPath 'D:\Tools\Inno Setup 6\ISCC.exe'
```

The script publishes a self-contained application to `artifacts\publish\app`, builds the native Explorer command DLL, and emits the installer under `artifacts\installer`. Install the generated setup from an elevated account. The installer registers an `IExplorerCommand` under `HKLM\Software\Classes\*\shell\EasyUnpack` and `Directory\shell\EasyUnpack`, which is a first-level Windows 10 Explorer context-menu command.

UI smoke tests can isolate settings and password data from the normal `%AppData%\EasyUnpack` directory by setting `EASYUNPACK_DATA_DIRECTORY` for the launched application process. This override is intended for automated verification; omit it for normal use.
