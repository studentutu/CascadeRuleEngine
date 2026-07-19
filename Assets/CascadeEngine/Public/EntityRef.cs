#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Stable entity handle owned by the Cascade runtime. Public ids are never reused; internal storage slots may be recycled.
    /// </summary>
    public readonly struct EntityRef : IEquatable<EntityRef>
    {
        public EntityRef(int value)
            : this(value, value)
        {
        }

        internal EntityRef(int value, int storageIndex)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (storageIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(storageIndex));
            }

            Value = value;
            StorageIndex = storageIndex;
        }

        public int Value { get; }
        internal int StorageIndex { get; }

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
