#nullable enable

using System;
using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Durable sample position output state.
    /// </summary>
    public readonly struct HestiaPositionState : IOutputState, IEquatable<HestiaPositionState>
    {
        public HestiaPositionState(float position)
        {
            Position = position;
        }

        public float Position { get; }

        public bool Equals(HestiaPositionState other)
            => Position.Equals(other.Position);

        public override bool Equals(object? obj)
            => obj is HestiaPositionState other && Equals(other);

        public override int GetHashCode()
            => Position.GetHashCode();
    }
}
