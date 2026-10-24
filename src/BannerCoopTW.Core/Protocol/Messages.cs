using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BannerCoopTW.Core.Battle;

namespace BannerCoopTW.Core.Protocol
{
    /// <summary>Wire protocol constants. Frame: magic(1) version(2 LE) type(1) payloadLength(4 LE) payload.</summary>
    public static class ProtocolInfo
    {
        public const byte Magic = 0xBC;

        /// <summary>Current protocol version. Bump on any incompatible change; peers must be equal in version 1 (no negotiation yet).</summary>
        public const ushort Version = 1;

        public const int HeaderSize = 8;

        public const int MaxPayload = 1 << 20;
    }

    public enum MessageType : byte
    {
        Hello = 1,
        HelloAck = 2,
        HelloReject = 3,
        BattleInvite = 10,
        ChooseSide = 11,
        SlotList = 12,
        ReserveRequest = 13,
        ReserveResponse = 14,
        ConfirmRequest = 15,
        ConfirmResponse = 16,
        ReleaseRequest = 17,
        SlotsChanged = 18,
        PlayerPhaseChanged = 19,
        BattleEnded = 20,
    }

    public enum RejectReason : byte
    {
        ProtocolMismatch = 1,
        GameBuildMismatch = 2,
        ModuleMismatch = 3,
        SessionFull = 4,
    }

    /// <summary>One slot as sent to clients.</summary>
    public sealed class SlotInfo
    {
        public SlotInfo(string id, BattleSide side, SlotKind kind, string label, int capacity, int available)
        {
            Id = id;
            Side = side;
            Kind = kind;
            Label = label;
            Capacity = capacity;
            Available = available;
        }

        public string Id { get; }

        public BattleSide Side { get; }

        public SlotKind Kind { get; }

        public string Label { get; }

        public int Capacity { get; }

        public int Available { get; }

        public static SlotInfo From(SlotView v) => new SlotInfo(v.Id, v.Side, v.Kind, v.Label, v.Capacity, v.Available);
    }

    public abstract class Message
    {
        public abstract MessageType Type { get; }

        internal abstract void Write(BinaryWriter w);
    }

    public sealed class Hello : Message
    {
        public ushort ProtocolVersion { get; set; } = ProtocolInfo.Version;

        public ulong PlayerId { get; set; }

        public string PlayerName { get; set; } = "";

        public string GameBuild { get; set; } = "";

        /// <summary>"ModuleId@version" entries.</summary>
        public List<string> Modules { get; set; } = new List<string>();

        public override MessageType Type => MessageType.Hello;

        internal override void Write(BinaryWriter w)
        {
            w.Write(ProtocolVersion);
            w.Write(PlayerId);
            WireText.Write(w, PlayerName);
            WireText.Write(w, GameBuild);
            WireText.WriteList(w, Modules);
        }

        internal static Hello Read(BinaryReader r) => new Hello
        {
            ProtocolVersion = r.ReadUInt16(),
            PlayerId = r.ReadUInt64(),
            PlayerName = WireText.Read(r),
            GameBuild = WireText.Read(r),
            Modules = WireText.ReadList(r),
        };
    }

    public sealed class HelloAck : Message
    {
        public ushort ProtocolVersion { get; set; } = ProtocolInfo.Version;

        public string SessionId { get; set; } = "";

        public override MessageType Type => MessageType.HelloAck;

        internal override void Write(BinaryWriter w)
        {
            w.Write(ProtocolVersion);
            WireText.Write(w, SessionId);
        }

        internal static HelloAck Read(BinaryReader r) => new HelloAck { ProtocolVersion = r.ReadUInt16(), SessionId = WireText.Read(r) };
    }

    public sealed class HelloReject : Message
    {
        public RejectReason Reason { get; set; }

        public string Detail { get; set; } = "";

        public override MessageType Type => MessageType.HelloReject;

        internal override void Write(BinaryWriter w)
        {
            w.Write((byte)Reason);
            WireText.Write(w, Detail);
        }

