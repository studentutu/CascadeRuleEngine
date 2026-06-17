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
            Reduce<AmmoSpendRequestedFact>()
                .With<HestiaAmmoSpendRequestReducer>();

            Reduce<MoveRequestedFact>()
                .With<HestiaMoveRequestReducer>();

            Ammo = Output<HestiaAmmoState>("Ammo")
                .AffectedBy<AmmoSpendAcceptedFact>()
                .ConflictPolicy(CommitConflictPolicy.FoldAll)
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
