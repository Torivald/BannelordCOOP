# Research 0011 — S2 time-box: four connect experiments, and S1 on a live game

## Question
1. S2 (continuation of research 0010): why does a second campaign instance not connect to a native listen server started inside the first? Four experiments in the order of 0010 "Follow-up 1": (a) loopback capture, (b) mission opened on the server before `StartMultiplayerOnServer`, (c) stock Multiplayer module loaded, (d) distinct realistic `PlayerId`s. Stop at the first one after which the client connects.
2. S1 on a live game: does a host's live session agree with a second instance that cold-loads the same file, for crafted items, heroes and parties?

## Bannerlord version tested
Client v1.4.8.119303, one Windows PC, two instances, loopback only, 2026-10-25. Modules: Harmony 2.4.2, Native, SandBoxCore, Sandbox, CustomBattle, StoryMode, BannerCoopTW (branch `phase2c/s2-timebox`; plus Multiplayer in (c) and (d)). Save `spikevanilla` as re-saved by the user with a crafted item (ItemObject 1289, MobileParty 1190, day 91078.84).

## Source
Spike code `src/BannerCoopTW.Module/Spikes/SpikeS2.cs`, `SpikeS1.cs`; `tools/dev/run-s2.sh` (two instances, `tshark` on `\Device\NPF_Loopback`, filter `udp`), `run-game.ps1 -WithMultiplayer`. Raw artefacts not in Git: `.local-llm/out/s2a..d.pcapng`, `Documents\...\Logs\BannerCoopTW\` (mod logs, `spikes\guid-dump-*.tsv`). Signatures read in `_decompiled/1.4.8` (not committed). Logs were first summarised by the local model, then every number below was confirmed with an exact search; the local model once reported "no OnDisconnectedFromServer" for (d), which is true only because the stock handler replaces ours when Multiplayer is loaded.

## Date checked
2026-10-25.

## Verified facts

### S2 — the four experiments (client never connected in any of them)
Common setup: host takes an exclusive lock file (role), runs `GameNetwork.Initialize`, `StartMultiplayerOnServer(9999)`, `AddNewPlayerOnServer(host, serverPeer:true)`; when the client signals ready it calls `AddNewPlayerOnServer(client, false)` (managed code picks the session key, 1..4000, and calls `PrepareNewUdpSession`), publishes key and index through a file; the client calls `StartMultiplayerOnClient(127.0.0.1, 9999, key, index)`.

| | variable | UDP 127.0.0.1:client→9999 | UDP from :9999 | ICMP | client result |
|---|---|---|---|---|---|
| (a) | baseline, capture | 16 787 packets in 291 s | **0** | 0 | `NetworkPeers` 0, `MyPeer` null, no `CreatePlayer`, no `SpikePing` |
| (b) | campaign map active, bare mission `arena_empire_a` opened and `Continuing` before `StartMultiplayerOnServer` | 13 468 | **0** | 0 | same |
| (c) | (b) + stock `Multiplayer` module loaded (its handler was already installed; `NetworkMain.SetPeers` ran) | 14 795 | **0** | 0 | same |
| (d) | (c) + PlayerIds 76561198000000001/2 instead of 1/2 | 12 858 | **0** | 0 | same |

1. **The packets flow one way.** The client sends a continuous stream of 19-byte UDP payloads (27-byte UDP datagrams, always the same length) from one ephemeral port; the first bytes are constant and one byte counts up (`0890e301…`, `0890e302…`, …; our reading is a retransmitted connect request with a counter — the format is not documented and was not verified). Nothing ever comes back from port 9999 and no ICMP "port unreachable" appears, so the server socket exists and swallows the datagrams without answering.
2. On the host `AddNewPlayerOnServer` returned a `NetworkCommunicator` with the key the client used (e.g. 3965, 2740), `peers=2`, `IsConnectionActive` true for the whole run.
3. About 120 s after `StartMultiplayerOnClient` the engine starts calling `OnDisconnectedFromServer` every frame (seen with our handler in (a)/(b); in (c)/(d) the stock handler is installed, so no line of ours).
4. `GameNetwork.ElapsedTimeSinceLastUdpPacketArrived()` on the **server** logs `SCRIPT ERROR: ... elapsed_time_since_last_udp_packet_arrived: must be is_client`; the `0.0 s` readings on the host in research 0010 therefore carry no information. On the client it grows with time (24.0 s after 24 s) — the client received nothing.
5. Experiment (b) was first run invalidly (mission opened while the save was still loading; the loader removed it, mission never `Continuing`). The valid run opened the mission after a 15 s wait on the campaign map; the log line `mission Continuing, scene ready` precedes `StartMultiplayerOnServer`.
6. Reading the decompile (no run): the stock host (`LobbyGameStatePlayerBasedCustomServer.HandleServerStartMultiplayer`) does `PreStartMultiplayerOnServer`, starts a multiplayer game type, waits for `Mission.Current.CurrentState == Continuing`, then `StartMultiplayerOnServer(9999)` and `CreateServerPeer()`. Our reproduction differs in the mission (a bare arena, no `MissionNetwork`/multiplayer game-mode behaviours) and in that no lobby told the server about the client.
7. Reading the decompile (no run): `GameNetwork.HandleNetworkPacketAsClient` calls `NetworkMain.GameClient.IsInGame` on **every** server message; `NetworkMain.GameClient` is null in a pure campaign process and is set only by `MultiplayerMain` (Multiplayer module). With the module loaded it is a `LobbyClient` whose `IsInGame` is true only in the states AtBattle / HostingCustomGame / InCustomGame; otherwise the message is dropped as "ignoring post mission message". So even if the native connection completed, managed messages would be dropped or throw in a campaign client without a lobby state. This is a second obstacle (unexercised: the connection never completes, so it was never hit; probably cheap to bypass with a Harmony patch, not tested).

### S1 — host's live session vs cold loads (re-saved `spikevanilla`, with crafted item)
8. Two instances cold-load `spikevanilla` at the same time: **parity in all nine groups** (CharacterObject 2918, Clan 95, Hero 1722, HeroDead 20, ItemModifier 94, ItemObject 1289 incl. `crafted_item_0`, Kingdom 8, MobileParty 1190, Settlement 494). `world_hash` lines identical apart from the PID.
9. Live host vs cold load of what the host saved (`spikevanilla` loaded, dumped, `save_as spike_live1`; another instance cold-loads `spike_live1`):
   - **CharacterObject: 765 of 2918 GUIDs differ** (no object missing).
   - **ItemObject: 421 of 1289 GUIDs differ, and `crafted_item_0` exists only in the host's live session** (1288 after reload). Cause of the loss not investigated (candidate: unreferenced crafted item not written to the save).
   - **Hero: 1722/1722 identical. HeroDead, Clan, Kingdom, Settlement, ItemModifier: identical. MobileParty: 1190/1190 identical.**
10. Consequence: references by MBGUID differ between a host in a live session and any peer that loaded the host's save, for characters and items (including newly created runtime items), but not (in this sample) for heroes and parties.

## Inferences / hypotheses
- (S2) The server's native layer admits UDP clients only after a registration this reproduction does not perform, or through a different native entry than `PrepareNewUdpSession` alone (e.g. one that the lobby/`CreateServerPeer` path triggers); or the native session requires state that the multiplayer game-mode mission creates. Neither tested; the capture shows only that the server never answers.
- (S2) Whether a stock-faithful host (multiplayer game mode started through `Module.CurrentModule.StartMultiplayerGame`, `BannerlordNetwork.CreateServerPeer`) would answer is the only remaining cheap experiment; it needs the Multiplayer module's game-mode classes inside a campaign `Game`, which is the "big patch" this time-box excluded.
- (S1) The host's loss of `crafted_item_0` after save→load may be a vanilla behaviour for unreferenced crafted items.

## Unknowns
- Why the server stays silent (native code, not visible in managed decompile).
- Whether the packets are rejected (key/IP check) or never processed (no native tick of the UDP server outside a multiplayer mission).
- S1: other machine, real network, mods, further crafted items that are referenced by a hero/party inventory, long real play.

## Implementation impact
- **Stop-point of S2:** `StartMultiplayerOnClient` from a campaign process (with or without a mission, with or without the Multiplayer module, with realistic PlayerIds) produces a one-way UDP stream and no answer from the native server. Not solved within the time-box. Whether a stock-faithful flow (game mode via `StartMultiplayerGame`, `CreateServerPeer`) inside a campaign `Game` would connect is **untested** (inference, not a fact, that it needs a large patch).
- **ADR-0006:** the premise "B-listen: campaign host is a native MP listen server, other players join natively" has not been demonstrated and was not demonstrated in four attempts, which build on each other: (b)–(d) each add one change to the previous variant, one run per variant, no positive control (a stock host/join was never shown working on this PC with one Steam account), and the Windows firewall was not ruled out. The managed obstacle `NetworkMain.GameClient` is unexercised. See the verdict below.
- **ADR-0010:** identity on the wire = (type, StringId); MBGUID is local; per-battle translation table host→local (rule added in the ADR). Items created at runtime travel by value.

## License / usage constraints
No third-party code. `BannerlordCoop-development/` and Bannerlord Together were not opened. Decompiled output stays in the ignored `_decompiled/`; only type/method names are quoted.

## Follow-up tests
- If option B is kept: stock-faithful host flow (game mode via `StartMultiplayerGame`, `CreateServerPeer`) as a separate, explicitly budgeted spike; Wireshark decode of the 19-byte header is not possible without the engine source.
- Option A spike plan (custom LiteNetLib transport, `ITransport` already exists in `BannerCoopTW.Networking`): one SP battle mission, host-authoritative agent snapshots to one client; measure bandwidth and desync.
- S1: referenced crafted items; same save on a second PC.

## Gate verdict (architect-reviewer, 2026-10-25)
- **S1: PARTIAL.** Proven: identical MBGUIDs in two cold loads (9 groups). Refuted: the host's live session equals a reload (765 CharacterObject, 421 ItemObject differ). The gate's own fallback (translation table) is adopted, hence PARTIAL. Missing: cause of the `crafted_item_0` loss (it also affects the host's own save/resume), a crafted item held by a hero/party, second PC, mods, real play, an implemented and tested translation table.
- **S2: FAIL** for the gate as written (no lobby, public API, inside the time-box). It does not prove B impossible: no positive control, firewall not ruled out, stock-faithful host flow never run.
- **Decision under the user's rule (B did not connect → A):** ADR-0006 → Accepted: option A for MVP battles, subject to Gate A1; B rejected for the MVP. Reopen B only if (a) a budgeted (≤ 2 windows) stock-faithful spike connects a client and delivers one mod message, (b) a TaleWorlds update gives a lobby-less LAN/custom host, or (c) Gate A1 or G3 fails.
