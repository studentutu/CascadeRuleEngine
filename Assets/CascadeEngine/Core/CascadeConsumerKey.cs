#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Identifies one consumer and its bit in the dirty consumer mask.
    /// </summary>
    public readonly struct CascadeConsumerKey : IEquatable<CascadeConsumerKey>
    {
        public CascadeConsumerKey(int index, string name)
        {
            if (index < 0 || index >= Bitmask512.BitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Index = index;
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public int Index { get; }
        public string Name { get; }

        public bool Equals(CascadeConsumerKey other)
            => Index == other.Index;

        public override bool Equals(object? obj)
            => obj is CascadeConsumerKey other && Equals(other);

        public override int GetHashCode()
            => Index;

        public override string ToString()
            => Name;

        public static bool operator ==(CascadeConsumerKey left, CascadeConsumerKey right)
            => left.Equals(right);

        public static bool operator !=(CascadeConsumerKey left, CascadeConsumerKey right)
            => !left.Equals(right);
    }
}
