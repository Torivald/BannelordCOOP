# Bannerlord Co-op

Experimental co-op mod for Mount & Blade II: Bannerlord.

## Primary MVP

The first gameplay target is Battle Participation:

- one player starts a battle
- other players choose attacker/defender/spectator
- players claim available lords, heroes, officers, or troop classes
- claims are reserved and validated centrally
- players with no available control slot become spectators

## Development strategy

The project is developed in small vertical milestones:

1. Environment discovery
2. Minimal loadable mod
3. Battle/Mission API research
4. Local two-instance/network harness
5. One synchronized test entity
6. Battle participation allocation
7. Basic battle control
8. Death/reallocation/spectator behavior
9. Reinforcements and battle completion
10. Campaign integration
11. Persistence/reconnect
12. Hardening and performance

## Documentation

See:
- `CLAUDE.md`
- `CURRENT_STATE.md`
- `docs/architecture/`
- `docs/decisions/`
- `docs/research/`
- `docs/testing/`
- `docs/plans/`

## Principle

Do not confuse "possible in theory" with "supported by the installed Bannerlord API." Verify first.
