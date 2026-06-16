#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Hestia gameplay (simulation).
    /// </summary>
    public sealed class HestiaGameSimulationFeature : FactFeature
    {
        public HestiaGameSimulationFeature()
        {
            Priority<AmmoSpendRequestedFact>()
                .With<AmmoSpendRequestedFactPriorityResolver>();

            Priority<AmmoSpendAcceptedFact>()
                .With<AmmoSpendAcceptedFactPriorityResolver>();

            Priority<MoveRequestedFact>()
                .With<MoveRequestedFactPriorityResolver>();

            Priority<MoveResolvedFact>()
                .With<MoveResolvedFactPriorityResolver>();

            Priority<FootstepCueFact>()
                .With<FootstepCueFactPriorityResolver>();

            Reduce<AmmoSpendRequestedFact>()
                .With<HestiaAmmoSpendRequestReducer>();

            Reduce<MoveRequestedFact>()
                .With<HestiaMoveRequestReducer>();

            Ammo = Output<HestiaAmmoState>("Ammo")
                .AffectedBy<AmmoSpendAcceptedFact>()
                .ConflictPolicy(CommitConflictPolicy.FoldAllInStableFactOrder)
                .CommitWith<HestiaAmmoCommitter>();

            Position = Output<HestiaPositionState>("Position")
                .AffectedBy<MoveResolvedFact>()
                .ConflictPolicy(CommitConflictPolicy.PriorityWinnerOrThrowOnTie)
                .CommitWith<HestiaPositionCommitter>();

            AudioCue = Output<HestiaAudioCueState>("AudioCue")
                .AffectedBy<FootstepCueFact>()
                .AffectedBy<AmmoSpendAcceptedFact>()
                .ConflictPolicy(CommitConflictPolicy.CollapseToSingleMarker)
                .CommitWith<HestiaAudioCueCommitter>();
        }

        public OutputState<HestiaAmmoState> Ammo { get; }
        public OutputState<HestiaPositionState> Position { get; }
        public OutputState<HestiaAudioCueState> AudioCue { get; }
    }
}
