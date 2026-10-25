using System;
using System.IO;

namespace BannerCoopTW.Core.Protocol
{
    public enum DecodeError : byte
    {
        None = 0,
        Truncated,
        BadMagic,
        /// <summary>The peer speaks another protocol version; <see cref="DecodeResult.PeerVersion"/> says which.</summary>
        UnsupportedVersion,
        UnknownType,
        BadLength,
        Malformed,
    }

    public sealed class DecodeResult
    {
        internal DecodeResult(DecodeError error, Message? message, ushort peerVersion)
        {
            Error = error;
            Message = message;
            PeerVersion = peerVersion;
        }

        public DecodeError Error { get; }

        public Message? Message { get; }

        public ushort PeerVersion { get; }

        public bool Ok => Error == DecodeError.None;
    }

    /// <summary>Frames and parses <see cref="Message"/>s. Never throws on bad input: all failures come back as a <see cref="DecodeError"/>.</summary>
    public static class MessageCodec
    {
        public static byte[] Encode(Message message) => Encode(message, ProtocolInfo.Version);

        /// <summary>Allows tests (and future compatibility shims) to stamp another version.</summary>
        public static byte[] Encode(Message message, ushort version)
        {
            byte[] payload;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                message.Write(w);
                w.Flush();
                payload = ms.ToArray();
            }

            if (payload.Length > ProtocolInfo.MaxPayload)
                throw new InvalidOperationException("Payload too large: " + payload.Length);

            var frame = new byte[ProtocolInfo.HeaderSize + payload.Length];
            frame[0] = ProtocolInfo.Magic;
            frame[1] = (byte)(version & 0xFF);
            frame[2] = (byte)(version >> 8);
            frame[3] = (byte)message.Type;
            var len = payload.Length;
            frame[4] = (byte)len;
            frame[5] = (byte)(len >> 8);
            frame[6] = (byte)(len >> 16);
            frame[7] = (byte)(len >> 24);
            Buffer.BlockCopy(payload, 0, frame, ProtocolInfo.HeaderSize, payload.Length);
            return frame;
        }

        public static DecodeResult Decode(byte[] frame)
        {
            if (frame == null || frame.Length < ProtocolInfo.HeaderSize)
                return new DecodeResult(DecodeError.Truncated, null, 0);
            if (frame[0] != ProtocolInfo.Magic)
                return new DecodeResult(DecodeError.BadMagic, null, 0);

            var version = (ushort)(frame[1] | (frame[2] << 8));
            if (version != ProtocolInfo.Version)
                return new DecodeResult(DecodeError.UnsupportedVersion, null, version);

            var type = (MessageType)frame[3];
            var len = frame[4] | (frame[5] << 8) | (frame[6] << 16) | (frame[7] << 24);
            if (len < 0 || len > ProtocolInfo.MaxPayload)
                return new DecodeResult(DecodeError.BadLength, null, version);
            if (frame.Length - ProtocolInfo.HeaderSize != len)
                return new DecodeResult(frame.Length - ProtocolInfo.HeaderSize < len ? DecodeError.Truncated : DecodeError.BadLength, null, version);

            try
            {
                using (var ms = new MemoryStream(frame, ProtocolInfo.HeaderSize, len))
                using (var r = new BinaryReader(ms))
                {
                    var message = Read(type, r);
                    if (message == null)
                        return new DecodeResult(DecodeError.UnknownType, null, version);
                    if (ms.Position != ms.Length)
                        return new DecodeResult(DecodeError.Malformed, null, version);
                    return new DecodeResult(DecodeError.None, message, version);
                }
            }
            catch (Exception e) when (e is EndOfStreamException || e is ArgumentException || e is IOException || e is FormatException)
            {
                return new DecodeResult(DecodeError.Malformed, null, version);
            }
        }

        private static Message? Read(MessageType type, BinaryReader r)
        {
            switch (type)
            {
                case MessageType.Hello: return Hello.Read(r);
                case MessageType.HelloAck: return HelloAck.Read(r);
                case MessageType.HelloReject: return HelloReject.Read(r);
                case MessageType.BattleInvite: return BattleInvite.Read(r);
                case MessageType.ChooseSide: return ChooseSide.Read(r);
                case MessageType.SlotList: return SlotList.Read(r);
                case MessageType.ReserveRequest: return ReserveRequest.Read(r);
                case MessageType.ReserveResponse: return ReserveResponse.Read(r);
                case MessageType.ConfirmRequest: return ConfirmRequest.Read(r);
                case MessageType.ConfirmResponse: return ConfirmResponse.Read(r);
                case MessageType.ReleaseRequest: return ReleaseRequest.Read(r);
                case MessageType.SlotsChanged: return SlotsChanged.Read(r);
                case MessageType.PlayerPhaseChanged: return PlayerPhaseChanged.Read(r);
                case MessageType.BattleEnded: return BattleEnded.Read(r);
                default: return null;
            }
        }
    }
}
