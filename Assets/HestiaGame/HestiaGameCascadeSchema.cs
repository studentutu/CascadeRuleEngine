#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Single explicit schema for the executable Hestia sample cascade.
    /// </summary>
    public static class HestiaGameCascadeSchema
    {
        public static class EntityFlags
        {
            public static readonly CascadeEntityFlagKey AcceptsAmmoInput = new CascadeEntityFlagKey(0, "AcceptsAmmoInput");
        }

        public static class Facts
        {
            public static readonly CascadeFactKey AmmoSpendRequested = new CascadeFactKey(0, "AmmoSpendRequested");
            public static readonly CascadeFactKey DesiredPosition = new CascadeFactKey(1, "DesiredPosition");
            public static readonly CascadeFactKey FootstepCue = new CascadeFactKey(2, "FootstepCue");
            public static readonly CascadeFactKey DryFireCue = new CascadeFactKey(3, "DryFireCue");
        }

        public static class Properties
        {
            public static readonly CascadePropertyKey None = new CascadePropertyKey(0, "None");
            public static readonly CascadePropertyKey AmmoCurrent = new CascadePropertyKey(1, "AmmoCurrent");
            public static readonly CascadePropertyKey AmmoEmpty = new CascadePropertyKey(2, "AmmoEmpty");
            public static readonly CascadePropertyKey Position = new CascadePropertyKey(3, "Position");
            public static readonly CascadePropertyKey PublishedAudioCues = new CascadePropertyKey(4, "PublishedAudioCues");
        }

        internal static void RegisterReducers(CascadeReducerMap<CascadeReducerContext> reducers)
        {
            // Canonical Hestia fact table: FactKind -> reducer function.
            reducers.Register(Facts.AmmoSpendRequested, ReduceAmmoSpendRequested);
            reducers.Register(Facts.DesiredPosition, ReduceDesiredPosition);
            reducers.Register(Facts.FootstepCue, ReduceAudioCue);
            reducers.Register(Facts.DryFireCue, ReduceAudioCue);
        }

        internal static void RegisterPropertyCommitters(CascadePropertyCommitMap committers)
        {
            // Canonical Hestia property table: PropertyKey -> commit function.
            committers.Register(Properties.AmmoCurrent, CommitAmmoCurrent);
            committers.Register(Properties.AmmoEmpty, CommitAmmoEmpty);
            committers.Register(Properties.Position, CommitPosition);
            committers.Register(Properties.PublishedAudioCues, CommitAudioCue);
        }

        /// <summary>
        /// Example of reducer function that generates other facts.
        /// </summary>
        /// <remarks> Reducer function may live outside the schema, this is just an example </remarks>
        private static void ReduceAmmoSpendRequested(CascadeReducerContext context, CascadeFact fact)
        {
            if (!context.Entity.HasFlag(EntityFlags.AcceptsAmmoInput))
            {
                return;
            }

            var amount = fact.Payload.Unwrap<int>();
            var becameEmpty = StageAmmoSpend(context, amount);
            if (!becameEmpty)
            {
                return;
            }

            context.Produce(
                Facts.DryFireCue,
                Properties.PublishedAudioCues,
                CascadeValue.Empty);
        }

        /// <summary>
        /// Example of reducer function that mutate property with data.
        /// </summary>
        /// <remarks> Reducer function may live outside the schema, this is just an example </remarks>
        private static void ReduceDesiredPosition(CascadeReducerContext context, CascadeFact fact)
        {
            var desiredPosition = fact.Payload.Unwrap<float>();
            context.StageIfPriorityAtLeast(Properties.Position, desiredPosition, fact.Priority);
        }

        /// <summary>
        /// Example of reducer function that mutate property as marker.
        /// </summary>
        /// <remarks> Reducer function may live outside the schema, this is just an example </remarks>
        private static void ReduceAudioCue(CascadeReducerContext context, CascadeFact fact)
        {
            context.Stage(Properties.PublishedAudioCues, CascadeValue.Empty);
        }

        /// <summary>
        /// Hestia specific domain helper. 
        /// </summary>
        private static bool StageAmmoSpend(CascadeReducerContext context, int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            var previousEmpty = context.Read<bool>(Properties.AmmoEmpty);
            var baseAmmo = context.Read<int>(Properties.AmmoCurrent);
            var nextAmmo = System.Math.Max(0, baseAmmo - amount);
            var nextEmpty = nextAmmo <= 0;

            context.Stage(Properties.AmmoCurrent, nextAmmo);
            context.Stage(Properties.AmmoEmpty, nextEmpty);

            return nextEmpty && !previousEmpty;
        }

        private static void CommitAmmoCurrent(CascadePropertyCommitContext context)
        {
            context.CommitStagedIfChanged();
        }

        private static void CommitAmmoEmpty(CascadePropertyCommitContext context)
        {
            context.CommitStagedIfChanged();
        }

        private static void CommitPosition(CascadePropertyCommitContext context)
        {
            var previous = context.GetCommittedOrDefault<float>();
            var next = context.GetStaged<float>();
            if (System.Math.Abs(previous - next) < 0.0001f)
            {
                return;
            }

            context.CommitStagedIfChanged();
        }

        private static void CommitAudioCue(CascadePropertyCommitContext context)
        {
            if (!context.CommitStagedIfChanged())
            {
                context.MarkMutated();
            }
        }
    }
}
