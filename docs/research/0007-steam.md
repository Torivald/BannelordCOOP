# Research 0007 — Steam: lobbies, invites, P2P sockets

## Question
For the "Steam invite" hosting mode (ADR-0007): what Steam functionality ships with the game, what does
the game itself use, and can co-op traffic (our channel + native battle traffic) go over Steam?

## Bannerlord version tested
v1.4.8.119303 (client), local decompilation + type listing (ADR-0005).

## Source
`bin/Win64_Shipping_Client/Steamworks.NET.dll`, `steam_api64.dll`, `steam_appid.txt`,
`TaleWorlds.PlatformService.Steam.dll`, `TaleWorlds.Diamond.AccessProvider.Steam.dll`; 0005 for native traffic.

## Date checked
2026-10-18

## Verified facts
- Shipped: **Steamworks.NET 20.1.0** (FileVersion/assembly version 20.1.0.0; Steamworks.NET is MIT)
  + `steam_api64.dll`. `steam_appid.txt` = `261550`; on this PC it was (re)written at 18:59:32 on
  2026-10-18, i.e. when the game was started directly via `Bannerlord.exe` (Phase 1 test); a directory
  listing during Phase 0 (before any direct launch on 1.4.8) did not show it.
- The DLL contains the full wrappers incl. `Steamworks.SteamMatchmaking`, `SteamNetworking`,
  `SteamNetworkingSockets`, `SteamGameServer`, `SteamFriends` and callbacks `GameLobbyJoinRequested_t`,
  `LobbyEnter_t`, `GameRichPresenceJoinRequested_t`, `SteamNetConnectionStatusChangedCallback_t`.
- The game's own Steam usage (`TaleWorlds.PlatformService.Steam`): friends list/persona/avatars,
  co-play friends, overlay, achievements, UGC (Workshop subscriptions), DLC checks, `SteamUser.GetSteamID`.
  **No matchmaking or networking calls** — MP uses the TaleWorlds Diamond lobby (Steam only as
  identity/access provider, `TaleWorlds.Diamond.AccessProvider.Steam`).
- Native MP game traffic goes through the engine's native transport (`MBAPI.IMBNetwork.InitializeServerSide(port)`
  / `InitializeClientSide(address, port, …)`, UDP; player-hosted server uses port 9999) — not through Steam (0005).

## Inferences / hypotheses
- Because Steam is already initialised by the game process, a mod can call Steamworks.NET directly
  (same `steam_api64.dll`, app id 261550): create a friends-only lobby (`SteamMatchmaking.CreateLobby`),
  invite via overlay (`SteamFriends.ActivateGameOverlayInviteDialog`), handle `GameLobbyJoinRequested_t`,
  exchange host data through lobby metadata.
- **Our co-op channel** (campaign sync, commands, slot reservations) can run over
  `SteamNetworkingSockets` P2P (Valve relay → NAT traversal without port forwarding) behind the same
  transport abstraction as LiteNetLib (MASTER_PLAN Phase 4).
- **Native battle traffic** (listen server, 0005 H1) needs IP:port reachability. Options:
  (a) direct/port-forwarded/VPN (Hamachi/WireGuard — already available to the user),
  (b) a local UDP relay that tunnels the engine's UDP packets over Steam P2P (client connects to
  `127.0.0.1:port` of a local proxy). (b) is feasible in principle but unverified (latency, packet size).
- Steam-invite mode therefore = Steam lobby/invite for discovery + Steam P2P for our channel + (VPN or
  tunnel) for battle traffic.

## Unknowns
- Callback pumping: does the game call `SteamAPI.RunCallbacks()` every frame (needed for lobby callbacks
  in a mod), or must the mod pump `CallResult`s itself?
- Whether invites/rich presence work when the game was not launched through Steam.
- Tunnel feasibility and MTU for native packets.

## Implementation impact
- ADR-0012 (transport): one `ITransport` with LiteNetLib (IP/dedicated/VPN) and Steam P2P backends.
- ADR-0011 (hosting): Steam invite = player-hosted campaign host + listen battle server on the same machine.

## License / usage constraints
Steamworks.NET is MIT and shipped by the game; we reference the game's copy, do not redistribute.
Valve Steamworks terms apply to app 261550 usage by a mod (normal for Bannerlord mods using Steam APIs).

## Follow-up tests
1. Spike: from a mod, create a lobby + overlay invite between two Steam accounts (needs a second account/PC).
2. Spike: SteamNetworkingSockets P2P echo between two machines.
3. Later: UDP tunnel for native battle traffic.
