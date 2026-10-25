#!/bin/bash
# Spike L1 (Phase 4): two instances at the main menu; one hosts the lobby, the other connects, goes ready and pings. Both windows capped at 60 fps for the run.
# usage: run-l1.sh [seconds]   -> prints the L1 lines of both mod logs
C="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Configs/BannerCoopTW/config.ini"
D="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Logs/BannerCoopTW"
E="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Configs/engine_config.txt"
rm -f "$D/spikes/role-host.lock" 2>/dev/null
[ -f "$E.bak-a1" ] || cp "$E" "$E.bak-a1"
trap '[ -f "$E.bak-a1" ] && mv -f "$E.bak-a1" "$E"' EXIT
sed -i 's/^max_framerate = .*/max_framerate = 60/' "$E"
printf '# BannerCoopTW settings\nLogLevel=Info\nShowLoadedMessage=true\nSpikesEnabled=true\nSpikeL1=auto\n' > "$C"
powershell -NoProfile -File "$(dirname "$0")/run-game.ps1" -Instances 2 2>&1 | grep -o 'PID [0-9]*'
n=$(( ${1:-90} / 10 ))
for i in $(seq 1 $n); do powershell -NoProfile -File "$(dirname "$0")/dismiss-safemode.ps1" >/dev/null 2>&1; sleep 10; done
taskkill //IM Bannerlord.exe >/dev/null 2>&1; sleep 15; taskkill //IM Bannerlord.exe //F >/dev/null 2>&1
printf '# BannerCoopTW settings\nLogLevel=Info\nShowLoadedMessage=true\nSpikesEnabled=false\nSpikeAutoLoadSave=\nSpikeAutoRunHost=\nSpikeAutoRunClient=\nSpikeA1=\nSpikeL1=\n' > "$C"
ls -t "$D"/*.log | head -2 | tac | while read -r f; do echo "== $(basename "$f")"; grep "L1:" "$f" | cut -c12-220 | awk 'NR<=6 || /PASS|done|retry|failed/'; done
