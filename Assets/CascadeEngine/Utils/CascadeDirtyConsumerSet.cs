#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Entity-scoped dirty consumer work tracked as per-entity consumer masks.
    /// </summary>
    public sealed class CascadeDirtyConsumerSet
    {
        private readonly Bitmask512[] _consumersByEntity;
        private readonly bool[] _dirtyEntityFlags;
        private readonly CascadeEntityId[] _dirtyEntities;
        private readonly CascadeConsumerWorkItem[] _workItems;
        private Bitmask512 _aggregateConsumers;

        public CascadeDirtyConsumerSet(
            int entityCapacity = Bitmask512.BitCount,
            int workCapacity = Bitmask512.BitCount)
        {
            if (entityCapacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            if (workCapacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(workCapacity));
            }

            _consumersByEntity = new Bitmask512[entityCapacity];
            _dirtyEntityFlags = new bool[entityCapacity];
            _dirtyEntities = new CascadeEntityId[entityCapacity];
            _workItems = new CascadeConsumerWorkItem[workCapacity];
        }

        /// <summary>
        /// Range: dirty work from the current or last tick. Condition: aggregate consumer check. Output: true if any entity dirtied the consumer.
        /// </summary>
        public bool Contains(CascadeConsumerKey consumer)
            => _aggregateConsumers.IsSet(consumer.Index);

        /// <summary>
        /// Range: dirty work from the current or last tick. Condition: exact entity-consumer check. Output: true if that pair is dirty.
        /// </summary>
        public bool Contains(CascadeEntityId entityId, CascadeConsumerKey consumer)
        {
            ValidateEntityId(entityId);

            return _consumersByEntity[entityId.Value].IsSet(consumer.Index);
        }

        /// <summary>
        /// Range: dirty entity index. Condition: caller scans entity-level dirty work. Output: entity with at least one dirty consumer.
        /// </summary>
        public CascadeEntityId GetEntity(int index)
        {
            if ((uint)index >= EntityCount)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }

            return _dirtyEntities[index];
        }

        /// <summary>
        /// Range: dirty work item index. Condition: caller drains exact consumer work. Output: entity-consumer work item in mark order.
        /// </summary>
        public CascadeConsumerWorkItem GetWorkItem(int index)
        {
            if ((uint)index >= Count)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }

            return _workItems[index];
        }

        /// <summary>
        /// Range: live entity id and consumer index 0-511. Condition: first pair per tick. Output: one dirty consumer work item.
        /// </summary>
        public void Mark(CascadeConsumerKey consumer, CascadeEntityId entityId)
        {
            ValidateEntityId(entityId);

            if (_consumersByEntity[entityId.Value].IsSet(consumer.Index))
            {
                return;
            }

            if (Count >= _workItems.Length)
            {
                throw new System.InvalidOperationException("Dirty consumer work capacity exceeded.");
            }

            _consumersByEntity[entityId.Value].SetDirty(consumer.Index);

            if (!_dirtyEntityFlags[entityId.Value])
            {
                _dirtyEntityFlags[entityId.Value] = true;
                _dirtyEntities[EntityCount] = entityId;
                EntityCount++;
            }

            _aggregateConsumers.SetDirty(consumer.Index);
            _workItems[Count] = new CascadeConsumerWorkItem(entityId, consumer);
            Count++;
        }

        /// <summary>
        /// Range: all dirty consumer work. Condition: beginning of tick or after consumer drain. Output: all dirty masks and work counts cleared.
        /// </summary>
        public void Clear()
        {
            for (var i = 0; i < EntityCount; i++)
            {
                var entityId = _dirtyEntities[i].Value;
                _dirtyEntityFlags[entityId] = false;
                _consumersByEntity[entityId].ClearAll();
            }

            _aggregateConsumers.ClearAll();
            EntityCount = 0;
            Count = 0;
        }

        /// <summary>
        /// Count of exact entity-consumer dirty work items.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Count of unique entities with at least one dirty consumer.
        /// </summary>
        public int EntityCount { get; private set; }

        private void ValidateEntityId(CascadeEntityId entityId)
        {
            if ((uint)entityId.Value >= _consumersByEntity.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(entityId));
            }
        }
    }
}
