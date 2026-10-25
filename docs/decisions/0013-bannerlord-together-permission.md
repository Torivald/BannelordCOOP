# ADR-0013: Bannerlord Together — permission scope and how we use it

Status: Accepted · 2026-10-25

## Context
- Bannerlord Together 0.5 (Steam Workshop 3761555398) ships only compiled DLLs, a proprietary
  `LICENSE.txt` and `AI_USAGE_POLICY_DO_NOT_DECOMPILE.txt` that forbid decompiling and AI processing.
- The user reports a Discord reply from "Mustang (BannerlordTogether Project Team)", 2026-10-16,
  granting the user permission to **read and view the source code** (C# files, configs, project
  structure, docs) to understand how the mod works for developing our own mod. Not permitted: copying,
  reproducing, redistributing, reusing code/assets, derivative works, implying affiliation.
  The user accepted these terms.

## Decision
1. The permission is personal to the user and covers **reading source code supplied by the authors**.
   It does not mention decompiling the shipped DLLs or processing their code with AI.
2. Therefore Claude (cloud and local) does **not** decompile, open or analyse the Bannerlord Together
   DLLs or files in `3761555398/` (CLAUDE.md rule stays).
3. Allowed use of the permission:
   - the user requests the source (repository/archive) from the authors and reads it personally;
   - the user describes ideas/approaches in their own words; Claude discusses and designs an
     independent implementation from those descriptions; nothing is copied;
   - questions to the authors (Discord) and their answers can be quoted in research notes as
     "BT team statement, date".
4. This ADR is revisited if the authors explicitly allow AI-assisted analysis (Claude/Anthropic) of
   the source or decompiled DLLs, with stated scope; the grant is then recorded here verbatim.

## Consequences
- No BT code, decompiled output or excerpts ever enter this repository or Claude's context.
- Knowledge from BT reaches the project only as the user's own description or the authors' answers,
  marked as such in research notes.
