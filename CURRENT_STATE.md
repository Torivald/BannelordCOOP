# Current State — Bannerlord Co-op

Last updated: 2026-10-18 (master plan + environment checklist)

## Phase

0 — Environment discovery — **IN PROGRESS**

## Completed

- Project concept and Battle Participation spec defined.
- Initial review of instructions and environment: `docs/research/0000-initial-review.md`.
- CLAUDE.md v2 (old one in `docs/archive/`).
- Master plan `docs/plans/MASTER_PLAN.md`, checklist `docs/plans/ENVIRONMENT_CHECKLIST.md`.
- ADR-0007 (product scope: shared campaign, 4 players, dedicated + Steam invite, PvP, sieges, immortal heroes).
- ADR-0004 (reference-mod license), ADR-0005 (decompilation policy),
  ADR-0006 (battle architecture options, OPEN).

## Verified

- Game path: `E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`
- Game client version: **v1.4.8** (updated by user, matches modding tools v1.4.8)
- TaleWorlds assemblies target .NET Standard 2.0
- Modules present: Native, SandBoxCore, SandBox, StoryMode, CustomBattle, Multiplayer,
  BirthAndDeath, FastMode
- Dedicated Server installed; Git, .NET Framework 4.7.2 dev pack, VS Code, Python present
- .NET SDK: only 8.0.426 (ilspycmd needs .NET 10 SDK); no Visual Studio/Rider, Node.js, gh CLI
- Game logs folder exists: C:\ProgramData\Mount and Blade II Bannerlord\logs
- GitHub push from Claude works (repo Torivald/BannelordCOOP)
- Local project folder is not a Git repository yet

## Blockers (user action)

1. ~~Update client to 1.4.8~~ done. ~~Toolchain + dedicated server~~ installed (versions to record).
2. ilspycmd install fails on current SDK (tool targets newer .NET) → install .NET 10 SDK or pin an older ilspycmd.
~~3. GitHub access~~ done.
4. Add `C:\Users\Torivald\.local\bin` to PATH and run local Claude Code from `E:\Claude Bannerlord`.
~~5. ADR-0007 open questions~~ answered: campaign pauses during battle, all players join; one battle at a time.

## Not yet verified

- Runtime the Steam client uses for mods (.NET Framework 4.7.2 vs bundled .NET runtime)
- .NET SDK / IDE / Git versions on the machine
- Log locations (expected `C:\ProgramData\Mount and Blade II Bannerlord\logs`, verify)
- Two local game instances
- Dedicated server availability for 1.4.8
- Mission/Agent APIs for control, death, reinforcements

## Next actions

1. User: blockers above.
2. `git init`, add `.gitignore`, first commit of docs.
3. Phase 0 finish: `docs/research/0001-environment.md`.
4. Phase 1: minimal loadable module (BUTR template) on 1.4.8.
5. Phase 2: decompile 1.4.8 and produce `0002-battle-api.md` with a verdict on ADR-0006 A vs B.

## Do not do yet

Campaign sync, save sync, matchmaking, NAT traversal, production servers, optimisation, UI polish.
