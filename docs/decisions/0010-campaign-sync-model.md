# ADR-0010: Campaign synchronisation model

Status: Accepted (Gate G1, Phase 2) · 2026-10-18 · inputs: research 0002, 0003, 0006, 0008, 0009; ADR-0007

## Context
ADR-0007 requires a shared campaign: every player moves their own party on one map at 1x, campaign pauses
for everyone during the single global battle. Research shows:
- the campaign tick uses real-frame dt, one global RNG, parallel loops and native pathfinding (0002) →
  **lockstep is not viable**;
- the whole save can be captured in memory through a custom `ISaveDriver` and loaded with public API
  (0006), ~6–8 MB per world;
- the SP stack assumes one local `MainParty`/`MainHero` (0003);
- game state may only be touched on the main thread; `OnNetworkTick` runs before the game tick (0008).

## Decision
1. **Host-authoritative**: exactly one process (the campaign host) simulates the campaign. Clients never
   simulate; their campaign tick is suppressed (patched `Campaign.RealTick`/`TickMapTime` → dt = 0 and AI
   off) and they only render, interpolate and run UI.
2. **Initial sync and resync = save transfer**: on join, reconnect and **before every battle** the host
   captures the world with a capturing `ISaveDriver` and sends it (compressed) to all clients, which load it
   (`SaveManager.Load` → `SandBoxGameManager(LoadResult)` or an in-place reload). Pre-battle resync
   guarantees an identical world for ADR-0006 (MBGUID parity, gate S1).
3. **Between resyncs: state deltas** at a fixed rate (e.g. 5–10 Hz) for what clients must see live:
   mobile party positions/targets/states, map events start/end, settlement ownership/sieges, player
   parties' rosters/gold/inventory, time & pause. Everything else waits for the next resync.
4. **Client input = commands**: a client never changes campaign state locally. Move orders, menu choices,
   trade, recruit, kingdom/Diplomacy actions (ADR-0008, 0009) are intercepted (Harmony prefix → skip
   locally) and sent as commands; the host validates, applies to that player's party/hero, and replicates.
5. **Player identity**: each player owns one `Hero` + `MobileParty` on the host. On the host they are
   regular non-main parties controlled by commands; on each client, after load, the client's own hero is
   made the local main hero (`ChangePlayerCharacterAction.Apply(Hero)` — to verify, Phase 5a).
6. **Time**: host locks 1x (`SpeedUpMultiplier`, `SetTimeControlModeLock`), owns pause/resume; a battle
   start is a host-serialised global event (ADR-0007 timing rule).
7. Mods with campaign behaviors (Diplomacy) run only on the host; their saved data rides in the save.

## Alternatives rejected
- Deterministic lockstep (each client simulates): impossible to keep bit-exact (0002).
- Full custom world serializer: the game's save system already does it.
- Each player simulating "their" region: no authority, conflicting writes.

## Consequences
- Biggest work: client tick suppression without breaking UI; delta replication schema; command coverage
  of all player actions (long tail).
- Join/battle latency includes a 6–8 MB save transfer + load (measure in Phase 5a; compress; consider
  delta-from-last-save later).
- Host CPU carries the whole campaign (vanilla load).

## Open questions (Phase 5a spikes)
- Can a client switch its main hero after loading a host save without side effects?
- In-place reload vs full game restart for pre-battle resync (time, UI flicker).
- Minimum delta set for a playable map view.
