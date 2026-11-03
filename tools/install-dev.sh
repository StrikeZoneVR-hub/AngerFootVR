#!/bin/bash
# Dev install into the game folder: BepInEx 5, Unity's OpenXR provider and loader (registered through a UnitySubsystems
# manifest), and the Anger Foot VR plugin. Everything it adds is listed in BepInEx/AngerFootVR/installed.txt, so it can
# all be taken out again (tools/uninstall-dev.sh). The loader stays disarmed until a dev run (tools/game.sh start).
set -e
cd "$(dirname "$0")/.."
[ -f tools/local.sh ] && . tools/local.sh
GAME="${AFVR_GAME_DIR:-C:/Users/cgors/OneDrive/Desktop/steam/steamapps/common/Anger Foot}"
DATA="$GAME/Anger Foot_Data"
XR="${OPENXR_PKG:-$(pwd)/vendor/openxr-0.1.2/package}" # the pre-1.0 provider: the one that starts on Unity 2019.4
if tools/game.sh busy; then echo "NOT INSTALLING: the player's game is running"; exit 1; fi
tools/game.sh stop >/dev/null

# BepInEx 5 (first time only: never overwrite its config)
if [ ! -f "$GAME/winhttp.dll" ]; then
  cp -r vendor/bepinex/BepInEx "$GAME/"
  cp vendor/bepinex/winhttp.dll "$GAME/"
  [ -f vendor/bepinex/.doorstop_version ] && cp vendor/bepinex/.doorstop_version "$GAME/"
fi
sed -e 's#^enabled *=.*#enabled = false#' vendor/bepinex/doorstop_config.ini > "$GAME/doorstop_config.ini"

# Unity's OpenXR provider (registered by its UnitySubsystems manifest) and the OpenXR loader
mkdir -p "$DATA/UnitySubsystems/UnityOpenXR" "$DATA/Plugins/x86_64"
cp "$XR/Runtime/UnitySubsystemsManifest.json" "$DATA/UnitySubsystems/UnityOpenXR/"
cp "$XR/Runtime/windows/x64/UnityOpenXR.dll" "$DATA/Plugins/x86_64/"
cp "$XR/RuntimeLoaders/windows/x64/openxr_loader.dll" "$DATA/Plugins/x86_64/"

# the plugin
mkdir -p "$GAME/BepInEx/plugins/AngerFootVR" "$GAME/BepInEx/AngerFootVR"
cp src/AngerFootVR.Plugin/bin/Release/netstandard2.0/AngerFootVR.dll assets/props.afvp "$GAME/BepInEx/plugins/AngerFootVR/"

cat > "$GAME/BepInEx/AngerFootVR/installed.txt" <<EOF
winhttp.dll
.doorstop_version
doorstop_config.ini
BepInEx/
Anger Foot_Data/UnitySubsystems/UnityOpenXR/
Anger Foot_Data/Plugins/x86_64/UnityOpenXR.dll
Anger Foot_Data/Plugins/x86_64/openxr_loader.dll
EOF
echo "installed"
