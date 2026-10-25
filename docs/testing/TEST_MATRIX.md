# Test Matrix

## Environment

- [x] exact game version recorded — v1.4.8.119303 (research 0001)
- [x] module loads — BannerCoopTW v0.1.0, 2026-10-18, `run-game.ps1`, main menu reached
- [x] no startup errors — `rgl_log_errors_<pid>.txt` empty, no exceptions in mod log
- [x] logs identified — game: `C:\ProgramData\Mount and Blade II Bannerlord\logs`; mod: `Documents\…\Logs\BannerCoopTW`
- [x] two local instances can launch — vanilla, research 0001 (not yet with the mod)

## Mod shell (Phase 1)

- [x] `dotnet build` Release, 0 errors (warnings only from Bannerlord.BuildResources)
- [x] Core unit tests (10) pass — `dotnet test`
- [x] deploy via `mb2` junction, SubModule.xml placeholders replaced
- [x] module listed in the launcher Mods tab
- [x] own log file written + mirrored to game log with `[BannerCoopTW]`
- [x] config.ini created with defaults
- [ ] "loaded" message visually confirmed in game (DisplayMessage called without error; message had faded before screenshot)
- [x] module loads in two instances at once (25+ two-instance launches 2026-10-24/25)
- [x] CI green on GitHub Actions (run 39134379296)

## Networking

- [x] client connects — fake clients over LiteNetLib UDP on localhost (no game)
- [x] disconnect detected — server notices a killed client and frees the seat (no game)
- [ ] session created
- [ ] one entity synchronized
- [x] claim accepted — integration, fake clients (no game)
- [x] conflicting claim rejected — integration, simultaneous last-slot claim (no game)
- [x] release works — integration, fake clients (no game)

## Battle Participation

- [x] attacker selection — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] defender selection — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] spectator selection — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] lord allocation — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] officer allocation — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] troop-class allocation — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] last-slot conflict — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] reservation timeout — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] controlled death — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] reallocation — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] reinforcement slot — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] disconnect during control — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game
- [x] battle completion — unit level only (Core: BattleSession, tests/BannerCoopTW.Core.Tests); not exercised in a game

## Session resume

- [ ] loaded co-op save stays paused until host starts
- [ ] returning player's previous hero is pre-selected (Steam ID)
- [ ] two players claim the same hero → one rejected
- [ ] absent player's hero runs on AI after start
- [ ] late player reclaims hero without pausing campaign
- [ ] Start disabled until all connected players have a hero
- [ ] late joiner with a free slot creates a new hero (own clan, culture town) — if S3 passes
- [ ] 5th player rejected when all 4 slots are taken

## Phase 3 core (unit level, no game)

- [x] seat/player state machines, atomic reservation (200-round concurrent claim tests), 10–15 s timeout, initiator hero pinned
- [x] AI losses shrink classes; agent death before confirm; ControlReleased on release/spectate/disconnect
- [x] versioned wire protocol (round-trip of every message, truncation, mutation and fuzz never throw, strict enums/bools)
- [ ] not covered: siege-specific defender roles / destroyed engines, host loss, snapshots/deltas with base revision, rate limiting

## Regression

- [ ] vanilla single-player battle still works
- [ ] vanilla campaign still works
- [ ] mod disabled cleanly
- [ ] save/load behavior documented
