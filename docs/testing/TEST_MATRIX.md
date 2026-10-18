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
- [ ] module loads in two instances at once
- [x] CI green on GitHub Actions (run 39134379296)

## Networking

- [ ] client connects
- [ ] disconnect detected
- [ ] session created
- [ ] one entity synchronized
- [ ] claim accepted
- [ ] conflicting claim rejected
- [ ] release works

## Battle Participation

- [ ] attacker selection
- [ ] defender selection
- [ ] spectator selection
- [ ] lord allocation
- [ ] officer allocation
- [ ] troop-class allocation
- [ ] last-slot conflict
- [ ] reservation timeout
- [ ] controlled death
- [ ] reallocation
- [ ] reinforcement slot
- [ ] disconnect during control
- [ ] battle completion

## Regression

- [ ] vanilla single-player battle still works
- [ ] vanilla campaign still works
- [ ] mod disabled cleanly
- [ ] save/load behavior documented
