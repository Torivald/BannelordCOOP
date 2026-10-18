# ADR-0009: Module ID, project layout and build strategy

Status: Accepted · 2026-10-18 (Phase 1)

## Context
CLAUDE.md planned projects named `BannerlordCoop.*` and asked for a module ID distinct from "Coop".
Assembly names equal to those of the BannerlordCoop mod could collide in one game process
(both loaded by simple name). CI must build without game files. Logs must survive game relaunches
(the game deletes the previous run's `rgl_log_*`, research 0001).

## Decision
1. Module ID, DLL name and root namespace: **`BannerCoopTW`** (`Modules/BannerCoopTW`,
   `BannerCoopTW.dll`, `SubModuleClassType BannerCoopTW.SubModule`). All projects use the
   `BannerCoopTW.*` prefix (replaces `BannerlordCoop.*` in the planned layout).
2. Layout: `src/BannerCoopTW.Core` (`netstandard2.0`, no TaleWorlds refs — shareable with a .NET 6
   dedicated server), `src/BannerCoopTW.Module` (`net472`, entry point, game adapters),
   `tests/BannerCoopTW.Core.Tests` (xUnit, `net8.0`). Further projects (Networking, Battle, UI,
   Compat.Diplomacy) get the same prefix when created.
3. Module project is based on BUTR `Bannerlord.Templates` 1.3.1.110 (`blmodfx`, MIT, © BUTR Team):
   csproj structure, `_Module/SubModule.xml` with placeholders, `Bannerlord.BuildResources` for
   SubModule.xml substitution and copying into `$(GameFolder)/Modules/BannerCoopTW`.
4. Always compile against **`Bannerlord.ReferenceAssemblies.*` = exact client build**
   (`1.4.8.119303`, `GameVersion`/`GameBuild` in `Directory.Build.props`) — locally and on CI.
   Game DLLs are never referenced from the game folder. Bump both numbers on a game update.
5. Harmony: `Lib.Harmony 2.4.2` compile-only; provided at runtime by the `Bannerlord.Harmony`
   module (hard dependency in SubModule.xml, Workshop v2.4.2.248 on the dev PC).
6. Deploy: git-ignored `mb2` junction at repo root → game folder (`tools/dev/setup-junction.ps1`);
   `Directory.Build.props` sets `GameFolder=mb2` only if it exists, so CI skips deploy.
   `tools/dev/deploy.ps1` builds + validates, `tools/dev/run-game.ps1` launches with an explicit
   `_MODULES_` list (Harmony + native + BannerCoopTW; Diplomacy excluded, ADR-0008).
7. Mod files outside the game folder:
   log `Documents\Mount and Blade II Bannerlord\Logs\BannerCoopTW\BannerCoopTW_<stamp>.log`
   (one per session, newest 5 kept), mirrored to the game log via `TaleWorlds.Library.Debug.Print`
   with a `[BannerCoopTW]` prefix; config `Documents\…\Configs\BannerCoopTW\config.ini`
   (`key=value`, created with defaults).
8. CI: GitHub Actions `windows-latest`, .NET 8 SDK: restore → build → test → package module via a
   fake `GameFolder` → artifact `BannerCoopTW-module`. `global.json` pins SDK ≥ 8.0.400
   (`rollForward: latestMajor`, the dev PC also has SDK 10).

## Consequences
- Renaming later is costly (save data, config paths) — the ID is fixed from now on.
- Reference assemblies may lag a new game patch; until BUTR publishes them, stay on the old build.
- CLAUDE.md "Planned source layout" updated to the new names.
