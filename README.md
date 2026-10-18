# Bannerlord Co-op

Experimental co-op mod for Mount & Blade II: Bannerlord.

## Primary MVP

The first gameplay target is Battle Participation:

- one player starts a battle
- other players choose attacker/defender/spectator
- players claim available lords, heroes, officers, or troop classes
- claims are reserved and validated centrally
- players with no available control slot become spectators

## Development strategy

The project is developed in small vertical milestones:

1. Environment discovery
2. Minimal loadable mod
3. Battle/Mission API research
4. Local two-instance/network harness
5. One synchronized test entity
6. Battle participation allocation
7. Basic battle control
8. Death/reallocation/spectator behavior
9. Reinforcements and battle completion
10. Campaign integration
11. Persistence/reconnect
12. Hardening and performance

## Building

Module ID: `BannerCoopTW` (ADR-0009). Requires .NET SDK 8+ and Bannerlord 1.4.8 (+ `Bannerlord.Harmony`).

```powershell
dotnet build BannerCoopTW.sln -c Release     # compiles against BUTR reference assemblies, no game needed
dotnet test  BannerCoopTW.sln -c Release

# local deploy into the game (once): junction mb2 -> game folder (git-ignored)
.\tools\dev\setup-junction.ps1 -GamePath "<path>\Mount & Blade II Bannerlord"
.\tools\dev\deploy.ps1                       # build + copy to Modules\BannerCoopTW
.\tools\dev\run-game.ps1                     # start with Harmony + native modules + BannerCoopTW
```

Mod log: `Documents\Mount and Blade II Bannerlord\Logs\BannerCoopTW\`,
config: `Documents\Mount and Blade II Bannerlord\Configs\BannerCoopTW\config.ini`.
CI (`.github/workflows/build.yml`) builds, tests and uploads the packaged module as an artifact.

## Documentation

See:
- `CLAUDE.md`
- `CURRENT_STATE.md`
- `docs/architecture/`
- `docs/decisions/`
- `docs/research/`
- `docs/testing/`
- `docs/plans/`

## Principle

Do not confuse "possible in theory" with "supported by the installed Bannerlord API." Verify first.
