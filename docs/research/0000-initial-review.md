# Research 0000 — Initial review of instructions and environment

Date checked: 2026-10-18 · Author: Claude (cloud session, device bridge: list/stage only, no shell)

## 1. Instruction set review

Read: CLAUDE.md, CURRENT_STATE.md, README.md, tools/*, docs/architecture/*, docs/decisions/*,
docs/plans/MVP_ROADMAP.md, docs/testing/TEST_MATRIX.md, docs/research/RESEARCH_TEMPLATE.md.

Verdict: solid, disciplined base (verify-first, authoritative session, small milestones).
Issues found and fixed in CLAUDE.md v2:
- CLAUDE.md pointed to `docs/status/CURRENT_STATE.md`; file is at repo root → unified to root.
- No rule about the reference mod's license → added (ADR-0004).
- No decompilation policy → added (ADR-0005).
- Architecture assumed custom netcode only; native MP stack option was missing → ADR-0006.
- Wording "attacker/defender" vs requirement "initiator's side / opposing side" → spec uses
  initiator-relative sides (maps to attacker/defender per battle).
- Module ID "Coop" in planned layout would clash with BannerlordCoop → use a distinct ID.
- Missing pure-C# core project for unit-testing allocation without the game → added to layout.

## 2. Verified environment facts

| Item | Value | Source |
|---|---|---|
| Game path | `E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord` | listing |
| Client version | v1.3.15 | `bin/Win64_Shipping_Client/Version.xml`, `Modules/Native/SubModule.xml` |
| Editor (modding tools) | v1.4.8 | `bin/Win64_Shipping_wEditor/Version.xml` |
| package_info.txt | `PC@v1.3.4`, changeset 110062 (stale/odd, ignore) | file |
| TaleWorlds DLL target | .NETStandard 2.0 | TargetFramework attribute in MountAndBlade.dll, Library.dll |
| Runtimes shipped | mono, Microsoft.NETCore.App (6.0.x) | bin listing |
| Modules | Native, SandBoxCore, SandBox, StoryMode, CustomBattle, Multiplayer, BirthAndDeath, FastMode | listing |
| modding_resources | FMOD sample project, skeleton FBX | listing |
| Network DLLs present | TaleWorlds.Network.dll, TaleWorlds.MountAndBlade.Diamond.dll, Steamworks.NET.dll | listing |
| Git | not initialised in E:\Claude Bannerlord | listing |

**Critical:** client 1.3.15 vs tools 1.4.8. Current stable on Steam per mod pages is 1.4.8
(Bannerlord Together, Warhost and BannerlordCoop all target 1.4.8). Client must be updated.

## 3. Reference mod (BannerlordCoop-development)

License: "BannerlordCoop Source Available License" since 2026-06-17 — forbids using the code
"in whole or in part" to create/improve a competing co-op/networking mod; AGENTS.md instructs
agents not to analyse it for that purpose. → Not studied. Folder must stay out of our repo.
Public facts only (README): campaign co-op, up to 8 players, dedicated server host model,
supported game v1.4.8, battles as allies/opponents.
Legit option: MIT-era releases (before 2026-06-17) — check tag/commit license first.

## 4. Other public projects (behaviour only)

- **Bannerlord Together** (Nexus 10426): 2 campaign players + extra players as spectators or
  "control soldiers in battles"; "Play as a Soldier" mode; 1.4.8. No public source; permission required.
- **Warhost / Bannerlord Coop Campaign** (Nexus 10846, GitHub zzACzz/BannerlordCoopSpectator
  releases): campaign battles are **recreated on a native dedicated MP server**, players join either
  side, results returned to campaign; up to 120 players; 1.4.8 client + 1.4.8 dedicated server.
  → Strong evidence that architecture B (ADR-0006) is viable.
- **SkyCraft** (github.com/chasmlol/SkyCraft, MIT): Skyrim+Minecraft bridge via shared memory; no decompilation docs. Useful idea: fake stand-in processes (`fake_skyrim.py`) to test protocol without the games → adopt for our network harness.

## 5. Decompilation — needed? Yes.

TaleWorlds publishes almost no API documentation. Mission/Agent/controller behaviour, MP agent
possession, Gauntlet UI and campaign encounter flow can only be confirmed by reading the
assemblies. Community norm: ILSpy/dnSpyEx decompile for study, Harmony for patching.
Helpful public resources:
- BUTR Bannerlord.ReferenceAssemblies (NuGet) — compile against any game version without game DLLs.
- BUTR Game Public API docs and Stable/Beta API diff (butr.github.io/Bannerlord.Game.Diff).
- BUTR Bannerlord.Module.Template (MIT) — module skeleton for VS/Rider/dotnet CLI.
- Bannerlord.Harmony, ButterLib, UIExtenderEx, Harmony.Extensions.
Cloud limitation: this session cannot download dotnet/nuget (proxy blocks), so decompilation
runs on the user's PC (ILSpy GUI or `ilspycmd`) or after the user allows nuget/dot.net domains.

## 6. What the user should install / do

Required:
1. Update Bannerlord client to 1.4.8.
2. .NET SDK 8 (or newer LTS) + .NET Framework 4.7.2 Developer Pack.
3. Visual Studio 2022 Community (".NET desktop development") or JetBrains Rider.
4. Git for Windows.
5. ILSpy (GUI) and `dotnet tool install -g ilspycmd`.
6. Claude Code on Windows (included in Pro) for build/run phases.
Likely (architecture B): "Mount & Blade II: Dedicated Server" from Steam → Library → Tools.
Optional: dnSpyEx (runtime debugging), Bannerlord.Harmony from Workshop, Steam second account
or a second PC for two-instance tests.

## 7. Tooling verdict (MCP / skills)

- External MCPs: **0 required.** Built-ins cover files, shell (local Claude Code), web, memory,
  project docs. Reconsider a GitHub MCP only if PR/issue work becomes frequent (gh CLI suffices).
- Skills: **3** — bannerlord-research (incl. decompile workflow), coop-architecture, mvp-check.
- Model: no extra models needed.

## 8. Open questions for the user (answered → ADR-0007)

1. Campaign model: shared campaign world (huge scope) vs each player in own campaign and
   others drop into the initiator's battles (much smaller)? Or battle-only first (custom battle)?
2. Target player count for MVP (2? 4? 8?).
3. Hosting: Steam P2P/invite, LAN/IP, or dedicated server?
4. Can players on the opposing side take the enemy lord (PvP vs initiator)? Siege/hideout in MVP?
5. What should happen to a player whose character dies (re-pick vs spectator)?
6. Link for "Skycraft" mod.
