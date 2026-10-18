# ADR-0008: Integration with the Diplomacy mod

Status: Accepted (requirement) · design OPEN until Phase 5 · 2026-10-18

## Context
User requirement: the co-op mod must work together with **Diplomacy** (DiplomacyTeam/Bannerlord.Diplomacy).
- Installed: v1.5.4 (Workshop 2881380744); targets game v1.4.0.*.
- Depends on Harmony ≥2.4.2, ButterLib ≥2.11.1, **MCM (Bannerlord.MBOptionScreen) ≥5.12.3**,
  UIExtenderEx ≥2.13.2. MCM is **not installed** on the dev PC → Diplomacy cannot load until it is.
- License: **CC BY-NC-SA 4.0**. Source may be read for compatibility work. Copying their code into
  our MIT repo is NOT allowed (ShareAlike/NonCommercial conflict). We integrate at runtime only.
- Features that change campaign state: messengers, non-aggression pacts, fief granting/claiming,
  war exhaustion, expansionism, influence balancing, gold donations, factions/civil wars,
  abdication, kingdom management/elections UI.

## Decision
1. Diplomacy is a **supported optional companion mod**; when enabled, host and all clients must run
   the same Diplomacy version (checked in the join handshake, like game/mod version).
2. Host-authoritative rule applies: Diplomacy's campaign behaviours run only on the host; their
   effects reach clients through our campaign sync.
3. Player actions in Diplomacy UI on a client (send messenger, propose NAP, donate, start civil war…)
   are intercepted and sent to the host as commands; host executes Diplomacy's own API; result
   replicated.
4. Diplomacy's saved data is part of the host save; clients receive it with the world state.
5. Integration code lives in a separate project `src/BannerlordCoop.Compat.Diplomacy` loaded only if
   the Diplomacy module is active (soft dependency, no hard reference in core projects).

## Phasing
- Phase 2: research note `0009-diplomacy-internals.md` (behaviours, saved types, UI actions, events).
- Phase 5g: implement after vanilla kingdoms/diplomacy sync works.
- Test matrix: each Diplomacy action from a client; war exhaustion consistency; civil war split;
  save/load with Diplomacy data.

## Consequences
- Adds MCM as a dev dependency; Diplomacy updates may break compat → pin tested versions.
- Until Phase 5g, disable Diplomacy when testing core sync to isolate bugs.
