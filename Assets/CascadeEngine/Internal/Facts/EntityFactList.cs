#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed per-entity fact list with array-backed span access.
    /// </summary>
    internal sealed class EntityFactList<TFact>
        where TFact : struct, IFact
    {
        private TFact[] _items;
        private FactListCapacityMode _capacityMode;

        internal EntityFactList()
            : this(4, FactListCapacityMode.GrowOnDemand)
        {
        }

        internal EntityFactList(int initialCapacity)
            : this(initialCapacity, FactListCapacityMode.GrowOnDemand)
        {
        }

        internal EntityFactList(int initialCapacity, FactListCapacityMode capacityMode)
        {
            _items = new TFact[NormalizeCapacity(initialCapacity)];
            _capacityMode = capacityMode;
        }

        internal int Count { get; private set; }
        internal int Capacity => _items.Length;
        internal FactListCapacityMode CapacityMode => _capacityMode;

        internal void SetCapacityMode(FactListCapacityMode capacityMode)
        {
            _capacityMode = capacityMode;
        }

        internal void EnsureCapacity(int capacity)
        {
            if (capacity <= _items.Length)
            {
                return;
            }

            Array.Resize(ref _items, capacity);
        }

        internal int Add(in TFact fact)
        {
            if (Count == _items.Length)
            {
                GrowForAdd();
            }

            var index = Count;
            _items[index] = fact;
            Count++;
            return index;
        }

        internal bool Contains(in TFact fact)
        {
            var comparer = EqualityComparer<TFact>.Default;
            for (var i = 0; i < Count; i++)
            {
                if (comparer.Equals(_items[i], fact))
                {
                    return true;
                }
            }

            return false;
        }

        internal bool TryGetLatest(out TFact fact)
        {
            if (Count == 0)
            {
                fact = default;
                return false;
            }

            fact = _items[Count - 1];
            return true;
        }

        internal ReadOnlySpan<TFact> AsSpan()
            => new ReadOnlySpan<TFact>(_items, 0, Count);

        internal ref readonly TFact Get(int index)
            => ref _items[index];

        internal void Clear()
        {
            var count = Count;
            Count = 0;

            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    _items[i].Dispose();
                }

                Array.Clear(_items, 0, count);
            }
        }

        private static int NormalizeCapacity(int capacity)
            => Math.Max(capacity, 1);

        private void GrowForAdd()
        {
            if (_capacityMode == FactListCapacityMode.Fixed)
            {
                throw new InvalidOperationException(
                    $"Fact list for '{typeof(TFact).Name}' exceeded fixed capacity '{_items.Length}'. Increase WarmupCapacityHints.FactsPerEntityPerTypeCapacity.");
            }

            Array.Resize(ref _items, _items.Length * 2);
        }
    }
}
