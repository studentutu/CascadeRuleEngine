#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Allocation-free query result over an engine-owned buffer. Consume it before issuing another query.
    /// </summary>
    public readonly struct EntityQueryResult
    {
        private readonly EntityRef[] _entities;

        internal EntityQueryResult(EntityRef[] entities, int count)
        {
            _entities = entities;
            Count = count;
        }

        public int Count { get; }

        public EntityRef this[int index]
        {
            get
            {
                if ((uint)index >= Count)
                {
                    throw new IndexOutOfRangeException();
                }

                return _entities[index];
            }
        }

        public ReadOnlySpan<EntityRef> AsSpan()
            => new ReadOnlySpan<EntityRef>(_entities, 0, Count);
    }
}
