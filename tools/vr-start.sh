#!/bin/bash
# Dev: launch Anger Foot against Unity's OpenXR mock runtime (no headset needed), on screen, through the safety gate.
# Pass extra game arguments as $1.
cd "$(dirname "$0")/.."
XR="${OPENXR_PKG:-C:/Users/cgors/InkVR/vendor/openxr/package}"
export XR_RUNTIME_JSON="$(cygpath -w "$XR/Runtime/MockRuntime/unity-mock-runtime.json")"
if tools/game.sh busy; then echo "NOT STARTING: the player's game is running"; exit 1; fi
tools/game.sh stop >/dev/null || exit 1
tools/game.sh start "$1" || exit 1
sleep 5
tools/game.sh wait "OpenXR session running|VR init failed|GAME NOT RUNNING" 90
