#!/bin/bash
# Background guard for a dev run (started by tools/game.sh start): every 5 s, while the dev game runs, checks free RAM,
# commit and video memory (tools/safe-check.sh) and stops the game at the first warning. Writes what it saw to
# analysis/guard.log.
TOOLS="$(cd "$(dirname "$0")" && pwd)"
LOG="$TOOLS/../analysis/guard.log"
EXE="Anger Foot.exe"
echo "guard started $(date +%H:%M:%S)" > "$LOG"
sleep 5
while tasklist //FI "IMAGENAME eq $EXE" //FO CSV //NH 2>/dev/null | grep -qi "$EXE"; do
  out="$("$TOOLS/safe-check.sh" during)"
  rc=$?
  echo "$(date +%H:%M:%S) $out" >> "$LOG"
  if [ $rc -ne 0 ]; then
    echo "$(date +%H:%M:%S) STOPPING THE GAME (safety)" >> "$LOG"
    "$TOOLS/game.sh" stop >> "$LOG" 2>&1
    exit 1
  fi
  sleep 5
done
echo "guard: game ended $(date +%H:%M:%S)" >> "$LOG"
