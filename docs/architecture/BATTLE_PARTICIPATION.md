# Battle Participation System

## Purpose

Allow several co-op players to participate in one Bannerlord battle while each player controls a defined playable slot.

## Player flow

### 1. Battle invitation

When a battle begins, connected clients receive a battle participation prompt.

Choices:
- Join attacker
- Join defender
- Spectate

The initiating player is already assigned according to the normal battle flow and is excluded from selecting their own character.

### 2. Side selection

A player selects one side.

The authoritative session validates:
- battle is still accepting participants
- requested side exists
- player is not already assigned elsewhere

### 3. Character allocation

The server/session produces available control slots in priority order:

1. Lords/Heroes
2. Officers/Special units
3. Regular troop classes
4. Spectator

The UI should avoid exposing every individual soldier as a separate choice.

Example:

ATTACKERS
- Lord A — available
- Captain B — available
- Archers — 1/2 available
- Infantry — 0/5 available

### 4. Reservation

A selected slot enters RESERVED state before becoming CONTROLLED.

This prevents two clients from selecting the same slot simultaneously.

Suggested lifecycle:

FREE
  -> RESERVED(player)
  -> CONTROLLED(player)
  -> DEAD / RELEASED

Reservation expiry returns the slot to FREE.

Default timeout: 10–15 seconds.

## Regular troop classes

The user-facing abstraction is a troop class, while the implementation must map that claim to an actual controllable agent/entity if Bannerlord permits it.

Example:
- two archer agents exist
- first player claims Archer class -> one archer is assigned
- second player claims Archer class -> the second archer is assigned
- third player cannot claim Archer because no archer slot remains

The exact implementation must be validated against the Mission/Agent APIs.

## Death

When a controlled character dies:

Preferred MVP behavior:
1. release the control slot
2. return the player to allocation if another playable slot exists
3. otherwise make the player a spectator

Alternative behavior may be selected after testing if Bannerlord's control model makes reallocation unsafe.

## Reinforcements

When new troops enter the battle:
- available troop-class counts are recalculated
- newly available slots can be claimed
- existing ownership is never invalidated by the count update

## Disconnect

Reservation disconnect:
- release reservation immediately

Controlled player disconnect:
- release control after a defined grace period or immediately for MVP
- AI may resume control if supported
- do not leave an inaccessible permanent slot

Reconnect behavior is a later milestone.

## Spectator

Spectator is a valid final state, not an error.

A spectator:
- receives battle state
- cannot issue control commands
- may become eligible for a newly available slot if the MVP permits reallocation

## Authoritative model

Preferred conceptual flow:

Client
  -> command
  -> authoritative BattleSession
  -> validation
  -> battle state change
  -> snapshot/delta
  -> clients

Clients must not be trusted to claim an entity merely because their local UI shows it as available.

## Required tests

- two players claim the same lord simultaneously
- two players claim the last archer simultaneously
- player claims an officer while another claims a troop class
- no slots remain
- controlled character dies
- reservation expires
- player disconnects during reservation
- player disconnects while controlling
- reinforcements create new slots
- battle ends while allocation UI is open

## Implementation status (Phase 3, `BannerCoopTW.Core.Battle` / `.Protocol`)

Implemented and unit-tested without the game: `SlotCatalog` (lords → officers → troop classes → siege engines per side, copy-on-write),
`ReservationService` (atomic seats, lazy expiry reported once, pinned initiator hero, AI-loss and agent-death handling),
`BattleSession` (player/seat state machines, PvP switch, 4-participant cap, battle epoch, events with revision, End while the UI is open),
and a versioned binary protocol (`MessageCodec`, frame = magic, version, type, length).

Decisions taken while implementing (review of 2026-10-25):
- The initiator can neither release nor spectate away their own hero; if they disconnect the seat is written off, never offered to others.
- A seat lost to death/knock-out is gone for the battle; the player is reseated if a slot is free on their side, otherwise spectates.
- An agent that dies before the reservation is confirmed cancels the reservation (same path as death).
- `ControlReleased` is emitted whenever a player stops controlling an agent (release, spectate, disconnect) so the engine can return it to AI.
- Requests carry the battle epoch; a request for another battle is refused.

Known limitations (open, not hidden):
- `Hello.PlayerId` is client-supplied in the protocol: the server must take the identity from the authenticated connection (Steam), not trust the payload.
- `ConfirmRequest` from a client is only an acknowledgement; binding the agent (Reserved → Controlling) is the engine side's job.
- `Hello`/`HelloReject` are rejected like every message when the protocol version differs, so an older peer cannot read the reason; a frozen handshake layout is still to be designed.
- Trailing bytes make a frame malformed (no forward-compatible extension without a version bump).
- No atomic snapshot (revision + both sides + players) and no delta base-revision; no rate limiting; host loss is undefined (session lives in memory only).
- Siege: engines are plain classes; defender roles and "engine destroyed" are not modelled.
