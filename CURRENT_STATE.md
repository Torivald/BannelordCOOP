# Current State — Bannerlord Co-op

Last updated: 2026-10-25 (spikes S1/S2 run; Phase 3 core and Phase 4 transport in separate PRs)

## Phase

2b — spikes S1/S2 **run** (PR `phase2b/spikes`, research 0010): **S1 partial, S2 blocked at the client connect** → ADR-0006 "provisional — at risk".
Parallel branches: `phase3/core-allocation` (slot/reservation core + wire protocol, unit-tested), `phase4/transport` (ITransport + LiteNetLib 1.3.5, fake-client tests).
→ next: S2 follow-up experiments (research 0010 §Follow-up), GUID translation vs canonical reload decision

(Phase 1 — mod shell — DONE, PR #2)

## Completed

- Project concept and Battle Participation spec defined.
- Initial review: `docs/research/0000-initial-review.md`; CLAUDE.md v2 (old one in `docs/archive/`).
- Master plan `docs/plans/MASTER_PLAN.md`, checklist `docs/plans/ENVIRONMENT_CHECKLIST.md`.
- ADR-0004 (reference-mod license), ADR-0005 (decompilation), ADR-0006 (battle architecture —
  **B-listen, provisional**), ADR-0007 (product scope), ADR-0008 (Diplomacy integration),
  ADR-0009 (module ID/layout), **ADR-0010** (campaign sync), **ADR-0011** (hosting), **ADR-0012** (transport).
- Player guide `docs/GAME_GUIDE.md` (RU) + `docs/architecture/SESSION_RESUME.md` (resume from save: paused until
  heroes reclaimed, absent players → bot), PR #4.
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

## Phase 2 — results (2026-10-18)

Decompiled 1.4.8 client + dedicated-server assemblies to `_decompiled/1.4.8/` (git-ignored, 18 assemblies).
Notes: 0002 time · 0003 battle start · 0004 mission/agents · 0005 native MP · 0006 saves · 0007 Steam ·
0008 threading · 0009 Diplomacy.

| Operation | Verdict | Where |
|---|---|---|
| Lock campaign at 1x / global pause | API (`SpeedUpMultiplier`, `SetTimeControlModeLock`, `TimeControlMode`) | 0002 |
| Campaign lockstep | Impossible (real-frame dt, global RNG, parallel tick) → host-authoritative | 0002, ADR-0010 |
| Suppress client campaign tick | Patch (`Campaign.RealTick`/`TickMapTime`) | 0002 |
| Intercept battle start | API (`CampaignEvents.MapEventStarted`, replaceable `CampaignMissionManager`) + patch (`EncounterAttackConsequence`) | 0003 |
| Sides/parties/heroes for slots | API (`MapEvent.AttackerSide/DefenderSide.Parties`) | 0003 |
| Filter which troops AI spawns | API (`PartyGroupTroopSupplier` custom allocation filter) | 0003 |
| Switch controlled agent (SP) | API (`Agent.Controller`, `Mission.MainAgent`) | 0004 |
| Knock-out instead of death | API (replace `AgentDecideKilledOrUnconsciousModel`) | 0004 |
| Reinforcements / battle size | API (`DefaultBattleMissionAgentSpawnLogic`, `BannerlordConfig.BattleSize`) | 0004 |
| Listen server from game client | API (`GameNetwork.StartMultiplayerOnServer` → `MultiClientServer`) — runtime unproven (S2) | 0005 |
| Admit players without TaleWorlds lobby | API (`AddNewPlayerOnServer` + `StartMultiplayerOnClient`) — unproven (S2) | 0005 |
| Replicate SP-spawned agents | API (SpawnAgent broadcasts `CreateAgent` when server) | 0004 |
| Player takes an AI soldier / retake on death | API (`Mission.ReplaceBotWithPlayer`) | 0005 |
| Campaign characters/items in MP | API by MBGUID reference — requires GUID parity (S1) | 0005 |
| Campaign scenes in MP | Available on clients (SandBoxCore), not on dedicated server | 0005 |
| Battle results back to campaign | API (`MapEventSide.OnTroopKilled/Wounded/Routed`) | 0003 |
| Save → bytes → load on client | API (`ISaveDriver`, `GameData`, `SaveManager.Load`, `SandBoxGameManager(LoadResult)`) | 0006 |
| Steam lobby/invite/P2P from a mod | API (Steamworks.NET 20.1.0 shipped) — untested | 0007 |
| Native battle traffic over Steam | Not native; needs UDP tunnel (hypothesis) | 0007 |
| Safe point for network input | API (`MBSubModuleBase.OnNetworkTick`, main-thread sync context) | 0008 |
| Diplomacy compat | Type-level map; host-only behaviors, intercept actions | 0009 |

## Verified

Details: `docs/research/0001-environment.md`, ADR-0009.

- Game path: `E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord` (junction `mb2` in repo)
- Client **v1.4.8.119303**; modding tools v1.4.8; Dedicated Server v1.4.8 (.NET 6, bundles coreclr 6.0.12)
- Steam client hosts mods on the **.NET Framework 4.x CLR** → `net472`
- BUTR `Bannerlord.ReferenceAssemblies.*` **1.4.8.119303** on NuGet = exact client build
- Direct launch `Bannerlord.exe /singleplayer _MODULES_*…*BannerCoopTW*_MODULES_` resolves
  Workshop modules (Harmony loaded from Workshop)
- **Mods (cleaned 2026-10-18)**: Workshop = official BUTR Harmony v2.4.2.248 (2859188632), UIExtenderEx
  v3.0.3 (2859222409), ButterLib v2.12.0 (2859232415) + Diplomacy v1.5.4 (2881380744); re-uploads 3747… unsubscribed
  and removed; **MCM v5.13.1 from Nexus (mod 612) in game `Modules/Bannerlord.MBOptionScreen`** (Steam copy
  unsubscribed). Test run (rgl_log_13668): all load with BannerCoopTW, Diplomacy picks `Bannerlord.Diplomacy.1.4.8.dll`
  ("Loaded Diplomacy v1.5.4!"), main menu in 35 s, `rgl_log_errors` empty. ButterLib log shows two non-blocking ERRs:
  MCM loader "MBSubModuleBaseSimpleWrapper … could not be converted to a delegate" and a patch target
  `CampaignBehaviorManager.OnGameLoaded` not found in 1.4.8 (impact in campaign unknown).
- Tooling: .NET SDK 8.0.426 + **10.0.402**; Visual Studio Community 2026 (18.11.0); `gh` 2.102.0
  (logged in, not on the bash PATH); ilspycmd 9.1; Git 2.55.0; Python 3.12.10; Ollama 0.40.5 + qwen3.5:9b
- Logs: game `C:\ProgramData\Mount and Blade II Bannerlord\logs` (old logs pruned at startup,
  rule unclear — copy before relaunch); mod `Documents\Mount and Blade II Bannerlord\Logs\BannerCoopTW`
- Local model trial (qwen3.5:9b, `local_llm.py` on a 258 KB rgl_log): 10 calls, 9 min; found the
  right issues and main-menu state, but counts were wrong (FMOD 46 vs 67 actual) and it quoted its
  own partial summaries as log text → use only for first-pass triage, always grep-verify

## Blockers (user action)

~~1. Duplicate Workshop modules~~ resolved (official BUTR kept, MCM from Nexus).
2. Optional: add `C:\Program Files\GitHub CLI` to PATH for shells.

## Not yet verified

- "loaded" message visible in game; module in two instances at once
- VS 2026 debugger attach to `Bannerlord.exe` (.NET Framework 4.x) with BannerCoopTW loaded
- Dedicated Server starts locally and accepts a 1.4.8 client (needs server token); custom module on server
- **S1** MBGUID parity after loading the same save on two instances (ADR-0006 gate)
- **S2** listen server started inside a campaign + second instance joins without the TaleWorlds lobby (ADR-0006 gate)
- Client main-hero switch after loading host save (ADR-0010)
- **S3** vanilla character creation mid-campaign for a late joiner (SESSION_RESUME; feature may be disabled if it fails)
- Steam lobby/P2P from the mod (needs second Steam account/PC)

## Next actions

1. Review and merge PR #2, then the Phase 2 PR.
2. Phase 2b spikes (in BannerCoopTW.Module, debug-only): S1 GUID dump on two instances with the same save;
   S2 listen server in campaign + join from instance 2. Result decides ADR-0006 B vs A.
3. Phase 3 (parallel): Core allocation/reservation logic + tests (independent of A/B).

## Do not do yet

Campaign sync, save sync, matchmaking, NAT traversal, production servers, optimisation, UI polish.

## 2026-10-25 — Spikes S1/S2 (research 0010)
- S1: cold loads of one file give identical (type, StringId) → MBGUID; a save→load cycle shifts 300 CharacterObject GUIDs (fixed point afterwards). Order-independent content checksum is stable and cheap (9–35 ms).
  Local costs: capture ≈1 s, load ≈3.7 s, file read ≈0.9 s; real-network transfer, crafted items, mods, real play not measured.
- S2: native listen server starts inside a campaign process and session key/peer index are readable without a lobby; the second instance never connects (120 s then disconnect callbacks). Cause open.
- Tooling: `tools/dev/run-session.sh`, `run-pair.sh`, `dismiss-safemode.ps1` (handles the Safe Mode dialog after a force-killed run), `compare-guid-dumps.ps1`.
- Tests actually run: Core unit tests 14 on this branch; spike results come from real two-instance runs, not tests.

## 2026-10-25 — Phase 3 core (branch `phase3/core-allocation`)
- `BannerCoopTW.Core.Battle`: SlotCatalog, ReservationService, BattleSession, seat/player state machines; `BannerCoopTW.Core.Protocol`: versioned binary wire protocol.
  Details and known limitations: `docs/architecture/BATTLE_PARTICIPATION.md` ("Implementation status").
- Tests actually run: 107 xUnit tests (net8.0), including 200-round concurrent claims, fuzz/mutation of frames, expiry with a moving clock. Not run on net472 / inside the game.
- Reviewed by the architect-reviewer agent; critical findings (pinned initiator hero, AI losses, agent death before confirm, ControlReleased, lost expiry events, catalog thread-safety, epoch, cap, strict decoding) fixed in the same PR.