        internal static HelloReject Read(BinaryReader r) => new HelloReject { Reason = (RejectReason)r.ReadByte(), Detail = WireText.Read(r) };
    }

    public sealed class BattleInvite : Message
    {
        public string BattleId { get; set; } = "";

        public BattleSide InitiatorSide { get; set; }

        public bool AllowPvp { get; set; }

        public override MessageType Type => MessageType.BattleInvite;

        internal override void Write(BinaryWriter w)
        {
            WireText.Write(w, BattleId);
            w.Write((byte)InitiatorSide);
            w.Write(AllowPvp);
        }

        internal static BattleInvite Read(BinaryReader r) => new BattleInvite { BattleId = WireText.Read(r), InitiatorSide = (BattleSide)r.ReadByte(), AllowPvp = r.ReadBoolean() };
    }

    /// <summary>Client picks a side, or spectates (<see cref="Spectate"/> = true).</summary>
    public sealed class ChooseSide : Message
    {
        public bool Spectate { get; set; }

        public BattleSide Side { get; set; }

        public override MessageType Type => MessageType.ChooseSide;

        internal override void Write(BinaryWriter w)
        {
            w.Write(Spectate);
            w.Write((byte)Side);
        }

        internal static ChooseSide Read(BinaryReader r) => new ChooseSide { Spectate = r.ReadBoolean(), Side = (BattleSide)r.ReadByte() };
    }

    public sealed class SlotList : Message
    {
        public int Revision { get; set; }

        public List<SlotInfo> Slots { get; set; } = new List<SlotInfo>();

        public override MessageType Type => MessageType.SlotList;

        internal override void Write(BinaryWriter w)
        {
            w.Write(Revision);
            WireSlots.Write(w, Slots);
        }

        internal static SlotList Read(BinaryReader r) => new SlotList { Revision = r.ReadInt32(), Slots = WireSlots.Read(r) };
    }

    public sealed class ReserveRequest : Message
    {
        public uint RequestId { get; set; }

        public string SlotId { get; set; } = "";

        public override MessageType Type => MessageType.ReserveRequest;

        internal override void Write(BinaryWriter w)
        {
            w.Write(RequestId);
            WireText.Write(w, SlotId);
        }

        internal static ReserveRequest Read(BinaryReader r) => new ReserveRequest { RequestId = r.ReadUInt32(), SlotId = WireText.Read(r) };
    }

    public sealed class ReserveResponse : Message
    {
        public uint RequestId { get; set; }

        public SessionError Error { get; set; }

        public long ReservationId { get; set; }

        /// <summary>Remaining lifetime of the reservation, milliseconds.</summary>
        public int ExpiresInMs { get; set; }

        public override MessageType Type => MessageType.ReserveResponse;

        internal override void Write(BinaryWriter w)
        {
            w.Write(RequestId);
            w.Write((byte)Error);
            w.Write(ReservationId);
            w.Write(ExpiresInMs);
        }

        internal static ReserveResponse Read(BinaryReader r) => new ReserveResponse
        {
            RequestId = r.ReadUInt32(),
            Error = (SessionError)r.ReadByte(),
            ReservationId = r.ReadInt64(),
            ExpiresInMs = r.ReadInt32(),
        };
    }

    public sealed class ConfirmRequest : Message
    {
        public long ReservationId { get; set; }

        public override MessageType Type => MessageType.ConfirmRequest;

        internal override void Write(BinaryWriter w) => w.Write(ReservationId);

        internal static ConfirmRequest Read(BinaryReader r) => new ConfirmRequest { ReservationId = r.ReadInt64() };
    }

    public sealed class ConfirmResponse : Message
    {
        public long ReservationId { get; set; }

        public SessionError Error { get; set; }

        public override MessageType Type => MessageType.ConfirmResponse;

        internal override void Write(BinaryWriter w)
        {
            w.Write(ReservationId);
            w.Write((byte)Error);
        }

        internal static ConfirmResponse Read(BinaryReader r) => new ConfirmResponse { ReservationId = r.ReadInt64(), Error = (SessionError)r.ReadByte() };
    }

