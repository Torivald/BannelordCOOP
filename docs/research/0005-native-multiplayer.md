# Research 0005 — Native multiplayer stack: can a campaign battle run on it?

## Question
Answer the five ADR-0006 option-B questions: (1) spawning campaign characters/equipment in an MP
mission, (2) agent possession and switching, (3) listen server vs dedicated server, (4) scenes,
(5) result round-trip — plus how players are admitted and whether custom modules/game modes can run.

## Bannerlord version tested
Client v1.4.8.119303; Dedicated Server build 24571419 (modules v1.4.8). Decompiled with ilspycmd 9.1
(`_decompiled/1.4.8/`, not in Git).

## Source
Client: `TaleWorlds.MountAndBlade.dll`, `Modules/Multiplayer/.../TaleWorlds.MountAndBlade.Multiplayer.dll`,
`TaleWorlds.MountAndBlade.Diamond.dll`, `TaleWorlds.ObjectSystem.dll`. Server: `bin/Win64_Shipping_Server/`
`TaleWorlds.MountAndBlade.dll`, `…Multiplayer.dll`, `…DedicatedCustomServer.dll`. Scene folders of both installs.

## Date checked
2026-10-18

## Verified facts

### Client and server share almost all MP code
- Client and server `TaleWorlds.MountAndBlade.dll` decompile to the same file set (1025 vs 1024 files;
  only `MultiplayerMissionAgentVisualSpawnComponent` is client-only). Server game-mode logic
  (`MissionMultiplayerFlagDomination`, `MissionNetworkComponent` server handlers, `SpawnComponent`, …)
  **ships in the client too**.
- Difference: `GameNetwork.IsDedicatedServer => false` (client) / `true` (server).
  `GameNetwork.PreStartMultiplayerOnServer()` sets `MBCommon.GameType.MultiClientServer` on the client
  (listen server: host plays and serves) and `MultiServer` on the dedicated server.
  `GameNetwork.IsServer` is true for both.

### (3) Listen server exists in the client
- `GameNetwork.StartMultiplayerOnServer(int port)` → `PreStartMultiplayerOnServer()` →
  `MBAPI.IMBNetwork.InitializeServerSide(port)` → `StartMultiplayer()`.
- The client's Multiplayer module uses it: `LobbyGameStatePlayerBasedCustomServer.HandleServerStartMultiplayer()`
  → `GameNetwork.PreStartMultiplayerOnServer()`, `Module.CurrentModule.StartMultiplayerGame(type, scene)`,
  wait for the mission, `GameNetwork.StartMultiplayerOnServer(9999)`, `BannerlordNetwork.CreateServerPeer()`
  (adds the host as server peer, admin), `GameNetwork.ClientFinishedLoading(GameNetwork.MyPeer)`.
  This is the official "player-based custom server" (hosted from a game client via the TaleWorlds lobby).

### Player admission
- `GameNetwork.AddNewPlayerOnServer(PlayerConnectionInfo info, bool serverPeer, bool isAdmin)` /
  `AddNewPlayersOnServer(PlayerConnectionInfo[], bool)` / `HandleNewClientConnect(…)` create the peer
  and a session key; `PlayerConnectionInfo(PlayerId)` with `Name`, `SessionKey`, `AddParameter(name, obj)`.
  Reconnect matches `DisconnectedNetworkPeers` by `PlayerData.PlayerId`.
- Client side: `GameNetwork.StartMultiplayerOnClient(string serverAddress, int port, int sessionKey, int playerIndex)`
  → `InitializeClientSide(...)`.
- Official flow (dedicated custom server, `DedicatedCustomGameServerHandler`): server registers with the
  TaleWorlds lobby using an **AuthToken** (`GetDedicatedCustomServerAuthTokenMessage`,
  `CustomBattleServerReadyMessage.AuthToken`); a player's join request arrives from the lobby in
  `OnClientWantsToConnectCustomGame(PlayerJoinGameData[])`, which builds `PlayerConnectionInfo`s
  (`PlayerData`, `UsedCosmetics`, `PlayerId`, `IsAdmin`, `IpAddress`) and admits them. The lobby then
  tells the client address/port/peer index/session key (`JoinGameData` →
  `LobbyGameStateCustomGameClient.SetStartingParameters(...)`).

### Game modes are pluggable
- `Module.AddMultiplayerGameMode(MultiplayerGameMode)`; `Module.StartMultiplayerGame(string type, string scene)`
  looks the mode up by name. Vanilla registers `MissionBasedMultiplayerGameMode` instances
  (TeamDeathmatch, Duel, Siege, Battle, Captain, Skirmish) that call
  `MultiplayerMissions.Open*Mission(scene)` = `MissionState.OpenNew(...)` with network behaviors
  (`MissionLobbyComponent`/`MissionMultiplayerGameMode*`, `SpawnComponent`, `MultiplayerTeamSelectComponent`,
  `AgentHumanAILogic` on the server, scoreboard, polls, admin…).
  `MissionBasedMultiplayerGameMode.StartMultiplayerGame` asserts that no mission is running.
- MP game state type: `MultiplayerGame : GameType` (client and server Multiplayer module).

### (2) Possession / switching
- `Agent.IsPlayerControlled => IsMine || MissionPeer != null`; `MissionRepresentativeBase.ControlledAgent`.
  Messages `SetAgentPeer`, `SetAgentOwningMissionPeer`, `ReplaceBotWithPlayer`, `BotsControlledChange`,
  `SetPeerTeam`; client request `RequestToSpawnAsBot`.
