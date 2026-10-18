# ADR-0011: Hosting model

Status: Accepted (Gate G1, Phase 2) · 2026-10-18 · inputs: ADR-0006, ADR-0007, ADR-0010; research 0005, 0007

## Context
ADR-0007 asks for two modes: "dedicated host" and "Steam invite" (player-hosted), 4 players MVP.
Phase 2 findings: the Bannerlord Dedicated Server is multiplayer-only, registers through the TaleWorlds
lobby with an AuthToken, ships without SandBoxCore scenes and cannot run a campaign (0005). The game client
can act as a listen server (`MultiClientServer`) and is the only thing that can host the campaign.

## Decision
1. **One host process = a normal game client** running our module: it is the campaign host (ADR-0010) and,
   during a battle, the native listen server (ADR-0006 B-listen). No separate battle server.
2. **Mode "Steam invite" (MVP)**: the host is one of the players (plays and hosts). Discovery/invite via a
   Steam lobby (0007); our co-op channel over Steam P2P or IP (ADR-0012); battle traffic to the host's UDP port.
3. **Mode "dedicated host" (post-MVP)**: the same game client run by a non-playing host on an always-on PC
   (its own hero idles/spectates). True headless operation is **not** planned until proven possible; the
   Bannerlord Dedicated Server is **not** used for the campaign. Revisit after Phase 9.
4. **Reachability for battles**: MVP requires the host's battle port to be reachable — LAN, port
   forwarding, or VPN (Hamachi/WireGuard). A Steam P2P UDP tunnel for battle traffic is a later improvement
   (0007 hypothesis).
5. **Host authority is final**: allocation (BattleSession), campaign commands, battle results.
   Host leaves → session ends (MVP); host migration is out of scope.

## Alternatives rejected
- Bannerlord Dedicated Server for battles: lobby/AuthToken dependency, missing scenes/objects, extra
  version-matched install for every user.
- Separate battle host per battle (initiator hosts): authority split, more reachability problems; the
  campaign host already owns the world and the `MapEvent`.

## Consequences
- The host needs the most CPU/bandwidth (campaign + battle server + its own client).
- Every player needs the identical module set (handshake: game build, mod versions, module list).
- Version gating: all players on v1.4.8.119303 until the mod is updated.

## Open questions
- Can a game client run windowed-minimised/low-graphics as a "dedicated host" without stalling ticks?
- Steam lobby callbacks when the game was launched outside Steam (0007).
