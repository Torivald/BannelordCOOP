# Research 0010 — Spikes S1 (MBGUID parity, save cost, world checksum) and S2 (listen server from a campaign)

## Question
ADR-0006 gates, with ADR-0010 §2:
- **S1** — do two instances that load the same save see identical MBGUIDs for heroes, troops and items? What do save capture, transfer
  and load cost? What must the pre-battle world checksum cover, and is a matching checksum enough for MBGUID parity?
- **S2** — can the campaign process act as a native multiplayer listen server and can a second instance join it without the TaleWorlds lobby?

## Bannerlord version tested
Client v1.4.8.119303, modules Harmony 2.4.2, Native, SandBoxCore, Sandbox, CustomBattle, StoryMode, BannerCoopTW (0.1.0, branch `phase2b/spikes`).
One Windows PC, two game instances (one Steam account), loopback only, 2026-10-24/25. Vanilla save `spikevanilla` (4.73 MB on disk, day 91078.43).

## Source
Spike code (debug-only, `SpikesEnabled=true`): `src/BannerCoopTW.Module/Spikes/SpikeS1.cs`, `SpikeS2.cs`; scripts `tools/dev/compare-guid-dumps.ps1`,
`run-session.sh`, `run-pair.sh`, `dismiss-safemode.ps1`. Raw artefacts (not in Git): `Documents\Mount and Blade II Bannerlord\Logs\BannerCoopTW\spikes\`
(`guid-dump-<pid>.tsv`, `world-hash-<pid>.txt`, `load-timing-*`, `save-timing-*`). API signatures verified against `_decompiled/1.4.8` (not committed).
Verdict reviewed by the architect-reviewer agent (see "Review" below).

## Date checked
2026-10-25.

## Verified facts

### S1 — MBGUID parity
1. **Cold load of the same file is deterministic.** Two instances loading `spikevanilla` simultaneously: 7 834 objects in 9 registry groups
   (CharacterObject 2918, Clan 95, Hero 1722, HeroDead 20, ItemModifier 94, ItemObject 1288, Kingdom 8, MobileParty 1195, Settlement 494):
   every (type, StringId) → MBGUID pair identical, no object on one side only (`compare-guid-dumps.ps1`). About 17 separate processes on this file and
   3 on `spike_s1_ii` gave the same registry hash each time.
2. **A save→load cycle moves the registry; the live session of the saver does not match what a loader sees.** Control (no mutation): instance loads
   `spikevanilla`, saves as `spike_ctrl` (`MBSaveLoad.SaveAsCurrentGame`, 1.4 s), another instance cold-loads `spike_ctrl`: 300 `CharacterObject` GUIDs
   differ from the original cold load. Cause by inspection of the dumps: one object (`western_mercenary_t5`) moved from registry index 2149 (GUID …2669) to
   index 2618 (…5213, +2544) and the 299 objects behind it each shifted by +1. Same object sets, same content hashes (see 4). The shift appeared
   without any mutation, so it comes from the save/load cycle, not from the `mutate` command (+gold, +5 troops) used in the first run.
3. **The shifted registry is a fixed point.** `spike_ctrl` loaded, re-saved as `spike_ctrl2`, cold-loaded again: parity with the `spike_ctrl` load
   (0 differences in all groups). So one canonicalising save→load makes later loads of that file identical.
4. **Content hash is stable across that cycle.** `A'/B'/C'` (sorted by StringId, no MBGUID values): identical before the cycle, after it, and after the
   second cycle (A' 45a24d71…, B' 8e587927…, C' 27c31b02…). The GUID-including hashes A/B/C differ between the original and the re-saved file
   (8b9407fb… vs 2eca324b…). Mutation (+1234 gold, +5 `imperial_archer` in the main party) leaves A' unchanged and changes B' (rosters) and C' (rosters+gold);
   a cold load of the mutated save reproduces the mutated B'/C' values.
5. **Hash cost.** A (registry, 15 480 items) 1.3–5 ms; B (+rosters, equipment; 76 633 items) 4–22 ms (first call slower: JIT); C (+gold, item rosters; 86 903 items)
   5–11 ms; the three sorted content variants together 9–35 ms. Computed on the main thread, one call each.
6. **Timings** (all `spikevanilla`; two instances usually running at once, which slows the second one):
   - read save file: median 904 ms (n = 25; min 798, max 2199);
   - start of load → campaign ready: median 3766 ms (n = 25; min 3513, max 8910; the high values are the second instance loading concurrently);
   - capture into memory (`Game.Save` → `InMemDriver`): median of per-instance medians 966 ms (13 instances × 5 runs; min 884, max 2712 for an instance under load);
     the raw in-memory payload is **33.2 MB** while the file on disk is **4.7 MB** (the stock file driver compresses; compression time not measured);
   - save to file through the engine (`SaveAsCurrentGame`): 1.4 s (3 runs).
   - Transfer was measured only through a loopback socket (median 18 ms for 33 MB). That says nothing about a real link; with the compressed file (≈4.5–4.7 MB)
     a real transfer time must be computed from the actual bandwidth. Estimate of one full resync on this hardware: ≈1 s capture + (compression + transfer, unmeasured)
     + ≈3.7 s load; the load dominates, not the capture.
7. **Programmatic day advance is impractical.** `advance_days` (UnstoppableFastForward) moved the clock only 0.41 days in 300 s while the game ran in the background.
   Scenario (ii) with real play (battles, purchases, N days) was therefore **not run**; only the single `mutate` change was exercised.

### S2 — listen server inside a campaign process
8. In the campaign process (no Multiplayer module, no mission, no lobby) these calls return without exception: own `IGameNetworkHandler` +
   `GameNetwork.Initialize(handler)` (the handler is null in a campaign; `OnInitialize` runs), `GameNetwork.StartMultiplayerOnServer(9999)` (`OnStartMultiplayer`;
   `MBCommon.CurrentGameType` becomes `MultiClientServer`; `netstat` shows the host process owning UDP 0.0.0.0:9999),
   `GameNetwork.AddNewPlayerOnServer(info, serverPeer: true, …)` for the host and `(…, false, false)` for a remote peer. `PlayerConnectionInfo` needs only a `PlayerId` and a name.
9. **`sessionKey` and `playerIndex` are available to managed code on the server without the lobby.** The `NetworkCommunicator` returned for the remote peer
   exposes `SessionKey` (observed 3331, 1441, 2650, 517) and `Index` (1). They can be sent to the client over our own channel.
10. **The client did not connect.** On the second instance (campaign loaded) `GameNetwork.Initialize` and `StartMultiplayerOnClient("127.0.0.1", 9999, key, index)` return
    without exception (`OnStartMultiplayer` runs, `IsClient = true`) but: `NetworkPeers` stays 0, `MyPeer` null, no `CreatePlayer` arrives, a custom server→client message
    (`SpikePing`, defined with `[DefineGameNetworkMessageTypeForMod]`, registered through `NetworkMessageHandlerRegisterer`, sent with `BeginModuleEventAsServer`) is sent without
    error and never received, and exactly 120 s after the call the engine starts calling `IGameNetworkHandler.OnDisconnectedFromServer` every frame (>50 000 calls in 9 minutes
    until `EndMultiplayer`). Three attempts: the first was invalid (client started 30 s after the peer was created), the next two were ordered correctly
    (client signals ready → host creates the peer → client connects within 0.1 s) with the same result.
11. Server side: in the invalid first attempt the unclaimed pseudo-peer was dropped after ≈20 s; in the two later attempts it stayed for the whole observation (≥ 30–40 s).
    `ElapsedTimeSinceLastUdpPacketArrived()` read 0.0 s at both samples (meaning unclear, no baseline).

## Inferences / hypotheses
- (S1) The relocated `western_mercenary_t5` is probably an object that the engine re-registers while restoring a save (a template character created at load); not confirmed.
  Whether other registries (heroes, items) shift after play (new parties, crafted items, children) was not tested; the single mutation did not change any GUID.
- (S1) A client that cold-loads the *file* the host saved will agree with every other cold loader of that file, but **not** with the host's live session. Hence the host must
  also reload from the bytes it sends (canonicalisation), or the protocol must not rely on MBGUID equality (translate by (type, StringId)).
- (S2) The managed side of a server is feasible; the blocker is in the native client connect. Candidates (untested): the stock host opens a multiplayer mission *before*
  `StartMultiplayerOnServer`; the native handshake needs state the stock lobby flow sets up; PlayerId collision between host and remote peer (both made from synthetic ids 1 and 2,
  so unlikely); firewall (not checked); a client without the Multiplayer module.

## Unknowns
- Whether the same shift occurs on other saves, with mods (Diplomacy), other module orders or on another machine.
- GUID behaviour of runtime-created objects (crafted items, new heroes/parties) between host and clients during play; scenario (ii) with real play.
- Real-network transfer time; compression time; loading from memory instead of from a file; reloading over an already running campaign.
- Everything past the stop-point in S2: connection, agents visible on the client, behaviour of single-player mission behaviours under `GameNetwork.IsServer`.
- Whether the 20–60 s per-battle resync estimate of ADR-0010 holds (only the local parts are measured: ≈5 s).

## Implementation impact
- **World checksum (ADR-0010 §2):** two parts, both order-independent (visited sorted by (type, StringId)): (a) the registry sets of CharacterObject, ItemObject, ItemModifier,
  Hero (dead and alive), Clan, Kingdom, Settlement, MobileParty by StringId only; (b) the content the battle needs — rosters, hero equipment, optionally gold and inventories.
  Cost is a few tens of milliseconds, so hashing whole types instead of only participants is fine. MBGUID values are **not** part of the checksum; a matching checksum
  says "same content", not "same MBGUIDs".
- **MBGUID rule:** an object's identity on the wire is (type, StringId); an MBGUID is a local handle. Either the host canonicalises (reload from its own saved bytes before sending,
  cost ≈ +1 s capture + ≈3.7 s load on the host) and every peer cold-loads the same bytes, or a per-battle translation table (host GUID → local GUID) is built from (type, StringId).