- **`Mission.ReplaceBotWithPlayer(Agent botAgent, MissionPeer missionPeer)`** (server): sends
  `ReplaceBotWithPlayer` to the peer, sets `botAgent.MissionPeer = missionPeer`, moves the bot into the
  peer's `ControlledFormation`, makes the player sergeant of that formation. Used by Captain mode
  (`MissionMultiplayerFlagDomination`): after death, with `WantsToSpawnAsBot`, the player takes over
  the followed bot or the healthiest AI agent of their formation.
  → Native "take control of an existing AI soldier" exists and is server-authoritative.

### (1) Spawning arbitrary characters / equipment
- `NetworkMessages.FromServer.CreateAgent` carries `BasicCharacterObject Character`, `Monster`,
  `Equipment SpawnEquipment` (armour/horse items), `MissionEquipment` (weapons), `BodyProperties` +
  seed, team, formation, peer, position… **Character and every item are sent as object references**:
  `GameNetworkMessage.WriteObjectReferenceToPacket(MBObjectBase)` writes `MBGUID`, the client resolves
  `MBObjectManager.Instance.GetObject(MBGUID)`. Weapons: `ModuleNetworkData.WriteWeaponReferenceToPacket`
  = item reference + raw data/ammo/usage + optional banner code (crafted weapons are still item refs).
  Body properties are sent by value (hero faces work).
- `MBGUID = (typeNo << 26) | subId`, `subId = ++_objCount` per type at registration
  (`MBObjectManager`) → **IDs depend on registration order**. Server and every client must have the
  same characters/items registered in the same order (or the same GUIDs restored).

### (4) Scenes
- Campaign field-battle scenes `battle_terrain_*` (98 on the client) live in **SandBoxCore/SceneObj**.
  The Dedicated Server install has only `Native` (80 scenes, no battle_terrain) and `Multiplayer`
  modules — no SandBoxCore. Siege/town scenes are in SandBox/SandBoxCore as well.

### Dedicated server and modules
- Dedicated custom server code is a set of SubModules (`DedicatedCustomServerSubModule`, …) loaded like
  any module; players arrive only through the TaleWorlds lobby + AuthToken (see above).
- Not verified: starting the server with an extra `_MODULES_` entry (our module, SandBoxCore copy).

## Inferences / hypotheses
- **H1 — listen server inside the campaign process (preferred variant of B).** Every co-op player runs
  the same synced campaign (ADR-0007), so their `MBObjectManager`s contain the same campaign
  characters/items. If GUIDs match, the host can start `GameNetwork.StartMultiplayerOnServer(port)`
  *within the campaign `Game`*, open a battle mission with SP battle behaviors **plus** MP network
  behaviors, and clients join with `StartMultiplayerOnClient(...)` without leaving their campaign.
  Pros: SandBoxCore scenes, campaign characters, equipment and crafted items exist on all machines;
  native agent/combat replication; `ReplaceBotWithPlayer` for slot taking and death→retake.
  Cons/unknowns: SP mission behaviors are not network-aware (must use/adapt MP equivalents);
  `MissionLobbyComponent`/game-mode components may assume `MultiplayerGame`; native layer behaviour
  in `MultiClientServer` without the official lobby; GUID determinism.
- **H2 — admission without the TaleWorlds lobby.** Host calls `AddNewPlayerOnServer` for each co-op
  player authenticated over our own channel (LiteNetLib/Steam), sends back `(address, port, sessionKey,
  playerIndex)`, client calls `StartMultiplayerOnClient`. Uses only public API; needs a spike.
- **Dedicated server variant of B is weak** for this project: needs TaleWorlds lobby + AuthToken,
  lacks SandBoxCore scenes and campaign objects (would have to be copied locally and registered in
  matching order), and does not run the campaign anyway (ADR-0007). Keep it only as a later option for
  "dedicated host" mode.
- Option A (custom netcode on SP missions) would have to rebuild agent movement/combat/AI replication
  that the engine already provides in H1 → much higher effort and desync risk.

## Unknowns (spikes for Phase 2/4)
1. **GUID parity**: after loading the same save on two machines, do campaign `CharacterObject`/`ItemObject`
   MBGUIDs match? (Log GUIDs of N objects on both instances.) If not: a GUID remap before battle or
   registering battle-only objects in a fixed order.
2. **Listen server in campaign**: call `GameNetwork.StartMultiplayerOnServer` from a campaign game,
   open a minimal networked mission, connect a second local instance with `StartMultiplayerOnClient`
   (two instances on one PC work, research 0001).
3. Which mission behaviors are required for replication (`MissionNetworkComponent`, lobby component,
   spawn components) and which SP campaign behaviors break under `GameNetwork.IsServer`.
4. Dedicated server: does it accept `_MODULES_*Native*Multiplayer*BannerCoopTW*_MODULES_` and run a
   custom game mode? (Low priority after H1.)
5. NAT/transport: native game traffic is UDP on the server port (9999 in the player-hosted path); does
   it work over Hamachi/WireGuard and can it be tunnelled over Steam? (→ 0007).

## Implementation impact
- ADR-0006 leans to **B via listen server inside the campaign (H1)**, gated by spikes 1–2.
  Option A stays the fallback if GUID parity or in-campaign server fails.
- Allocation core (BattleSession, slots) is unaffected; the battle adapter maps a claimed slot to
  `ReplaceBotWithPlayer(agent, peer)` (troop class → a free AI agent of that formation; lord → that
  hero's agent).
- Result round-trip: server reports per-troop outcomes keyed by `UniqueTroopDescriptor` → host applies
  via `MapEventSide.OnTroop*` (0003). In H1 the host *is* the server, so this is local.

## License / usage constraints
Decompiled TaleWorlds code used for API study only (ADR-0005). No third-party mod code consulted.

## Follow-up tests
Spike 1 (GUID parity) and spike 2 (listen server in campaign) are the gate for ADR-0006.
