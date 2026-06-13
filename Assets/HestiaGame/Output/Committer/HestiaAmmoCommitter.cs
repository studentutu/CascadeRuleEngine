#nullable enable

using CascadeEngineApi;
using UnityEngine;

namespace Hestia
{
    /// <summary>
    /// Folds all accepted ammo spend facts into one durable ammo state write.
    /// </summary>
    public sealed class HestiaAmmoCommitter : IOutputCommitter<HestiaAmmoState>
    {
        public CommitDecision<HestiaAmmoState> Commit(
            ICommitContext ctx,
            EntityRef entity,
            in Optional<HestiaAmmoState> previous)
        {
            if (!previous.HasValue)
            {
                return CommitDecision<HestiaAmmoState>.Unchanged();
            }

            var spends = ctx.Facts(entity).All<AmmoSpendAcceptedFact>();
            if (spends.Length == 0)
            {
                return CommitDecision<HestiaAmmoState>.Unchanged();
            }

            var totalSpend = 0;
            for (var i = 0; i < spends.Length; i++)
            {
                totalSpend += Mathf.Max(0, spends[i].Amount);
            }

            if (totalSpend <= 0)
            {
                return CommitDecision<HestiaAmmoState>.Unchanged();
            }

            var old = previous.Value;
            var next = new HestiaAmmoState(Mathf.Max(0, old.Current - totalSpend));
            return next.Equals(old)
                ? CommitDecision<HestiaAmmoState>.Unchanged()
                : CommitDecision<HestiaAmmoState>.Set(next);
        }
    }
}
