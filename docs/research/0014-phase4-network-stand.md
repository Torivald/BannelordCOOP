# Research 0014 — Phase 4: network stand (lobby, channels, fake clients, two game instances)

## Question
Can the host run an authoritative lobby and battle-participation flow over our transport for four clients, with snapshots on an unreliable-sequenced channel and events on a reliable one — first with fake clients (no game), then with two real game instances?

## Setup
Bannerlord v1.4.8.119303 (game part only); LiteNetLib 1.3.5 (MIT); .NET 8 test runner for the library tests; one PC, loopback; 2026-10-25. Branch `phase4/lobby-stand`. Code: `src/BannerCoopTW.Core/Protocol` (protocol v2, `SequenceGate`), `src/BannerCoopTW.Networking` (`BattleServerHost` lobby, `CoopClient`, `NetworkConditionTransport`), `src/BannerCoopTW.Module/Spikes/L1`, `tools/dev/run-l1.sh`.

## What was built
1. **Channels.** `DeliveryMode.UnreliableSequenced` maps to LiteNetLib `Sequenced`. Snapshots carry a payload sequence number and the receiver drops anything not newer (`SequenceGate`, wrap-safe). Spike A1 now sends snapshots this way (one number per chunk).
2. **Lobby on the host.** Hello checks protocol version, game build and (optionally) the exact mod version, with a specific `HelloReject` reason each; lobby cap; duplicate id refused; `ReadyRequest` / `LobbyUpdate`; `Ping`/`Pong` (unreliable) with RTT reported back by the client and shown to everyone; disconnect keeps the place for a grace period (default 30 s) then frees it; reconnect with the same id inside the grace period is `HelloAck.Resumed` and re-enters the battle at `Invited` (`BattleSession.Rejoin`; the seat had been freed on disconnect). `StartBattle(session)` invites everyone already in the lobby.
3. **`CoopClient`** (no game dependency): handshake, ready, ping, side, spectate, reserve/confirm/release, state view. Used by the fake clients and by the game.
4. **Test decorator** `NetworkConditionTransport` kept; now also models sequenced packets (loss + drop of a packet that arrives after a newer one).

## Verified (tests actually run)
- Core 122 / Networking 43 xUnit tests pass; the whole test run was repeated three times in a row on the final tree without a failure (the 37-test suite before the review fixes was run four times in a row).
- `LobbyScenarioTests` (4 fake clients over real UDP, no game), once on a clean link and once with every endpoint behind the decorator (5 % loss, 100 ms each way): lobby join with a fifth client refused for a wrong mod version → ping/RTT (≥ 150 ms behind the decorator) → ready → battle created → sides → two clients send the request for one lord back to back (exactly one winner, the lord disappears for everyone; this proves the host's arbitration order, not true concurrency — both requests are handled in one `Poll`) → reservation timeout (lord offered again, owner told) → archer slot claimed (the confirm uses `AllowClientConfirm`, the test stand-in for the engine; off in production) and a second claimant refused → the archer holder disconnects (seat freed for others) → same id reconnects (`Resumed`) → defender side has no slot left → spectate → battle end reaches all.
- `TransportChannelTests`: mode preserved over LiteNetLib for all three channels; under 0–100 ms jitter the sequenced channel never delivers an older snapshot after a newer one (some are dropped), plain unreliable does reorder (control) and `SequenceGate` fixes it, reliable stays ordered; loss applies to sequenced packets.
- `LobbyTests` (12 cases), `SequenceGateTests`, `RejoinTests` (see TEST_MATRIX).
- **In game (Spike L1, `run-l1.sh`, 60 fps cap on both windows, `StopGameOnFocusLost` off, run twice, the second time on the final build):** two instances at the main menu; one hosts, the other connects → host log: client 4242 in the lobby, ready, RTT 33 ms; client log: `HelloAck 'lobby'`, ready sent, 24+ pongs, RTT 17–34 ms. "See each other" means: the host lists the client (connected, ready, RTT), the client gets the host's answer and a lobby view with itself; the host's own player is not a lobby member. A1 smoke after switching snapshots to Sequenced (n50:brawl, 170 s, both 60 fps): no tick errors, no packet-size exceptions, 0 stale chunks dropped on loopback, alive sets equal.

## Not verified / limits
- A campaign entity synchronised between two games (Phase 5); Steam backend; real network or second PC; packet loss on bursts; reconnect restoring a seat (by design the seat is freed, the player starts again at `Invited`); lobby cap and `MaxParticipants` are separate settings (4 remote players in the lobby by default; the session cap is configured per battle).
- One unexplained observation: in a raw two-transport test, three packets sent in the same loop iteration in which `IsConnected` first became true never arrived (5 s); with a 200 ms settle they did. The Hello flow (sent from the `Connected` callback) is not affected in any of the 37 tests × 4 runs or in the game runs. Not root-caused.
- The Ping/Pong is not on a separate connection or priority, so a congested reliable queue could still delay them in principle (not seen).

## Review fixes (architect-reviewer, PASS WITH CONDITIONS)
Fixed with regression tests (`LobbyRegressionTests`): H1 the host went dead after `EndBattle` (session never cleared) — now back to a plain lobby, ready flags reset, late reserve/confirm answered `BattleClosed`; H2 players inside their reconnect grace were invited by `StartBattle` and kept a participant place after the grace expired — now skipped, and expiry calls `Session.Disconnect`; H3 an id whose grace ran out during a battle could never come back (`AlreadyInSession`) — now `Rejoin`; M1 a refused peer could slip in with a second Hello during the 200 ms linger — refused connections are ignored; M2 `StartBattle` sent `HelloReject` to an accepted client and left it half-connected — now it is refused and cut, and its lobby place removed.

## Known limits (accepted for now)
- **M3, before Steam or any public use:** identity is the self-reported `Hello.PlayerId` (anyone can take a disconnected player's id during the grace, known, ADR-0012); RTT is self-reported (to be measured on the host); names are not length-limited beyond the wire's 4 KB; no rate limit on Ready/Ping (each Ready toggle makes a lobby broadcast).
- L1: LiteNetLib sequences per channel, not per message kind — a second snapshot stream needs its own channel. L2: the receiver's `SequenceGate` is not reset on a new battle/host by `CoopClient` (A1 creates a gate per mission). L3: `Rejoin` sets the phase directly (the one exception to "Left is terminal"); the lobby cap (4 remote players) is separate from ADR-0007's 4 players including the host, to be reconciled when the real session settings exist.

## Verdict
Phase 4 acceptance ("4 fake clients pass the slot-reservation scenario over the network; 2 games see each other") is met within the limits above. Review by architect-reviewer: see below.

## Review
architect-reviewer, 2026-10-25: **PASS WITH CONDITIONS** (the conditions H1, H2, H3, M1, M2 are fixed above; M3 and L1–L3 are recorded as limits). The reviewer ran both suites once (Networking 37/37, Core 122/122 at that time) and did not re-run Spike L1.
