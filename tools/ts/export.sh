#!/usr/bin/env bash
# Export one or more Digimon from the unpacked Time Stranger tree to GLB.
# Usage: tools/ts/export.sh agumon [gabumon ...]
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VENV="$ROOT/tools/ts/.venv"
DATA="$ROOT/private/reference/time-stranger/unpacked"
OUT="$ROOT/private/reference/time-stranger/glb"

if [[ $# -lt 1 ]]; then
    echo "usage: $0 <slug-or-chr> [...]" >&2
    echo "  e.g. $0 agumon gabumon" >&2
    exit 1
fi
if [[ ! -x "$VENV/bin/dsts" ]]; then
    echo "venv missing — run tools/ts/setup.sh first" >&2
    exit 1
fi
if [[ ! -f "$DATA/.dsts-setup.json" ]]; then
    echo "unpacked data missing — run tools/ts/setup.sh first" >&2
    exit 1
fi

mkdir -p "$OUT"
"$VENV/bin/dsts" export --data-root "$DATA" -o "$OUT" "$@"
