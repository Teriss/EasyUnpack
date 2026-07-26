# Runtime Smoke and Recycle Fix

## Goal

Run the installed EasyUnpack workflow against a disguised archive and ensure successful extraction moves the source archive to the Windows recycle bin.

## Work Items

1. Install the generated setup and verify first-level Explorer registrations.
2. Run a ZIP renamed to `.jpg` through the installed WPF application.
3. Correct and harden source recycling if the runtime smoke test exposes a failure.
4. Rebuild, reinstall, rerun the smoke test, and record the evidence.

## Verification

- Setup installation exited with code 0.
- `*` and `Directory` registrations point to the `IExplorerCommand` CLSID.
- 46 managed tests passed; managed build completed with 0 warnings and 0 errors.
- Native Release x64 build and Inno Setup compilation succeeded.
- The installed app extracted the disguised ZIP, published `payload`, removed the source from its original directory, and reported `已完成 1 个解压任务` through UI Automation.
- The P/Invoke entry point is `SHFileOperation`; the recycler verifies source removal and has a managed recycle-bin fallback.
