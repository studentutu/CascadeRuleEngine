#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Stable entity handle owned by the Cascade runtime. Destroyed ids are not reused by the MVP implementation.
    /// </summary>
    public readonly struct EntityRef : IEquatable<EntityRef>
    {
        public static readonly EntityRef Global = new EntityRef(-1, true);

        public EntityRef(int value)
            : this(value, false)
        {
        }

        private EntityRef(int value, bool allowGlobal)
        {
            if (!allowGlobal && value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Value = value;
        }

        public int Value { get; }
        public bool IsGlobal => Value < 0;

        public bool Equals(EntityRef other)
            => Value == other.Value;

        public override bool Equals(object? obj)
            => obj is EntityRef other && Equals(other);

        public override int GetHashCode()
            => Value;

        public override string ToString()
            => IsGlobal ? "Global" : Value.ToString();

        public static bool operator ==(EntityRef left, EntityRef right)
            => left.Equals(right);

        public static bool operator !=(EntityRef left, EntityRef right)
            => !left.Equals(right);
    }
}
