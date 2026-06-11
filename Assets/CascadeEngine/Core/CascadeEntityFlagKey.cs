#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Identifies one entity-local flag and its bit in the entity flag mask.
    /// </summary>
    public readonly struct CascadeEntityFlagKey : IEquatable<CascadeEntityFlagKey>
    {
        public CascadeEntityFlagKey(int index, string name)
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

        public bool Equals(CascadeEntityFlagKey other)
            => Index == other.Index;

        public override bool Equals(object? obj)
            => obj is CascadeEntityFlagKey other && Equals(other);

        public override int GetHashCode()
            => Index;

        public override string ToString()
            => Name;

        public static bool operator ==(CascadeEntityFlagKey left, CascadeEntityFlagKey right)
            => left.Equals(right);

        public static bool operator !=(CascadeEntityFlagKey left, CascadeEntityFlagKey right)
            => !left.Equals(right);
    }
}
