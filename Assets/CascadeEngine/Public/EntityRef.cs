#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Generational entity handle owned by the Cascade runtime. Value identifies a recyclable slot;
    /// Generation prevents stale handles from resolving after that slot is reused.
    /// </summary>
    public readonly struct EntityRef : IEquatable<EntityRef>
    {
        /// <summary>
        /// Creates a generation-zero handle. Prefer handles returned by FactSimulation for runtime entities.
        /// </summary>
        public EntityRef(int value)
            : this(value, 0)
        {
        }

        internal EntityRef(int value, uint generation)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Value = value;
            Generation = generation;
        }

        /// <summary>
        /// Recyclable runtime slot id.
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Slot version used to reject stale handles.
        /// </summary>
        public uint Generation { get; }
        internal int StorageIndex => Value;

        public bool Equals(EntityRef other)
            => Value == other.Value && Generation == other.Generation;

        public override bool Equals(object? obj)
            => obj is EntityRef other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Value * 397) ^ (int)Generation;
            }
        }

        public override string ToString()
            => $"{Value}:{Generation}";

        public static bool operator ==(EntityRef left, EntityRef right)
            => left.Equals(right);

        public static bool operator !=(EntityRef left, EntityRef right)
            => !left.Equals(right);
    }
}
