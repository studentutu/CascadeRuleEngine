#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Preallocated fact buffer for one tick.
    /// </summary>
    public sealed class CascadeFactBuffer
    {
        private readonly CascadeFact[] _items;

        public CascadeFactBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _items = new CascadeFact[capacity];
        }

        public int Count { get; private set; }

        public CascadeFact this[int index]
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

        public void Add(CascadeFact fact)
        {
            if (Count >= _items.Length)
            {
                throw new InvalidOperationException("Fact capacity exceeded.");
            }

            _items[Count] = fact;
            Count++;
        }

        public void Clear()
        {
            Count = 0;
        }
    }
}
