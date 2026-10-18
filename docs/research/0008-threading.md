# Research 0008 — Thread model: where network input may touch game state

## Question
On which thread does game logic run, in what order are hooks ticked each frame, and where can a mod
safely apply messages received on a background network thread?

## Bannerlord version tested
v1.4.8.119303, local decompilation (ADR-0005).

## Source
`TaleWorlds.MountAndBlade.dll` (`Module.OnApplicationTick`, `MBSubModuleBase`), `TaleWorlds.Library.dll`
(`SingleThreadedSynchronizationContext[Manager]`, `TWParallel`), `TaleWorlds.CampaignSystem.dll` (0002).

## Date checked
2026-10-18

## Verified facts
- `Module.OnApplicationTick(float dt)` (engine callback, main thread) per frame, in order:
  1. installs once `SynchronizationContext.SetSynchronizationContext(new SingleThreadedSynchronizationContext())`;
  2. `OnNetworkTick(dt)` → every `MBSubModuleBase.OnNetworkTick(dt)` (unless `GameNetwork.MultiplayerDisabled`);
  3. global/`GameStateManager` tick → active state (`MapState` → campaign tick, `MissionState` → mission tick);
  4. `Utilities.RunJobs()`, `PlatformServices.Instance?.Tick(dt)`;
  5. **`_synchronizationContext.Tick()`** — runs posted continuations on the main thread;
  6. `GameManagerBase.Current.OnTick(dt)`;
  7. every `MBSubModuleBase.OnApplicationTick(dt)`; then `JobManager.OnTick`, avatar services.
- Other hooks: `MBSubModuleBase.AfterAsyncTickTick(dt)`, `OnGameStart(Game, IGameStarter)`,
  campaign behaviors (`CampaignEvents.TickEvent`, hourly/daily), `MissionBehavior.OnMissionTick(dt)`.
- `async`/`await` in game code resumes on the main thread via that context (used by the lobby code,
  e.g. `await Task.Delay(1)` loops in `LobbyGameStatePlayerBasedCustomServer`).
- Campaign uses `TWParallel` worker loops inside its tick for cached party data (0002) — game objects are
  touched off-thread only inside those engine-controlled loops.

## Inferences / hypotheses
- Rule for our code: **network I/O on background threads, game-state mutation only on the main thread.**
  Receive → enqueue into a lock-free queue → drain in `OnNetworkTick` (before the game tick, so this
  frame's simulation sees the input) or in `OnApplicationTick` (after). Alternatively
  `SynchronizationContext.Post` from a captured main-thread context.
- Host campaign: apply client commands in `OnNetworkTick`, run the campaign tick, then build and send
  state deltas in `OnApplicationTick` (end of frame).
- Battle (0005 H1): native replication runs inside the engine; our slot/reservation messages follow the
  same queue rule; mission-specific actions (`ReplaceBotWithPlayer`) inside a `MissionBehavior.OnMissionTick`.
- Never call TaleWorlds APIs from LiteNetLib/Steam callback threads (Steam callbacks fire inside
  `SteamAPI.RunCallbacks` — whichever thread pumps it; pump on the main thread).

## Unknowns
- Whether `OnNetworkTick` is called while `GameNetwork` is inactive in single-player (it is gated only by
  `MultiplayerDisabled`, which is `false` → expected yes; verify by log in Phase 4).

## Implementation impact
- `BannerCoopTW.Networking`: transport threads + `ConcurrentQueue<Message>`; a `MainThreadPump` drained from
  `SubModule.OnNetworkTick`; Core logic (BattleSession) stays pure and single-threaded.

## License / usage constraints
Signatures/facts only (ADR-0005).

## Follow-up tests
1. Log thread ids and call order of `OnNetworkTick` / `OnApplicationTick` / campaign tick in Phase 4.
