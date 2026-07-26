# README and GitHub 1.0.0 release

## Goal

Complete the humorous Chinese README, identify the project and its actual Windows workflow, and publish a reproducible 1.0.0 installer release to the requested GitHub repository.

## Work Items

1. Preserve the existing README opening and expand it with features, installation, right-click usage, supported engines, password-vault behavior, troubleshooting, development, and privacy notes.
2. Set managed application and installer metadata to version 1.0.0.
3. Verify the release with full tests/build and a clean installer build.
4. Initialize/configure Git using `https://github.com/Teriss/EasyUnpack.git`, commit source and documentation while excluding build output and local test artifacts.
5. Push the default branch and create GitHub release/tag `v1.0.0` with the installer attached.

## Completion Rule

Rename this file to `2026-07-27-readme-github-release-done.md` only after the remote push and release asset upload are confirmed. If GitHub credentials or network access are unavailable, keep the plan open and report the exact blocker.

## Verification

- `dotnet test EasyUnpack.slnx --no-restore`: 56 passed.
- `dotnet build EasyUnpack.slnx --configuration Release`: 0 warnings, 0 errors.
- Installer built successfully with Inno Setup; application and installer metadata report 1.0.0.
- Git repository initialized with `main`, pushed to `https://github.com/Teriss/EasyUnpack.git`.
- Tag `v1.0.0` pushed successfully.
- GitHub Release `EasyUnpack 1.0.0` created with `EasyUnpack-Setup.exe` attached; public download returned HTTP 200 and the expected 46,445,227-byte asset.
