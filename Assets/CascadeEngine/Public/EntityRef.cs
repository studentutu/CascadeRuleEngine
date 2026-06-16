#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Stable entity handle owned by the Cascade runtime. Destroyed ids are not reused by the MVP implementation.
    /// </summary>
    public readonly struct EntityRef : IEquatable<EntityRef>
    {
        public EntityRef(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(EntityRef other)
            => Value == other.Value;

        public override bool Equals(object? obj)
            => obj is EntityRef other && Equals(other);

        public override int GetHashCode()
            => Value;

        public override string ToString()
            => Value.ToString();

        public static bool operator ==(EntityRef left, EntityRef right)
            => left.Equals(right);

        public static bool operator !=(EntityRef left, EntityRef right)
            => !left.Equals(right);
    }
}
