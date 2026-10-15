param(
    [ValidateSet("x64", "arm64")][string]$Architecture = "x64",
    [ValidateSet("Debug", "Release")][string]$Configuration = "Debug"
)
$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") { throw "The WPF application must be built on Windows." }
$root = Split-Path $PSScriptRoot -Parent
$target = if ($Architecture -eq "arm64") { "aarch64-pc-windows-msvc" } else { "x86_64-pc-windows-msvc" }
Push-Location "$root/shared-core-rs"
try {
    rustup target add $target
    if ($LASTEXITCODE -ne 0) { throw "Rust target installation failed" }
    cargo build --locked --release --target $target
    if ($LASTEXITCODE -ne 0) { throw "Rust core build failed" }
    $dest = "$root/app/windows/HyperWhisper/Resources/rust-core/$Architecture"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item "target/$target/release/hyperwhisper_core.dll" "$dest/hyperwhisper_core.dll" -Force
} finally { Pop-Location }
dotnet build "$root/app/windows/HyperWhisper/HyperWhisper.csproj" -c $Configuration -r "win-$Architecture"
if ($LASTEXITCODE -ne 0) { throw "Windows application build failed" }
