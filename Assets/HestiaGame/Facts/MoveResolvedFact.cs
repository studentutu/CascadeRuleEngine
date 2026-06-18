#nullable enable

using System;
using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Derived fact: movement request resolved to a candidate durable position.
    /// </summary>
    public readonly struct MoveResolvedFact : IFact, IEquatable<MoveResolvedFact>
    {
        public MoveResolvedFact(float position)
        {
            Position = position;
        }

        public float Position { get; }

        public bool Equals(MoveResolvedFact other)
            => Position.Equals(other.Position);

        public override bool Equals(object? obj)
            => obj is MoveResolvedFact other && Equals(other);

        public override int GetHashCode()
            => Position.GetHashCode();

        public void Dispose()
        {
        }
    }
}
