#!/bin/bash
# Safety gate before (and during) every dev run, after a MOUSE VR test loop crashed the user's laptop (2026-11-02):
# refuses when free RAM, free commit memory or free video memory are low, or when a game instance is still running.
# Exit 0 = safe; 1 = not safe (the reason is printed). "tools/safe-check.sh during" skips the leftover-game check.
MIN_RAM_GB=${MIN_RAM_GB:-6}
MIN_COMMIT_GB=${MIN_COMMIT_GB:-8}
MIN_VRAM_FREE_MB=${MIN_VRAM_FREE_MB:-1500}
read -r ram commit < <(powershell.exe -NoProfile -Command "\$o=Get-CimInstance Win32_OperatingSystem; '{0:N1} {1:N1}' -f (\$o.FreePhysicalMemory/1MB), (\$o.FreeVirtualMemory/1MB)" | tr -d '\r')
vram=$(nvidia-smi --query-gpu=memory.used,memory.total --format=csv,noheader,nounits 2>/dev/null | head -1 | tr -d ' \r')
used=${vram%,*}; total=${vram#*,}; free=$(( ${total:-0} - ${used:-0} ))
echo "SAFE-CHECK RAM free ${ram} GB, commit free ${commit} GB, VRAM free ${free} MB"
bad=""
awk "BEGIN{exit !($ram < $MIN_RAM_GB)}" && bad="$bad low RAM;"
awk "BEGIN{exit !($commit < $MIN_COMMIT_GB)}" && bad="$bad low commit memory;"
[ -n "$vram" ] && [ "$free" -lt "$MIN_VRAM_FREE_MB" ] && bad="$bad low video memory;"
if [ "$1" != "during" ]; then
  tasklist //FI "IMAGENAME eq Anger Foot.exe" //FO CSV //NH 2>/dev/null | grep -qi "Anger Foot.exe" && bad="$bad a game instance is still running;"
fi
if [ -n "$bad" ]; then echo "SAFE-CHECK STOP:$bad"; exit 1; fi
exit 0
