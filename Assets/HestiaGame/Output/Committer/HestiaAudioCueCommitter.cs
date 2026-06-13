#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Publishes one marker-like audio cue output from relevant facts.
    /// </summary>
    public sealed class HestiaAudioCueCommitter : IOutputCommitter<HestiaAudioCueState>
    {
        public CommitDecision<HestiaAudioCueState> Commit(
            ICommitContext ctx,
            EntityRef entity,
            in Optional<HestiaAudioCueState> previous)
        {
            var cue = HestiaAudioCueKind.None;
            if (ctx.Facts(entity).Has<FootstepCueFact>())
            {
                cue = HestiaAudioCueKind.Footstep;
            }

            if (ShouldDryFire(ctx, entity))
            {
                cue = HestiaAudioCueKind.DryFire;
            }

            if (cue == HestiaAudioCueKind.None)
            {
                return CommitDecision<HestiaAudioCueState>.Unchanged();
            }

            var nextVersion = previous.HasValue ? previous.Value.Version + 1 : 1;
            return CommitDecision<HestiaAudioCueState>.Set(new HestiaAudioCueState(cue, nextVersion));
        }

        private static bool ShouldDryFire(ICommitContext ctx, EntityRef entity)
        {
            if (!ctx.TryGetState<HestiaAmmoState>(entity, out var ammo) || ammo.IsEmpty)
            {
                return false;
            }

            var spends = ctx.Facts(entity).All<AmmoSpendAcceptedFact>();
            var totalSpend = 0;
            for (var i = 0; i < spends.Length; i++)
            {
                totalSpend += spends[i].Amount;
            }

            return totalSpend >= ammo.Current;
        }
    }
}
