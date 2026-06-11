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

        /// <summary>
        /// Range: positive entity capacity. Condition: engine bootstrap. Output: dense store with preallocated entity states.
        /// </summary>
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

        /// <summary>
        /// Range: entity id inside capacity. Condition: caller needs entity state. Output: mutable entity state slot.
        /// </summary>
        public CascadeEntityState Get(CascadeEntityId entityId)
        {
            if ((uint)entityId.Value >= _entities.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            return _entities[entityId.Value];
        }

        /// <summary>
        /// Range: entity id inside capacity. Condition: fact loop checks live state. Output: true when entity is destroyed.
        /// </summary>
        public bool IsDestroyed(CascadeEntityId entityId)
            => Get(entityId).IsDestroyed;

        /// <summary>
        /// Range: entity id inside capacity. Condition: entity lifetime ends. Output: entity state is destroyed immediately.
        /// </summary>
        public void Destroy(CascadeEntityId entityId)
        {
            Get(entityId).Destroy();
        }

        /// <summary>
        /// [INTEGRATION] Range: touched live entities only. Condition: staged properties have commit functions. Output: committed state and property mutations.
        /// </summary>
        public void CommitTouched(
            CascadeTouchedEntitySet touchedEntities,
            CascadePropertyCommitMap committers,
            CascadePropertyMutationSet mutations)
        {
            ClearDestroyedEntities(touchedEntities);
            ValidateCommitters(touchedEntities, committers);

            var context = new CascadePropertyCommitContext(mutations);
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

        /// <summary>
        /// Range: touched entities from the current tick. Condition: tick finished or failed. Output: staged state is cleared without changing committed state.
        /// </summary>
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

        private void ValidateCommitters(
            CascadeTouchedEntitySet touchedEntities,
            CascadePropertyCommitMap committers)
        {
            for (var i = 0; i < touchedEntities.Count; i++)
            {
                var entity = Get(touchedEntities[i]);
                if (entity.IsDestroyed)
                {
                    continue;
                }

                for (var propertyIndex = 0; propertyIndex < entity.StagedPropertyCount; propertyIndex++)
                {
                    committers.GetRequired(entity.GetStagedProperty(propertyIndex));
                }
            }
        }
    }
}
