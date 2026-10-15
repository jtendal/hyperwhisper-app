---
name: windows-release
description: Build Windows x64 and ARM64 installers for this fork.
---

# Windows installers

Read the root README and `app/windows/AGENTS.md`. On Windows, build matching
Rust DLLs using `tools/build-windows.ps1` for each desired architecture. Run the
Windows smoke suite and `app/windows/HyperWhisper/scripts/verify_release_readiness.ps1`.
Then run `app/windows/build-release.ps1 -Architecture both -Version X.Y.Z` with
Inno Setup 6 installed. Output is under `app/windows-installers/`.

The manual Windows Installers workflow builds unsigned artifacts. It does not
publish, deploy a website, modify the upstream feed, or use production credentials.
Distribution requires the user's branding, signing credentials and update feed.
Never bypass Cloud entitlement or update-signature verification.
