#define AppName "EasyUnpack"
#define AppVersion "1.0.0"
#define AppPublisher "EasyUnpack"
#define AppExeName "EasyUnpack.App.exe"
#define ShellClsid "{{A7B99305-3DA8-4EAB-965E-72070CDBA1A8}"

[Setup]
AppId={{F3B94CEE-6577-4DB8-BCBB-D1EF3E67C6C4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\EasyUnpack
DefaultGroupName=EasyUnpack
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=EasyUnpack-Setup
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}

[Files]
Source: "..\artifacts\publish\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "..\artifacts\publish\shell\EasyUnpack.ShellExtension.dll"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKLM; Subkey: "Software\Classes\CLSID\{#ShellClsid}"; ValueType: string; ValueName: ""; ValueData: "EasyUnpack Explorer Command"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\CLSID\{#ShellClsid}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\EasyUnpack.ShellExtension.dll"
Root: HKLM; Subkey: "Software\Classes\CLSID\{#ShellClsid}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKLM; Subkey: "Software\Classes\*\shell\EasyUnpack"; ValueType: string; ValueName: "MUIVerb"; ValueData: "使用 EasyUnpack 自动解压"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\*\shell\EasyUnpack"; ValueType: string; ValueName: "ExplorerCommandHandler"; ValueData: "{#ShellClsid}"
Root: HKLM; Subkey: "Software\Classes\Directory\shell\EasyUnpack"; ValueType: string; ValueName: "MUIVerb"; ValueData: "使用 EasyUnpack 自动解压"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\Directory\shell\EasyUnpack"; ValueType: string; ValueName: "ExplorerCommandHandler"; ValueData: "{#ShellClsid}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "打开 EasyUnpack 设置"; Flags: nowait postinstall skipifsilent
