# ADR-0006: Battle transport — custom netcode vs native multiplayer stack

Status: **Accepted (2026-10-25, research 0011): option A — custom netcode over single-player missions — for MVP battles, subject to Gate A1. Option B (native multiplayer stack, listen or dedicated) rejected for the MVP.** Superseded history: decided 2026-10-18 as B-listen (provisional, gates S1/S2); "provisional — at risk" 2026-10-25 (research 0010).

**Note (2026-10-25):** the owner has no second PC, so the positive control for B (stock host + client on two Steam accounts/PCs) cannot be run now; B stays closed unless a tester with a second copy becomes available.

**Reopen B only if** (a) a budgeted (≤ 2 windows) stock-faithful spike (`Module.CurrentModule.StartMultiplayerGame` + `BannerlordNetwork.CreateServerPeer` inside a campaign `Game`, with a positive control and the firewall ruled out) connects a client and delivers one mod message; (b) a TaleWorlds update provides a lobby-less LAN/custom host; (c) Gate A1 or G3 fails.

> The "Decision (Phase 2 verdict)" section below describes the **former** B-listen decision and is kept as history. Reason for the change: S2 (four experiments, research 0011) — the native listen server inside a campaign process never answered the client's UDP connect stream; S1 — the host's live session does not match peers that load its save (765 CharacterObject, 421 ItemObject GUIDs differ). Neither proves B impossible (no positive control; stock-faithful flow not run).

**Option A consequences:** battles are replicated over our `ITransport` (Phase 4; unreliable-sequenced channel for snapshots, reliable for events). References to game objects use (type, StringId) (ADR-0010). New Spike A1 and Gate A1 precede Phase 6 (see MASTER_PLAN).

## Decision (Phase 2 verdict)
Run campaign battles on the **native multiplayer stack, hosted by the campaign host's own game process**:
the host keeps its campaign `Game`, calls `GameNetwork.StartMultiplayerOnServer(port)` (client builds run
this as `MultiClientServer` = listen server, 0005), opens the campaign battle mission with its normal SP
battle behaviors plus the MP network behaviors; the other co-op players (who run the same synced campaign)
join with `GameNetwork.StartMultiplayerOnClient(...)`. Players take slots via
`Mission.ReplaceBotWithPlayer(agent, peer)`; casualties go back through `MapEventSide.OnTroop*` locally on
the host (0003). The Dedicated Server variant of B is **rejected for the MVP** (needs TaleWorlds lobby +
AuthToken, has no SandBoxCore scenes/campaign objects, cannot host the campaign).

### Evidence
| ADR question | Answer (1.4.8) | Source |
|---|---|---|
| 1. Spawn campaign characters/equipment in MP | Yes, `CreateAgent` carries any `BasicCharacterObject` + equipment **by MBGUID reference** → requires identical object registration on all machines; campaign players have the same world loaded | 0005 |
| 2. Possession / switch on death | Native: `ReplaceBotWithPlayer` (Captain mode uses it after death), `MissionPeer.ControlledAgent`, `Agent.IsPlayerControlled` | 0005, 0004 |
| 3. Listen server from the SP client | Yes: `StartMultiplayerOnServer` → `MultiClientServer`; used by `LobbyGameStatePlayerBasedCustomServer`; admission via public `AddNewPlayerOnServer` | 0005 |
| 4. Campaign scenes in MP | `battle_terrain_*` are in client SandBoxCore → present on every player's machine (listen server), absent on the dedicated server | 0005 |
| 5. Result round-trip | Host = server; per-troop results via public `MapEventSide.OnTroopKilled/Wounded/Routed(UniqueTroopDescriptor)`, then normal `PlayerEncounter` result flow | 0003 |
| Agent replication of SP spawns | `Mission.SpawnAgent` broadcasts `CreateAgent` whenever `GameNetwork.IsServerOrRecorder`; late joiners get `SendExistingObjectsToPeer` | 0004 |

### Gates (must pass before Phase 6 commits to B; run as Phase 2b/4 spikes)
- **S1 — MBGUID parity**: same save loaded on two instances → identical MBGUIDs for heroes, troops, items
  (incl. crafted). Fail → GUID remap layer or registration of battle objects in fixed order; if impossible → A.
  S1 also measures save capture/transfer/load times and validates the pre-battle checksum scope
  (ADR-0010 §2: resync only on checksum mismatch).
