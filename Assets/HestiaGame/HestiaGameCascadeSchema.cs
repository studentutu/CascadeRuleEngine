#nullable enable

using System;
using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Single declaration point for the Hestia sample cascade: typed properties, flags, and facts bound to their reducers.
    /// </summary>
    public sealed class HestiaGameCascadeSchema
    {
        public HestiaGameCascadeSchema(int entityCapacity)
        {
            Schema = new CascadeSchema(entityCapacity);

            // Entity specific flags.
            AcceptsAmmoInput = Schema.AddFlag("AcceptsAmmoInput");

            // Output state (for consumers).
            AmmoCurrent = Schema.AddProperty<int>("AmmoCurrent");
            AmmoEmpty = Schema.AddProperty<bool>("AmmoEmpty");
            Position = Schema.AddProperty<float>("Position", AreClosePositions);
            PublishedAudioCues = Schema.AddMarkerProperty<CascadeSignal>("PublishedAudioCues");

            // Reducers.
            AmmoSpendRequested = Schema.AddFact<int>("AmmoSpendRequested", ReduceAmmoSpendRequested);
            DesiredPosition = Schema.AddFact<HestiaMoveRequest>("DesiredPosition", ReduceDesiredPosition);
            FootstepCue = Schema.AddFact<CascadeSignal>("FootstepCue", ReduceAudioCue);
            DryFireCue = Schema.AddFact<CascadeSignal>("DryFireCue", ReduceAudioCue);
        }

        /// <summary>
        /// Underlying engine schema. Pass to the CascadeEngine constructor exactly once.
        /// </summary>
        public CascadeSchema Schema { get; }

        public CascadeEntityFlagKey AcceptsAmmoInput { get; }

        public CascadeProperty<int> AmmoCurrent { get; }
        public CascadeProperty<bool> AmmoEmpty { get; }
        public CascadeProperty<float> Position { get; }
        public CascadeProperty<CascadeSignal> PublishedAudioCues { get; }

        public CascadeFact<int> AmmoSpendRequested { get; }
        public CascadeFact<HestiaMoveRequest> DesiredPosition { get; }
        public CascadeFact<CascadeSignal> FootstepCue { get; }
        public CascadeFact<CascadeSignal> DryFireCue { get; }

        /// <summary>
        /// Example of a reducer that derives state and produces a follow-up fact.
        /// </summary>
        /// <remarks> Reducer functions may live outside the schema, this is just an example. </remarks>
        private void ReduceAmmoSpendRequested(CascadeReducerContext context, int amount)
        {
            if (!context.HasFlag(AcceptsAmmoInput))
            {
                return;
            }

            var becameEmpty = StageAmmoSpend(context, amount);
            if (!becameEmpty)
            {
                return;
            }

            context.Produce(DryFireCue);
        }

        /// <summary>
        /// Example of a reducer that resolves same-property conflicts by payload priority.
        /// </summary>
        private void ReduceDesiredPosition(CascadeReducerContext context, HestiaMoveRequest request)
        {
            context.StageIfPriorityAtLeast(Position, request.Position, request.Priority);
        }

        /// <summary>
        /// Example of a reducer that stages a marker property (publishes a mutation even when the value is unchanged).
        /// </summary>
        private void ReduceAudioCue(CascadeReducerContext context, CascadeSignal signal)
        {
            context.Stage(PublishedAudioCues, signal);
        }

        /// <summary>
        /// Hestia specific domain helper. Range: positive amount. Condition: ammo spend. Output: true when ammo just hit empty.
        /// </summary>
        private bool StageAmmoSpend(CascadeReducerContext context, int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            var previousEmpty = context.Read(AmmoEmpty);
            var baseAmmo = context.Read(AmmoCurrent);
            var nextAmmo = Math.Max(0, baseAmmo - amount);
            var nextEmpty = nextAmmo <= 0;

            context.Stage(AmmoCurrent, nextAmmo);
            context.Stage(AmmoEmpty, nextEmpty);

            return nextEmpty && !previousEmpty;
        }

        /// <summary>
        /// Position change policy: commits only when the value moved beyond the epsilon.
        /// </summary>
        private static bool AreClosePositions(float previous, float next)
            => Math.Abs(previous - next) < 0.0001f;
    }
}