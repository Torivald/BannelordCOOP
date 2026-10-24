#!/bin/bash
# Starts 1 instance, dismisses the Safe Mode dialog, waits until <marker> appears in the newest mod log (default: "world_hash" twice), then closes the game.
D="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Logs/BannerCoopTW"
marker=${1:-script world_hash}; need=${2:-1}; timeout=${3:-60}
pid=$(powershell -NoProfile -File "$(dirname "$0")/run-game.ps1" -Instances 1 2>&1 | grep -o 'PID [0-9]*' | awk '{print $2}')
for i in $(seq 1 $timeout); do
  powershell -NoProfile -File "$(dirname "$0")/dismiss-safemode.ps1" >/dev/null 2>&1
  f=$(ls -t "$D"/BannerCoopTW_*.log 2>/dev/null | head -1)
  [ -n "$f" ] && [ "$(grep -c "$marker" "$f")" -ge "$need" ] && grep -q "pid=$pid\|log file" "$f" && break
  sleep 5
done
sleep 5
echo "PID $pid log=$f"
taskkill //IM Bannerlord.exe >/dev/null 2>&1; sleep 12; taskkill //IM Bannerlord.exe //F >/dev/null 2>&1
