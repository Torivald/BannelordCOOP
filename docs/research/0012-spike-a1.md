# Research 0012 — Spike A1 (option A: host-authoritative mission mirrored over our ITransport), partial

STATUS: **unfinished** — written when the usage limit was reached. Gate A1 verdict by architect-reviewer NOT obtained; crafted-item investigation (task 3) NOT done; mvp-check NOT run.

## Question
Can two game instances run the same single-player Custom Battle mission, the host authoritative, the client a mirror driven over `ITransport` (LiteNetLib)? Agents addressed by (type, StringId) through a translation table.

## Bannerlord version tested
v1.4.8.119303, one PC, two instances, loopback only, 2026-10-25. Branch `phase2d/spike-a1`. Code: `src/BannerCoopTW.Module/Spikes/A1/*`, `tools/dev/run-a1.sh`, `a1-summary.py`.

## Verified facts
### Positive control for option B (task 1)
1. Stock multiplayer, two instances, one Steam account: logging into the lobby in one window drops the other ("Соединение с лобби разорвано"). The stock custom-server pair therefore **cannot be tested on this PC** (needs a second account/PC). S2 (research 0011) is neither confirmed nor refuted by this; it stays untested, not "invalid by environment" proven.
### A1 field battle (Custom Battle, Empire vs Vlandia, scene battle_terrain_029)
2. Both instances start the same battle by script from the main menu; host/client chosen by lock file; link through `LiteNetLibServerTransport/ClientTransport`.
3. **Translation table** (host agent index → local agent) built from (character StringId, side, nearest spawn position): 26/26, 51/51 matched (spawn distance up to 1–90 m depending on timing: positions drift before deployment ends, so match order matters).
4. **Crash found and fixed:** calling `DeploymentHandler.FinishDeployment()` inside a MissionBehavior's `OnMissionTick` crashes the game natively on both instances (`FinishDeployment` removes the deployment controller from the behaviour list being iterated). From the application tick it works.
5. **LiteNetLib limit:** unreliable packets above 1023 bytes throw `TooBigPacketException`; a 51-agent snapshot (36 B/agent) must be split (25 agents per packet used).
6. **Calling `Agent.SetIsAIPaused(true)` on every agent every frame stalls the engine for 4–22 s.** Once per agent: no stalls.
7. 52 agents, 150–200 s runs, loopback: host ~74 fps and client ~73 fps during sync (wall-clock; before the battle 170–200 fps); host→client 35 KB/s (20 Hz, 36 B/agent); input→acknowledged-by-snapshot latency p50 41 ms, p95 66 ms, max 94 ms; input→visible movement of the controlled agent 21–171 ms (single samples); 28 hits and 1 death replicated (host computed damage). Client applies a 25-agent chunk in ~0.05 ms.
8. Controlled agent: `Controller = AgentControllerType.None` + `MovementFlags`/`LookDirection` writes moves it; `SetIsAIPaused` does not. Player formations need a `Charge` order or no fighting happens.
9. Client death replication: `MakeDead` throws inside the client's morale logic (no killer); `Die(Blow)` with a real killer index is used instead (applied in the 52-agent run, no error).
10. `dt` is clamped by the engine (~67 ms): frame rate must be measured on the wall clock (earlier numbers in this session based on dt were wrong).
### A1 siege (scene empire_siege_001, 30 per side)
11. 44 siege objects matched 44/44 by (script type name, entity name) on both instances.
12. State events host→client flow (59–137 per 2.5 min). On the client, writing `SiegeLadder.State` works (137 applied, no crash); **writing `RangedSiegeWeapon.State` crashes the client natively**; applying `UseGameObject` for the host's controlled agent also ended in a crash (one run). The client's own siege logic drifts from the host (67 of 137 events found a different local state): puppet siege objects must be driven, not left to run.
### 100 agents
13. Host+client with sync: stalls up to 22 s (host) / 13 s (client), ack latency mean ~1.4 s; the battle ends after ~90 s so a 10-minute soak was **not** achieved. The solo baseline for 100 agents was running when the limit was hit — result not read, so it is unknown whether the stalls come from sync or from 2×100-agent battles on one PC.

## Not verified
10-minute stability; 100 agents without stalls; attacks/animations replicated visually (action channel values are copied, not observed); knock-out (Unconscious) path; siege weapon use/firing; a second PC / real network; crafted-item loss after save→load (task 3).

## Gate A1 (MASTER_PLAN: 1 controlled + 100 puppets, 10 min, <150 ms input latency, bandwidth)
**Not passed, not failed:** 52 agents work with measured latency/bandwidth; 100 agents and 10 minutes unproven; siege replication partly crashes. Verdict by architect-reviewer pending.

## Follow-up
Read the solo n100 run; soak with `DisableDying`; puppet siege objects (disable local logic, apply ladder state, avoid ranged State writes); crafted item (`SpikeCrafted.cs` report command exists, not run); architect-reviewer verdict; PR review.
