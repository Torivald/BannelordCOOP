# ADR-0006: Battle transport — custom netcode vs native multiplayer stack

Status: OPEN — decide after Phase 2 (battle API research) · 2026-10-18

## Option A — custom netcode over single-player missions
Own transport (e.g. LiteNetLib, MIT), Harmony patches, own agent/state replication on top of SP
missions; authoritative host.
+ full control, works with any SP mission content
− SP missions are not network-aware: agent movement, combat, AI, physics must be replicated by us;
  highest effort and desync risk.

## Option B — native multiplayer mission for the battle
When a campaign battle starts, the initiator's game exports the battle (parties, heroes, troops,
equipment, scene, sides) and a Bannerlord multiplayer (dedicated or listen) server runs a custom
game mode that spawns those agents; other players join as MP clients, pick a slot, and possess the
agent via native MP control; result is sent back to the campaign.
+ engine already replicates agents, combat and orders; public evidence (Warhost) shows it works
− needs version-matched dedicated server, campaign→MP data transfer, custom MP game mode/UI,
  SP-only items/scenes may need workarounds; host-side campaign must pause/wait.

## To verify in Phase 2
1. Can an MP mission spawn arbitrary BasicCharacterObject/equipment from campaign data?
2. How MP agent possession works (peer ↔ controlled agent), switching agent on death.
3. Can a listen server run from the SP client, or is the dedicated server mandatory?
4. Scene availability (campaign battle scenes in MP).
5. Result round-trip (casualties, captured heroes, loot) back to campaign.

## Allocation logic is independent
Reservation/allocation (BattleSession, slots, timeouts) is pure C# and identical in both options.
Build and unit-test it first.
