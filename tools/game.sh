#!/bin/bash
# Dev helper. Usage:
#   tools/game.sh start [args]       launch Anger Foot directly (BepInEx loads via winhttp.dll; AFVR_VANILLA=1: without it)
#   tools/game.sh stop               stop the game, only if a dev session started it (never the player's own session)
#   tools/game.sh busy               exit 0 when the player's own game is running (hands off the game folder)
#   tools/game.sh cmd "<command>"    send an Anger Foot VR dev command (see src/AngerFootVR.Plugin/Plugin.cs)
#   tools/game.sh log [n]            tail the BepInEx log
#   tools/game.sh wait "<regex>" [timeout_s]   (also stops the game at the first safety warning)
[ -f "$(dirname "$0")/local.sh" ] && . "$(dirname "$0")/local.sh"
TOOLS="$(cd "$(dirname "$0")" && pwd)"
GAME="${AFVR_GAME_DIR:-C:/Users/cgors/OneDrive/Desktop/steam/steamapps/common/Anger Foot}"
EXE="Anger Foot.exe"
LOG="$GAME/BepInEx/LogOutput.log"
DATA="$GAME/BepInEx/AngerFootVR"
PIDFILE="$TOOLS/../analysis/dev-game.pid"
INI="$GAME/doorstop_config.ini"
# the dev loader only runs for a dev session: armed on start, disarmed when it ends, so the player's own launches stay
# vanilla
arm() { [ -f "$INI" ] && sed -i "s/^enabled *=.*/enabled = $1/" "$INI"; }
running_pids() { tasklist //FI "IMAGENAME eq $EXE" //FO CSV //NH 2>/dev/null | grep -i "$EXE" | cut -d, -f2 | tr -d '"'; }
case "$1" in
  start)
    if [ -n "$(running_pids)" ]; then echo "GAME ALREADY RUNNING (not starting another)"; exit 1; fi
    "$TOOLS/safe-check.sh" || exit 1
    rm -f "$LOG"
    if [ "$AFVR_VANILLA" = 1 ]; then arm false; else arm true; fi
    # dev runs keep their own saves and settings (BepInEx/AngerFootVR/dev-saves); AFVR_REALSAVES=1 to use the player's
    [ "$AFVR_REALSAVES" = 1 ] || export AFVR_DEVSAVES=1
    args=""; [ -n "$2" ] && args="-ArgumentList '$2'"
    mkdir -p "$(dirname "$PIDFILE")"
    powershell.exe -NoProfile -Command "(Start-Process -FilePath '$GAME/$EXE' -WorkingDirectory '$GAME' $args -PassThru).Id" | tr -d '\r' > "$PIDFILE" ;;
  stop)
    pids="$(running_pids)"
    [ -z "$pids" ] && { arm false; echo stopped; exit 0; }
    mine="$(cat "$PIDFILE" 2>/dev/null)"
    for p in $pids; do
      if [ "$p" != "$mine" ]; then echo "THE PLAYER'S OWN GAME IS RUNNING (pid $p): not stopping it"; exit 1; fi
    done
    taskkill //F //PID "$mine" >/dev/null 2>&1; rm -f "$PIDFILE"
    for i in $(seq 1 40); do [ -z "$(running_pids)" ] && break; sleep 0.25; done
    arm false
    echo stopped ;;
  busy)
    pids="$(running_pids)"; mine="$(cat "$PIDFILE" 2>/dev/null)"
    for p in $pids; do [ "$p" != "$mine" ] && exit 0; done
    exit 1 ;;
  cmd) mkdir -p "$DATA"; printf '%s\n' "$2" >> "$DATA/cmd.txt" ;;
  log) tail -n "${2:-40}" "$LOG" ;;
  wait)
    t=${3:-120}
    for ((i=0;i<t;i++)); do
      grep -qE "$2" "$LOG" 2>/dev/null && { grep -E "$2" "$LOG" | tail -3; exit 0; }
      [ -n "$(running_pids)" ] || { arm false; echo "GAME NOT RUNNING"; tail -20 "$LOG" 2>/dev/null; exit 2; }
      if (( i % 10 == 9 )) && ! "$TOOLS/safe-check.sh" during >/dev/null; then
        "$TOOLS/safe-check.sh" during; echo "STOPPING THE GAME (safety)"; "$0" stop; exit 3
      fi
      sleep 1
    done
    echo "TIMEOUT waiting for: $2"; exit 1 ;;
  *) echo "usage: $0 start|stop|busy|cmd|log|wait"; exit 1 ;;
esac
