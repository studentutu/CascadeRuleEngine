#nullable enable

using System;
using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Derived fact: movement request resolved to a candidate durable position.
    /// </summary>
    public readonly struct MoveResolvedFact : IFact, IPrioritizedFact, IEquatable<MoveResolvedFact>
    {
        public static readonly CascadeTypeId CascadeId = CascadeTypeId.FromName(nameof(MoveResolvedFact));

        public MoveResolvedFact(float position, FactPriority priority)
        {
            Position = position;
            Priority = priority;
        }

        public float Position { get; }
        public FactPriority Priority { get; }

        public bool Equals(MoveResolvedFact other)
            => Position.Equals(other.Position) && Priority == other.Priority;

        public override bool Equals(object? obj)
            => obj is MoveResolvedFact other && Equals(other);

        public override int GetHashCode()
            => Position.GetHashCode().CombineHestiaHash((int)Priority);

        public void Dispose()
        {
        }
    }
}
