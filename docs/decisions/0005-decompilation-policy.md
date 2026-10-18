# ADR-0005: Decompilation of game assemblies

Status: Accepted · 2026-10-18

## Decision
- TaleWorlds assemblies may be decompiled with ILSpy/ilspycmd for API study.
- Output: `_decompiled/<game-version>/<Assembly>/` (one project per assembly), git-ignored.
- Docs quote signatures and short facts with the game version, never large code blocks.
- Search with grep/rg; read only the types needed. Record findings in docs/research.
- Re-decompile on every game update; compare with BUTR Stable/Beta API diff.

## Suggested command (local PC)
```
ilspycmd -p -o "_decompiled\1.4.8\TaleWorlds.MountAndBlade" "<game>\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.dll"
```
Priority assemblies: TaleWorlds.MountAndBlade, TaleWorlds.CampaignSystem, TaleWorlds.Core,
Modules/SandBox/bin/*.dll, Modules/Multiplayer/bin/*.dll, TaleWorlds.MountAndBlade.Diamond,
TaleWorlds.Network, TaleWorlds.GauntletUI.
