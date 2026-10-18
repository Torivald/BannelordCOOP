# ADR-0007: Product scope decisions (user answers 2026-10-18)

Status: Accepted

| Topic | Decision |
|---|---|
| Campaign | **Shared campaign world.** Every player moves on the map in parallel with own party/army, quests, economy. Campaign speed locked at 1x. |
| Battle join | When a player starts a battle, other players may join it: pick side (initiator's / opposing), then a slot, or spectate. |
| MVP player count | 4 |
| Hosting | Two modes: dedicated host and Steam invite (player-hosted). |
| PvP | Allowed: players may join the opposing side and control enemy lords. |
| Sieges | In scope (not only field battles). |
| Player characters | Immortal (never die; knock-out only). |
| Resume from save (2026-10-18) | Host loads the co-op save → campaign **paused until every connected player has reclaimed their hero** in a selection window (SESSION_RESUME.md). |
| Late join (2026-10-18) | Up to **4 player slots**; a player joining later (e.g. 4th of a 3-player campaign) may **create a new hero** via vanilla character creation, as **their own new clan** in a town of their culture. Gated by spike S3; **may be disabled for MVP** if too complex (user-accepted). |
| Absent player (2026-10-18) | Their hero/party is **controlled by a bot (game AI)** until they reconnect and reclaim it (no new pause). |

## Consequences
- Campaign synchronisation is unavoidable → custom campaign netcode with an authoritative host is
  required regardless of the battle option chosen in ADR-0006. This is the largest work item.
- Bannerlord's dedicated server is multiplayer-only and does not run a campaign. "Dedicated host"
  for the campaign therefore means a separate (ideally headless) game instance that owns the
  campaign; feasibility must be researched.
- ADR-0006 option B (native MP mission for battles) remains possible: campaign host spawns/hands
  off a battle server; option A remains the fallback.
- Allocation must handle PvP (opposing-side lord slots) and siege-specific roles.
- "Immortal" player heroes: knocked-out player returns to slot selection if a slot is free,
  otherwise spectates (default, to confirm).

## Battle/campaign timing (answered 2026-10-18)
- When any player starts a battle, the **campaign pauses for everyone** (as in vanilla).
- **All players are pulled into that battle**: each picks a side + slot or spectates.
- Therefore only **one battle at a time** exists globally; no concurrent battles, and no question
  of what happens to a joined player's map party (it is frozen with the paused campaign).

## Consequences of the timing rule
- Campaign sync and battle sync are separate phases: campaign runs → pause → battle session →
  results applied by the authoritative host → campaign resumes. Big simplification.
- Battle start must be a global, host-confirmed event (two players triggering encounters in the
  same tick → host serialises them; the second waits or is cancelled).
- The battle host is the initiator's encounter, but authority stays with the campaign host.
