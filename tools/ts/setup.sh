#!/usr/bin/env bash
# Unpack Digimon Story: Time Stranger model data into private/reference.
# Steam install is read-only. Requires Python 3.12+ and ~16 GB free.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VENV="$ROOT/tools/ts/.venv"
DEST="$ROOT/private/reference/time-stranger/unpacked"
GAMEDATA="${DSTS_GAMEDATA:-/mnt/c/Program Files (x86)/Steam/steamapps/common/Digimon Story Time Stranger/gamedata}"
LOG="$ROOT/private/reference/time-stranger/setup.log"

if [[ ! -f "$GAMEDATA/app_0.dx11.mvgl" ]]; then
    echo "gamedata not found at: $GAMEDATA" >&2
    echo "Set DSTS_GAMEDATA to the folder that contains app_0.dx11.mvgl" >&2
    exit 1
fi

if [[ ! -x "$VENV/bin/dsts" ]]; then
    python3 -m venv "$VENV"
    "$VENV/bin/pip" install -r "$ROOT/tools/ts/requirements.txt"
fi

mkdir -p "$DEST" "$(dirname "$LOG")"
# --yes: no prompts. --force only if DEST already has a .dsts-setup.json
# and you explicitly want a redo.
extra=()
if [[ "${1:-}" == "--force" ]]; then
    extra+=(--force)
fi

"$VENV/bin/dsts" setup \
    --yes \
    --gamedata "$GAMEDATA" \
    --data-root "$DEST" \
    "${extra[@]}" 2>&1 | tee "$LOG"
