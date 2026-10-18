# Initial Data Model

## PlayerSlot

- PlayerId
- ConnectionState
- Team
- Role
- ControlledEntityId
- ReservationId

## BattleEntity

- EntityId
- EntityType
- Team
- OwnerPlayerId
- ControlState
- SourceTroopClass
- Alive

## Reservation

- ReservationId
- PlayerId
- TargetId
- CreatedAt
- ExpiresAt

## BattleSession

- SessionId
- BattleId
- Players
- Entities
- Reservations
- Phase

These are conceptual models. They are not yet required to map one-to-one to Bannerlord engine objects.
