#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Core reducer context: current entity, fact production, and staged property writes.
    /// </summary>
    public class CascadeReducerContext
    {
        private readonly CascadeEntityStateStore _entities;
        private readonly CascadeFactBuffer _facts;
        private readonly CascadeTouchedEntitySet _touchedEntities;

        public CascadeReducerContext(
            CascadeEntityStateStore entities,
            CascadeFactBuffer facts,
            CascadeTouchedEntitySet touchedEntities)
        {
            _entities = entities;
            _facts = facts;
            _touchedEntities = touchedEntities;
        }

        public CascadeEntityId EntityId { get; private set; }
        public CascadeEntityState Entity { get; private set; } = null!;

        /// <summary>
        /// Range: core runner only. Condition: before one reducer invocation. Output: context points at the fact target entity.
        /// </summary>
        internal void Bind(CascadeEntityId entityId)
        {
            EntityId = entityId;
            Entity = _entities.Get(entityId);
        }

        /// <summary>
        /// Range: currently bound entity. Condition: reducer decides the entity is no longer live. Output: entity state is cleared and touched for cleanup.
        /// </summary>
        public void DestroyEntity()
        {
            _entities.Destroy(EntityId);
            _touchedEntities.Mark(EntityId);
        }

        /// <summary>
        /// Range: any entity inside the store. Condition: reducer owns a valid destruction decision. Output: entity state is cleared and touched for cleanup.
        /// </summary>
        public void DestroyEntity(CascadeEntityId entityId)
        {
            _entities.Destroy(entityId);
            _touchedEntities.Mark(entityId);
        }

        /// <summary>
        /// Range: current tick fact queue. Condition: reducer derives a follow-up fact. Output: fact is appended for later reduction in the same tick.
        /// </summary>
        public void Produce(CascadeFact fact)
        {
            _facts.Add(fact);
        }

        /// <summary>
        /// Range: current tick fact queue. Condition: reducer derives a fact for a specific entity. Output: fact is appended; no state is staged or committed.
        /// </summary>
        public void Produce(
            CascadeEntityId entityId,
            CascadeFactKey factKey,
            CascadePropertyKey target,
            CascadeValue payload,
            int priority = 0)
        {
            Produce(new CascadeFact(entityId, factKey, target, payload, priority));
        }

        /// <summary>
        /// Range: currently bound entity. Condition: reducer derives a follow-up fact for the same entity. Output: fact is appended; reducers run later by queue order.
        /// </summary>
        public void Produce(
            CascadeFactKey factKey,
            CascadePropertyKey target,
            CascadeValue payload,
            int priority = 0)
        {
            Produce(EntityId, factKey, target, payload, priority);
        }

        /// <summary>
        /// Range: current tick fact queue. Condition: reducer derives typed fact payload. Output: payload is wrapped and appended as a fact.
        /// </summary>
        public void Produce<T>(
            CascadeEntityId entityId,
            CascadeFactKey factKey,
            CascadePropertyKey target,
            T payload,
            int priority = 0)
        {
            Produce(entityId, factKey, target, CascadeValue.From(payload), priority);
        }

        /// <summary>
        /// Range: currently bound live entity. Condition: reducer has a candidate property value. Output: value is staged and entity is marked touched for commit.
        /// </summary>
        public void Stage(CascadePropertyKey property, CascadeValue value, int priority = 0)
        {
            Entity.Stage(property, value, priority);
            if (!Entity.IsDestroyed)
            {
                _touchedEntities.Mark(EntityId);
            }
        }

        /// <summary>
        /// Range: currently bound live entity. Condition: reducer has a typed candidate property value. Output: value is wrapped, staged, and entity is marked touched.
        /// </summary>
        public void Stage<T>(CascadePropertyKey property, T value, int priority = 0)
        {
            Stage(property, CascadeValue.From(value), priority);
        }

        /// <summary>
        /// Range: currently bound live entity. Condition: reducer resolves same-property conflicts by priority. Output: stages only when priority is not lower.
        /// </summary>
        public bool StageIfPriorityAtLeast(CascadePropertyKey property, CascadeValue value, int priority)
        {
            var staged = Entity.StageIfPriorityAtLeast(property, value, priority);
            if (staged)
            {
                _touchedEntities.Mark(EntityId);
            }

            return staged;
        }

        /// <summary>
        /// Range: currently bound live entity. Condition: reducer resolves typed same-property conflicts by priority. Output: wraps and stages only when accepted.
        /// </summary>
        public bool StageIfPriorityAtLeast<T>(CascadePropertyKey property, T value, int priority)
            => StageIfPriorityAtLeast(property, CascadeValue.From(value), priority);

        /// <summary>
        /// Range: currently bound entity. Condition: reducer needs current working value. Output: staged value wins, otherwise committed value or default.
        /// </summary>
        public T GetStagedOrCommittedOrDefault<T>(CascadePropertyKey property)
            => Entity.GetStagedOrCommittedOrDefault<T>(property);

        /// <summary>
        /// Range: currently bound entity. Condition: reducer needs a query with no side effects. Output: staged value wins, otherwise committed value or default.
        /// </summary>
        public T Read<T>(CascadePropertyKey property)
            => GetStagedOrCommittedOrDefault<T>(property);
    }
}
