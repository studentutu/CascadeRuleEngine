#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Reducer-facing API bound to the entity of the current fact: typed reads, typed staging, fact production, and destroy.
    /// </summary>
    public sealed class CascadeReducerContext
    {
        private readonly CascadeEngine _engine;

        internal CascadeReducerContext(CascadeEngine engine)
        {
            _engine = engine;
        }

        /// <summary>
        /// Entity targeted by the fact currently being reduced.
        /// </summary>
        public CascadeEntityId EntityId { get; private set; }

        /// <summary>
        /// Range: live entity from the fact loop. Condition: engine dispatches a fact. Output: context bound to that entity.
        /// </summary>
        internal void Bind(CascadeEntityId entityId)
        {
            EntityId = entityId;
        }

        /// <summary>
        /// Range: bound entity. Condition: query, no side effects. Output: staged-this-tick value if present, otherwise committed value.
        /// </summary>
        public T Read<T>(CascadeProperty<T> property)
            => Read(EntityId, property);

        /// <summary>
        /// Range: any entity in capacity. Condition: cross-entity query, no side effects. Output: staged-this-tick value if present, otherwise committed value.
        /// </summary>
        public T Read<T>(CascadeEntityId entityId, CascadeProperty<T> property)
        {
            _engine.ValidateOwnership(property);
            _engine.ValidateEntity(entityId);
            return property.ReadStagedOrCommitted(entityId);
        }

        /// <summary>
        /// Range: bound entity. Condition: query, no side effects. Output: true when the flag is set.
        /// </summary>
        public bool HasFlag(CascadeEntityFlagKey flag)
            => _engine.HasFlag(EntityId, flag);

        /// <summary>
        /// Range: any entity in capacity. Condition: cross-entity query, no side effects. Output: true when the flag is set.
        /// </summary>
        public bool HasFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
            => _engine.HasFlag(entityId, flag);

        /// <summary>
        /// Range: bound live entity. Condition: reducer stages a candidate value. Output: value staged for commit; ignored when the entity is destroyed.
        /// </summary>
        public void Stage<T>(CascadeProperty<T> property, T value)
        {
            _engine.ValidateOwnership(property);
            if (_engine.IsDestroyed(EntityId))
            {
                return;
            }

            property.Stage(EntityId, value, priority: 0);
            _engine.MarkTouched(EntityId);
        }

        /// <summary>
        /// Range: bound live entity. Condition: same-property conflict resolution; equal priority overwrites. Output: true when the value became the staged candidate.
        /// </summary>
        public bool StageIfPriorityAtLeast<T>(CascadeProperty<T> property, T value, int priority)
        {
            _engine.ValidateOwnership(property);
            if (_engine.IsDestroyed(EntityId))
            {
                return false;
            }

            if (!property.StageIfPriorityAtLeast(EntityId, value, priority))
            {
                return false;
            }

            _engine.MarkTouched(EntityId);
            return true;
        }

        /// <summary>
        /// Range: bound entity. Condition: reducer derives follow-up work. Output: typed fact queued for this tick.
        /// </summary>
        public void Produce<TPayload>(CascadeFact<TPayload> fact, TPayload payload)
            => _engine.EnqueueFact(EntityId, fact, payload);

        /// <summary>
        /// Range: any entity in capacity. Condition: reducer derives follow-up work for another entity. Output: typed fact queued for this tick.
        /// </summary>
        public void Produce<TPayload>(CascadeEntityId entityId, CascadeFact<TPayload> fact, TPayload payload)
            => _engine.EnqueueFact(entityId, fact, payload);

        /// <summary>
        /// Range: bound entity. Condition: payload-less follow-up work. Output: signal fact queued for this tick.
        /// </summary>
        public void Produce(CascadeFact<CascadeSignal> fact)
            => _engine.EnqueueFact(EntityId, fact, default);

        /// <summary>
        /// Range: bound entity. Condition: entity lifetime ends mid-tick. Output: entity destroyed immediately; its staged work is dropped at commit.
        /// </summary>
        public void DestroyEntity()
            => _engine.DestroyEntity(EntityId);

        /// <summary>
        /// Range: any entity in capacity. Condition: entity lifetime ends mid-tick. Output: entity destroyed immediately; its staged work is dropped at commit.
        /// </summary>
        public void DestroyEntity(CascadeEntityId entityId)
            => _engine.DestroyEntity(entityId);
    }
}