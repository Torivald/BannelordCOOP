# ADR-0004: Do not study or reuse BannerlordCoop source

Status: Accepted · 2026-10-18

## Context
`BannerlordCoop-development/` is under the BannerlordCoop Source Available License (since
2026-06-17). It forbids copying or using the code "in whole or in part" to create a competing
Bannerlord co-op/networking/sync mod. Its AGENTS.md tells automated agents not to analyse the code
for that purpose.

## Decision
- No file in that folder is opened, searched, summarised or ported by Claude.
- The folder is git-ignored and never redistributed.
- Allowed: its public README/marketing facts; MIT-era releases (pre-2026-06-17) after verifying
  the exact tag/commit license and recording it in a research note.

## Consequences
Architecture knowledge comes from TaleWorlds assemblies (decompiled), BUTR docs, permissive
libraries and public behaviour of other mods.
