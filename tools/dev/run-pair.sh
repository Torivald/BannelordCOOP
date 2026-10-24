#!/bin/bash
# Starts 2 instances (run-game.ps1), waits until both wrote save-timing-<pid>.txt, then kills the game. Prints the two PIDs.
D="$USERPROFILE/Documents/Mount and Blade II Bannerlord/Logs/BannerCoopTW/spikes"
out=$(powershell -NoProfile -File "$(dirname "$0")/run-game.ps1" -Instances 2 2>&1 | grep -o 'PID [0-9]*' | awk '{print $2}')
pids=($out)
for i in $(seq 1 60); do
  ok=1; for p in "${pids[@]}"; do [ -f "$D/save-timing-$p.txt" ] || ok=0; done
  [ $ok = 1 ] && break
  powershell -NoProfile -File "$(dirname "$0")/dismiss-safemode.ps1" >/dev/null 2>&1
  sleep 10
done
sleep 3
taskkill //IM Bannerlord.exe >/dev/null 2>&1; sleep 15
taskkill //IM Bannerlord.exe //F >/dev/null 2>&1
echo "PIDS ${pids[*]} ok=$ok"
