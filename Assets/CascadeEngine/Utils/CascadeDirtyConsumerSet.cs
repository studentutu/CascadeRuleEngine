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
        private Bitmask512 _aggregateConsumers;

        public CascadeDirtyConsumerSet(int entityCapacity = Bitmask512.BitCount)
        {
            if (entityCapacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            _consumersByEntity = new Bitmask512[entityCapacity];
            _dirtyEntityFlags = new bool[entityCapacity];
            _dirtyEntities = new CascadeEntityId[entityCapacity];
        }

        public bool Contains(CascadeConsumerKey consumer)
            => _aggregateConsumers.IsSet(consumer.Index);

        public bool Contains(CascadeEntityId entityId, CascadeConsumerKey consumer)
        {
            ValidateEntityId(entityId);

            return _consumersByEntity[entityId.Value].IsSet(consumer.Index);
        }

        public CascadeEntityId GetEntity(int index)
        {
            if ((uint)index >= EntityCount)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }

            return _dirtyEntities[index];
        }

        /// <summary>
        /// Range: live entity id and consumer index 0-511. Condition: first pair per tick. Output: one dirty consumer work item.
        /// </summary>
        public void Mark(CascadeConsumerKey consumer, CascadeEntityId entityId)
        {
            ValidateEntityId(entityId);

            if (!_consumersByEntity[entityId.Value].Set(consumer.Index))
            {
                return;
            }

            if (!_dirtyEntityFlags[entityId.Value])
            {
                _dirtyEntityFlags[entityId.Value] = true;
                _dirtyEntities[EntityCount] = entityId;
                EntityCount++;
            }

            _aggregateConsumers.SetDirty(consumer.Index);
            Count++;
        }

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

        public int Count { get; private set; }

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
