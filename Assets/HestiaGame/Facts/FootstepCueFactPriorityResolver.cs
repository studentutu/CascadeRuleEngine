#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Explicit queue priority resolver for footstep cue facts.
    /// </summary>
    public sealed class FootstepCueFactPriorityResolver : IFactPriorityResolver<FootstepCueFact>
    {
        public FactPriority Resolve(in FootstepCueFact fact)
            => fact.Priority;
    }
}
