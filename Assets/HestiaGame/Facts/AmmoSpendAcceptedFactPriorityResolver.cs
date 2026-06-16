#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Explicit queue priority resolver for accepted ammo spend facts.
    /// </summary>
    public sealed class AmmoSpendAcceptedFactPriorityResolver : IFactPriorityResolver<AmmoSpendAcceptedFact>
    {
        public FactPriority Resolve(in AmmoSpendAcceptedFact fact)
            => fact.Priority;
    }
}
