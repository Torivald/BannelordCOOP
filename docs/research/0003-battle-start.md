# Research 0003 — How a campaign battle starts, runs and returns results

## Question
What is the call path from a map encounter to a battle mission and back; where are sides,
participants and troops defined; which hooks let a co-op host (a) intercept battle start,
(b) supply/allocate troops to players, (c) feed casualties back into the campaign?

## Bannerlord version tested
v1.4.8.119303 (client). Decompiled with ilspycmd 9.1 into `_decompiled/1.4.8/` (not in Git).

## Source
`TaleWorlds.CampaignSystem.dll`, `SandBox.dll`, `TaleWorlds.MountAndBlade.dll`, `TaleWorlds.Core.dll`
(signatures quoted, no code copied). Paths below are namespaces/types.

## Date checked
2026-10-18

## Verified facts

### 1. Encounter → MapEvent
- `Actions.StartBattleAction`: `Apply(PartyBase attacker, PartyBase defender)`,
  `ApplyStartBattle(MobileParty, MobileParty)`, `ApplyStartRaid`, `ApplyStartSallyOut`,
  `ApplyStartAssaultAgainstWalls`. Internally: if the defender has no `MapEvent`,
  `Campaign.Current.Models.EncounterModel.CreateMapEventComponentForEncounter(attacker, defender, battleType)`
  creates it; otherwise the attacker joins the existing event via
  `attackerParty.MapEventSide = defenderParty.MapEvent.GetMapEventSide(side)`. Then
  `CampaignEventDispatcher.Instance.OnStartBattle(attacker, defender, subject, showNotification)`.
- Player path: `Encounters.PlayerEncounter` (singleton `Campaign.Current.PlayerEncounter`,
  `PlayerEncounter.Start()` creates it). `public static MapEvent StartBattle()` picks the concrete
  event by situation (raid, sally-out, siege via `MapEventManager.StartSiegeMapEvent`, hideout,
  siege ambush, blockade, field battle). `public static void JoinBattle(BattleSideEnum side)` joins
  an existing NPC battle on a chosen side (sets `PartyBase.MainParty.MapEventSide`) — the vanilla
  "help side X" mechanic.
- `MapEvents.MapEvent.BattleTypes`: `None, FieldBattle, Raid, IsForcingVolunteers,
  IsForcingSupplies, Siege, Hideout, SallyOut, SiegeOutside, BlockadeBattle, BlockadeSallyOutBattle`.
  `IsNavalMapEvent => !Position.IsOnLand`. Naval (War Sails) code paths exist in 1.4.8
  CampaignSystem even without the DLC installed (`OpenNavalBattleMission`, `List<Ship>` in
  `PlayerEncounter`).
- Sides: `MapEvent.DefenderSide => _sides[0]`, `AttackerSide => _sides[1]`
  (`BattleSideEnum.Defender = 0`, `Attacker = 1`), `GetMapEventSide(BattleSideEnum)`,
  `InvolvedParties`, `MapEventSettlement`, `EventType`, `IsFieldBattle`, `IsSiegeAssault`.
  `MapEventSide.Parties` → `MapEventParty` (wraps `PartyBase`, rosters).
- Events: `CampaignEvents.MapEventStarted` (`IMbEvent<MapEvent, PartyBase, PartyBase>`),
  `MapEventEnded`, `OnPlayerBattleEndEvent`, `BeforeMissionOpenedEvent`, `OnMissionStartedEvent`,
  `AfterMissionStarted`, `OnMissionEndedEvent`, `OnPartyAddedToMapEventEvent`,
  `NearbyPartyAddedToPlayerMapEvent`.

