param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$InnoCompilerPath,
    [string]$VisualStudioDeveloperCommandPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repositoryRoot 'artifacts\publish'
$applicationOutput = Join-Path $publishRoot 'app'
$shellOutput = Join-Path $publishRoot 'shell'
$vsDevCommand = if ([string]::IsNullOrWhiteSpace($VisualStudioDeveloperCommandPath)) {
    'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
} else {
    $VisualStudioDeveloperCommandPath
}

dotnet publish (Join-Path $repositoryRoot 'src\EasyUnpack.App\EasyUnpack.App.csproj') --configuration $Configuration --runtime win-x64 --self-contained true --output $applicationOutput

if (!(Test-Path -LiteralPath $vsDevCommand)) { throw "Visual C++ Build Tools were not found: $vsDevCommand" }
$nativeCommand = '"' + $vsDevCommand + '" && msbuild "' + (Join-Path $repositoryRoot 'src\EasyUnpack.ShellExtension\EasyUnpack.ShellExtension.vcxproj') + '" /p:Configuration=' + $Configuration + ' /p:Platform=x64 /m'
& cmd.exe /d /c $nativeCommand
if ($LASTEXITCODE -ne 0) { throw 'Shell Extension build failed.' }

New-Item -ItemType Directory -Path $shellOutput -Force | Out-Null
Copy-Item (Join-Path $repositoryRoot "src\EasyUnpack.ShellExtension\x64\$Configuration\EasyUnpack.ShellExtension.dll") $shellOutput -Force

$innoCompiler = if (![string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $InnoCompilerPath
} else {
    $pathCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($null -ne $pathCommand) {
        $pathCommand.Source
    } else {
        Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    }
}
if (!(Test-Path -LiteralPath $innoCompiler)) { throw 'Inno Setup 6 was not found. Install it, add ISCC.exe to PATH, or pass -InnoCompilerPath.' }
& $innoCompiler (Join-Path $repositoryRoot 'installer\EasyUnpack.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
