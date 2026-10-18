---
name: "bannerlord-research"
description: "Use when verifying a Mount & Blade II Bannerlord API, hook or behaviour for the co-op mod: decompile, search, and write a research note."
---

# Bannerlord API research

1. Read `CLAUDE.md` and `CURRENT_STATE.md` in `E:\Claude Bannerlord`. Note the installed game version (bin/Win64_Shipping_Client/Version.xml) — every fact is tied to it.
2. Never open `BannerlordCoop-development/` (ADR-0004).
3. Sources in order: BUTR public API docs / reference assemblies -> decompiled TaleWorlds assemblies in `_decompiled/<version>/` -> public mod behaviour descriptions.
4. If the needed assembly is not decompiled yet: `ilspycmd -p -o _decompiled\<ver>\<Asm> <game>\bin\Win64_Shipping_Client\<Asm>.dll` (or Modules/<Mod>/bin/...).
5. Search with grep/rg for type and method names; read only the relevant types. Never load whole decompiled files or large trees into context.
6. Write `docs/research/NNNN-<topic>.md` using RESEARCH_TEMPLATE.md: question, version, sources, verified facts (signature + file), hypotheses, unknowns, impact, follow-up tests. Quote signatures only, not code bodies.
7. Separate verified from inferred. Record conflicts instead of choosing silently.
8. Update CURRENT_STATE.md "Verified" / "Not yet verified".