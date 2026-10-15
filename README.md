# HyperWhisper for Windows

A Windows-only fork of [HyperWhisper](https://github.com/ray-amjad/hyperwhisper-app).
Global hotkey dictation with local Whisper / Parakeet models, optional third-party
API transcription using your own keys, post-processing, modes, vocabulary, and backup/restore.

## Requirements

- Windows 10 (build 19041) or newer; x64 or ARM64.
- .NET 10 SDK, Rust 1.86.0 via rustup, and Visual Studio C++ Build Tools
  with the Windows SDK and MSVC tools for the selected architecture.
- VC++ Redistributable 2015–2022 for native inference.
- Inno Setup 6 for installers (optional).

## Build and run

From the repository root in Windows PowerShell:

```powershell
.\tools\build-windows.ps1 -Architecture x64
 dotnet run --project app/windows/HyperWhisper -c Debug -r win-x64
```

For ARM64, use `-Architecture arm64` and `-r win-arm64`. Install the ARM64
MSVC tools on an x64 development host. The build script compiles the matching
Rust DLL, stages it where the app expects it, then builds the WPF application.
The committed inference engines are retained; rebuild them only when needed
using `tools/build-sherpa-onnx.bat` and `tools/build-parakeet-engine.bat`.
The sherpa build requires its upstream source checkout under `tools/sherpa-onnx`.

## Validate

```powershell
dotnet run --project app/windows/HyperWhisper.SmokeTests -c Release
cd shared-core-rs
cargo test --workspace --locked
cargo clippy --workspace --all-targets --locked -- -D warnings
```

Windows CI builds the app and executes smoke and conformance checks.
Portable tests may run on Linux in the cloud development environment; this
is test infrastructure only. The WPF application requires Windows and cannot
be run or functionally validated on a Linux host.

## Installers

```powershell
.\tools\build-windows.ps1 -Architecture x64 -Configuration Release
cd app/windows
.\build-release.ps1 -Architecture x64 -Version 1.14.0
```

Installers are written to `app/windows-installers/`. The manual Windows
installer workflow produces unsigned artifacts for x64 and ARM64 without
production credentials or deployment to the upstream infrastructure.
Signing for your own distribution requires your own signing configuration.

## Layout

| Path | Purpose |
| --- | --- |
| `app/windows` | WPF app, smoke tests, installers and update-feed fixture |
| `app/shared-dotnet` | Libraries used by Windows and their test harnesses |
| `shared-core-rs` | Rust business logic and generated C# UniFFI binding |
| `shared-models`, `shared-app-classification` | Model and provider catalogs |
| `shared-prompts`, `shared-backup`, `shared-conformance` | Prompts, backup schema and regression vectors |
| `tools` | Windows native-engine, build and validation tools |
| `docs/local-api.openapi.yaml` | Local API reference |

The macOS/iOS/Linux apps, Apple bindings, websites, hosted backend source,
server deployment workflows and unrelated tooling have been removed.
Backup compatibility data remains so existing backups can still be imported.
No submodule initialization or Node package installation is required.

Local transcription requires no cloud account. HyperWhisper Cloud account
activation, credits, purchase screens, transcription/post-processing relay,
background account validation and connection warmup have been removed.
New installations start with local transcription and post-processing off.
Other providers and custom endpoints still work with your own API keys.
Existing hosted-mode settings remain readable for backup compatibility, but
attempts to run those providers are rejected before audio is uploaded. Edit an
old hosted mode to select a local model or a supported provider.

The retained update feed is historical test data. The app still uses the upstream
update service; configure your own feed and verification key before distributing
a separately maintained build.

## License and attribution

Apache-2.0: see [LICENSE](LICENSE) and
[THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).
Original project by Ray Amjad. The HyperWhisper name, logo and icon have separate
trademark restrictions: see [TRADEMARKS.md](TRADEMARKS.md).
