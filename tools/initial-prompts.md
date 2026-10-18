# Initial Claude Prompts

## Prompt 1 — Environment discovery

Read `CLAUDE.md`, `CURRENT_STATE.md`, and `tools/claude-setup-checklist.md`.

Do not implement networking.

Inspect the local Bannerlord/modding environment and determine:
- exact Bannerlord version
- game path
- module path
- available DLL/API references
- .NET/SDK/toolchain
- existing mod examples/templates
- log locations
- whether two local game instances can be launched

Separate verified facts from assumptions.

Write the findings into `docs/research/0001-environment.md`, update `CURRENT_STATE.md`, and stop for review.

## Prompt 2 — Battle API feasibility

Using official Bannerlord documentation/API and locally available references, investigate whether the mod can:
- detect battle creation
- identify participants
- identify sides
- enumerate relevant agents/entities
- assign/control a player-controlled agent
- detect death
- release control
- reassign control
- observe reinforcements
- detect battle completion

Do not implement the full system.

Write a feasibility matrix into `docs/research/0002-battle-api.md`.

## Prompt 3 — Network prototype

Only after the previous phases are approved:

Implement the smallest local network harness possible:
- two clients
- one session
- one test entity
- claim/release ownership

Do not implement campaign synchronization.

Add tests and update documentation.
