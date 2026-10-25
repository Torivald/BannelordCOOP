# Research 0013 — Spike A1b (conditions of the Gate A1 verdict)

STATUS: done (second session, 2026-10-25, after a pause). Field battle only (sieges out of scope, `A1Siege` untouched); no second PC. First-session results are kept below; the second session's results are in "Results, second session" and supersede them where they differ.

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


---

# Second session (after the pause) — supersedes the open items above

## Additional findings
4. **Test-harness artefact, not a mod problem: an uncapped host starves the second window.** After ~20:00 the client collapsed to 2–12 fps (ack 700–800 ms, hundreds of STALL lines) while the host ran at its 200 fps cap. It reproduced without emulation, with the committed (earlier good) build and with the old code — so it was the environment/GPU share, not code. Capping **both** windows at 60 fps (`engine_config.txt: max_framerate = 60`, set and restored by `run-a1.sh`) removed it completely (client 60.0 fps, min 56–57, in all later runs). The cause is **not proven**: GPU contention was never measured; Windows throttling of the background window or CPU scheduling fit equally well. It does not explain the first-session 36 s dip (there the host ran at ~77 fps, not 200, and apply cost per chunk rose ~200x), so that dip stays unexplained. Cost: with a 60 fps cap each side adds up to ~17 ms frame granularity to latency figures.
5. **Network emulation now works** through our own decorator (wired into `A1Behavior.Initialise`; the useless LiteNetLib switches were removed). Added latency shows up exactly: ack p50 ≈ 197–200 ms (≈ 40 ms base + 2 × ~75 ms one-way mean).

## Results, second session (60 fps cap, loopback, one PC)
| Run | Setup | Result |
|---|---|---|
| netA3 | `n50:brawl:loss2:lat50-100` 6 min | host/client 60 fps, 0 STALL; **alive 0/71 mismatching windows, hp equal**, 57 deaths + 1551 hits replicated, 0 local blows; pos error p50 0.19 / p95 0.35 m; ack p50 197 / p95 234 ms; effect start p50 266 / p95 317 ms (n=101). |
| netB3 | `n50:brawl:loss5:lat50-100` 6 min | 60 fps, 0 STALL; alive 0/71 mismatching, hp equal, 85 deaths; ack p50 200 / p95 236 ms; effect start p50 267 / p95 333 ms (n=101). Reliable events (deaths) never lost; no divergence even at 5 % loss. |
| soakD | `n100:brawl`, 100 vs 100, no emulation, 13 min | 157 windows, both 60.0 fps (min 56.8), 0 tick errors, 0 STALL; 201 → 78 agents in 2.5 min (123 deaths), then 75–78 agents for the remaining ~10 min (≥ 100 agents for 3.25 min, ≥ 75 for the full 13 min); 4886 hits, 124 deaths; ack p50 39 / p95 53 ms; 77 KB/s; effect start n=120 p50 84 / p95 134 ms; **one divergence (the counted "local death" is any client removal without a host death message — routing, retreat or a fall would count too; not proven to be a death): from minute ~7 the client had one live agent less than the host (76/75) for the rest of the run** (73/157 windows with a mismatch, hp |d| 100 = one agent, `localDeaths = 1`, 0 local blows). No log of the removal (diagnostic added afterwards). |
| soakE | same as soakD with diagnostic for local removals, 12 min | 146 windows, 60 fps, 0 STALL, 178 deaths, 4196 hits; **0 alive mismatches, 0 local deaths**; the fight ran down to 22 survivors (≥ 100 agents only 2.5 min); effect start n=208 p50 99 / p95 133 ms (enemy > 15 m: p95 134; melee: p95 117); stop p50 150 / p95 184. The soakD divergence did **not** recur, so its cause is unknown. |

