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

        internal static void RegisterReducers(CascadeReducerMap<HestiaGameCascadeReducerContext> reducers)
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
            committers.Register(Properties.AmmoCurrent, CommitAmmoCurrent);
            committers.Register(Properties.AmmoEmpty, CommitAmmoEmpty);
            committers.Register(Properties.Position, CommitPosition);
            committers.Register(Properties.PublishedAudioCues, CommitAudioCue);
        }

        private static void ReduceAmmoSpendRequested(HestiaGameCascadeReducerContext context, CascadeFact fact)
        {
            var amount = fact.Payload.Unwrap<int>();
            var becameEmpty = context.StageAmmoSpend(amount);
            if (!becameEmpty)
            {
                return;
            }

            context.Produce(new CascadeFact(
                context.EntityId,
                Facts.DryFireCue,
                Properties.PublishedAudioCues,
                ReducerPayload.Empty));
        }

        private static void ReduceDesiredPosition(HestiaGameCascadeReducerContext context, CascadeFact fact)
        {
            var desiredPosition = fact.Payload.Unwrap<float>();
            context.StagePosition(desiredPosition, fact.Priority);
        }

        private static void ReduceAudioCue(HestiaGameCascadeReducerContext context, CascadeFact fact)
        {
            context.StageAudioCue();
        }

        private static void CommitAmmoCurrent(CascadePropertyCommitContext context)
        {
            if (context.PublishStagedIfChanged())
            {
                context.MarkDirty(Consumers.HudAmmoText);
            }
        }

        private static void CommitAmmoEmpty(CascadePropertyCommitContext context)
        {
            if (context.PublishStagedIfChanged())
            {
                context.MarkDirty(Consumers.HudAmmoIcon);
            }
        }

        private static void CommitPosition(CascadePropertyCommitContext context)
        {
            var previous = context.Entity.GetCommittedOrDefault<float>(context.Property);
            var next = context.GetStaged<float>();
            if (System.Math.Abs(previous - next) < 0.0001f)
            {
                return;
            }

            context.PublishStagedIfChanged();
            context.MarkDirty(Consumers.CharacterMotor);
        }

        private static void CommitAudioCue(CascadePropertyCommitContext context)
        {
            context.MarkDirty(Consumers.AudioCue);
        }
    }
}
