#nullable enable

using System;
using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Input fact: gameplay requested a position change.
    /// </summary>
    public readonly struct MoveRequestedFact : IFact, IEquatable<MoveRequestedFact>
    {
        public MoveRequestedFact(float position)
        {
            Position = position;
        }

        public float Position { get; }

        public bool Equals(MoveRequestedFact other)
            => Position.Equals(other.Position);

        public override bool Equals(object? obj)
            => obj is MoveRequestedFact other && Equals(other);

        public override int GetHashCode()
            => Position.GetHashCode();

        public void Dispose()
        {
        }
    }
}
