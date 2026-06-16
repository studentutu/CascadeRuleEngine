#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Explicit queue priority resolver for ammo spend requests.
    /// </summary>
    public sealed class AmmoSpendRequestedFactPriorityResolver : IFactPriorityResolver<AmmoSpendRequestedFact>
    {
        public FactPriority Resolve(in AmmoSpendRequestedFact fact)
            => fact.Priority;
    }
}
