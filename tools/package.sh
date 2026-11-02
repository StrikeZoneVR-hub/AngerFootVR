#!/bin/bash
# Builds the release folder dist/AngerFootVR-<version>: the launcher ("Anger Foot VR.exe") with its AngerFootVR-Payload
# (BepInEx 5 and the mod; Unity's OpenXR plugin is downloaded from Unity by the launcher, never shipped).
set -e
cd "$(dirname "$0")/.."
dotnet build src/AngerFootVR.Plugin -c Release -nologo -v q | grep -E " error |Build succeeded" | sort -u
dotnet build src/AngerFootVR.Launcher -c Release -nologo -v q | grep -E " error |Build succeeded" | sort -u
V="$(sed -n 's#.*<Version>\(.*\)</Version>.*#\1#p' src/AngerFootVR.Launcher/AngerFootVR.Launcher.csproj)"
OUT="dist/AngerFootVR-$V"
rm -rf "$OUT"
P="$OUT/AngerFootVR-Payload"
mkdir -p "$P/BepInEx/plugins/AngerFootVR"
cp "src/AngerFootVR.Launcher/bin/Release/Anger Foot VR.exe" "src/AngerFootVR.Launcher/bin/Release/Anger Foot VR.exe.config" "$OUT/"
cp -r vendor/bepinex/BepInEx/core "$P/BepInEx/"
cp vendor/bepinex/winhttp.dll vendor/bepinex/.doorstop_version "$P/"
sed -e 's#^enabled *=.*#enabled = true#' vendor/bepinex/doorstop_config.ini > "$P/doorstop_config.ini"
cp src/AngerFootVR.Plugin/bin/Release/netstandard2.0/AngerFootVR.dll "$P/BepInEx/plugins/AngerFootVR/"
cp src/AngerFootVR.Launcher/Assets/Fonts/*-OFL.txt "$OUT/" 2>/dev/null || true
echo "packaged $OUT"
