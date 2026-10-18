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
