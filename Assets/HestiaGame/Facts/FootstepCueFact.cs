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
        public FootstepCueFact(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }

        public bool Equals(FootstepCueFact other)
            => Priority == other.Priority;

        public override bool Equals(object? obj)
            => obj is FootstepCueFact other && Equals(other);

        public override int GetHashCode()
            => Priority;

        public void Dispose()
        {
        }
    }
}
