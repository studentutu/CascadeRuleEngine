#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Tick-local compact list of fact type ids accepted for one entity.
    /// </summary>
    internal sealed class EntityFactIdList
    {
        private CascadeTypeId[] _ids;

        internal EntityFactIdList(int initialCapacity)
        {
            _ids = new CascadeTypeId[NormalizeCapacity(initialCapacity)];
        }

        internal int Count { get; private set; }
        internal int Capacity => _ids.Length;

        internal void EnsureCapacity(int capacity)
        {
            if (capacity <= _ids.Length)
            {
                return;
            }

            Array.Resize(ref _ids, capacity);
        }

        internal void Add(CascadeTypeId id)
        {
            if (Count == _ids.Length)
            {
                Array.Resize(ref _ids, _ids.Length * 2);
            }

            _ids[Count] = id;
            Count++;
        }

        internal ReadOnlySpan<CascadeTypeId> AsSpan()
            => new ReadOnlySpan<CascadeTypeId>(_ids, 0, Count);

        internal void Clear()
        {
            if (Count > 0)
            {
                Array.Clear(_ids, 0, Count);
                Count = 0;
            }
        }

        private static int NormalizeCapacity(int capacity)
            => Math.Max(capacity, 1);
    }
}
