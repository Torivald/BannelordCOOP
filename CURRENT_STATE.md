# Current State — Bannerlord Co-op

Last updated: 2026-10-18 (Phase 0 environment research)

## Phase

0 — Environment discovery — **DONE** (PR pending) → next: Phase 1

## Completed

- Project concept and Battle Participation spec defined.
- Initial review of instructions and environment: `docs/research/0000-initial-review.md`.
- CLAUDE.md v2 (old one in `docs/archive/`).
- Master plan `docs/plans/MASTER_PLAN.md`, checklist `docs/plans/ENVIRONMENT_CHECKLIST.md`.
- ADR-0007 (product scope: shared campaign, 4 players, dedicated + Steam invite, PvP, sieges, immortal heroes).
- ADR-0004 (reference-mod license), ADR-0005 (decompilation policy),
  ADR-0006 (battle architecture options, OPEN).
- Phase 0 environment research: `docs/research/0001-environment.md`.

## Verified

Details and sources: `docs/research/0001-environment.md`.

- Game path: `E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`
- Client **v1.4.8.119303** (Steam build 24573425, branch public); modding tools v1.4.8
- Dedicated Server installed (app 1863440, build 24571419), Native/Multiplayer modules v1.4.8;
  server runs on **.NET 6** (bundles coreclr 6.0.12; system 6.0.36 also installed)
- **Steam client hosts mods on the .NET Framework 4.x CLR** (verified via loaded modules:
  clr.dll + Framework native images; `Mono Loading Step` log lines are misleading) → client
  module targets `net472`; VS managed debugging should be possible
- TaleWorlds assemblies target .NET Standard 2.0
- Modules present: Native, SandBoxCore, SandBox, StoryMode, CustomBattle, Multiplayer,
  BirthAndDeath, FastMode; Workshop: Harmony 2.4.2.248, UIExtenderEx 3.0.3, ButterLib 2.12.0,
  Diplomacy 1.5.4 (keep disabled for tests)
- .NET SDK 8.0.426 only; .NET Framework 4.8.1 + 4.7.2 Developer Pack; **ilspycmd 9.1 works**
- VS Code 1.142.0 (no C# extension), no Visual Studio/Rider; Git 2.55.0; Python 3.12.10;
  no Node.js, no `gh`
- Logs: `C:\ProgramData\Mount and Blade II Bannerlord\logs` (`rgl_log_<pid>.txt`,
  `rgl_log_errors_<pid>.txt`, `watchdog_log_<pid>.txt`)
- **Two game instances on one PC work** (both reached main menu, no errors, closed cleanly)
- Project folder is a Git repo (origin `Torivald/BannelordCOOP`); push works

## Blockers (user action)

~~1. Update client / toolchain / dedicated server~~ done.
~~2. ilspycmd~~ resolved: ilspycmd 9.1.0.7988 runs on .NET 8 (.NET 10 runtime only needed for
   the optional ILSpy 11 GUI in `ilspy/`).
~~3. GitHub access~~ done.
4. Add `C:\Users\Torivald\.local\bin` to PATH (if not done yet).
~~5. ADR-0007 open questions~~ answered.
6. Optional: install `gh` (lets Claude open PRs), C# extension for VS Code or Visual Studio 2022.

## Not yet verified

- Dedicated Server starts locally and accepts a 1.4.8 client (needs server token)
- Custom module loadable on the Dedicated Server
- VS debugger attach to `Bannerlord.exe` (.NET Framework 4.x) with a mod loaded
- Two instances inside a mission simultaneously (performance)
- Mission/Agent APIs for control, death, reinforcements

## Next actions

1. Merge Phase 0 PR.
2. Phase 1: minimal loadable module (BUTR template, `net472`) on 1.4.8; verify it loads in two
   instances at once.
3. Phase 2: decompile 1.4.8 (ilspycmd 9.1), produce `0002-battle-api.md` with a verdict on
   ADR-0006 A vs B; include the Dedicated Server smoke test.

## Do not do yet

Campaign sync, save sync, matchmaking, NAT traversal, production servers, optimisation, UI polish.