    public sealed class ReleaseRequest : Message
    {
        public override MessageType Type => MessageType.ReleaseRequest;

        internal override void Write(BinaryWriter w)
        {
        }

        internal static ReleaseRequest Read(BinaryReader r) => new ReleaseRequest();
    }

    /// <summary>Delta: only slots whose availability changed (Available = 0 means gone).</summary>
    public sealed class SlotsChanged : Message
    {
        public int Revision { get; set; }

        public List<SlotInfo> Slots { get; set; } = new List<SlotInfo>();

        public override MessageType Type => MessageType.SlotsChanged;

        internal override void Write(BinaryWriter w)
        {
            w.Write(Revision);
            WireSlots.Write(w, Slots);
        }

        internal static SlotsChanged Read(BinaryReader r) => new SlotsChanged { Revision = r.ReadInt32(), Slots = WireSlots.Read(r) };
    }

    public sealed class PlayerPhaseChanged : Message
    {
        public int Revision { get; set; }

        public ulong PlayerId { get; set; }

        public PlayerPhase Phase { get; set; }

        public string SlotId { get; set; } = "";

        public override MessageType Type => MessageType.PlayerPhaseChanged;

        internal override void Write(BinaryWriter w)
        {
            w.Write(Revision);
            w.Write(PlayerId);
            w.Write((byte)Phase);
            WireText.Write(w, SlotId);
        }

        internal static PlayerPhaseChanged Read(BinaryReader r) => new PlayerPhaseChanged
        {
            Revision = r.ReadInt32(),
            PlayerId = r.ReadUInt64(),
            Phase = (PlayerPhase)r.ReadByte(),
            SlotId = WireText.Read(r),
        };
    }

    public sealed class BattleEnded : Message
    {
        public string BattleId { get; set; } = "";

        public override MessageType Type => MessageType.BattleEnded;

        internal override void Write(BinaryWriter w) => WireText.Write(w, BattleId);

        internal static BattleEnded Read(BinaryReader r) => new BattleEnded { BattleId = WireText.Read(r) };
    }

    internal static class WireText
    {
        private const int MaxLength = 4096;
        private const int MaxItems = 1024;

        public static void Write(BinaryWriter w, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            if (bytes.Length > MaxLength)
                throw new ArgumentException("String too long for the wire (" + bytes.Length + ").");
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }

        public static string Read(BinaryReader r)
        {
            int n = r.ReadUInt16();
            var bytes = r.ReadBytes(n);
            if (bytes.Length != n)
                throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        public static void WriteList(BinaryWriter w, List<string> items)
        {
            if (items.Count > MaxItems)
                throw new ArgumentException("List too long for the wire.");
            w.Write((ushort)items.Count);
            foreach (var i in items)
                Write(w, i);
        }

        public static List<string> ReadList(BinaryReader r)
        {
            int n = r.ReadUInt16();
            var list = new List<string>(Math.Min(n, 64));
            for (var i = 0; i < n; i++)
                list.Add(Read(r));
            return list;
        }
    }

    internal static class WireSlots
    {
        public static void Write(BinaryWriter w, List<SlotInfo> slots)
        {
            w.Write((ushort)slots.Count);
            foreach (var s in slots)
            {
                WireText.Write(w, s.Id);
                w.Write((byte)s.Side);
                w.Write((byte)s.Kind);
                WireText.Write(w, s.Label);
                w.Write((ushort)s.Capacity);
                w.Write((ushort)s.Available);
            }
        }

        public static List<SlotInfo> Read(BinaryReader r)
        {
            int n = r.ReadUInt16();
            var list = new List<SlotInfo>(Math.Min(n, 256));
            for (var i = 0; i < n; i++)
            {
                var id = WireText.Read(r);
                var side = (BattleSide)r.ReadByte();
                var kind = (SlotKind)r.ReadByte();
                var label = WireText.Read(r);
                list.Add(new SlotInfo(id, side, kind, label, r.ReadUInt16(), r.ReadUInt16()));
            }

            return list;
        }
    }
}
