# Networking Architecture — Initial Direction

## Goal

Synchronize a co-op battle without making every client authoritative over arbitrary battlefield state.

## Preferred model

Authoritative battle/session state:

Client input
-> command
-> validation
-> authoritative state
-> snapshot/delta
-> clients

## What must be synchronized first

The first network prototype should contain only:
- session ID
- player ID
- connection state
- ready state
- one test entity
- entity owner
- entity control state

## What is explicitly not part of the first prototype

- complete campaign state
- all agents
- physics replication
- inventory replication
- save synchronization
- NAT traversal
- public matchmaking
- anti-cheat
- server fleet

## Library choice

Do not hard-code a networking library before research.

Compare candidates using:
- compatibility with Bannerlord/.NET runtime
- UDP/reliable transport support
- serialization model
- threading model
- latency behavior
- NAT support if required later
- maintenance/activity
- license
- ease of embedding into a Bannerlord module

Record the decision in `docs/decisions/`.

## Tick rate

Do not assume 60 Hz network replication is necessary.

Measure:
- input frequency
- state change frequency
- acceptable latency
- bandwidth
- CPU overhead

Use separate concepts for:
- game simulation/update
- input commands
- network snapshots
- interpolation/extrapolation

## Authority

The first implementation should make authority explicit.

A client may request:
`ClaimSlot(slotId)`

The server/session answers:
`ClaimAccepted`
or
`ClaimRejected(reason)`

The client UI must react to authoritative results.
