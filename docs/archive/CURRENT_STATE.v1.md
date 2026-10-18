# Current State — Bannerlord Co-op

Last updated: initial project setup

## Project status

Phase: 0 — Environment discovery

Status: NOT STARTED

## Goal

Create a Bannerlord co-op mod with a Battle Participation MVP:
- players join one battle
- choose a side
- choose a controllable lord/officer/troop class
- reserve control slots
- spectate when no playable slot exists

## Completed

- Project concept defined.
- Battle Participation concept defined.
- Initial engineering constraints defined.

## Not yet verified

- Bannerlord version
- game installation path
- module structure
- API version
- .NET/IDE/toolchain
- DLL references
- local two-instance execution
- logging behavior
- network library choice
- exact Battle/Mission hooks
- controllable agent/entity APIs

## Current architectural assumptions

These are hypotheses, not verified Bannerlord facts:
- An authoritative battle/session model is preferable.
- Players should claim control slots rather than independently simulate the entire battle.
- Regular troops should be represented to the user as troop classes.
- Reservations need atomic validation.

## Immediate next actions

1. Inspect the local Bannerlord installation.
2. Identify exact game/API version.
3. Build or locate the smallest loadable module.
4. Verify module loading and logs.
5. Document Battle/Mission APIs relevant to:
   - battle creation
   - participants
   - agent spawning
   - player control
   - death
   - reinforcements
   - battle completion
6. Only then design the first network harness.

## Do not do yet

- Full campaign synchronization
- Save-game synchronization
- Complex matchmaking
- NAT traversal
- Production server infrastructure
- Large-scale optimization
- Full UI polish
