#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Converts valid ammo spend requests into accepted facts. Durable ammo changes happen only in HestiaAmmoCommitter.
    /// </summary>
    public sealed class HestiaAmmoSpendRequestReducer : IFactReducer<AmmoSpendRequestedFact>
    {
        public void Reduce(IReduceContext ctx, EntityRef entity, in AmmoSpendRequestedFact fact)
        {
            if (fact.Amount <= 0)
            {
                return;
            }

            if (!ctx.HasState<HestiaAmmoState>(entity))
            {
                return;
            }

            ctx.Emit(entity, new AmmoSpendAcceptedFact(fact.Amount, fact.Priority));
        }
    }
}
