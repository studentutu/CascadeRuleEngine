#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Identifies the state property targeted by a fact or commit policy.
    /// </summary>
    public readonly struct CascadePropertyKey : IEquatable<CascadePropertyKey>
    {
        public CascadePropertyKey(int index, string name)
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

        public bool Equals(CascadePropertyKey other)
            => Index == other.Index;

        public override bool Equals(object? obj)
            => obj is CascadePropertyKey other && Equals(other);

        public override int GetHashCode()
            => Index;

        public override string ToString()
            => Name;

        public static bool operator ==(CascadePropertyKey left, CascadePropertyKey right)
            => left.Equals(right);

        public static bool operator !=(CascadePropertyKey left, CascadePropertyKey right)
            => !left.Equals(right);
    }
}
