#!/bin/bash
# Spike A1: two instances start the same Custom Battle (role by lock file), host/client mission sync runs ~4 min, then the game is closed.
# usage: run-a1.sh [mode] [seconds]
C="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Configs/BannerCoopTW/config.ini"
D="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Logs/BannerCoopTW"
rm -f "$D/spikes/role-host.lock" 2>/dev/null
printf '# BannerCoopTW settings\nLogLevel=Info\nShowLoadedMessage=true\nSpikesEnabled=true\nSpikeA1=auto%s\n' "${1:+:$1}" > "$C"
powershell -NoProfile -File "$(dirname "$0")/run-game.ps1" -Instances ${INSTANCES:-2} 2>&1 | grep -o 'PID [0-9]*'
n=$(( ${2:-240} / 10 ))
for i in $(seq 1 $n); do powershell -NoProfile -File "$(dirname "$0")/dismiss-safemode.ps1" >/dev/null 2>&1; sleep 10; done
taskkill //IM Bannerlord.exe >/dev/null 2>&1; sleep 15; taskkill //IM Bannerlord.exe //F >/dev/null 2>&1
printf '# BannerCoopTW settings\nLogLevel=Info\nShowLoadedMessage=true\nSpikesEnabled=false\nSpikeAutoLoadSave=\nSpikeAutoRunHost=\nSpikeAutoRunClient=\nSpikeA1=\n' > "$C"
echo DONE
