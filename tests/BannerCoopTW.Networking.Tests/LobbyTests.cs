using System;
using System.Collections.Generic;
using System.Linq;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Protocol;
using BannerCoopTW.Networking;
using Xunit;

namespace BannerCoopTW.Networking.Tests
{
    /// <summary>Lobby behaviour of the host without a battle: connect gate, ready, ping/RTT, disconnect and the reconnect stub.</summary>
    public class LobbyTests
    {
        [Fact]
        public void Clients_join_and_every_one_sees_all_players()
        {
            using var rig = new LobbyRig();
            var a = rig.Join(11);
            var b = rig.Join(12);
            var c = rig.Join(13);
            rig.Pump(() => rig.Clients.All(x => x.Lobby.Count == 3), "everyone sees 3 players");
            Assert.All(rig.Clients, x => Assert.Equal(CoopClientState.InLobby, x.State));
            Assert.Equal(new ulong[] { 11, 12, 13 }, a.Lobby.Select(p => p.PlayerId).OrderBy(i => i));
            Assert.Equal("lobby", a.SessionId);
            Assert.All(a.Lobby, p => Assert.True(p.Connected));
        }

        [Fact]
        public void Another_mod_version_game_build_or_protocol_is_refused_with_the_reason()
        {
            using var rig = new LobbyRig();
            var mod = rig.AddClient(21, modVersion: "0.0.9");
            var build = rig.AddClient(22, build: "1.4.7.1");
            rig.Pump(() => mod.State == CoopClientState.Rejected && build.State == CoopClientState.Rejected, "both refused");
            Assert.Equal(RejectReason.ModuleMismatch, mod.RejectReason);
            Assert.Equal(RejectReason.GameBuildMismatch, build.RejectReason);
            Assert.Empty(rig.Host.LobbyPlayers);
        }

        [Fact]
        public void Lobby_is_full_after_the_configured_number_of_players()
        {
            using var rig = new LobbyRig(options: new HostOptions { MaxLobbyPlayers = 2 });
            rig.Join(31);
            rig.Join(32);
            var extra = rig.Join(33);
            Assert.Equal(CoopClientState.Rejected, extra.State);
            Assert.Equal(RejectReason.SessionFull, extra.RejectReason);
        }

        [Fact]
        public void Duplicate_id_while_connected_is_refused()
        {
            using var rig = new LobbyRig();
            rig.Join(41);
            var dup = rig.Join(41);
            Assert.Equal(CoopClientState.Rejected, dup.State);
        }

        [Fact]
        public void Ready_flags_are_shared_and_all_ready_needs_everyone()
        {
            using var rig = new LobbyRig();
            var a = rig.Join(51);
            var b = rig.Join(52);
            Assert.False(rig.Host.AllReady);
            a.SetReady(true);
            rig.Pump(() => rig.Host.LobbyPlayers.Any(p => p.PlayerId == 51 && p.Ready), "a ready on host");
            Assert.False(rig.Host.AllReady);
            b.SetReady(true);
            rig.Pump(() => rig.Host.AllReady, "all ready");
            rig.Pump(() => rig.Clients.All(x => x.Lobby.Count == 2 && x.Lobby.All(p => p.Ready)), "clients see the ready flags");
            b.SetReady(false);
            rig.Pump(() => !rig.Host.AllReady, "ready withdrawn");
        }

        [Fact]
        public void Ping_measures_rtt_on_the_client_and_the_host_shows_it_to_everyone()
        {
            using var rig = new LobbyRig(options: new HostOptions { RttBroadcastIntervalMs = 100 });
            var a = rig.Join(61);
            var b = rig.Join(62);
            rig.Pump(() => a.PongsReceived >= 3 && b.PongsReceived >= 3, "pongs");
            Assert.InRange(a.RttMs, 0, 500);
            rig.Pump(() => a.Lobby.Count == 2 && a.Lobby.All(p => p.RttMs >= 0), "host-published RTT visible to a");
        }

        [Fact]
        public void Disconnect_marks_the_player_away_and_the_place_is_kept_for_the_grace_period_then_freed()
        {
            using var rig = new LobbyRig(options: new HostOptions { ReconnectGraceMs = 30_000 });
            var a = rig.Join(71);
            var b = rig.Join(72);
            a.Dispose();
            rig.Pump(() => rig.Host.LobbyPlayers.Any(p => p.PlayerId == 71 && !p.Connected), "host noticed 71 left");
            Assert.Equal(2, rig.Host.LobbyPlayers.Count);
            rig.Pump(() => b.Lobby.Any(p => p.PlayerId == 71 && !p.Connected), "b sees 71 away");

            rig.Clock.Advance(30_001);
            rig.Pump(() => rig.Host.LobbyPlayers.Count == 1, "place expired");
            rig.Pump(() => b.Lobby.Count == 1, "b sees the lobby shrink");
        }

        [Fact]
        public void Reconnect_with_the_same_id_inside_the_grace_period_is_resumed_and_after_it_is_a_new_player()
        {
            using var rig = new LobbyRig(options: new HostOptions { ReconnectGraceMs = 30_000 });
            var a = rig.Join(81);
            a.SetReady(true);
            rig.Pump(() => rig.Host.AllReady, "a ready");
            a.Dispose();
            rig.Pump(() => rig.Host.LobbyPlayers.Any(p => !p.Connected), "left");

            rig.Clock.Advance(10_000);
            var again = rig.Join(81);
            Assert.Equal(CoopClientState.InLobby, again.State);
            Assert.True(again.Resumed);
            Assert.False(rig.Host.AllReady);                           // ready is reset on reconnect
            Assert.Single(rig.Host.LobbyPlayers);

            again.Dispose();
            rig.Pump(() => rig.Host.LobbyPlayers.Any(p => !p.Connected), "left again");
            rig.Clock.Advance(30_001);
            var late = rig.Join(81);
            Assert.Equal(CoopClientState.InLobby, late.State);
            Assert.False(late.Resumed);
        }

        [Fact]
        public void Battle_messages_before_any_battle_drop_the_client()
        {
            using var rig = new LobbyRig();
            var a = rig.Join(91);
            a.ChooseSide(BattleSide.Attacker);
            rig.Pump(() => a.State == CoopClientState.Disconnected, "dropped");
        }

        [Fact]
        public void Starting_a_battle_invites_everyone_already_in_the_lobby()
        {
            using var rig = new LobbyRig();
            var a = rig.Join(101);
            var b = rig.Join(102);
            var catalog = SlotCatalog.Create().AddLord(BattleSide.Attacker, "a-lord1", "L").AddLord(BattleSide.Attacker, "a-lord2", "L2").AddLord(BattleSide.Defender, "d-lord", "D").Build();
            var session = new BattleSession("b1", catalog, rig.Clock, new SessionOptions { MaxParticipants = 5, Epoch = 3 }, new PlayerId(1000), BattleSide.Attacker, "a-lord1");
            rig.Host.StartBattle(session);
            rig.Pump(() => a.State == CoopClientState.InBattle && b.State == CoopClientState.InBattle, "invites");
            Assert.Equal(3u, a.Epoch);
            Assert.Equal(PlayerPhase.Invited, session.GetPhase(new PlayerId(101)));
            Assert.Throws<InvalidOperationException>(() => rig.Host.StartBattle(session));
        }
    }
}
