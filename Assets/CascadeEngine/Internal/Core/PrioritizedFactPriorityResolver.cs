#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed bridge for explicitly registered IPrioritizedFact structs without runtime boxing.
    /// </summary>
    internal sealed class PrioritizedFactPriorityResolver<TFact> : IFactPriorityResolver<TFact>
        where TFact : struct, IFact, IPrioritizedFact
    {
        public FactPriority Resolve(in TFact fact)
            => fact.Priority;
    }
}
