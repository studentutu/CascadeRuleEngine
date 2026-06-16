#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Explicit queue priority resolver for movement requests.
    /// </summary>
    public sealed class MoveRequestedFactPriorityResolver : IFactPriorityResolver<MoveRequestedFact>
    {
        public FactPriority Resolve(in MoveRequestedFact fact)
            => fact.Priority;
    }
}
