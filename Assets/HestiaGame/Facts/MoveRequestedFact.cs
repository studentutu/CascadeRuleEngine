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
        public MoveRequestedFact(float position, int priority)
        {
            Position = position;
            Priority = priority;
        }

        public float Position { get; }
        public int Priority { get; }

        public bool Equals(MoveRequestedFact other)
            => Position.Equals(other.Position) && Priority == other.Priority;

        public override bool Equals(object? obj)
            => obj is MoveRequestedFact other && Equals(other);

        public override int GetHashCode()
            => Position.GetHashCode().CombineHestiaHash(Priority);

        public void Dispose()
        {
        }
    }
}
