#!/bin/bash
# S2 attempt: 2 instances (host/client roles by lock file), optional UDP capture on loopback, ~4 min observation, then kills the game.
# usage: run-s2.sh <save> <captureFile|-> [hostScript] [clientScript]
C="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Configs/BannerCoopTW/config.ini"
D="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Logs/BannerCoopTW"
H=${3:-s2_clear,s2_host_start,s2_wait_ready,wait:6,s2_ping:7,wait:10,s2_state,wait:20,s2_state}
K=${4:-s2_ready,s2_join:127.0.0.1,wait:20,s2_state}
rm -f "$D/spikes/role-host.lock" 2>/dev/null
printf '# BannerCoopTW settings\nLogLevel=Info\nShowLoadedMessage=true\nSpikesEnabled=true\nSpikeAutoLoadSave=%s\nSpikeAutoRunHost=%s\nSpikeAutoRunClient=%s\n' "$1" "$H" "$K" > "$C"
if [ "$2" != "-" ]; then
  "/c/Program Files/Wireshark/tshark.exe" -i '\Device\NPF_Loopback' -f "udp" -w "$2" >/dev/null 2>&1 &
  sleep 3
fi
powershell -NoProfile -File "$(dirname "$0")/run-game.ps1" -Instances 2 ${S2_MP:+-WithMultiplayer} 2>&1 | grep -o 'PID [0-9]*'
for i in $(seq 1 24); do powershell -NoProfile -File "$(dirname "$0")/dismiss-safemode.ps1" >/dev/null 2>&1; sleep 10; done
taskkill //IM Bannerlord.exe >/dev/null 2>&1; sleep 15; taskkill //IM Bannerlord.exe //F >/dev/null 2>&1
taskkill //IM tshark.exe //F >/dev/null 2>&1
printf '# BannerCoopTW settings\nLogLevel=Info\nShowLoadedMessage=true\nSpikesEnabled=false\nSpikeAutoLoadSave=\nSpikeAutoRunHost=\nSpikeAutoRunClient=\n' > "$C"
echo DONE
