---
name: "mvp-check"
description: "Use at the end of a Bannerlord co-op milestone or task to verify it is really done: build, tests, docs, state, git diff."
---

# Milestone check

Run and report each item honestly (PASS / FAIL / NOT RUN — never claim PASS without running):

1. Build: `dotnet build` (or MSBuild) of changed projects with no new warnings treated as errors.
2. Unit tests: `dotnet test` for Core tests.
3. In-game check if the milestone touches the game: module loads in launcher, no errors in the game log (record the log path), behaviour observed.
4. Update docs/testing/TEST_MATRIX.md checkboxes only for what actually ran.
5. Docs: research/ADR/architecture updated for any new fact or decision.
6. CURRENT_STATE.md: phase, completed, verified, blockers, next actions.
7. `git status` and `git diff --stat`; ensure `_decompiled/`, `BannerlordCoop-development/`, bin/obj and local paths are not staged.
8. Summarise: what changed, what was tested, open assumptions, next step.