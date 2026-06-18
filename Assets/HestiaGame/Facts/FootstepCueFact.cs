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
        public bool Equals(FootstepCueFact other)
            => true;

        public override bool Equals(object? obj)
            => obj is FootstepCueFact other && Equals(other);

        public override int GetHashCode()
            => 0;

        public void Dispose()
        {
        }
    }
}
