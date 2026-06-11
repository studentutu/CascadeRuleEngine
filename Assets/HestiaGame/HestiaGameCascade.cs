#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Hestia sample Cascade facade: input facts and project queries only.
    /// Don't forget to execute 'RunTick' to actuly use the engine.
    /// </summary>
    public sealed class HestiaGameCascade : CascadeEngine<HestiaGameCascadeReducerContext>
    {
        public HestiaGameCascade(int entityCapacity, int factCapacity = 128, int maxReducerRunsPerTick = 64)
            : base(
                entityCapacity,
                factCapacity,
                maxReducerRunsPerTick,
                CreateReducerContext,
                HestiaGameCascadeSchema.RegisterReducers,
                HestiaGameCascadeSchema.RegisterPropertyCommitters)
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
            entity.SetCommitted(HestiaGameCascadeSchema.Properties.AmmoCurrent, ReducerPayload.From(ammo));
            entity.SetCommitted(HestiaGameCascadeSchema.Properties.AmmoEmpty, ReducerPayload.From(ammo <= 0));
        }

        public void InputFireWeapon(CascadeEntityId entityId)
        {
            AddFact(new CascadeFact(
                entityId,
                HestiaGameCascadeSchema.Facts.AmmoSpendRequested,
                HestiaGameCascadeSchema.Properties.AmmoCurrent,
                ReducerPayload.From(1)));
        }

        public void InputMove(CascadeEntityId entityId, float desiredPosition)
        {
            AddFact(new CascadeFact(
                entityId,
                HestiaGameCascadeSchema.Facts.DesiredPosition,
                HestiaGameCascadeSchema.Properties.Position,
                ReducerPayload.From(desiredPosition)));
        }

        public void InputFootstepCue(CascadeEntityId entityId, bool isRelevant)
        {
            if (!isRelevant)
            {
                SkipNonRelevant();
                return;
            }

            AddFact(new CascadeFact(
                entityId,
                HestiaGameCascadeSchema.Facts.FootstepCue,
                HestiaGameCascadeSchema.Properties.PublishedAudioCues,
                ReducerPayload.Empty));
        }

        private static HestiaGameCascadeReducerContext CreateReducerContext(
            CascadeEntityStateStore entities,
            CascadeFactBuffer facts,
            CascadeTouchedEntitySet touchedEntities)
            => new HestiaGameCascadeReducerContext(entities, facts, touchedEntities);
    }
}
