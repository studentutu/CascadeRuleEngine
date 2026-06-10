#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Dense core store for Cascade entity state.
    /// </summary>
    public sealed class CascadeEntityStateStore
    {
        private readonly CascadeEntityState[] _entities;

        public CascadeEntityStateStore(int entityCapacity)
        {
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            _entities = new CascadeEntityState[entityCapacity];
            for (var i = 0; i < _entities.Length; i++)
            {
                _entities[i] = new CascadeEntityState();
            }
        }

        public CascadeEntityState Get(CascadeEntityId entityId)
        {
            if ((uint)entityId.Value >= _entities.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            return _entities[entityId.Value];
        }

        public bool IsDestroyed(CascadeEntityId entityId)
            => Get(entityId).IsDestroyed;

        public void Destroy(CascadeEntityId entityId)
        {
            Get(entityId).Destroy();
        }

        /// <summary>
        /// [INTEGRATION] Range: touched live entities only. Condition: staged properties have commit functions. Output: committed state and dirty consumers.
        /// </summary>
        public void CommitTouched(
            CascadeTouchedEntitySet touchedEntities,
            CascadePropertyCommitMap committers,
            CascadeDirtyConsumerSet dirtyConsumers)
        {
            ClearDestroyedEntities(touchedEntities);

            var context = new CascadePropertyCommitContext(dirtyConsumers);
            for (var i = 0; i < touchedEntities.Count; i++)
            {
                var entityId = touchedEntities[i];
                var entity = Get(entityId);
                if (entity.IsDestroyed)
                {
                    continue;
                }

                for (var propertyIndex = 0; propertyIndex < entity.StagedPropertyCount; propertyIndex++)
                {
                    var property = entity.GetStagedProperty(propertyIndex);
                    var committer = committers.GetRequired(property);
                    context.Bind(entityId, entity, property);
                    committer(context);
                }
            }
        }

        public void ClearTouched(CascadeTouchedEntitySet touchedEntities)
        {
            for (var i = 0; i < touchedEntities.Count; i++)
            {
                Get(touchedEntities[i]).ClearStage();
            }
        }

        private void ClearDestroyedEntities(CascadeTouchedEntitySet touchedEntities)
        {
            for (var i = 0; i < touchedEntities.Count; i++)
            {
                var entity = Get(touchedEntities[i]);
                if (entity.IsDestroyed)
                {
                    entity.ClearStage();
                }
            }
        }
    }
}