- **Conditional resync** still pays off only with the translation table; with canonicalisation the host reloads at every resync and the saving shrinks.
- **ADR-0006:** gate S1 is partially passed, S2 is blocked (see verdict). Option A stays a live fallback; do not start the battle phase under option B.

## License / usage constraints
No third-party code used. API shapes verified from the locally decompiled game assemblies (not committed). Diplomacy and the closed-source reference mods were not touched.

## Follow-up tests
1. S2, in priority order: (1) capture UDP on loopback (Npcap/Wireshark) and `netstat` on the client to separate "packets do not flow" from "handshake does not complete";
   (2) targeted decompile of the stock host/client flow (`LobbyGameStatePlayerBasedCustomServer`, client join path) and repeat with a minimal mission opened on the server before
   `StartMultiplayerOnServer`; (3) the same pair with the Multiplayer module loaded; (4) distinct, realistic `PlayerId`s. Time-box the four experiments before any battle work.
2. S1: crafted items and new heroes/parties after real play; the same save on a second machine; scenario (ii) by hand (battle, purchases, several days) and compare live host vs cold clients.
3. Measure compression + a real network transfer of the 4.7 MB file; measure loading from memory.
4. Gauntlet crash before the main menu (2026-10-24 22:24, two instances 20 s apart): **not reproduced** in about nine later launches; cause unknown, not fixed.

## Gate verdict (architect-reviewer, 2026-10-25)
- **S1: PARTIAL.** Proven: identical (StringId → MBGUID) pairs for 9 types in independent cold loads of the same bytes; order-independent content hash is stable. Refuted: "host and
  a client that loaded the host's save have equal MBGUIDs". Not proven: crafted items, runtime-created objects, mods, other machine. PASS is not possible without the crafted-item case.
- **S2: not passed** (blocked at the client connect; the managed server half works and sessionKey/playerIndex are obtainable). Next steps above.
- ADR-0006 status changed from "Accepted (provisional)" to "Provisional — at risk" and ADR-0010 §2 amended accordingly (separate commit).
- Review remarks applied here: the GUID-shift description (single relocated object, not a systematic +1), the missing control run (now done), and that "OK" in the S2 log only means "no exception".
