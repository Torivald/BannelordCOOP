# Research 0004 — Mission: spawning, controlled agent, knock-out, reinforcements, end

## Question
Which mission/agent APIs does the battle adapter need: spawn, the player-controlled agent and
switching it, death vs knock-out (immortal player heroes), reinforcements, retreat, battle end —
and how network-aware is the single-player mission code?

## Bannerlord version tested
v1.4.8.119303 (client + dedicated server assemblies), decompiled locally (ADR-0005).

## Source
`TaleWorlds.MountAndBlade.dll` (`Mission`, `Agent`, `DefaultBattleMissionAgentSpawnLogic`, `BattleEndLogic`,
`MissionNetworkComponent`), `TaleWorlds.Core.dll` (`AgentState`), `SandBox.dll` (game models).

## Date checked
2026-10-18

## Verified facts

### Controlled agent (single player)
- `Agent.Controller` (`AgentControllerType`) setter: calls native `SetController`; when set to
  `Player` it sets `Mission.MainAgent = this`, re-attaches the agent to its formation, adds
  `AgentFlag.CanRide`, notifies `Formation.OnAgentControllerChanged`. → In SP, switching the
  controlled agent = set old main to `AI`, new agent to `Player`.
- `Mission.MainAgent` setter raises `OnMainAgentChanged` and (when not a client) sets `MainAgentServer`.
- `Agent.IsMine => Controller == Player`; `Agent.IsPlayerControlled => IsMine || MissionPeer != null`.
- Initial player agent: `Mission` spawn sets `Controller(AgentControllerType.Player)` if
  `isPlayerSide && troop == Game.Current.PlayerTroop` (0003).

### Spawning and reinforcements
- `DefaultBattleMissionAgentSpawnLogic(IMissionTroopSupplier[] suppliers, BattleSideEnum playerSide,
  Mission.BattleSizeType)`; API: `InitWithSinglePhase(defTotal, attTotal, defInitial, attInitial, …)`,
  `SetSpawnTroops(side, bool, enforce)`, `SetSpawnHorses`, `StartSpawner/StopSpawner(side)`,
  `SetReinforcementsSpawnEnabled(bool, resetTimers)`, `SetCustomReinforcementSpawnTimer(ICustomReinforcementSpawnTimer)`,
  `GetReinforcementInterval(side)`, `IsSideDepleted(side)`, `GetTotalNumberOfTroopsForSide`,
  `GetNumberOfPlayerControllableTroops()`, `AddPhaseChangeAction(side, delegate)`.
  Companion behaviors: `BattleReinforcementsSpawnController`, `MissionReinforcementsHelper`.
- Max agents on the field: `BannerlordConfig.BattleSize` (user option) → in co-op the host's value
  must be authoritative.
- Troops come from `IMissionTroopSupplier` (campaign: `PartyGroupTroopSupplier`, 0003).

### Death / knock-out
- `AgentState { None, Active, Routed, Unconscious, Killed, Deleted }`.
- Killed vs unconscious is a **game model**: `AgentDecideKilledOrUnconsciousModel.GetAgentStateProbability(
  Agent affectorAgent, Agent effectedAgent, DamageTypes damageType, WeaponFlags weaponFlags,
  out float useSurgeryProbability)`; campaign uses
  `SandBox.GameComponents.SandboxAgentDecideKilledOrUnconsciousModel`. Models are replaceable by a mod
  (`gameStarter.AddModel`) → immortal player heroes = probability 0 of death for player heroes.
- `MissionBehavior.OnAgentRemoved(Agent affected, Agent affector, AgentState state, KillingBlow blow)`
  is the hook for "player's agent went down → back to slot selection / spectator".
- `Agent.Die(Blow, KillInfo)`, `Agent.Retreat(WorldPosition)`.

### Battle end / retreat
- `BattleEndLogic : MissionLogic, IBattleEndLogic`: `MissionEnded(ref MissionResult)`, `TryExit()`
  (player retreat/leave), `ChangeCanCheckForEndCondition(bool)`, `EnableEnemyDefenderPullBack(int)`.
  `AgentVictoryLogic` for celebrations; results flow to the campaign via the troop supplier (0003).

### Network awareness of the "SP" mission code (key for ADR-0006)
- `Mission` and `Agent` are shared by SP and MP and contain server/client branches
  (Mission.cs: 14× `GameNetwork.IsServer`, 17× `WriteMessage`; Agent.cs: 30× / 31×).
- **`Mission.SpawnAgent(...)` broadcasts `CreateAgent` (or `CreateFreeMountAgent`) whenever
  `GameNetwork.IsServerOrRecorder`** — i.e. agents spawned by the campaign's SP spawn logic would be
  replicated automatically if the process runs as a (listen) server.
- `MissionNetworkComponent` sends all existing objects to a peer that finishes loading
  (`SendExistingObjectsToPeer`), so late joiners get the current battle state.
- SP battle behaviors (`DefaultBattleMissionAgentSpawnLogic`, `BattleEndLogic`,
  `BattleReinforcementsSpawnController`) contain no or almost no network code: they run as
  server-side logic only; SandBox has 3 `GameNetwork.` references in total.

## Inferences / hypotheses
- For ADR-0006 H1 (listen server inside the campaign, 0005): the campaign battle mission could keep
  its SP behaviors on the host and add MP network behaviors; replication of agents, combat and
  movement is engine work. What needs custom work: client-side mission setup (clients must open the
  same mission as MP clients), slot→agent assignment (`ReplaceBotWithPlayer`), UI, orders for
  player-controlled formations, end-of-battle sync.
- For option A: switching control in SP is trivial (`Agent.Controller`), but everything else must be
  replicated by custom code.
- Immortal heroes: a replacement `AgentDecideKilledOrUnconsciousModel` + campaign-side rule in
  results (no `DeathMark` for player heroes).

## Unknowns
- Which `MissionBehavior`s break with `GameNetwork.IsServer == true` in a campaign mission
  (e.g. behaviors that assume `Agent.Main` is local, conversation/morale UI, deployment).
- Deployment phase (`DeploymentMissionController`) in MP context.
- Siege engines/`UsableMachine` replication in a campaign siege scene (MP siege mode exists → likely ok).

## Implementation impact
- Battle adapter API surface: troop supplier filter, `ReplaceBotWithPlayer`/`Agent.Controller`,
  `OnAgentRemoved`, spawn logic reinforcement switches, battle size from host.
- Knock-out rule goes into a game model, not a Harmony patch.

## License / usage constraints
Signatures only, from local decompilation (ADR-0005).

## Follow-up tests
1. In spike 2 (0005): spawn the campaign battle with the host as listen server and observe whether a
   connected client sees agents (CreateAgent traffic) without extra code.
2. Unit-level: replace `AgentDecideKilledOrUnconsciousModel` and confirm player hero is knocked out.
