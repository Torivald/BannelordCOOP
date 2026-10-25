#!/bin/bash
# Spike A1: two instances start the same Custom Battle (role by lock file), host/client mission sync runs ~4 min, then the game is closed.
# usage: run-a1.sh [mode] [seconds]
C="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Configs/BannerCoopTW/config.ini"
D="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Logs/BannerCoopTW"
rm -f "$D/spikes/role-host.lock" 2>/dev/null
# Two instances on one PC: an uncapped (200 fps) host starves the second window (client 2-12 fps, research 0013). Cap both at 60 fps for the run, restore afterwards.
E="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Configs/engine_config.txt"
# keep the FIRST backup (an aborted earlier run must not leave the capped file as the "original"), restore on any exit
[ -f "$E.bak-a1" ] || cp "$E" "$E.bak-a1"
trap '[ -f "$E.bak-a1" ] && mv -f "$E.bak-a1" "$E"' EXIT
sed -i 's/^max_framerate = .*/max_framerate = 60/' "$E"
printf '# BannerCoopTW settings\nLogLevel=Info\nShowLoadedMessage=true\nSpikesEnabled=true\nSpikeA1=auto%s\n' "${1:+:$1}" > "$C"
powershell -NoProfile -File "$(dirname "$0")/run-game.ps1" -Instances ${INSTANCES:-2} 2>&1 | grep -o 'PID [0-9]*'
n=$(( ${2:-240} / 10 ))
for i in $(seq 1 $n); do powershell -NoProfile -File "$(dirname "$0")/dismiss-safemode.ps1" >/dev/null 2>&1; sleep 10; done
taskkill //IM Bannerlord.exe >/dev/null 2>&1; sleep 15; taskkill //IM Bannerlord.exe //F >/dev/null 2>&1
printf '# BannerCoopTW settings\nLogLevel=Info\nShowLoadedMessage=true\nSpikesEnabled=false\nSpikeAutoLoadSave=\nSpikeAutoRunHost=\nSpikeAutoRunClient=\nSpikeA1=\n' > "$C"
echo DONE
