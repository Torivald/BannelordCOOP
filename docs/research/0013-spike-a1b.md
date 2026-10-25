# Research 0013 — Spike A1b (conditions of the Gate A1 verdict), PARTIAL — paused by the owner

STATUS: **unfinished.** Written when the owner asked to stop (2026-10-25) and continue later. Gate A1b verdict by architect-reviewer **not obtained**; no PR merged for this branch. Field battle only (sieges are out of scope, `A1Siege` untouched). No second PC (owner).

## What was asked (MASTER_PLAN "A1b")
1. Soak ≥ 10 min, ~100 agents, deaths **on**, host↔client divergence counters every 5 s. 2. ≥ 30 input→visible-effect samples (p95 < 150 ms). 3. Suppress client-local damage and count attempts. 4. Reproduce the client FPS dip (~36 s, 4–6 fps) and profile it. 5. Network emulation (2–5 % loss, 50–100 ms) with the same metrics.

## Bannerlord version / setup
v1.4.8.119303, one PC, two instances, loopback, 2026-10-25. Branch `phase2d/spike-a1b`. Code: `Spikes/A1/A1Metrics.cs` (new), hooks in `A1Behavior.cs` / `A1Driver.cs`, `tools/dev/a1-summary.py` (A1b section), `Networking/NetworkConditionTransport.cs` + 4 unit tests. Modes: `SpikeA1=auto:n50:brawl` (+ `loss<N>`, `lat<min>-<max>`, `fightdiag`, `countblows`, `localdmg`). Raw logs of the runs are not committed (copies in the session scratchpad only).

## Findings that change earlier conclusions
1. **All earlier "n100" runs (research 0012, first and second session) were 100 agents of ONE side against 1 enemy.** `CustomBattleHelper.GetCustomBattleParties` consumes the troop-count array: the same `counts` array passed for both sides left the enemy with one soldier (`alive def/att=1/100`, the battle ended when that soldier died). With a separate array per side `n50` = 50 vs 50 (101 agents). So: the 100-agent *throughput/latency* numbers stay valid as "100 moving puppets", but **no 12-minute soak of 0012 exercised a real fight or the death path** (deaths 0–1, hits were mostly blocked hits). The 0012 "deaths disabled" caveat is therefore too weak: deaths were practically absent in every n100 run. Fixed in `A1Driver.StartBattle` (`enemyCounts`).
2. **The game pauses itself when its window loses focus** (`BannerlordConfig.StopGameOnFocusLost`, default `true`). Two soak runs lost their host exactly at an `OnGameWindowFocusChange: False` line in `rgl_log` (host log stopped for 12 min; the client then saw no traffic). This is not a hang of our code. Mitigation in the spike: `BannerlordConfig.StopGameOnFocusLost = false` at battle start (static field, not saved); the next full run (n100, 155 windows) had no stop. **Product consequence:** a co-op host must keep running unfocused; set the option in the real mod and tell players. It also likely explains part of the "unexplained" instabilities of two windows on one desktop.
3. LiteNetLib 1.3.5 `SimulatePacketLoss/SimulateLatency` exist in the DLL, but **had no effect** in the referenced build (ack latency stayed 37–41 ms with "50–100 ms per direction" and "loss 2/5 %"; both endpoints printed "network emulation ON"). Replaced by our own decorator `NetworkConditionTransport` (4 passing unit tests: latency bounds, loss share, reliable never lost and in order, pass-through). **It is not wired into `A1Behavior` yet** — `ApplyNetEmulation` in `A1Metrics.cs` still calls the ineffective LiteNetLib switches. The two "network emulation" runs below are therefore **runs without emulation** and prove nothing about loss/latency.

