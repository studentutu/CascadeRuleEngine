#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Hestia sample Cascade facade: owns the schema and engine, exposes input facts and committed-state queries.
    /// Don't forget to execute 'RunTick' to actually use the engine.
    /// </summary>
    public sealed class HestiaGameCascade
    {
        private readonly HestiaGameCascadeSchema _schema;
        private readonly CascadeEngine _engine;

        public HestiaGameCascade(int entityCapacity, int maxReducerRunsPerTick = 64)
        {
            _schema = new HestiaGameCascadeSchema(entityCapacity);
            _engine = new CascadeEngine(_schema.Schema, maxReducerRunsPerTick);
        }

        /// <summary>
        /// Typed keys for consumers that route mutations or read specific properties.
        /// </summary>
        public HestiaGameCascadeSchema Schema => _schema;

        /// <summary>
        /// Underlying engine for generic queries: mutations, counters, flags, destroy.
        /// </summary>
        public CascadeEngine Engine => _engine;

        /// <summary>
        /// [INTEGRATION] Runs one cascade tick: facts reduce, staged values commit, mutations publish.
        /// </summary>
        public void RunTick()
            => _engine.RunTick();

        public int GetAmmo(CascadeEntityId entityId)
            => _engine.ReadCommitted(entityId, _schema.AmmoCurrent);

        public bool IsAmmoEmpty(CascadeEntityId entityId)
            => _engine.ReadCommitted(entityId, _schema.AmmoEmpty);

        public float GetPosition(CascadeEntityId entityId)
            => _engine.ReadCommitted(entityId, _schema.Position);

        public bool HasEntityFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
            => _engine.HasFlag(entityId, flag);

        public void SetEntityFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
            => _engine.SetFlag(entityId, flag);

        public void ClearEntityFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
            => _engine.ClearFlag(entityId, flag);

        public void SetEntityFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag, bool enabled)
            => _engine.SetFlag(entityId, flag, enabled);

        public void SetInitialAmmo(CascadeEntityId entityId, int ammo)
        {
            _engine.SetCommitted(entityId, _schema.AmmoCurrent, ammo);
            _engine.SetCommitted(entityId, _schema.AmmoEmpty, ammo <= 0);
        }

        public void InputFireWeapon(CascadeEntityId entityId)
            => _engine.EnqueueFact(entityId, _schema.AmmoSpendRequested, 1);

        public void InputMove(CascadeEntityId entityId, float desiredPosition, int priority = 0)
            => _engine.EnqueueFact(entityId, _schema.DesiredPosition, new HestiaMoveRequest(desiredPosition, priority));

        public void InputFootstepCue(CascadeEntityId entityId, bool isRelevant)
        {
            if (!isRelevant)
            {
                _engine.SkipNonRelevant();
                return;
            }

            _engine.EnqueueFact(entityId, _schema.FootstepCue);
        }
    }
}