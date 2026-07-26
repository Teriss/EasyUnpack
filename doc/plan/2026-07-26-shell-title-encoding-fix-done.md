# Shell Title Encoding Fix

## Goal

Ensure the Explorer context-menu command title is displayed as Chinese instead of mojibake when returned by the native `IExplorerCommand` implementation.

## Implementation

The native title now uses C++ Unicode escape sequences so it is independent of the compiler source-code page.

## Verification

- Native Release x64 build completed with 0 warnings and 0 errors.
- Installer rebuilt and reinstalled after stopping Explorer to release the previous DLL.
- COM smoke call to `IExplorerCommand::GetTitle` returned `使用 EasyUnpack 自动解压`.
