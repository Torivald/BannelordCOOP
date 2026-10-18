# Research 0001 — Development environment (Phase 0)

## Question

Which exact versions of game, dedicated server, SDKs and tools are on the dev machine, where
are the logs, which managed runtime does the Steam client use, and can two game instances run
side by side on one PC?

## Bannerlord version tested

Client **v1.4.8.119303** (Steam build id 24573425, branch `public`).

## Source

Local Claude Code session on the dev PC (Windows 11 Pro 10.0.22621): file listings,
`Version.xml` / `SubModule.xml`, Steam `appmanifest_*.acf`, `dotnet` CLI, file version info,
game logs in `C:\ProgramData\Mount and Blade II Bannerlord\logs`, a live launch of two
client instances, and a re-check launch inspecting the client's loaded native modules.
Decompilation limited to an `ilspycmd -l c` type listing (smoke test); no third-party mod code read.

## Date checked

2026-10-18

## Verified facts

### Game, modding tools, dedicated server

| Item | Value | Source |
|---|---|---|
| Client | v1.4.8, build 119303 | `bin/Win64_Shipping_Client/Version.xml`; log `Build Version: 119303`; watchdog `v1.4.8.119303.119303` |
| Client Steam app | 261550, buildid 24573425, BetaKey `public` | `appmanifest_261550.acf` |
| Modding tools / editor | v1.4.8 | `bin/Win64_Shipping_wEditor/Version.xml` |
| Dedicated Server | installed, app 1863440, buildid 24571419 | `appmanifest_1863440.acf` |
| Dedicated Server modules | Native v1.4.8, Multiplayer v1.4.8, FastMode, ExampleSoundMod | `Modules/*/SubModule.xml` |
| Dedicated Server binaries | `bin/Win64_Shipping_Server` and `bin/Linux64_Shipping_Server`; starters `DedicatedCustomServer.Starter.exe`, `CommunityServer.Starter.exe`, `TaleWorlds.Starter.DotNetCore.exe` | listing |
| Dedicated Server runtime | **.NET 6** (`TaleWorlds.Starter.DotNetCore.runtimeconfig.json`: tfm `net6.0`, `Microsoft.NETCore.App 6.0.0`). Server bundles its own runtime in `bin/Win64_Shipping_Server/Microsoft.NETCore.App/` (coreclr **6.0.12**, 6.0.1222.56807 — same build as the client's bundled one); system .NET 6.0.36 is also installed. Which of the two the starter actually uses is not verified | runtimeconfig, coreclr.dll ProductVersion, `dotnet --list-runtimes` |
| Server vs client DLLs | `TaleWorlds.MountAndBlade.dll` hashes differ (server build ≠ client build); FileVersion is `1.0.0.0` on both (not useful for versioning) | `Get-FileHash`, VersionInfo |
| Client modules (game folder) | Native, SandBoxCore, SandBox, StoryMode, CustomBattle, Multiplayer, BirthAndDeath, FastMode | listing |
| Workshop modules (subscribed) | Bannerlord.Harmony v2.4.2.248 (2859188632), Bannerlord.UIExtenderEx v3.0.3 (2859222409), Bannerlord.ButterLib v2.12.0 (2859232415), Bannerlord.Diplomacy v1.5.4 (2881380744) | `workshop/content/261550/*/SubModule.xml` |
| War Sails DLC | not installed | listing |

Harmony / ButterLib / UIExtenderEx come from the Workshop, not from `Modules/`. The launcher
loads Workshop modules automatically (seen in log `Loading xml file: …\workshop\content\261550\…`).
Diplomacy is a gameplay mod — keep it disabled when testing core sync until Phase 5g (ADR-0008).
It also needs MCM (`Bannerlord.MBOptionScreen`), which is **not** installed, so it cannot load
right now anyway.

### Managed runtime used by the Steam client

- Client `bin/Win64_Shipping_Client` ships **both** `mono/` + `mono-2.0-sgen.dll` and a bundled
  `Microsoft.NETCore.App` (coreclr **6.0.12**, `mscordaccore_amd64_amd64_6.0.1222.56807.dll`).
- **Verified by the loaded-module list of a running 1.4.8 client** (`Bannerlord.exe`
  `/singleplayer`, main menu, `Get-Process -Id … | % Modules`, 2026-10-18 re-check):
  **the Windows Steam client hosts managed code on the .NET Framework 4.x CLR** — loaded:
  `MSCOREE.DLL`, `Framework64\v4.0.30319\mscoreei.dll`, `clr.dll`, `clrjit.dll`, and system
  .NET Framework native images (`mscorlib.ni.dll`, `System.ni.dll`, `System.Core.ni.dll`,
  `netstandard.ni.dll`, … from `C:\Windows\assembly\NativeImages_v4.0.30319_64`).
  No `coreclr.dll`, no Mono class libraries.
- `mono-2.0-sgen.dll` from the game folder **is also loaded**, and the log prints
  `Mono Loading Step::…`; these step names are evidently legacy labels — they are *not* proof
  that Mono runs the mods. (The first version of this note drew that wrong conclusion.)
  What mono-2.0-sgen is used for is unknown.
- TaleWorlds managed assemblies do not show up as process modules (probably loaded from byte
  arrays), so per-assembly host attribution was not possible this way.
- The client's runtime is the machine's .NET Framework (here 4.8.1), not something bundled.
- Consequence: target **`net472`** (BUTR template default) for the client module and reference
  the .NET Standard 2.0 TaleWorlds assemblies. The dedicated server is .NET 6 → shared libraries
  as `netstandard2.0`.
- Debugging: a .NET Framework host means Visual Studio "Attach to Process → Managed (.NET
  Framework 4.x)" should work on `Bannerlord.exe` (not yet tried).

