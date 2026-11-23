#!/usr/bin/env bash
# Smoke-checks build/Pounce.app on a temporary home and Watched Folder (never your real folders):
#   1. the app starts its bundled Engine, which indexes the folder and answers a search through the CLI;
#   2. quitting the app (SIGTERM) also stops the Engine;
#   3. force-killing the app (SIGKILL) still stops the Engine.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/build/Pounce.app"
[ -x "$APP/Contents/MacOS/Pounce" ] || { echo "build it first: scripts/build-app.sh" >&2; exit 2; }
if curl -s -o /dev/null --max-time 1 http://127.0.0.1:7431/search; then
  echo "Something already answers on 127.0.0.1:7431; stop it first so the check tests the bundled Engine." >&2; exit 2
fi
TMP="$(mktemp -d)"
trap 'pkill -f "pounce.home=$TMP/home" 2>/dev/null || true; rm -rf "$TMP"' EXIT
mkdir -p "$TMP/home" "$TMP/watched"
printf '# Falconry\nThe falcon returned to the glove after the hunt.\n' > "$TMP/watched/falconry.md"
printf 'pounce:\n  watched-folders: %s\n' "$TMP/watched" > "$TMP/home/pounce.yml"
export POUNCE_HOME="$TMP/home"

start_app() { "$APP/Contents/MacOS/Pounce" >"$TMP/app.log" 2>&1 & APP_PID=$!; }
engine_pid() { pgrep -f "pounce.parent-pid=$APP_PID" || true; }
gone_within() { for _ in $(seq 1 "$2"); do kill -0 "$1" 2>/dev/null || return 0; sleep 1; done; return 1; }

start_app
started=$(date +%s)
until out=$("$ROOT/scripts/pounce" --color=never falcon glove 2>/dev/null) && grep -q falconry.md <<<"$out"; do
  [ $(( $(date +%s) - started )) -lt 90 ] || { echo "FAIL: no search result after 90 s"; tail -20 "$POUNCE_HOME/logs/engine.log"; exit 1; }
  sleep 1
done
echo "ok   bundled Engine answered a search $(( $(date +%s) - started )) s after launch"
ENGINE=$(engine_pid); [ -n "$ENGINE" ] || { echo "FAIL: no Engine child process found"; exit 1; }
echo "ok   Engine runs as pid $ENGINE with $(ps -o rss= -p "$ENGINE" | awk '{printf "%d MB", $1/1024}')"

kill -TERM "$APP_PID"
gone_within "$ENGINE" 15 && echo "ok   quitting Pounce stopped the Engine" || { echo "FAIL: Engine still running after quit"; exit 1; }

start_app
until [ -n "$(engine_pid)" ]; do sleep 0.5; done; sleep 3
ENGINE=$(engine_pid)
kill -KILL "$APP_PID"
gone_within "$ENGINE" 15 && echo "ok   force-killing Pounce still stopped the Engine" || { echo "FAIL: orphaned Engine after kill -9"; exit 1; }
echo "PASS"
