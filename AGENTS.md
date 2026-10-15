# Windows-only HyperWhisper

This is the public Windows-only fork. Never commit credentials, signing keys,
customer data or internal infrastructure details. Keep `.env`, internal notes
and local agent configuration ignored. Preserve LICENSE, third-party notices
and trademark attribution.

## Project map

- `app/windows`: WPF desktop application (.NET 10), smoke tests and installers.
- `app/shared-dotnet`: libraries consumed by Windows and regression tests.
- `shared-core-rs`: Rust 1.86 core and canonical C# UniFFI binding.
- `shared-models`, `shared-app-classification`, `shared-prompts`: runtime catalogs.
- `shared-backup`, `shared-conformance`: backup contract and regression vectors.
- `tools`: Windows build helpers and validation scripts.

Read `app/windows/AGENTS.md` before changing the application. Build with
`tools/build-windows.ps1`; run the Windows smoke suite after application edits.
A Linux cloud host supports portable tests but cannot execute WPF.
Use the existing checkout; do not create a worktree unless requested.

Keep the generated C# binding only in `shared-core-rs/bindings/csharp`.
Every consumer references HyperWhisper.SharedCore; never vendor another copy.
Regenerate with `shared-core-rs/build-bindings.ps1` and check with
`tools/check-binding-drift.sh`.

When changing settings, modes or vocabulary, update the shared backup schema
and mappings together. Preserve import compatibility for existing backups.
Never bypass hosted Cloud authentication, licensing or server-side entitlement.
HyperWhisper Cloud is removed from the application. Keep new defaults local,
reject legacy hosted-provider requests, and preserve existing backup fields.
Third-party providers use the user's own API keys.