### Dev tooling

| Tool | Version / state |
|---|---|
| .NET SDK | 8.0.426 (only SDK) |
| .NET runtimes | NETCore 6.0.36 + 8.0.32, WindowsDesktop 6.0.36 + 8.0.32, AspNetCore 8.0.32 |
| .NET Framework | 4.8.1 installed (Release 533320); 4.7.2 Developer Pack (reference assemblies) present |
| ilspycmd | **9.1.0.7988, works on .NET 8**: `ilspycmd -l c TaleWorlds.Library.dll` (1.4.8) lists 140 classes → decompile blocker resolved by the older pinned version |
| ILSpy GUI | 11.1.0.9782 unpacked in `E:\Claude Bannerlord\ilspy\` — requires .NET 10 runtime (not installed) → won't start; folder now git-ignored |
| IDE | VS Code 1.142.0 with `ms-dotnettools.vscode-dotnet-runtime` only (no C# / C# Dev Kit); **no Visual Studio** (no `vswhere`), no Rider |
| Git | 2.55.0.windows.5 |
| Python | 3.12.10 |
| Node.js | not installed |
| GitHub CLI `gh` | not installed |
| Steam | running |

### Logs

- Location confirmed: `C:\ProgramData\Mount and Blade II Bannerlord\logs`
  (crash dumps: `C:\ProgramData\Mount and Blade II Bannerlord\crashes`, per watchdog log).
- Per process (PID suffix): `rgl_log_<pid>.txt` (main), `rgl_log_errors_<pid>.txt`,
  `watchdog_log_<pid>.txt`; launcher writes `launcher_log_<n>.txt`.
- Header gives build version; the watchdog log gives the full `v1.4.8.<build>` string.
- Main-menu reached marker: `GauntletInitialScreen::HandleInitialize`.
- `Mono Loading Step::…` lines appear during startup but do **not** identify the runtime
  (see above).
- **Previous `rgl_log_*` / `watchdog_log_*` files are deleted on the next game launch**
  (observed twice: May logs gone after the 2-instance test; the test's logs gone after the
  re-check launch). Copy logs out before relaunching when debugging. Unrelated old files stay: `launcher_log_*`, `CrashUploader.*.txt` (2024, May 2026),
  `crashlist.txt`, a third-party mod debug log.

### Two game instances on one PC — **works**

Procedure (run 2026-10-18 18:04):

```
cd "<game>\bin\Win64_Shipping_Client"
Bannerlord.exe /singleplayer _MODULES_*Native*SandBoxCore*CustomBattle*_MODULES_
(20 s later, same command again)
```

- No single-instance lock: two `Bannerlord.exe` processes (+ two `Watchdog` processes) ran
  concurrently. Steam did not block the direct exe launch.
- Both showed the "Game shut down unexpectedly on previous session. Would you like to enable
  safe mode?" dialog (left over from an earlier unclean exit); answered "No" in both.
- Both reached the main menu (`GauntletInitialScreen::HandleInitialize` at 18:06:59 and
  18:07:27), ~2.0 GB RAM each, no entries in `rgl_log_errors_*`, no exceptions.
- Both closed cleanly via window close.
- Workshop modules' `SubModule.xml` were still scanned even though `_MODULES_` named only native
  modules, but none of their assemblies were loaded (no `Loading assembly:` line for Harmony,
  ButterLib, UIExtenderEx or Diplomacy in the re-check run).

## Inferences / hypotheses

- Two local instances should be enough for Phase 1–3 loopback networking tests
  (one host, one client, `127.0.0.1`). Not yet tested: both instances inside a mission at once,
  both logged into Steam multiplayer services, GPU/CPU headroom during battles.
- Both instances share one Steam account. Anything that relies on Steam identity (lobbies,
  invites, Steam P2P) cannot be tested with two instances on one PC — needs a second account/PC
  (already noted for Phase 4–5).
- Dedicated Server buildid (24571419) differs from client (24573425): normal, separate Steam
  apps; module versions match (v1.4.8). Actual MP compatibility only provable by connecting.

## Unknowns

- Whether the Dedicated Server starts and accepts a 1.4.8 client locally (needs a server token:
  official custom server flow requires `customserver.gettoken` from the in-game console — to
  verify in Phase 2 for ADR-0006 option B).
- Whether mods can be loaded into the Dedicated Server (`_MODULES_` with a custom module).
- Whether Visual Studio's managed (.NET Framework 4.x) debugger attaches cleanly to
  `Bannerlord.exe` and hits breakpoints in a mod assembly — impacts the IDE choice.
- What `mono-2.0-sgen.dll` is used for in the client process.
- Which runtime the Dedicated Server starter actually uses (bundled 6.0.12 vs system 6.0.36).

## Implementation impact

- Phase 1 module: target **`net472`** for the client module (Mono), keep `BannerlordCoop.Core`
  as `netstandard2.0` so it can be shared with a .NET 6 dedicated-server module later.
- Build with .NET SDK 8 (SDK-style projects can build `net472` with the 4.7.2 Developer Pack).
- Decompilation for Phase 2 can start now with `ilspycmd` 9.1 (no .NET 10 needed). Installing
  the .NET 10 runtime is only needed for the ILSpy 11 GUI (optional).
- Test launches: use direct `Bannerlord.exe /singleplayer _MODULES_*…*_MODULES_` with an
  explicit module list (excludes Diplomacy etc.).

## License / usage constraints

Only game files' metadata (Version.xml, SubModule.xml, runtimeconfig, logs) were read.
TaleWorlds code was not decompiled beyond a class-name listing (nothing stored in the repo);
no third-party mod code was opened.

## Follow-up tests

1. Phase 1: minimal module loads in both instances simultaneously (log line from our SubModule).
2. Phase 2: start Dedicated Server locally (token) and connect one 1.4.8 client; try loading a
   custom module on the server.
3. Two instances in a Custom Battle at the same time (performance).
4. Attach Visual Studio (Managed .NET Framework 4.x) to `Bannerlord.exe` with the Phase 1
   module loaded and hit a breakpoint.
