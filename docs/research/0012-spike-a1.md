# Research 0012 — Spike A1 (option A: host-authoritative mission mirrored over our ITransport)

STATUS: field battle part **done** (100 agents, 12 min); siege part **partly done and deliberately paused** (owner decision 2026-10-25: sieges after a working multiplayer exists); crafted-item investigation **static analysis only**.

## Question
Can two game instances run the same single-player Custom Battle mission, the host authoritative, the client a mirror driven over `ITransport` (LiteNetLib)? Agents addressed by (type, StringId) through a translation table.

## Bannerlord version tested
v1.4.8.119303, one PC, two instances, loopback only, 2026-10-25. Branches `phase2d/spike-a1` (PR #15) and `phase2d/spike-a1-cont`. Code: `src/BannerCoopTW.Module/Spikes/A1/*`, tools `tools/dev/run-a1.sh` (env `INSTANCES=1|2`), `a1-summary.py`. Modes are passed as `SpikeA1=auto:<mode>` (e.g. `n100:nodie`, `siege:n30`, `solo:n100`).

## Verified facts (first session, 52 agents)
### Positive control for option B
1. Stock multiplayer, two instances, one Steam account: logging into the lobby in one window drops the other. The stock custom-server pair therefore cannot be tested on this PC (needs a second account/PC). S2 (research 0011) stays untested.
### Field battle (Custom Battle, Empire vs Vlandia, scene battle_terrain_029)
2. Both instances start the same battle by script; host/client chosen by lock file; link through `LiteNetLibServerTransport/ClientTransport`.
3. Translation table (host agent index → local agent) from (character StringId, side, nearest spawn position): 26/26, 51/51, and 101/101 at 100 agents (max spawn distance up to 291 m: positions drift before deployment ends, match order matters).
4. **Crash:** `DeploymentHandler.FinishDeployment()` inside a MissionBehavior's `OnMissionTick` crashes the game natively. From the application tick it works.
5. LiteNetLib: unreliable packets above 1023 bytes throw `TooBigPacketException`; snapshots are split (25 agents/packet, 36 B/agent).
6. Calling `Agent.SetIsAIPaused(true)` on every agent every frame stalls the engine for seconds; once per agent is fine.
7. Controlled agent: `Controller = AgentControllerType.None` + `MovementFlags`/`LookDirection` writes move it; `SetIsAIPaused` does not. Player formations need a `Charge` order.
8. Client death replication: `MakeDead` throws in the client's morale logic; `Die(Blow)` with a real killer index is used.
9. `dt` is clamped by the engine (~67 ms): frame rate must be measured on the wall clock.

## Verified facts (second session — 100 agents, 2026-10-25)
All runs: one PC, two instances (unless stated), loopback, `a1-summary.py` output; wall-clock fps.

| Run | Mode | Result |
|---|---|---|
| Solo, 1 instance, no network | `solo:n100` | 35 windows (175 s): fps mean 168, min 138, maxDt 67 ms, no stalls. The battle itself is not the cause of the old stalls. |
| Solo, 2 instances, no network | `solo:n100` | host fps 100 (median 83), client 78, maxDt ≤ 98 ms. Two 100-agent battles on one PC do not stall either. |
| Sync, no puppet AI pause | `n100:nopuppetpause` | host/client fps ~73, maxDt 80 / 275 ms, ack p50 40 ms p95 82 ms, 421 chunks/5 s. |
| Sync, default | `n100` | host fps 74 maxDt 99 ms; client fps 72 maxDt 540 ms; ack p50 42 / p95 70 ms; tx 70 KB/s (host), rx 42 KB/s; 5 hits. Input→visible movement 152 ms (single sample). |
| **Soak, 12 min, no deaths** | `n100:nodie` (`Mission.DisableDying = true`) | 143 windows (715 s) of sync, 102 agents, **0 tick errors, no crash**. Host fps mean 77 (min 26), maxDt 121 ms. Client fps mean 75; ack p50 ≈ 40 ms, p95 ≈ 62 ms for all but one episode; host→client 67 KB/s; 824 hits replicated, 0 deaths (by design). Input→movement 159 ms (single sample). |

10. **The multi-second stalls of the first session (22 s / 13 s, ack ~1.4 s) did not reproduce** with the same 100-agent setup in the current build (`n100`, `n100:nodie`). Neither the battle size nor two instances on one PC causes them (solo controls). The first-session run had the per-frame pause bug fixed later in the same session; the exact cause of the earlier stall is not proven — likeliest the per-frame `SetIsAIPaused` (fact 6) in the binary used then. Treat as "not reproduced", not "explained".
11. **One unexplained transient in the soak:** minutes 3–4 (15:44:31–15:45:07, 7 windows ≈ 36 s), client only: apply cost per 25-agent chunk 10 ms instead of 0.05 ms, client fps 4–6 (min 4.3), ack p50 ≈ 120 ms; host unaffected, self-recovered, not caused by our own builds (those ran later). Cause unknown (not looked into: no profiler run). Must be understood before relying on the client frame rate; Gate A1 numbers below include it.
12. Bandwidth: ~36 B/agent × 20 Hz ≈ 70 KB/s at 100 agents (uncompressed, no delta). Plenty of headroom for compression; not measured on a real network (no loss/jitter).

## Siege (scene empire_siege_001, `siege:n30`) — paused by owner decision
13. 44 siege objects matched 44/44 by (script type name, entity name) on both instances (first session, repeated).
14. **Puppet mode implemented** (`SiegePuppetize`): the client calls the public `ScriptComponentBehavior.SetScriptComponentToTick(TickRequirement.None)` on every ladder / ranged weapon, so its own OnTick/OnTickParallel (state machine, AI usage, animation) is off; only host events move the state. Switch `nopuppetsiege` restores the old behaviour.
15. **Finding: in a single-player mission `GameNetwork.IsClientOrReplay` is false, so writing `RangedSiegeWeapon.State` through the setter runs the server branch on the client**: the Ballista's state 2 (Shooting) calls `ShootProjectile` locally (managed exception in the first puppet run; this is also the likely cause of the earlier native crash). Fix: write only the value into the private `_state` field by reflection (default; mode `setter` = old behaviour). Ladders still go through the setter.
16. Run `siege:n30` with puppets + raw ranged state (46 sync windows, 30 agents/side): host sent 290 siege events, client applied 290, 240 state writes, **0 local-vs-host divergences (was 67 of 137 without puppet mode)**, 242 agent-uses-object (puppet teleported to the standing point and `UseGameObject`), **0 tick errors, no crash**. Client fps 43 / host 50 (scene is heavy).
17. **Not verified**: ladder events were not confirmed in this run (counts are per object kind not logged); throwing machines (mangonel/trebuchet): the `mach` mode (adds Catapult, Onager, Trebuchet, Bricole to the siege parties) **crashed the game natively right after the battle opened**, before any sync — not investigated, probably the engine list is invalid for this scene; visuals (ladder animation does not run on the puppet: it is driven by OnTick, so the ladder stays at its pose; projectiles are not replicated at all since the client never fires), real use of a ranged weapon by a client-controlled agent, gates/rams/towers.
18. Consequence for design: siege objects in a mirrored mission need **host-driven frames/animations and replicated projectiles**, not only a state enum. Estimate stays "largest risk of option A"; deferred to after multiplayer works (owner, 2026-10-25).

## Crafted item lost after save→load (task 3) — NOT reproduced
19. Static reading of `CraftingCampaignBehavior` (1.4.8): a crafted item is registered in `MBObjectManager` and added to `_craftedItemDictionary` (saved) by `OnNewItemCrafted`, plus `_cratingItemsHistory` (max 10). On load, `InitializeCraftedItemData` → `Crafting.InitializePreCraftedWeaponOnLoad`; **if that returns `DefaultItems.Trash`/null the item is unregistered** (the only path found that drops it silently). Older saves (< v1.3.2) also prune dictionary entries whose template pieces are not ready. Hypothesis for `crafted_item_0` missing after reload: the load-time rebuild returned Trash/null for that design (e.g. piece set not unlocked). Not proven.
20. Attempted: `crafted_report` on user saves `livaem` and `ne livaem` through `SpikeAutoLoadSave` + `SpikeAutoRun=crafted_report`: the saves started loading (1.2–1.4 s read) but the report never printed in 90 s (cause not investigated: load dialog, module-list mismatch with the save, or the script queue). **No data.**
21. The vanilla (no-mod) repetition needs manual play in the clean game. Procedure for the owner: new campaign (or any save), craft one weapon in a smithy, put it in the inventory, save as `vanilla_craft_a`, quit to menu, load it, check whether the weapon is in the inventory; repeat with the weapon equipped on the hero. Record yes/no per case here. Until then the loss is not attributed to the mod or to vanilla.

## Not verified
Real network / second PC; knock-out path; visual fidelity of attacks/animations (channel values are copied, not observed); client frame-rate transient (fact 11); siege items in fact 17; crafted item cause.

## Gate A1 (MASTER_PLAN: 1 controlled + 100 puppets, 10 min, <150 ms input latency, bandwidth)
Measured on the field battle, loopback, one PC:
- 1 controlled agent + 101 puppets for 12 min: **passed** (no crash, no tick errors, deaths disabled).
- Latency: input→acknowledged-by-snapshot p50 ≈ 40 ms, p95 ≈ 62–72 ms (this is the host echoing the input sequence, i.e. receipt, not effect); the gate metric input→visible movement is only single samples, 21–171 ms, **159 ms at 100 agents in the soak = over the 150 ms limit** (n=1, includes acceleration and 33/50 ms send cadences).
- Bandwidth: ≈ 70 KB/s host→client at 100 agents.
- Caveats: deaths disabled in the soak (the death path was only checked at 52 agents), loopback only, one unexplained client transient, sieges not covered (paused).
Verdict: **PASS WITH CONDITIONS** (architect-reviewer, below).

## Review
architect-reviewer, 2026-10-25: **Gate A1 PASS WITH CONDITIONS** for the field battle; ADR-0006 option A stays accepted (no reopen condition met).
- Why not clean: soak ran with deaths disabled (deaths only checked at 52 agents); the only end-to-end latency figure is 159 ms (> 150 ms, n=1); unexplained client transient.
- Over-stated claims corrected above: the ack latency is receipt, not effect; "0 tick errors" does not mean "in sync" — the client does not switch off its own combat (puppets get action channels and MovementFlags, only AI is paused), health/position are overwritten every 50 ms, which hides divergence, and there is no agent divergence counter; teleport above 0.25 m without interpolation; the controlled agent is scripted (forward + periodic attack), no prediction; bandwidth headroom untested on a real network.
- Conditions before Phase 6/7 (A1b, 1–2 windows): (1) ≥ 10 min at 100 agents **with deaths on**, per window log client vs host alive count, health mismatches, max position error; (2) ≥ 30 input→visible-effect samples measured from the first host position change, p95 < 150 ms; (3) suppress or count client-local damage/dying, rerun (1); (4) reproduce/profile the client transient — if it recurs it blocks 7b, not Phase 6; (5) one run on a second PC or with loss/jitter emulation before Gate G3.
- Not checked by the reviewer: raw logs, a1-summary.py, whether the client takes local hits in practice.
