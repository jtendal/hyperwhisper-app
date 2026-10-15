#!/usr/bin/env bash
# The generated C# binding must have exactly one compiling owner.
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ $# -gt 0 && "${1:-}" != "--fix" ]]; then
  echo "usage: $0 [--fix]" >&2
  exit 2
fi
python3 - <<'PY'
from pathlib import Path
import subprocess
source = 'shared-core-rs/bindings/csharp/hyperwhisper_core.cs'
if not Path(source).is_file():
    raise SystemExit('Missing canonical C# binding')
files = subprocess.check_output(['git', 'ls-files', '-z']).decode().split('\0')
copies = [p for p in files if Path(p).name == 'hyperwhisper_core.cs' and p != source and Path(p).is_file()]
if copies:
    raise SystemExit('Vendored C# bindings must be removed: ' + ', '.join(copies))
print('OK: one canonical C# binding; no vendored copies.')
PY
