namespace BannerCoopTW.Core.Protocol
{
    /// <summary>
    /// Receiver side of an unreliable-sequenced channel (state snapshots): accepts a packet only if it is newer than the newest one seen,
    /// so late or duplicated packets are dropped instead of rewinding the world. Wrap-around safe (32-bit serial number arithmetic).
    /// Not thread-safe: one gate per stream, used from the thread that polls the transport.
    /// </summary>
    public sealed class SequenceGate
    {
        private uint _newest;
        private bool _any;

        /// <summary>Packets accepted / dropped as old or duplicate.</summary>
        public long Accepted { get; private set; }

        public long Dropped { get; private set; }

        /// <summary>The newest sequence accepted so far (0 before the first packet).</summary>
        public uint Newest => _newest;

        public bool Accept(uint seq)
        {
            if (!_any || unchecked((int)(seq - _newest)) > 0)
            {
                _any = true;
                _newest = seq;
                Accepted++;
                return true;
            }

            Dropped++;
            return false;
        }

        public void Reset()
        {
            _any = false;
            _newest = 0;
        }
    }

    /// <summary>Sender side: hands out increasing sequence numbers for one stream.</summary>
    public sealed class SequenceCounter
    {
        private uint _next;

        public uint Next() => unchecked(++_next);
    }
}
