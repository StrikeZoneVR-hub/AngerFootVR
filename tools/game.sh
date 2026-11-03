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
REGKEY='HKCU\Software\Free Lives\Anger Foot'
REGSNAP="$TOOLS/../analysis/reg-before-run.reg"
# A dev run plays windowed (the test settings copy); Unity writes its window settings to the registry when the game
# quits, so the game's registry keys are snapshotted just before a run and put back after it.
snapshot_reg() { rm -f "$REGSNAP"; reg export "$REGKEY" "$(cygpath -w "$REGSNAP")" //y >/dev/null 2>&1; }
finish() {
  arm false
  rm -f "$DATA/dev-run.token"
  if [ -f "$REGSNAP" ]; then
    reg import "$(cygpath -w "$REGSNAP")" >/dev/null 2>&1 && rm -f "$REGSNAP"
  fi
}
running_pids() { tasklist //FI "IMAGENAME eq $EXE" //FO CSV //NH 2>/dev/null | grep -i "$EXE" | cut -d, -f2 | tr -d '"'; }
case "$1" in
  start)
    if [ -n "$(running_pids)" ]; then echo "GAME ALREADY RUNNING (not starting another)"; exit 1; fi
    "$TOOLS/safe-check.sh" || exit 1
    rm -f "$LOG"
    snapshot_reg
    if [ "$AFVR_VANILLA" = 1 ]; then arm false; else arm true; fi
    # dev runs keep their own saves and settings (BepInEx/AngerFootVR/dev-saves); AFVR_REALSAVES=1 to use the player's
    [ "$AFVR_REALSAVES" = 1 ] || export AFVR_DEVSAVES=1
    # the dev run's token: the mod treats a run as a dev run only while this file holds the token it was started with
    # (a dev setting once leaked into Steam's environment, and the player's own runs came up on the test saves)
    # (a launcher run's launch.txt makes any run the player's: never left for a dev run)
    rm -f "$DATA/launch.txt"
    mkdir -p "$DATA"; export AFVR_DEVTOKEN="$RANDOM$RANDOM$RANDOM"; printf '%s' "$AFVR_DEVTOKEN" > "$DATA/dev-run.token"
    args=""; [ -n "$2" ] && args="-ArgumentList '$2'"
    mkdir -p "$(dirname "$PIDFILE")"
    powershell.exe -NoProfile -Command "(Start-Process -FilePath '$GAME/$EXE' -WorkingDirectory '$GAME' $args -PassThru).Id" | tr -d '\r' > "$PIDFILE"
    # the background guard stops the game at the first memory warning, for the whole run
    nohup "$TOOLS/guard.sh" >/dev/null 2>&1 &
    ;;
  stop)
    pids="$(running_pids)"
    [ -z "$pids" ] && { finish; echo stopped; exit 0; }
    mine="$(cat "$PIDFILE" 2>/dev/null)"
    for p in $pids; do
      if [ "$p" != "$mine" ]; then echo "THE PLAYER'S OWN GAME IS RUNNING (pid $p): not stopping it"; exit 1; fi
    done
    taskkill //F //PID "$mine" >/dev/null 2>&1; rm -f "$PIDFILE"
    for i in $(seq 1 40); do [ -z "$(running_pids)" ] && break; sleep 0.25; done
    finish
    echo stopped ;;
  finish) [ -z "$(running_pids)" ] && finish ;;
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
      [ -n "$(running_pids)" ] || { finish; echo "GAME NOT RUNNING"; tail -20 "$LOG" 2>/dev/null; exit 2; }
      if (( i % 10 == 9 )) && ! "$TOOLS/safe-check.sh" during >/dev/null; then
        "$TOOLS/safe-check.sh" during; echo "STOPPING THE GAME (safety)"; "$0" stop; exit 3
      fi
      sleep 1
    done
    echo "TIMEOUT waiting for: $2"; exit 1 ;;
  *) echo "usage: $0 start|stop|busy|cmd|log|wait"; exit 1 ;;
esac
