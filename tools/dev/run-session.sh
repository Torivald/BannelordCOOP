#!/bin/bash
# One game instance: sets SpikeAutoLoadSave/SpikeAutoRun, starts the game, dismisses Safe Mode, waits for <marker> in the mod log, closes the game.
# usage: run-session.sh <save> <script> <marker> ; prints PID
C="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Configs/BannerCoopTW/config.ini"
D="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Logs/BannerCoopTW"
printf '# BannerCoopTW settings\nLogLevel=Info\nShowLoadedMessage=true\nSpikesEnabled=true\nSpikeAutoLoadSave=%s\nSpikeAutoRun=%s\n' "$1" "$2" > "$C"
before=$(ls "$D" | grep -c BannerCoopTW_)
powershell -NoProfile -File "$(dirname "$0")/run-game.ps1" -Instances 1 >/dev/null 2>&1
for i in $(seq 1 15); do powershell -NoProfile -File "$(dirname "$0")/dismiss-safemode.ps1" | grep -q 1 && break; sleep 3; done
for i in $(seq 1 30); do [ "$(ls "$D" | grep -c BannerCoopTW_)" -gt "$before" ] && break; sleep 2; done
f="$D/$(ls -t "$D" | grep BannerCoopTW_ | head -1)"
for i in $(seq 1 80); do grep -q "$3" "$f" && break; sleep 5; done
sleep 6
echo "LOG $f"
taskkill //IM Bannerlord.exe >/dev/null 2>&1; sleep 15; taskkill //IM Bannerlord.exe //F >/dev/null 2>&1
