# Architecture Roadmap

## Stage A — Mod shell

Bannerlord module
+ diagnostics
+ configuration
+ logging

## Stage B — Battle abstraction

BattleSession
PlayerSlot
BattleEntity
AllocationService
ReservationService

## Stage C — Networking

Transport
Session
Serialization
Command validation
Snapshot/delta

## Stage D — Battle integration

Bannerlord Mission/Battle adapters

Keep Bannerlord-specific APIs behind adapters where practical.

## Stage E — UI

Side selection
Character allocation
Reservation status
Spectator state
Death/reallocation

## Stage F — Campaign

Campaign state
Party state
Battle entry/exit
Persistence

Do not let campaign concerns leak into the minimal battle/network prototype before they are required.
