#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Deduplicated set of entities touched during one tick.
    /// </summary>
    public sealed class CascadeTouchedEntitySet
    {
        private readonly bool[] _flags;
        private readonly CascadeEntityId[] _items;

        public CascadeTouchedEntitySet(int entityCapacity)
        {
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            _flags = new bool[entityCapacity];
            _items = new CascadeEntityId[entityCapacity];
        }

        public int Count { get; private set; }

        public CascadeEntityId this[int index]
        {
            get
            {
                if ((uint)index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _items[index];
            }
        }

        /// <summary>
        /// Range: entity id inside set capacity. Condition: first touch per tick. Output: entity appears once in commit order.
        /// </summary>
        public void Mark(CascadeEntityId entityId)
        {
            ValidateEntityId(entityId);

            if (_flags[entityId.Value])
            {
                return;
            }

            _flags[entityId.Value] = true;
            _items[Count] = entityId;
            Count++;
        }

        public void Clear()
        {
            for (var i = 0; i < Count; i++)
            {
                _flags[_items[i].Value] = false;
            }

            Count = 0;
        }

        private void ValidateEntityId(CascadeEntityId entityId)
        {
            if ((uint)entityId.Value >= _flags.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }
        }
    }
}
