#nullable enable

using System;
using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Input fact: relevant entity should publish a footstep cue.
    /// </summary>
    public readonly struct FootstepCueFact : IFact, IEquatable<FootstepCueFact>
    {
        public FootstepCueFact(FactPriority priority)
        {
            Priority = priority;
        }

        public FactPriority Priority { get; }

        public bool Equals(FootstepCueFact other)
            => Priority == other.Priority;

        public override bool Equals(object? obj)
            => obj is FootstepCueFact other && Equals(other);

        public override int GetHashCode()
            => (int)Priority;

        public void Dispose()
        {
        }
    }
}
