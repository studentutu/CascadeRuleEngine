#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Hestia reducer context over the core Cascade reducer context.
    /// </summary>
    public sealed class HestiaGameCascadeReducerContext : CascadeReducerContext
    {
        public HestiaGameCascadeReducerContext(
            CascadeEntityStateStore entities,
            CascadeFactBuffer facts,
            CascadeTouchedEntitySet touchedEntities)
            : base(entities, facts, touchedEntities)
        {
        }

        public bool StageAmmoSpend(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            var previousEmpty = GetStagedOrCommittedOrDefault<bool>(HestiaGameCascadeSchema.Properties.AmmoEmpty);
            var baseAmmo = GetStagedOrCommittedOrDefault<int>(HestiaGameCascadeSchema.Properties.AmmoCurrent);
            var nextAmmo = System.Math.Max(0, baseAmmo - amount);
            var nextEmpty = nextAmmo <= 0;

            Stage(HestiaGameCascadeSchema.Properties.AmmoCurrent, ReducerPayload.From(nextAmmo));
            Stage(HestiaGameCascadeSchema.Properties.AmmoEmpty, ReducerPayload.From(nextEmpty));

            return nextEmpty && !previousEmpty;
        }

        public void StagePosition(float value, int priority)
        {
            StageIfPriorityAtLeast(HestiaGameCascadeSchema.Properties.Position, ReducerPayload.From(value), priority);
        }

        public void StageAudioCue()
        {
            Stage(HestiaGameCascadeSchema.Properties.PublishedAudioCues, ReducerPayload.Empty);
        }
    }
}
