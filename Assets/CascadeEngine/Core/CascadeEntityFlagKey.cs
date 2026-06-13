#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Schema-declared entity flag with an auto-assigned bit index (0-511). Create only via <see cref="CascadeSchema.AddFlag"/>.
    /// </summary>
    public readonly struct CascadeEntityFlagKey : IEquatable<CascadeEntityFlagKey>
    {
        internal CascadeEntityFlagKey(int index, string name)
        {
            Index = index;
            Name = name;
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
            => Name ?? string.Empty;

        public static bool operator ==(CascadeEntityFlagKey left, CascadeEntityFlagKey right)
            => left.Equals(right);

        public static bool operator !=(CascadeEntityFlagKey left, CascadeEntityFlagKey right)
            => !left.Equals(right);
    }
}