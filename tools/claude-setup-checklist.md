# Claude Code Setup Checklist

## Required

- [ ] Git repository initialized
- [ ] Claude Code installed and authenticated
- [ ] project opened at repository root
- [ ] `CLAUDE.md` present
- [ ] `CURRENT_STATE.md` present
- [ ] README present

## Initial MCP count

Target: 0 external MCPs.

Do not install:
- filesystem MCP
- Git MCP
- memory MCP

unless a demonstrated requirement appears.

## Initial skills

Target: 3.

- `/bannerlord-research`
- `/coop-architecture`
- `/mvp-check`

## First Claude session

Ask Claude to:
1. inspect repository
2. inspect CLAUDE.md and CURRENT_STATE.md
3. inspect local Bannerlord environment
4. identify exact game version
5. report available modding/API files
6. identify module loading/logging path
7. stop and write findings to `docs/research/`

Do not ask it to implement networking yet.

## Context management

For long sessions:
- use `/context`
- use `/usage`
- use `/compact`
- use `/clear` between unrelated phases
- use `/resume` when continuing a paused session

Exact command availability can vary by Claude Code version; verify with Claude Code's current help if needed.
