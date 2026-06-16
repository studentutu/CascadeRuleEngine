#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Tick-local compact list of accepted fact routes for one entity.
    /// </summary>
    internal sealed class EntityFactRouteList
    {
        private IFactCommitRoute[] _routes;

        internal EntityFactRouteList(int initialCapacity)
        {
            _routes = new IFactCommitRoute[NormalizeCapacity(initialCapacity)];
        }

        internal int Count { get; private set; }
        internal int Capacity => _routes.Length;

        internal void EnsureCapacity(int capacity)
        {
            if (capacity <= _routes.Length)
            {
                return;
            }

            Array.Resize(ref _routes, capacity);
        }

        internal void Add(IFactCommitRoute route)
        {
            if (Count == _routes.Length)
            {
                Array.Resize(ref _routes, _routes.Length * 2);
            }

            _routes[Count] = route;
            Count++;
        }

        internal ReadOnlySpan<IFactCommitRoute> AsSpan()
            => new ReadOnlySpan<IFactCommitRoute>(_routes, 0, Count);

        internal void Clear()
        {
            if (Count > 0)
            {
                Array.Clear(_routes, 0, Count);
                Count = 0;
            }
        }

        private static int NormalizeCapacity(int capacity)
            => Math.Max(capacity, 1);
    }
}
