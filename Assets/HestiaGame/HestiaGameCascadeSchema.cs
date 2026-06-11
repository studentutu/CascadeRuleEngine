#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Single explicit schema for the executable Hestia sample cascade.
    /// </summary>
    public static class HestiaGameCascadeSchema
    {
        public static class Consumers
        {
            public static readonly CascadeConsumerKey HudAmmoText = new CascadeConsumerKey(0, "HudAmmoText");
            public static readonly CascadeConsumerKey HudAmmoIcon = new CascadeConsumerKey(1, "HudAmmoIcon");
            public static readonly CascadeConsumerKey CharacterMotor = new CascadeConsumerKey(2, "CharacterMotor");
            public static readonly CascadeConsumerKey AudioCue = new CascadeConsumerKey(3, "AudioCue");

            private static readonly HestiaGameCascadeConsumerRoute[] RoutesByConsumerIndex =
            {
                HestiaGameCascadeConsumerRoute.HudAmmoText,
                HestiaGameCascadeConsumerRoute.HudAmmoIcon,
                HestiaGameCascadeConsumerRoute.CharacterMotor,
                HestiaGameCascadeConsumerRoute.AudioCue
            };

            internal static HestiaGameCascadeConsumerRoute ResolveRoute(CascadeConsumerKey consumer)
            {
                if ((uint)consumer.Index >= RoutesByConsumerIndex.Length)
                {
                    throw new System.InvalidOperationException($"Unknown Hestia dirty consumer '{consumer.Name}'.");
                }

                return RoutesByConsumerIndex[consumer.Index];
            }
        }

        public static class EntityFlags
        {
            public static readonly CascadeEntityFlagKey AcceptsAmmoInput = new CascadeEntityFlagKey(0, "AcceptsAmmoInput");
            public static readonly CascadeEntityFlagKey PublishesHudAmmo = new CascadeEntityFlagKey(1, "PublishesHudAmmo");
            public static readonly CascadeEntityFlagKey PublishesCharacterMotor = new CascadeEntityFlagKey(2, "PublishesCharacterMotor");
            public static readonly CascadeEntityFlagKey PublishesAudioCues = new CascadeEntityFlagKey(3, "PublishesAudioCues");
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
            // Canonical Hestia property table: PropertyKey -> commit function -> exact consumers.
            // TODO: we should be able to register multiple consumers on the same property!
            // TODO: Conceptual question: do we even need commit phase/stage/complexity? All we really needed Input -> Fact-> Reduction Loop -> Notify Consumer.
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
            if (context.PublishStagedIfChanged() && context.Entity.HasFlag(EntityFlags.PublishesHudAmmo))
            {
                context.MarkDirty(Consumers.HudAmmoText);
            }
        }

        private static void CommitAmmoEmpty(CascadePropertyCommitContext context)
        {
            if (context.PublishStagedIfChanged() && context.Entity.HasFlag(EntityFlags.PublishesHudAmmo))
            {
                context.MarkDirty(Consumers.HudAmmoIcon);
            }
        }

        private static void CommitPosition(CascadePropertyCommitContext context)
        {
            var previous = context.GetCommittedOrDefault<float>();
            var next = context.GetStaged<float>();
            if (System.Math.Abs(previous - next) < 0.0001f)
            {
                return;
            }

            context.PublishStagedIfChanged();
            if (context.Entity.HasFlag(EntityFlags.PublishesCharacterMotor))
            {
                context.MarkDirty(Consumers.CharacterMotor);
            }
        }

        private static void CommitAudioCue(CascadePropertyCommitContext context)
        {
            if (context.Entity.HasFlag(EntityFlags.PublishesAudioCues))
            {
                context.MarkDirty(Consumers.AudioCue);
            }
        }
    }
}
