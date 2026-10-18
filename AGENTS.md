# Instructions for any AI agent (Codex, Gemini, Continue, local models, …)

The single source of truth is **CLAUDE.md** in this folder. Read it first, then CURRENT_STATE.md.
Hard rules that apply to every agent:
- Never open, search, summarise or port anything in `BannerlordCoop-development/` (license, ADR-0004).
- Decompiled game code lives only in `_decompiled/` (git-ignored); never commit or paste large parts of it (ADR-0005).
- Verified facts go to `docs/research/NNNN-*.md` with game version; decisions to `docs/decisions/`.
- Do not change CLAUDE.md, ADRs or CURRENT_STATE.md without being asked; propose changes in your output instead.
- Work on a branch, open a PR; never push to main directly.
