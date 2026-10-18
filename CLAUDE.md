# Bannerlord Co-op — Claude Project Instructions (v2)

Previous version: `docs/archive/CLAUDE.v1.md`. v2 adds verified environment facts,
the reference-mod license rule, the decompilation policy, the two candidate battle
architectures and concrete token discipline.

## Project goal

A Mount & Blade II: Bannerlord co-op mod whose first target is **Battle Participation**
(Total War-style "join a battle"):

1. One player initiates a battle.
2. Every other connected player chooses: **initiator's side**, **opposing side**, or **spectate**.
3. The player then picks a controllable slot on that side (never the initiator's own hero).
4. Slot list is compact and ordered: Lords/Heroes → Officers/Companions → troop classes
   (`Archers 1/2`, `Infantry 4/6`) → Spectator.
5. Every pick is reserved atomically by the authority; a taken lord or an exhausted troop
   class disappears for everyone else. No free slot → spectator (a valid state, not an error).
6. Death / disconnect rules are explicit (see `docs/architecture/BATTLE_PARTICIPATION.md`).

## Verified environment (2026-10-18, see docs/research/0000-initial-review.md)

- Game path: `E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`
- **Game client: v1.4.8** (`bin/Win64_Shipping_Client/Version.xml`, Native SubModule.xml)
- **Modding tools/editor: v1.4.8** (`bin/Win64_Shipping_wEditor/Version.xml`)
- Dedicated server installed by user (path/version to record in research 0001).
- Scope decisions: see ADR-0007 (shared campaign, 4 players, dedicated + Steam invite, PvP, sieges,
  immortal player heroes).
- GitHub: https://github.com/Torivald/BannelordCOOP (history of record).
- TaleWorlds DLLs target **.NET Standard 2.0**; Steam client runs them on .NET Framework (mono/
  netcore runtimes are also shipped — confirm which one the Steam client uses before choosing TFM).
- Installed modules: Native, SandBoxCore, SandBox, StoryMode, CustomBattle, Multiplayer,
  BirthAndDeath, FastMode. No War Sails DLC module seen.
- `modding_resources/` = FMOD sample project + skeleton FBX only (no code samples).
- Project folder `E:\Claude Bannerlord` is **not a Git repo yet**.

## Hard rules

### Third-party code
- `BannerlordCoop-development/` is **source-available, NOT open source** (license change 2026-06-17).
  Its license forbids using it "in whole or in part" to build a competing co-op mod, and its
  AGENTS.md forbids agents analysing it for that purpose. **Do not open, grep, summarise or port
  any file in that folder.** Only its public README/LICENSE have been read.
  It must never be committed to our repo (it is in `.gitignore`).
- Older BannerlordCoop releases that were MIT (before 2026-06-17) may be studied **only** after
  confirming the exact commit/tag was MIT; record commit + license in a research note.
- Closed mods (Bannerlord Together, Warhost/BannerlordCoopSpectator, …) — study only public
  descriptions/behaviour, never their binaries or code.
- Allowed MIT/permissive dependencies: BUTR Module.Template, Harmony (Bannerlord.Harmony),
  ButterLib, UIExtenderEx, Harmony.Extensions, LiteNetLib (MIT) — verify license at add time.

### Decompilation (ADR-0005)
- Decompiling TaleWorlds assemblies for API study is allowed and expected (ILSpy/ilspycmd).
- Decompiled output lives **outside the repo** (`E:\Claude Bannerlord\_decompiled\<version>\`,
  git-ignored). Never commit it, never paste large decompiled blocks into docs — quote
  type/method signatures only, with the game version.
- Prefer, in order: BUTR public API docs / reference assemblies → targeted decompile of a single
  type → reading broad decompiled code. Use `grep` on decompiled sources, do not read whole files.

### Engineering
- Never assume a Bannerlord API exists — verify against the installed version (decompile/ref asm).
- Order: loadable mod → two local instances talk → one entity synced → allocation → battle.
- Authoritative session for allocation/ownership; clients never self-assign.
- Keep networking / allocation / UI / campaign / Bannerlord adapters separate.
- Every architectural assumption → ADR or research note (template in docs/research/).
- No machine paths or secrets in committed code (use a `mb2` junction / local props file).
- Update `CURRENT_STATE.md` after every milestone. Never claim a test passed unless it ran.

## Companion mod: Diplomacy (ADR-0008)
- Required compatibility with DiplomacyTeam/Bannerlord.Diplomacy (CC BY-NC-SA 4.0): read its source
  for compat work, **never copy its code** into this MIT repo; integrate at runtime only
  (`src/BannerlordCoop.Compat.Diplomacy`, soft dependency). Disable it when testing core sync.

## Local model helper (optional)
- `python tools/local_llm.py "<task>" <files>` sends large text to the local Ollama model and prints
  a short answer; `--check` verifies setup. Use it for first-pass triage of big logs or many
  decompiled files so only the summary enters context. Treat output as an unverified draft: confirm
  any identifier/fact by grep before relying on it. Never use it for decisions, ADRs or final code.
  If Ollama is not running, skip it and work normally.

## Open architecture choice (ADR-0006, must be decided after Phase 2 research)

- **A. Custom netcode over single-player missions** (custom transport e.g. LiteNetLib, Harmony
  patches, own agent replication). Max control, very high effort and desync risk.
- **B. Native multiplayer stack for battles** (when a campaign battle starts, recreate it on a
  Bannerlord (dedicated) multiplayer server; players join as MP clients; native agent replication
  and agent possession; results returned to campaign). Engine already replicates agents —
  likely far less work, but must prove: campaign troops/heroes/equipment can be spawned in an MP
  mission, custom game mode, version-matched dedicated server, result round-trip.
- Phase 2 must produce a feasibility verdict for both.

## Where things live

| What | Where |
|---|---|
| Project rules | `CLAUDE.md` (this file) |
| Current state | `CURRENT_STATE.md` (repo root) |
| Architecture | `docs/architecture/` |
| Decisions | `docs/decisions/NNNN-*.md` |
| Research notes | `docs/research/NNNN-*.md` |
| Tests | `docs/testing/TEST_MATRIX.md` |
| Plans | `docs/plans/MVP_ROADMAP.md` |
| Decompiled game | `_decompiled/<version>/` (ignored) |

Planned source layout (create when Phase 1 starts):
`src/BannerlordCoop.Core` (pure C#, unit-testable allocation/reservations),
`src/BannerlordCoop.Networking`, `src/BannerlordCoop.Battle` (Mission adapters),
`src/BannerlordCoop.UI` (Gauntlet), `src/BannerlordCoop.Module` (SubModule entry),
`tests/BannerlordCoop.Core.Tests`.
Rename the mod/module ID to something distinct from "Coop" (avoid clashing with BannerlordCoop).

## Session protocol

Start: read CLAUDE.md → CURRENT_STATE.md → relevant ADR/research → `git status`.
End: summarise changes, unresolved assumptions, tests actually run; update CURRENT_STATE.md.

## Execution environments

- **Cloud/Cowork session (claude.ai app)**: can read/write these folders via the device bridge,
  web research, docs. Cannot build or run Windows tools unless a device shell is available.
- **Local Claude Code on Windows** (recommended for Phase 1+): needed for `dotnet build`,
  MSBuild, ilspycmd, launching the game, reading logs, Git.

## Model routing (local Claude Code)
- Project default model is `opusplan` (`.claude/settings.json`): plan mode runs on Opus, execution on Sonnet.
  For any non-trivial task, start in plan mode (Shift+Tab), approve the plan, execution switches to Sonnet.
- Delegate by subagent (`.claude/agents/`), each pinned to a model:
  `bannerlord-researcher` (Sonnet) API lookups · `log-triage` (Haiku) logs/build output ·
  `implementer` (Sonnet) specified coding · `architect-reviewer` (Opus) ADRs, hard debugging, milestone review.
- At the start of a task, if the current main model is clearly mismatched (e.g. Opus outside plan mode
  for routine edits, or Haiku/Sonnet for an architecture decision), say so in one line and suggest the
  exact command (`/model sonnet`, `/model opusplan`, `/model opus`) before continuing.

## Token / context discipline (Claude Pro)

- Limits are shared between claude.ai and Claude Code and reset in 5-hour windows; weekly caps
  may apply. Check `/usage` (Claude Code) or Settings → Usage (claude.ai) at session start.
- Default model: Sonnet for routine coding/edits; Opus for architecture, hard debugging, reviews.
- One phase per session; `/clear` between unrelated tasks, `/compact` when context > ~60%.
- Never dump directory trees or whole decompiled files into context — list narrowly, grep.
- Durable knowledge goes into repo docs, not chat.

## Definition of done

Implementation exists, checks actually run, docs + CURRENT_STATE updated, limitations
recorded, Git diff reviewed.
