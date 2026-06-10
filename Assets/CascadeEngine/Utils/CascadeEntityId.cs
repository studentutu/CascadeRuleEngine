#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Identifies one Cascade entity in dense runtime stores.
    /// </summary>
    public readonly struct CascadeEntityId : IEquatable<CascadeEntityId>
    {
        public CascadeEntityId(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(CascadeEntityId other)
            => Value == other.Value;

        public override bool Equals(object? obj)
            => obj is CascadeEntityId other && Equals(other);

        public override int GetHashCode()
            => Value;

        public override string ToString()
            => Value.ToString();

        public static bool operator ==(CascadeEntityId left, CascadeEntityId right)
            => left.Equals(right);

        public static bool operator !=(CascadeEntityId left, CascadeEntityId right)
            => !left.Equals(right);
    }
}