## Results (loopback, one PC)
| Run | Setup | Result |
|---|---|---|
| soakB3 | `n100`, 1 vs 100, deaths on, focus fix | 155 windows (12.9 min), both fps mean 84, no tick errors, 2 STALL lines (> 200 ms, 252–276 ms, GC changed once). Effect latency (start edge) n=221 p50 104 / p95 128 / max 138 ms; stop p50 187 / p95 217. Alive host/client equal in all windows; hp equal; position error before correction p50 0.10 / p95 0.22 m, worst 0.52 m. Only 25 hits / 1 death (see finding 1). |
| soakC | `n50:brawl`, 50 vs 50, deaths on, Charge re-ordered every 10 s | 156 windows (13 min) both sides, no tick errors, host fps 80 / client 75. **5359 hits, 63 deaths replicated; alive host/client equal in every window (0/156 mismatches), health sums equal, local blows on client 0, local deaths 0.** Agents: ~101 for the first ~4 min, then 38 (the fight turned into 35 defenders vs the immortal controlled agent + 1, so "~100 agents" held only for the first ~4 minutes). Position error p50 0.15 / p95 0.29 m (initial settling up to 242 m). 1 STALL (252 ms). Effect latency: start p50 94 / p95 **324** / max 574 ms, stop p95 878 — **confounded**: the controlled agent was in melee, blocked by bodies. The script was changed afterwards (walk backward, samples split by "enemy > 15 m" / "≤ 15 m"), **not re-measured**. |
| netA, netB | `n50:brawl` + `loss2/loss5 lat50-100` | emulation inactive (finding 3). With the split metric: effect start n=100/101 p50 104/103, p95 134/133 ms (enemy > 15 m: p95 127/132 ms), alive/hp equal, 0 local blows, 0 STALL lines. netA ended with 5 agents (96 deaths). Useful only as two more no-emulation runs. |
| countblows (solo) | `solo:n50:brawl:countblows` | `OnRegisterBlow` counted 279 blows (9178 damage) in 150 s → the counter works, so "0 local blows on the client" is a real result: AI-paused puppets with copied action channels do not hit anything locally. `DisableDying = true` on the client is set as a safety net (never triggered). |

## Task status
1. Soak with deaths, counters: **done** in soakC (13 min, divergence 0) — but "~100 agents" only for ~4 min; for a true 10 min at ~100 agents with deaths use n100 (100 vs 100 = ~200 agents) or reinforcements/respawn.
2. ≥ 30 effect samples: **done** (n > 100 per run). Gate figure: **p95 128 ms (soakB3), 133–134 ms (net runs, enemy far) < 150 ms**; in melee p95 324 ms (measurement flaw, to be redone). Stop latency (agent decelerating) is 170–220 ms and is not a network metric.
3. Local damage suppression/counting: **done** (see countblows).
4. FPS dip: **not reproduced** in 6 sync runs of 6–13 min each after the focus fix (STALLs ≤ 276 ms, ≤ 2 per run); the first-session dip (36 s, 10 ms/chunk apply) did not recur; instrumentation (STALL lines with poll time, packets per frame, GC counters, apply cost) is in place. One bad run (soakB2, client 800 STALL lines, 1 s frames, ack 433 ms) happened while the host window was paused by focus loss and **was not diagnosed**; the stalls were outside our code (poll 0.5–3 ms, own tick < 90 ms).
5. Network emulation: **not done** (decorator written and tested, not wired, no run).

## Open / next steps
- Wire `NetworkConditionClientTransport/ServerTransport` into `A1Behavior.Initialise` (replace `ApplyNetEmulation` → LiteNetLib switches), then run `n50:brawl:loss2:lat50-100` and `loss5:lat50-100` (one-way delays: the extra RTT is 100–200 ms, so p95 > 150 ms is expected by construction; judge degradation, divergence and recovery, not the 150 ms line).
- Re-measure effect latency in the walk-forward/back script with the free/near split on a 50 vs 50 fight; a 10-min run with ~100 agents alive (n100 two-sided, or n50 plus reinforcements).
- Decide what to do with the focus option in the product (default off while hosting).
- Understand the soakB2 client slowdown if it recurs.
- Verdict by architect-reviewer on A1b (PASS / FAIL with conditions) and PR — **not done**.
- Correct research 0012 header: n100 = 100 vs 1 (done as a note there).

## Tests actually run
`dotnet build -c Release` 0 errors; `dotnet test` for `NetworkCondition*`: 4/4 pass (other test projects not re-run after the last change; they passed before it: Core 111, Networking 16). Game runs as listed above.
