# Current State — Bannerlord Co-op

Last updated: 2026-10-18 (Phase 1 mod shell)

## Phase

1 — Mod shell — **DONE** (PR #2, CI green) → next: Phase 2

## Completed

- Project concept and Battle Participation spec defined.
- Initial review: `docs/research/0000-initial-review.md`; CLAUDE.md v2 (old one in `docs/archive/`).
- Master plan `docs/plans/MASTER_PLAN.md`, checklist `docs/plans/ENVIRONMENT_CHECKLIST.md`.
- ADR-0004 (reference-mod license), ADR-0005 (decompilation), ADR-0006 (battle architecture, OPEN),
  ADR-0007 (product scope), ADR-0008 (Diplomacy integration).
- Phase 0 environment research: `docs/research/0001-environment.md` (PR #1, merged).
- Project skills in `.claude/skills/` (mvp-check, bannerlord-research, coop-architecture,
  local-delegate); local model helper `tools/local_llm.py` (Ollama qwen3.5:9b).
- **Phase 1**: mod shell `BannerCoopTW` (ADR-0009):
  `src/BannerCoopTW.Core` (netstandard2.0: FileLog, ModConfig, ModInfo),
  `src/BannerCoopTW.Module` (net472, BUTR template, SubModule entry),
  `tests/BannerCoopTW.Core.Tests` (10 xUnit tests), `tools/dev/*.ps1` (junction, deploy, run),
  `.github/workflows/build.yml` (build + test + module artifact against BUTR reference assemblies).

## Phase 1 — mvp-check (2026-10-18)

| Check | Result | Evidence |
|---|---|---|
| Build `dotnet build -c Release` | PASS | 0 errors, 0 warnings (one CS0168 only from BuildResources' inline task on first build) |
| Unit tests `dotnet test` | PASS | 10/10 |
| Deploy via `mb2` junction | PASS | `mb2\Modules\BannerCoopTW`, placeholders replaced, `deploy.ps1` validation |
| Visible in launcher | PASS | Launcher → Mods: `BannerCoopTW v0.1.0` (screenshot, not committed) |
| Loads without errors | PASS | `rgl_log_1756.txt`: `Loading assembly: …BannerCoopTW.dll`, `[BannerCoopTW] Info: …`; `rgl_log_errors_1756.txt` empty |
| Writes own log | PASS | `Documents\…\Logs\BannerCoopTW\BannerCoopTW_20261018_190012.log` (load, main menu, unload) |
| Config created | PASS | `Documents\…\Configs\BannerCoopTW\config.ini` |
| Version shown in game | NOT CONFIRMED | `DisplayMessage` ran without error; message had faded before the screenshot |
| CI green | PASS | run 39134379296: build + test + artifact `BannerCoopTW-module` (1m18s); note: actions v4 run on forced Node 24 (deprecation warning) |
| Two instances with the mod | NOT RUN | vanilla only (research 0001) |

## Verified

Details: `docs/research/0001-environment.md`, ADR-0009.

- Game path: `E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord` (junction `mb2` in repo)
- Client **v1.4.8.119303**; modding tools v1.4.8; Dedicated Server v1.4.8 (.NET 6, bundles coreclr 6.0.12)
- Steam client hosts mods on the **.NET Framework 4.x CLR** → `net472`
- BUTR `Bannerlord.ReferenceAssemblies.*` **1.4.8.119303** on NuGet = exact client build
- Direct launch `Bannerlord.exe /singleplayer _MODULES_*…*BannerCoopTW*_MODULES_` resolves
  Workshop modules (Harmony loaded from Workshop)
- **Workshop now has duplicate module IDs**: Harmony (2859188632 v2.4.2.248 and 3747619959
  v2.4.99.0), UIExtenderEx (v3.0.3 / v2.14.99.0), ButterLib (v2.12.0 / v2.10.99.0), plus MCM
  v5.11.99.0 (3747620191). The game picked Harmony **v2.4.99.0** (3747619959) in the test run.
- Tooling: .NET SDK 8.0.426 + **10.0.402**; Visual Studio Community 2026 (18.11.0); `gh` 2.102.0
  (logged in, not on the bash PATH); ilspycmd 9.1; Git 2.55.0; Python 3.12.10; Ollama 0.40.5 + qwen3.5:9b
- Logs: game `C:\ProgramData\Mount and Blade II Bannerlord\logs` (old logs pruned at startup,
  rule unclear — copy before relaunch); mod `Documents\Mount and Blade II Bannerlord\Logs\BannerCoopTW`
- Local model trial (qwen3.5:9b, `local_llm.py` on a 258 KB rgl_log): 10 calls, 9 min; found the
  right issues and main-menu state, but counts were wrong (FMOD 46 vs 67 actual) and it quoted its
  own partial summaries as log text → use only for first-pass triage, always grep-verify

## Blockers (user action)

1. Decide which duplicate Workshop modules to keep (Harmony/UIExtenderEx/ButterLib: original
   BUTR items 2859… vs 3747… "x.99" items). Two copies with one ID make load order unpredictable.
2. Optional: add `C:\Program Files\GitHub CLI` to PATH for shells.

## Not yet verified

- "loaded" message visible in game; module in two instances at once
- VS 2026 debugger attach to `Bannerlord.exe` (.NET Framework 4.x) with BannerCoopTW loaded
- Dedicated Server starts locally and accepts a 1.4.8 client (needs server token); custom module on server
- Mission/Agent APIs for control, death, reinforcements

## Next actions

1. Review and merge PR #2.
2. Phase 2: decompile 1.4.8 (ilspycmd), research notes 0002–0009, verdict on ADR-0006 A vs B
   (incl. Dedicated Server smoke test).
3. Phase 3 (can run in parallel): Core allocation/reservation logic + tests.

## Do not do yet

Campaign sync, save sync, matchmaking, NAT traversal, production servers, optimisation, UI polish.
