#!/bin/bash
# Removes everything the dev install added to the game folder (the game is left as Steam installed it).
cd "$(dirname "$0")/.."
GAME="${AFVR_GAME_DIR:-C:/Users/cgors/OneDrive/Desktop/steam/steamapps/common/Anger Foot}"
if tools/game.sh busy; then echo "NOT UNINSTALLING: the player's game is running"; exit 1; fi
tools/game.sh stop >/dev/null
cd "$GAME" || exit 1
rm -rf BepInEx winhttp.dll .doorstop_version doorstop_config.ini "Anger Foot_Data/UnitySubsystems/UnityOpenXR" \
  "Anger Foot_Data/Plugins/x86_64/UnityOpenXR.dll" "Anger Foot_Data/Plugins/x86_64/openxr_loader.dll"
rmdir "Anger Foot_Data/UnitySubsystems" 2>/dev/null
echo "uninstalled"
