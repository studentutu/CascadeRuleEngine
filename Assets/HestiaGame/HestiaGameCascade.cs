#nullable enable

using CascadeEngineApi;
using System;
using System.Collections.Generic;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Hestia sample Cascade facade: input facts and project queries only.
    /// Don't forget to execute 'RunTick' to actually use the engine.
    /// </summary>
    public sealed class HestiaGameCascade : CascadeEngine<CascadeReducerContext>
    {
        public HestiaGameCascade(
            int entityCapacity,
            int factCapacity = 128,
            int maxReducerRunsPerTick = 64)
            : base(
                entityCapacity,
                factCapacity,
                maxReducerRunsPerTick,
                CreateReducerContext,
                HestiaGameCascadeSchema.RegisterReducers,
                HestiaGameCascadeSchema.RegisterPropertyCommitters,
                HestiaGameCascadeSchema.RegisterConsumerSubscriptions)
        {
        }

        public int GetAmmo(CascadeEntityId entityId)
            => Entities.Get(entityId).GetCommittedOrDefault<int>(HestiaGameCascadeSchema.Properties.AmmoCurrent);

        public bool IsAmmoEmpty(CascadeEntityId entityId)
            => Entities.Get(entityId).GetCommittedOrDefault<bool>(HestiaGameCascadeSchema.Properties.AmmoEmpty);

        public float GetPosition(CascadeEntityId entityId)
            => Entities.Get(entityId).GetCommittedOrDefault<float>(HestiaGameCascadeSchema.Properties.Position);

        public bool HasEntityFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
            => Entities.Get(entityId).HasFlag(flag);

        public void SetEntityFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
        {
            Entities.Get(entityId).SetFlag(flag);
        }

        public void ClearEntityFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
        {
            Entities.Get(entityId).ClearFlag(flag);
        }

        public void SetEntityFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag, bool enabled)
        {
            Entities.Get(entityId).SetFlag(flag, enabled);
        }

        public void SetInitialAmmo(CascadeEntityId entityId, int ammo)
        {
            var entity = Entities.Get(entityId);
            entity.SetCommitted(HestiaGameCascadeSchema.Properties.AmmoCurrent, CascadeValue.From(ammo));
            entity.SetCommitted(HestiaGameCascadeSchema.Properties.AmmoEmpty, CascadeValue.From(ammo <= 0));
        }

        public void InputFireWeapon(CascadeEntityId entityId)
        {
            EnqueueFact(
                entityId,
                HestiaGameCascadeSchema.Facts.AmmoSpendRequested,
                HestiaGameCascadeSchema.Properties.AmmoCurrent,
                1);
        }

        public void InputMove(CascadeEntityId entityId, float desiredPosition)
        {
            EnqueueFact(
                entityId,
                HestiaGameCascadeSchema.Facts.DesiredPosition,
                HestiaGameCascadeSchema.Properties.Position,
                desiredPosition);
        }

        public void InputFootstepCue(CascadeEntityId entityId, bool isRelevant)
        {
            if (!isRelevant)
            {
                SkipNonRelevant();
                return;
            }

            EnqueueFact(
                entityId,
                HestiaGameCascadeSchema.Facts.FootstepCue,
                HestiaGameCascadeSchema.Properties.PublishedAudioCues,
                CascadeValue.Empty);
        }

        /// <summary>
        /// [INTEGRATION] Range: dirty work from the last tick. Condition: consumers read committed values only. Output: dirty work is dispatched and cleared.
        /// </summary>
        public void DrainDirtyConsumers(IHestiaGameCascadeConsumer consumer)
        {
            if (consumer == null)
            {
                throw new ArgumentNullException(nameof(consumer));
            }

            List<Exception>? failures = null;
            var workCount = DirtyConsumerWorkCount;
            try
            {
                for (var i = 0; i < workCount; i++)
                {
                    var workItem = GetDirtyConsumerWorkItem(i);
                    try
                    {
                        DispatchDirtyConsumer(consumer, workItem);
                    }
                    catch (Exception exception)
                    {
                        failures ??= new List<Exception>();
                        failures.Add(exception);
                    }
                }
            }
            finally
            {
                ClearDirtyConsumers();
            }

            if (failures != null)
            {
                throw new AggregateException("One or more Hestia Cascade consumers failed.", failures);
            }
        }

        private void DispatchDirtyConsumer(
            IHestiaGameCascadeConsumer consumer,
            CascadeConsumerWorkItem workItem)
        {
            // TODO: This is not designed properly, this is a bad design!
            switch (HestiaGameCascadeSchema.Consumers.ResolveRoute(workItem.Consumer))
            {
                case HestiaGameCascadeConsumerRoute.HudAmmoText:
                    consumer.RefreshHudAmmoText(workItem.EntityId, GetAmmo(workItem.EntityId));
                    return;
                case HestiaGameCascadeConsumerRoute.HudAmmoIcon:
                    consumer.RefreshHudAmmoIcon(workItem.EntityId, IsAmmoEmpty(workItem.EntityId));
                    return;
                case HestiaGameCascadeConsumerRoute.CharacterMotor:
                    consumer.RefreshCharacterMotor(workItem.EntityId, GetPosition(workItem.EntityId));
                    return;
                case HestiaGameCascadeConsumerRoute.Replication:
                    consumer.RefreshReplication(workItem.EntityId, GetPosition(workItem.EntityId));
                    return;
                case HestiaGameCascadeConsumerRoute.AudioCue:
                    consumer.PlayAudioCue(workItem.EntityId);
                    return;
                default:
                    throw new InvalidOperationException($"Unknown Hestia dirty consumer '{workItem.Consumer.Name}'.");
            }
        }

        private static CascadeReducerContext CreateReducerContext(
            CascadeEntityStateStore entities,
            CascadeFactBuffer facts,
            CascadeTouchedEntitySet touchedEntities)
            => new CascadeReducerContext(entities, facts, touchedEntities);
    }
}
