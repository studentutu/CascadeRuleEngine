#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Reusable entity buffer with explicit capacity ownership for query and batch surfaces.
    /// </summary>
    internal sealed class EntityRefBuffer
    {
        private EntityRef[] _items;

        internal EntityRefBuffer(int initialCapacity)
        {
            _items = new EntityRef[NormalizeCapacity(initialCapacity)];
        }

        internal int Capacity => _items.Length;

        internal EntityRef this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        internal void EnsureCapacity(int required)
        {
            if (required <= _items.Length)
            {
                return;
            }

            Array.Resize(ref _items, required);
        }

        internal ReadOnlySpan<EntityRef> AsSpan(int count)
            => new ReadOnlySpan<EntityRef>(_items, 0, count);

        internal EntityQueryResult ToQueryResult(int count)
            => new EntityQueryResult(_items, count);

        private static int NormalizeCapacity(int capacity)
            => capacity > 0 ? capacity : 1;
    }
}