## Task status (final)
1. Soak ≥ 10 min with deaths and counters: **done** (soakD/soakE 12–13 min, 124–178 deaths; ~100 agents held 2.5–3.25 min, 75+ agents 13 min in soakD). One unexplained alive-set divergence in soakD (a single client-side removal without host message, not repeated in soakE).
2. ≥ 30 effect samples: **done**, 120–208 per run. **p95 128–134 ms (< 150 ms) without emulation**; melee does not worsen it any more once the script walks back and forth (the first-session 324 ms was the agent being blocked). Under emulated +RTT the figure is p95 ≈ 320–333 ms by construction.
3. Local damage suppression/counting: **done** (counter validated in solo; 0 local blows in all synced runs).
4. FPS dip: **explained as harness starvation** (finding 4), not reproduced after the cap; no code defect found.
5. Network emulation: **done** (loss 2 % and 5 %, 50–100 ms each way, 6 min each): no divergence, no stalls, recovery not an issue; latency grows by the added delay as expected. Not tested: bursty loss, longer outages, reordering beyond jitter, asymmetric links, a real network.

## Design requirements that fall out (for Phase 6/7)
- Periodic **alive-set reconciliation**: the host should send the set of live agent ids every second (not only a count) so the client can detect and repair a missed or local removal (soakD); suppress client-local removals of mirrored agents.
- Host must keep running unfocused (`StopGameOnFocusLost = false`) and test harnesses must cap fps of both windows.
- Latency budget: base effect latency p50 ≈ 85–100 ms / p95 ≈ 130 ms on loopback at 60 fps; any real link adds its round trip on top, so on a 100 ms RTT link p95 ≈ 230 ms — client-side prediction for the controlled agent is likely needed for feel (not measured).
- Interpolation of puppet positions and snapshot delta/compression remain open items for 7b (position error before correction p50 0.15–0.2 m / p95 0.3–0.4 m is from correction on every snapshot, with no interpolation).

## Verdict
See "Review" below.

## Review (architect-reviewer, 2026-10-25): A1b = PASS WITH CONDITIONS; ADR-0006 option A stays accepted
Per condition: C1 soak PASS WITH CONDITIONS (12–13 min with deaths, but ~100 agents only 2.5–3.25 min, so "10 min at 100 agents" was **not** met; one unexplained divergence); C2 latency PASS (p95 128–134 ms, n = 120–208, client→host→client); C3 local damage PASS with a caveat (counter validated, but a client-side removal got through in soakD, so suppression is not complete); C4 FPS dip PASS WITH CONDITIONS (mitigated by the cap, root cause unknown); C5 emulation PASS (receiving side only; a lost reliable packet is a fixed +300 ms).

Corrections accepted from the review:
- Effect latency = first snapshot with > 2 cm movement after the input edge; bumping in melee can trigger it early (melee p95 117 ms < far 134 ms), so "melee does not worsen it" is **not shown**; "near" only means an enemy within 15 m. The edge is stamped when the input packet is built, so the wait for the next 33 ms input send is not included. All figures are at a 60 fps cap.
- "hp equal" is a weak check (the snapshot overwrites Health on every agent every 50 ms) and the alive check compares counts only, so two opposite errors would cancel.
- The snapshot sequence number is read and discarded: under 50–100 ms jitter an older snapshot can be applied after a newer one (not handled, not measured).
- A removed agent is skipped by `!a.IsActive()`, so nothing can repair a missed or local removal.

Before Phase 6: this note corrected (done); focus-pause and fps-cap requirements recorded (above); `dotnet test` run after the last change — Core 111, Networking 20, all pass; `run-a1.sh` fixed (first backup kept, restore on any exit). Before Phase 7b / Gate G3: full alive-id set from the host every second plus a client repair path; drop out-of-order snapshots; find the soakD cause with the `LOCAL removal` log line; a run that keeps ~100 agents alive for 10 min (reinforcements); client uncapped while the host stays capped; knock-out and death of the controlled agent (it is set immortal here); a real network during playtests.
