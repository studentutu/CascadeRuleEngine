#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Input fact: relevant entity should publish a footstep cue.
    /// </summary>
    public readonly struct FootstepCueFact : IFact<FootstepCueFact>
    {
        public bool Equals(FootstepCueFact other)
            => true;
        public void Dispose() { }
    }
}
