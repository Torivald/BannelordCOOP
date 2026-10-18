# Research 0002 — Campaign time: tick, speed, pause, 1x lock, determinism

## Question
How does campaign time advance; how can a co-op host lock speed to 1x, pause/resume for everyone;
is the campaign deterministic enough for lockstep, or must state be replicated?

## Bannerlord version tested
v1.4.8.119303, local decompilation (ADR-0005).

## Source
`TaleWorlds.CampaignSystem.dll` (`Campaign`, `MapState`, `CampaignTickCacheDataStore`), `TaleWorlds.Core.dll`
(`MBRandom`, `MBFastRandom`, `Game`, `GameStateManager`).

## Date checked
2026-10-18

## Verified facts
- Tick chain: `GameStateManager.OnTick` → active `MapState.OnTick` → `OnMapModeTick` →
  `Campaign.RealTick(float realDt)` → `TickMapTime(realDt)`, entity components, `SiegeEventManager.Tick`,
  `_tickData.RealTick`; plus `Campaign.Tick()` (events, `CampaignEventDispatcher.Tick(_dt)`) and
  `Campaign.LateAITick()` (`PartiesThink(_dt)`). Only the active state ticks → no campaign time during
  missions or other pushed states (0003).
- `TickMapTime`: `dt = 0.25 * realDt` for play, `× SpeedUpMultiplier` (default `4f`) for fast-forward,
  `0` for `Stop`/`FastForwardStop`; "Stoppable*" modes stop while the main party is waiting
  (`IsMainPartyWaiting = MobileParty.MainParty.ComputeIsWaiting()`). Then
  `MapTimeTracker.Tick(4320f * dt)`. **Campaign dt is derived from real frame time.**
- `CampaignTimeControlMode { Stop, UnstoppablePlay, UnstoppableFastForward, StoppablePlay,
  StoppableFastForward, UnstoppableFastForwardForPartyWaitTime, FastForwardStop }`;
  `Campaign.TimeControlMode` (get/set), `Campaign.SetTimeSpeed(int 0|1|2)`,
  `Campaign.TimeControlModeLock` + `SetTimeControlModeLock(bool)`, `Campaign.SpeedUpMultiplier` (settable),
  `Campaign.CampaignDt`, `CampaignTime.Now`.
- Randomness: `MBRandom` uses `Game.Current.RandomGenerator` (`MBFastRandom`, seeded once at game
  creation; default ctor seeds from `Environment.TickCount`); separate `NondeterministicRandom`.
- Parallel work in the tick: `CampaignTickCacheDataStore` uses `TWParallel` predicates
  (party variable caching, moving armies), also `CampaignPeriodicEventManager`.

## Inferences / hypotheses
- **1x lock**: host sets `SpeedUpMultiplier = 1` (or forces `TimeControlMode` to a Play mode) and
  `SetTimeControlModeLock(true)`; Harmony guard on `SetTimeSpeed` for clients. Cheap.
- **Global pause**: host sets `TimeControlMode = Stop` and broadcasts; clients' own menus must not
  advance or stop host time (client time is not authoritative at all — see below).
- **No lockstep**: dt from real frame time, global RNG shared by everything, parallel tick code,
  plus native pathfinding → bit-exact lockstep across machines is unrealistic.
  → Campaign sync must be **host-authoritative state replication**: only the host simulates; clients
  run the campaign with simulation suppressed (dt = 0 / patched tick) and apply host snapshots/deltas
  (party positions, events, ownership, economy). This drives ADR-0010.
- Each client needs its own controllable party: client input (move orders, menu actions) → command to
  host → host applies to that player's party → replicated back.

## Unknowns
- How much of the client UI keeps working with the tick suppressed (map rendering, party movement
  interpolation, menus that call `Campaign` APIs directly).
- Snapshot size/frequency for ~1000+ parties; which state can be derived locally (e.g. visuals).

## Implementation impact
- ADR-0010 (campaign sync model): host-authoritative, command-based input, state replication.
- Phase 5b (time): 1x lock + global pause are low-risk; the tick suppression on clients is the work.

## License / usage constraints
Signatures/facts only from local decompilation (ADR-0005).

## Follow-up tests
1. Harmony probe: force `SpeedUpMultiplier = 1`, lock, verify no fast-forward possible.
2. Spike: patch `Campaign.RealTick` on instance 2 to dt = 0 and move a party by host data.
