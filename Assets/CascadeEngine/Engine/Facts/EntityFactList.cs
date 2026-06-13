#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed per-entity fact list with array-backed span access.
    /// </summary>
    internal sealed class EntityFactList<TFact>
        where TFact : struct, IFact
    {
        private TFact[] _items = new TFact[4];

        internal int Count { get; private set; }

        internal void Add(in TFact fact)
        {
            if (Count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }

            _items[Count] = fact;
            Count++;
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
    }
}
