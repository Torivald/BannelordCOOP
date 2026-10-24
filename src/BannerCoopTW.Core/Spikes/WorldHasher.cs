using System;
using System.Text;

namespace BannerCoopTW.Core.Spikes
{
    /// <summary>
    /// Order-sensitive 64-bit FNV-1a accumulator used by the S1 world-checksum spike (ADR-0010 §2).
    /// Not cryptographic: it only has to expose divergence between two instances of the same save.
    /// </summary>
    public sealed class WorldHasher
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        private ulong _state = Offset;

        public ulong Value => _state;

        public long Count { get; private set; }

        public WorldHasher Add(uint value)
        {
            for (var i = 0; i < 4; i++)
                Step((byte)(value >> (i * 8)));
            Count++;
            return this;
        }

        public WorldHasher Add(int value) => Add(unchecked((uint)value));

        public WorldHasher Add(string? value)
        {
            // Length prefix keeps ("ab","c") distinct from ("a","bc").
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            Add((uint)bytes.Length);
            foreach (var b in bytes)
                Step(b);
            return this;
        }

        public string ToHex() => _state.ToString("x16");

        private void Step(byte b)
        {
            _state ^= b;
            _state *= Prime;
        }
    }
}
