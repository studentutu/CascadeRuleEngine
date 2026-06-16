#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Dense entity counter with explicit capacity ownership.
    /// </summary>
    internal sealed class DenseEntityCounter
    {
        private int[] _counts;

        internal DenseEntityCounter(int initialEntityCapacity)
        {
            _counts = new int[NormalizeCapacity(initialEntityCapacity)];
        }

        internal int Capacity => _counts.Length;

        internal void EnsureCapacity(int entityCapacity)
        {
            if (entityCapacity <= _counts.Length)
            {
                return;
            }

            Array.Resize(ref _counts, entityCapacity);
        }

        internal int Increment(EntityRef entity)
        {
            EnsureCapacity(entity.Value + 1);
            _counts[entity.Value]++;
            return _counts[entity.Value];
        }

        internal int Get(EntityRef entity)
        {
            return (uint)entity.Value < _counts.Length ? _counts[entity.Value] : 0;
        }

        internal void Clear(DenseEntitySet touchedEntities)
        {
            for (var i = 0; i < touchedEntities.Count; i++)
            {
                var entityIndex = touchedEntities[i].Value;
                if ((uint)entityIndex < _counts.Length)
                {
                    _counts[entityIndex] = 0;
                }
            }
        }

        private static int NormalizeCapacity(int capacity)
            => capacity > 0 ? capacity : 1;
    }
}
