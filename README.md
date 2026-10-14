# Tibernia Launcher

Open-source Windows launcher for the Tibernia game network. It lets a player install one selected client or all available clients, verifies every downloaded file with SHA-256, repairs damaged files and shows percentage, transferred size and the active file while downloading.

This repository intentionally contains only the launcher. It does **not** contain game servers, maps, databases, player data, Tibia/OTClient assets or client packages.

## Build

Requirements:

- Windows 10 or newer
- .NET 8 SDK
- Inno Setup 6

```powershell
dotnet test .\OtsLauncher.sln -c Release
.\scripts\Build-LauncherInstaller.ps1 -OutputDirectory .\artifacts\launcher-installer -AppVersion 1.0.0
```

Output: `artifacts/launcher-installer/Tibernia-Setup.exe`.

## Security model

- HTTPS is required for remote manifests and downloads.
- Every client file is checked against its declared SHA-256 hash.
- Paths from manifests are confined to the selected client's directory.
- Downloads are staged before replacement, so a failed update does not silently replace a valid file.
- No certificate or signing secret is stored in this repository.

See [SECURITY.md](SECURITY.md), [PRIVACY.md](PRIVACY.md) and [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

## Code signing

Unsigned CI builds are produced entirely by GitHub Actions. The SignPath workflow is prepared for SignPath Foundation onboarding. Once the project is accepted, the repository owner adds the identifiers and API token supplied by SignPath; release artifacts can then be signed without purchasing a commercial certificate.

## License

The launcher source, installer definition and branding assets in this repository are released under the [MIT License](LICENSE). Third-party game clients and their assets are not included.

