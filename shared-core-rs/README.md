# Windows Rust core

Rust 1.86.0 supplies the business logic used by the Windows WPF application
through UniFFI 0.28.3. Generated C# bindings are committed under `bindings/csharp`
and compiled exactly once by HyperWhisper.SharedCore.

Use `../tools/build-windows.ps1` from PowerShell to build and stage the DLL.
Both `x86_64-pc-windows-msvc` and `aarch64-pc-windows-msvc` are supported.
The matching MSVC toolchain and Windows SDK must be installed.

```powershell
cargo test --workspace --locked
cargo clippy --workspace --all-targets --locked -- -D warnings
```

To regenerate C# bindings after an FFI change, install the matching generator:

```powershell
cargo install uniffi-bindgen-cs --git https://github.com/NordSecurity/uniffi-bindgen-cs --tag v0.9.2+v0.28.3 --locked
.\build-bindings.ps1
```

Keep the generator and UniFFI versions aligned. Rebuild the native DLL and run
the Windows smoke suite to verify the binding checksum handshake. No Apple
static libraries or Swift/Kotlin binding generation are needed.

Portable core tests also run on the cloud Linux host. When .NET tests need a
native test library, build it with
`cargo build --locked --release --target x86_64-unknown-linux-gnu`.
This library is used only by tests and is never shipped in the Windows app.
