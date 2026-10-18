# MVP Roadmap

## Phase 0 — Environment discovery

Deliverables:
- exact Bannerlord version
- modding environment
- module skeleton/reference
- toolchain
- logs
- two-instance feasibility

Estimate: 2–5 focused hours.

## Phase 1 — Minimal module

Deliverable:
- mod loads
- one controlled diagnostic behavior
- no networking

Estimate: 4–12 focused hours.

## Phase 2 — Battle/Mission research

Deliverable:
- verified API map for battle lifecycle and agent control
- documented hooks
- feasibility verdict for each required operation

Estimate: 4–12 hours depending on documentation/source availability.

## Phase 3 — Network harness

Deliverable:
- two local clients
- connection/session
- one test entity synchronized
- claim/release message

Estimate: roughly 1–3 weeks of focused engineering, depending heavily on Bannerlord constraints and chosen transport.

## Phase 4 — Battle Participation

Deliverable:
- side selection
- lord/officer allocation
- troop-class allocation
- atomic reservations
- spectator fallback

Estimate: 1–3 weeks after the network/battle foundations are proven.

## Phase 5 — Death/reallocation/reinforcements

Deliverable:
- death handling
- slot release
- reallocation
- reinforcement slots
- disconnect behavior

Estimate: 1–3 weeks.

## Phase 6 — Campaign integration

Deliverable:
- campaign battle entry
- consistent party/player state
- battle return
- persistence boundaries

Estimate: 1–3 months for a credible MVP, potentially more.

## Phase 7 — Full co-op

Potential scope:
- campaign synchronization
- saves
- reconnect
- desync recovery
- hosting
- NAT traversal
- performance
- compatibility
- mod interoperability

Estimate: several additional months to a year+ depending on ambition and available time.

## Main risk

The largest technical risk is not MCP configuration or choosing a networking library.

It is reproducing/synchronizing Bannerlord's single-player campaign and battle lifecycle without fighting the engine's assumptions.
