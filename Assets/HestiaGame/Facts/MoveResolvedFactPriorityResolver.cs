#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Explicit queue priority resolver for resolved movement facts.
    /// </summary>
    public sealed class MoveResolvedFactPriorityResolver : IFactPriorityResolver<MoveResolvedFact>
    {
        public FactPriority Resolve(in MoveResolvedFact fact)
            => fact.Priority;
    }
}
