# ADR-0003: Authoritative Battle Session

## Decision

Use an authoritative session/state owner for battle participation and ownership claims.

## Rationale

A player's UI can become stale between rendering and claiming. Ownership therefore requires server/session-side validation.

The initial implementation should avoid giving each client arbitrary authority over battlefield state.

## Consequences

Positive:
- deterministic ownership decisions
- simpler conflict handling
- clear disconnect/release behavior
- easier spectator implementation

Costs:
- state owner becomes a synchronization bottleneck
- more work is required for snapshots and recovery
- actual Bannerlord engine authority must be reconciled with the mod's session authority
