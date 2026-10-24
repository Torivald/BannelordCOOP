# ADR-0012: Co-op transport

Status: Accepted (Gate G1, Phase 2) · 2026-10-18 · inputs: research 0005, 0007, 0008; ADR-0011

## Context
Two kinds of traffic exist: (1) our co-op channel — handshake, campaign commands/deltas, save transfer,
slot reservations; (2) native battle traffic of the listen server (engine UDP, not replaceable from C#, 0005).
Players are behind home NATs; Steam is initialised by the game and ships Steamworks.NET 20.1.0 with
`SteamNetworkingSockets` and `SteamMatchmaking` (0007).

## Decision
1. Our channel uses an **`ITransport` abstraction** (reliable-ordered + unreliable channels, connection
   events) in `BannerCoopTW.Networking`, with two backends:
   - **LiteNetLib** (MIT) over UDP — LAN, port-forwarded, VPN, dedicated host; also used by fake test
     clients without the game (Phase 4);
   - **Steam P2P** (`SteamNetworkingSockets` via the game's Steamworks.NET) — Steam-invite mode, NAT
     traversal through Valve relays.
2. Large payloads (save transfer, 6–8 MB, ADR-0010) are chunked + compressed on the reliable channel.
3. Threading per research 0008: transport threads only enqueue; main thread drains in `OnNetworkTick`.
4. Native battle traffic stays on the engine transport to the host's IP:port (ADR-0011 §4).
5. Messages are versioned; the handshake checks protocol version, game build, module list + versions.

## Consequences
- Two backends to test; the abstraction keeps Core/Networking testable without Steam.
- LiteNetLib license must be re-verified when added (CLAUDE.md dependency rule).

## Amendment 2026-10-25 (Phase 4 start)
- **LiteNetLib version pin: 1.3.5** (MIT, verified on NuGet: license expression `MIT`). It is the last 1.x release and still targets `netstandard2.0`;
  2.x targets only `net8.0`/`netstandard2.1`, which the net472 game module cannot load. `BannerCoopTW.Networking` is therefore `netstandard2.0` on LiteNetLib 1.3.5.
  Re-check when the module moves off .NET Framework.
- The connection key is `bannercoop/<protocol version>`: peers with another protocol version are refused by LiteNetLib before any of our code runs.
- `ITransport` is split into `IServerTransport` / `IClientTransport`; events are raised only from `Poll()` (main thread); a refused client is cut after a short
  linger so the reliable `HelloReject` still reaches it.
- Server glue: `BattleServerHost` maps wire messages to `BattleSession` calls. Identity currently comes from `Hello.PlayerId` (to be replaced by the authenticated connection).