### 2. Player choice → mission
- Encounter menu option "Attack" → `Helpers.MenuHelper.EncounterAttackConsequence(MenuCallbackArgs)`
  → per situation: siege (`PlayerSiege.StartSiegeMission`, lord's hall troop selection), village
  (`PlayerEncounter.StartVillageBattleMission`), hideout, naval, caravan, or field battle:
  scene from `Campaign.Current.Models.SceneModel.GetBattleSceneForMapPatch(mapPatch, isNaval)`,
  `new MissionInitializerRecord(scene)` (+ terrain, time of day, map patch), then
  `CampaignMission.OpenBattleMission(MissionInitializerRecord rec)`; finally
  `PlayerEncounter.StartAttackMission()` (creates `CampaignBattleResult`).
- `CampaignMission.Open*Mission(...)` (20+ variants: Battle, Siege with/without deployment,
  Hideout, Naval, Caravan, Alley, Arena, Conversation…) forward to
  `Campaign.Current.CampaignMissionManager` (interface `ICampaignMissionManager`), implemented in
  SandBox (`SandBox.CampaignMissionManager` → `SandBox.SandBoxMissions`).
- `SandBoxMissions.OpenBattleMission(MissionInitializerRecord)`: `MissionState.OpenNew("Battle", rec, behaviors)`
  with ~29 `MissionBehavior`s, among them `DefaultBattleMissionAgentSpawnLogic`,
  `BattleSpawnLogic("battle_set")`, `SandBoxBattleMissionSpawnHandler`, `CampaignMissionComponent`,
  `BattleAgentLogic`, `BattleEndLogic`, `BattleReinforcementsSpawnController`,
  `MissionCombatantsLogic(MapEvent.InvolvedParties, PartyBase.MainParty, …, isPlayerSergeant)`,
  `AgentHumanAILogic`, `AgentVictoryLogic`.
- Troops come from **`TroopSuppliers.PartyGroupTroopSupplier : IMissionTroopSupplier`**, one per side:
  `new PartyGroupTroopSupplier(MapEvent.PlayerMapEvent, side, priorTroops, customAllocationConditions)`.
  `IMissionTroopSupplier` is an engine interface (TaleWorlds.MountAndBlade):
  `SupplyTroops(int)`, `SupplyOneTroop()`, `GetAllTroops()`, `GetGeneralCharacter()`,
  `GetNumberOfPlayerControllableTroops()`, `OnTroopWounded/Killed/Routed(UniqueTroopDescriptor…)`.
  The optional `Func<UniqueTroopDescriptor, MapEventParty, bool>` filter controls which troops spawn.
- Player agent: `Mission` spawn code sets `AgentControllerType.Player` when
  `isPlayerSide && troop == Game.Current.PlayerTroop`. `Mission.MainAgent` (setter raises
  `OnMainAgentChanged`; on non-clients also sets `MainAgentServer`). `Agent.IsMine =>
  Controller == AgentControllerType.Player`; **`Agent.IsPlayerControlled => IsMine || MissionPeer != null`**
  — the same `Agent` class carries native multiplayer ownership (`MissionPeer`), see 0005.

### 3. Campaign is frozen during the mission
- Campaign time advances only in `GameState.MapState.OnTick → OnMapModeTick → Campaign.Current.RealTick(dt)`.
- `GameStateManager.OnTick` ticks only `ActiveState`; `MissionState` is pushed over `MapState`,
  so the campaign does not tick while a mission runs (matches ADR-0007 "campaign pauses for everyone").

### 4. Results back to the campaign
- During the mission, casualties are written immediately through the supplier:
  `PartyGroupTroopSupplier.OnTroopKilled(d)` → `MapEventSide.OnTroopKilled(UniqueTroopDescriptor)`
  (same for `OnTroopWounded`, `OnTroopRouted(descriptor, isOrderRetreat)`). All public on `MapEventSide`,
  together with `MakeReadyForMission(FlattenedTroopRoster priorTroops)`,
  `AllocateTroops(ref List<UniqueTroopDescriptor>, int, Func<…>)`, `GetAllocatedTroop(descriptor)`.
- After the mission `PlayerEncounter.Update()` runs a state machine
  `PlayerEncounterState { Begin, Wait, PrepareResults, ApplyResults, PlayerVictory, PlayerTotalDefeat,
  CaptureHeroes, FreeHeroes, LootParty, LootInventory, LootShips, End }`; `DoApplyMapEventResults`
  calls `MapEvent.CalculateAndCommitMapEventResults()` (internal) → XP, loot, prisoners, renown;
  `MapEvent.FinalizeEvent()` (public) closes the event.
- Auto-resolve: `MapEvent.SimulateBattleSetup(FlattenedTroopRoster[])`, `PlayerEncounter.InitSimulation(...)`.

## Inferences / hypotheses
- **Battle-start interception point** for co-op: `CampaignEvents.MapEventStarted` (observe) and a
  Harmony prefix on `MenuHelper.EncounterAttackConsequence` / `CampaignMission.OpenBattleMission`
  (or replacing `Campaign.Current.CampaignMissionManager`, which is an interface → cleaner than
  patching). The host can broadcast "battle pending", run the slot selection (BattleSession), then
  open the mission.
- **Slot catalog source**: `MapEvent.AttackerSide/DefenderSide.Parties` (heroes = party leaders and
  hero troops in rosters; troop classes from `CharacterObject.DefaultFormationClass`/roster counts).
  Initiator's side = `PartyBase.MainParty.Side` of the initiating player.
- **Option A** (SP mission + custom netcode): a custom `IMissionTroopSupplier` /
  `customAllocationConditions` can mark player-claimed troops; controller switching is via
  `Agent.Controller`/`Mission.MainAgent` (0004).
- **Option B** (MP mission): the campaign host keeps the `MapEvent` open (mission state) and applies
  per-troop casualties reported by the MP server through `MapEventSide.OnTroop*` using the same
  `UniqueTroopDescriptor`s, then lets `PlayerEncounter.Update` finish normally. This avoids
  reimplementing XP/loot/prisoners. Needs a descriptor ↔ MP-agent mapping.
- The whole SP stack assumes **one local player**: `MobileParty.MainParty`, `PartyBase.MainParty`,
  `Hero.MainHero`, `Game.Current.PlayerTroop`, `MapEvent.PlayerMapEvent`, singleton `PlayerEncounter`.
  Other players' heroes must be represented as non-main parties on the host (campaign-sync ADR).

## Unknowns
- Siege specifics: `PlayerSiege.StartSiegeMission` / `OpenSiegeMissionWithDeployment` parameters
  (wall HP, siege weapons) — needed for siege slots; not read yet.
- Whether `PlayerEncounter` can be driven for a battle started by a *non-main* party (another
  player's hero) — likely requires making that party the main party on the host or a custom flow.
- Hero knock-out vs death rules in the battle result (immortal player heroes, ADR-0007) — check
  `KillCharacterAction`/`DeathMark` handling in results.

## Implementation impact
- Phase 6 (battle start sync) hooks `MapEventStarted` + `CampaignMissionManager`.
- Core `SlotCatalog` input format = sides → parties → heroes + troop counts per class (DATA_MODEL).
- Result round-trip in option B is feasible via public `MapEventSide.OnTroop*` APIs.

## License / usage constraints
TaleWorlds code studied via local decompilation for API understanding (ADR-0005); only
signatures/short facts recorded.

## Follow-up tests
1. Harmony probe: log `MapEventStarted` + `OpenBattleMission` args in a field battle (Phase 2 spike).
2. Spike: custom `customAllocationConditions` to exclude a claimed troop from AI spawn.
3. Read siege mission parameters for 0004/siege slots.
