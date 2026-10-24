using System.Collections.Generic;
using System.Linq;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Protocol;
using Xunit;

namespace BannerCoopTW.Core.Tests
{
    public class MessageCodecTests
    {
        private static readonly SlotInfo Slot = new SlotInfo("a-lord", BattleSide.Defender, SlotKind.Officer, "Captain Б", 3, 2);

        public static IEnumerable<object[]> AllMessages()
        {
            yield return new object[] { new Hello { PlayerId = 76561198000000001UL, PlayerName = "Игрок", GameBuild = "1.4.8.119303", Modules = new List<string> { "Native@1.4.8", "BannerCoopTW@0.1.0" } } };
            yield return new object[] { new HelloAck { SessionId = "s-1" } };
            yield return new object[] { new HelloReject { Reason = RejectReason.ModuleMismatch, Detail = "x" } };
            yield return new object[] { new BattleInvite { BattleId = "b1", InitiatorSide = BattleSide.Defender, AllowPvp = true } };
            yield return new object[] { new ChooseSide { Spectate = true, Side = BattleSide.Defender } };
            yield return new object[] { new SlotList { Revision = 7, Slots = new List<SlotInfo> { Slot, Slot } } };
            yield return new object[] { new ReserveRequest { RequestId = 99, SlotId = "a-arch" } };
            yield return new object[] { new ReserveResponse { RequestId = 99, Error = SessionError.SlotUnavailable, ReservationId = 1L << 40, ExpiresInMs = 12000 } };
            yield return new object[] { new ConfirmRequest { ReservationId = 5 } };
            yield return new object[] { new ConfirmResponse { ReservationId = 5, Error = SessionError.ReservationExpired } };
            yield return new object[] { new ReleaseRequest() };
            yield return new object[] { new SlotsChanged { Revision = 8, Slots = new List<SlotInfo> { Slot } } };
            yield return new object[] { new PlayerPhaseChanged { Revision = 9, PlayerId = 3, Phase = PlayerPhase.Controlling, SlotId = "a-off" } };
            yield return new object[] { new BattleEnded { BattleId = "b1" } };
        }

        [Fact]
        public void Every_message_type_has_a_roundtrip_case()
        {
            var covered = AllMessages().Select(a => ((Message)a[0]).Type).OrderBy(t => t);
            Assert.Equal(System.Enum.GetValues(typeof(MessageType)).Cast<MessageType>().OrderBy(t => t), covered);
        }

        [Theory]
        [MemberData(nameof(AllMessages))]
        public void Roundtrip_preserves_type_and_all_fields(Message message)
        {
            var frame = MessageCodec.Encode(message);
            var result = MessageCodec.Decode(frame);
            Assert.True(result.Ok, result.Error.ToString());
            Assert.Equal(message.Type, result.Message!.Type);
            // Re-encoding the decoded message must give identical bytes: proves every field survived.
            Assert.Equal(frame, MessageCodec.Encode(result.Message));
        }

        [Fact]
        public void Slot_info_fields_survive()
        {
            var r = (SlotList)MessageCodec.Decode(MessageCodec.Encode(new SlotList { Revision = 3, Slots = new List<SlotInfo> { Slot } })).Message!;
            var s = r.Slots.Single();
            Assert.Equal((3, "a-lord", BattleSide.Defender, SlotKind.Officer, "Captain Б", 3, 2), (r.Revision, s.Id, s.Side, s.Kind, s.Label, s.Capacity, s.Available));
        }

        [Fact]
        public void Other_protocol_version_is_reported_with_peer_version()
        {
            var r = MessageCodec.Decode(MessageCodec.Encode(new BattleEnded { BattleId = "b" }, 99));
            Assert.Equal(DecodeError.UnsupportedVersion, r.Error);
            Assert.Equal(99, r.PeerVersion);
        }

        [Fact]
        public void Bad_magic_unknown_type_and_trailing_bytes_are_rejected()
        {
            var frame = MessageCodec.Encode(new ReserveRequest { RequestId = 1, SlotId = "x" });

            var badMagic = (byte[])frame.Clone();
            badMagic[0] = 0;
            Assert.Equal(DecodeError.BadMagic, MessageCodec.Decode(badMagic).Error);

            var unknown = (byte[])frame.Clone();
            unknown[3] = 200;
            Assert.Equal(DecodeError.UnknownType, MessageCodec.Decode(unknown).Error);

            var extra = frame.Concat(new byte[] { 0 }).ToArray();
            Assert.False(MessageCodec.Decode(extra).Ok);
        }

        [Fact]
        public void Every_truncation_fails_without_throwing()
        {
            var frame = MessageCodec.Encode(new ReserveResponse { RequestId = 1, ReservationId = 2, ExpiresInMs = 3 });
            for (var n = 0; n < frame.Length; n++)
                Assert.False(MessageCodec.Decode(frame.Take(n).ToArray()).Ok, "length " + n);
        }

        [Fact]
        public void Garbage_never_throws()
        {
            var rng = new System.Random(1234);
            for (var i = 0; i < 2000; i++)
            {
                var buf = new byte[rng.Next(0, 64)];
                rng.NextBytes(buf);
                if (buf.Length > 0 && i % 2 == 0)
                    buf[0] = ProtocolInfo.Magic;
                if (buf.Length > 2 && i % 4 == 0)
                {
                    buf[1] = (byte)ProtocolInfo.Version;
                    buf[2] = 0;
                }

                MessageCodec.Decode(buf);
            }
        }

        [Fact]
        public void Lying_payload_length_is_rejected()
        {
            var frame = MessageCodec.Encode(new BattleEnded { BattleId = "b" });
            frame[7] = 0x7F; // length ~2 GB
            Assert.Equal(DecodeError.BadLength, MessageCodec.Decode(frame).Error);
        }
    }
}
