# Windows Rust DLL

The Windows application requires `hyperwhisper_core.dll`, built from the
matching source and C# binding. It is not committed. The build fails explicitly
when the selected architecture's DLL is absent.

From the repository root on Windows:

```powershell
.\tools\build-windows.ps1 -Architecture x64
# or:
.\tools\build-windows.ps1 -Architecture arm64
```

The helper builds the MSVC target and stages the DLL under `x64/` or `arm64/`
before building the WPF app. The project copies it next to the application EXE.
The managed ARM64 inference daemon also needs its own copy in its output folder.

See `shared-core-rs/README.md` for toolchain and C# binding regeneration details.
