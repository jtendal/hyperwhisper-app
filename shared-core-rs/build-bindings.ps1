$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    cargo build --locked --release --target x86_64-pc-windows-msvc
    if ($LASTEXITCODE -ne 0) { throw "Rust core build failed" }
    # Install the matching generator as documented in README.md before running.
    uniffi-bindgen-cs --library target/x86_64-pc-windows-msvc/release/hyperwhisper_core.dll --out-dir bindings/csharp
    if ($LASTEXITCODE -ne 0) { throw "C# binding generation failed" }
} finally { Pop-Location }
