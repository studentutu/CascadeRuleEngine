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

        public void Bind(CascadeEntityId entityId)
        {
            EntityId = entityId;
            Entity = _entities.Get(entityId);
        }

        public void DestroyEntity()
        {
            _entities.Destroy(EntityId);
            _touchedEntities.Mark(EntityId);
        }

        public void DestroyEntity(CascadeEntityId entityId)
        {
            _entities.Destroy(entityId);
            _touchedEntities.Mark(entityId);
        }

        public void Produce(CascadeFact fact)
        {
            _facts.Add(fact);
        }

        public void Stage(CascadePropertyKey property, ReducerPayload value, int priority = 0)
        {
            Entity.Stage(property, value, priority);
            if (!Entity.IsDestroyed)
            {
                _touchedEntities.Mark(EntityId);
            }
        }

        public bool StageIfPriorityAtLeast(CascadePropertyKey property, ReducerPayload value, int priority)
        {
            var staged = Entity.StageIfPriorityAtLeast(property, value, priority);
            if (staged)
            {
                _touchedEntities.Mark(EntityId);
            }

            return staged;
        }

        public T GetStagedOrCommittedOrDefault<T>(CascadePropertyKey property)
            => Entity.GetStagedOrCommittedOrDefault<T>(property);
    }
}