- **S2 — listen server in campaign**: host in campaign starts `StartMultiplayerOnServer`, opens a minimal
  networked battle mission; instance 2 connects via `AddNewPlayerOnServer` + `StartMultiplayerOnClient`
  (no TaleWorlds lobby) and sees agents. Fail → A.
- Also record: which SP mission behaviors misbehave with `GameNetwork.IsServer`.

### Consequences
- Battle traffic = engine UDP to the host's port → host must be reachable (port forward / VPN / tunnel,
  ADR-0011/0012).
- Allocation core unchanged; battle adapter targets `ReplaceBotWithPlayer` + troop supplier filtering.
- Campaign sync (ADR-0010) must guarantee all players have the identical world when a battle starts.

## Option A — custom netcode over single-player missions
Own transport (e.g. LiteNetLib, MIT), Harmony patches, own agent/state replication on top of SP
missions; authoritative host.
+ full control, works with any SP mission content
− SP missions are not network-aware: agent movement, combat, AI, physics must be replicated by us;
  highest effort and desync risk.

## Option B — native multiplayer mission for the battle
When a campaign battle starts, the initiator's game exports the battle (parties, heroes, troops,
equipment, scene, sides) and a Bannerlord multiplayer (dedicated or listen) server runs a custom
game mode that spawns those agents; other players join as MP clients, pick a slot, and possess the
agent via native MP control; result is sent back to the campaign.
+ engine already replicates agents, combat and orders; public evidence (Warhost) shows it works
− needs version-matched dedicated server, campaign→MP data transfer, custom MP game mode/UI,
  SP-only items/scenes may need workarounds; host-side campaign must pause/wait.

## To verify in Phase 2
1. Can an MP mission spawn arbitrary BasicCharacterObject/equipment from campaign data?
2. How MP agent possession works (peer ↔ controlled agent), switching agent on death.
3. Can a listen server run from the SP client, or is the dedicated server mandatory?
4. Scene availability (campaign battle scenes in MP).
5. Result round-trip (casualties, captured heroes, loot) back to campaign.

## Allocation logic is independent
Reservation/allocation (BattleSession, slots, timeouts) is pure C# and identical in both options.
Build and unit-test it first.


## Spike results (2026-10-25, research 0010)
- **S1 — partial.** Cold loads of the same file agree on every (type, StringId) → MBGUID; a save→load cycle relocates one CharacterObject and shifts 299 others by +1, after which the registry is a fixed point.
  The host's live session therefore differs from clients that loaded its save. Remedy to adopt: identity on the wire = (type, StringId) with a per-battle GUID translation table, or canonical reload on the host.
  Not tested: crafted items, runtime-created objects, mods, other machines.
- **S2 — blocked.** Server half works in a campaign process (Initialize, StartMultiplayerOnServer, AddNewPlayerOnServer; session key and peer index readable without a lobby);
  the second instance's `StartMultiplayerOnClient` returns but never connects (disconnect callback after 120 s). Cause open; four time-boxed experiments are listed in research 0010.
- Do not start Phase 6 under option B until S2 passes; keep a short spike plan for option A ready.

## Spike A1 result (2026-10-25, research 0012)
Gate A1 for the field battle: **passed with conditions** (architect-reviewer). 1 controlled + 101 puppets for 12 min without crash, ack p50 ≈ 40 ms, ≈ 70 KB/s on loopback; conditions (deaths on, effect-latency p95 < 150 ms, divergence counters, client transient, second PC) are listed as "A1b" in MASTER_PLAN. No reopen condition is met; option A stays accepted. Sieges are deferred until a working multiplayer exists (owner decision).

## Spike A1b result (2026-10-25, research 0013)
Gate A1b: **passed with conditions** (architect-reviewer). Real two-sided fights (100 vs 100), 12–13 min, deaths on: no divergence except one unexplained single-agent alive-set mismatch; effect latency p95 128–134 ms on loopback; emulated loss 2–5 % and 50–100 ms each way tolerated without divergence. Option A stays accepted; the remaining conditions are carried to Gate G3.
