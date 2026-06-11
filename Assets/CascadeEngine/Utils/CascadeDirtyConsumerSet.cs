#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Entity-scoped dirty consumer work tracked as an unbounded deduplicated queue.
    /// </summary>
    public sealed class CascadeDirtyConsumerSet
    {
        private readonly Dictionary<int, HashSet<int>> _consumerIndexesByEntity = new Dictionary<int, HashSet<int>>();
        private readonly HashSet<int> _aggregateConsumerIndexes = new HashSet<int>();
        private readonly HashSet<int> _dirtyEntityIndexes = new HashSet<int>();
        private readonly HashSet<long> _workItemKeys = new HashSet<long>();
        private readonly List<CascadeEntityId> _dirtyEntities = new List<CascadeEntityId>();
        private readonly List<CascadeConsumerWorkItem> _workItems = new List<CascadeConsumerWorkItem>();
        private readonly int _entityCapacity;

        public CascadeDirtyConsumerSet(int entityCapacity = Bitmask512.BitCount)
        {
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            _entityCapacity = entityCapacity;
        }

        /// <summary>
        /// Range: dirty work from the current or last tick. Condition: aggregate consumer check. Output: true if any entity dirtied the consumer.
        /// </summary>
        public bool Contains(CascadeConsumerKey consumer)
            => _aggregateConsumerIndexes.Contains(consumer.Index);

        /// <summary>
        /// Range: dirty work from the current or last tick. Condition: exact entity-consumer check. Output: true if that pair is dirty.
        /// </summary>
        public bool Contains(CascadeEntityId entityId, CascadeConsumerKey consumer)
        {
            ValidateEntityId(entityId);

            return _consumerIndexesByEntity.TryGetValue(entityId.Value, out var consumerIndexes) &&
                   consumerIndexes.Contains(consumer.Index);
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
        /// Range: live entity id and non-negative consumer index. Condition: first pair per tick. Output: one dirty consumer work item.
        /// </summary>
        public void Mark(CascadeConsumerKey consumer, CascadeEntityId entityId)
        {
            ValidateEntityId(entityId);

            if (!_workItemKeys.Add(CreateWorkItemKey(entityId, consumer)))
            {
                return;
            }

            if (!_consumerIndexesByEntity.TryGetValue(entityId.Value, out var consumerIndexes))
            {
                consumerIndexes = new HashSet<int>();
                _consumerIndexesByEntity.Add(entityId.Value, consumerIndexes);
            }

            consumerIndexes.Add(consumer.Index);

            if (_dirtyEntityIndexes.Add(entityId.Value))
            {
                _dirtyEntities.Add(entityId);
            }

            _aggregateConsumerIndexes.Add(consumer.Index);
            _workItems.Add(new CascadeConsumerWorkItem(entityId, consumer));
        }

        /// <summary>
        /// Range: all dirty consumer work. Condition: beginning of tick or after consumer drain. Output: all dirty masks and work counts cleared.
        /// </summary>
        public void Clear()
        {
            _consumerIndexesByEntity.Clear();
            _aggregateConsumerIndexes.Clear();
            _dirtyEntityIndexes.Clear();
            _workItemKeys.Clear();
            _dirtyEntities.Clear();
            _workItems.Clear();
        }

        /// <summary>
        /// Count of exact entity-consumer dirty work items.
        /// </summary>
        public int Count
            => _workItems.Count;

        /// <summary>
        /// Count of unique entities with at least one dirty consumer.
        /// </summary>
        public int EntityCount
            => _dirtyEntities.Count;

        private void ValidateEntityId(CascadeEntityId entityId)
        {
            if ((uint)entityId.Value >= _entityCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }
        }

        private static long CreateWorkItemKey(CascadeEntityId entityId, CascadeConsumerKey consumer)
            => ((long)entityId.Value << 32) ^ (uint)consumer.Index;
    }
}
