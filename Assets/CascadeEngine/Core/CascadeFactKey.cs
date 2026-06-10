#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Identifies the kind of fact without exposing enum-indexed control flow.
    /// </summary>
    public readonly struct CascadeFactKey : IEquatable<CascadeFactKey>
    {
        public CascadeFactKey(int index, string name)
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

        public bool Equals(CascadeFactKey other)
            => Index == other.Index;

        public override bool Equals(object? obj)
            => obj is CascadeFactKey other && Equals(other);

        public override int GetHashCode()
            => Index;

        public override string ToString()
            => Name;

        public static bool operator ==(CascadeFactKey left, CascadeFactKey right)
            => left.Equals(right);

        public static bool operator !=(CascadeFactKey left, CascadeFactKey right)
            => !left.Equals(right);
    }
}
