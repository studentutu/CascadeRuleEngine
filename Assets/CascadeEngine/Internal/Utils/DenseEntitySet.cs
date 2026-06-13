#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Dense non-global entity set optimized for per-tick add-once and clear-by-touched operations.
    /// </summary>
    internal sealed class DenseEntitySet
    {
        private readonly List<EntityRef> _entities;
        private bool[] _contains;

        internal DenseEntitySet(int initialEntityCapacity)
        {
            var capacity = NormalizeCapacity(initialEntityCapacity);
            _entities = new List<EntityRef>(capacity);
            _contains = new bool[capacity];
        }

        internal int Count => _entities.Count;
        internal int Capacity => _contains.Length;

        internal EntityRef this[int index] => _entities[index];

        internal void EnsureCapacity(int entityCapacity)
        {
            if (entityCapacity <= _contains.Length)
            {
                return;
            }

            Array.Resize(ref _contains, entityCapacity);
            if (_entities.Capacity < entityCapacity)
            {
                _entities.Capacity = entityCapacity;
            }
        }

        internal bool Add(EntityRef entity)
        {
            ThrowIfGlobal(entity);
            EnsureCapacity(entity.Value + 1);

            if (_contains[entity.Value])
            {
                return false;
            }

            _contains[entity.Value] = true;
            _entities.Add(entity);
            return true;
        }

        internal bool Contains(EntityRef entity)
        {
            ThrowIfGlobal(entity);
            return (uint)entity.Value < _contains.Length && _contains[entity.Value];
        }

        internal void CopyTo(EntityRefBuffer destination, out int count)
        {
            count = _entities.Count;
            destination.EnsureCapacity(count);
            for (var i = 0; i < count; i++)
            {
                destination[i] = _entities[i];
            }
        }

        internal void Clear()
        {
            for (var i = 0; i < _entities.Count; i++)
            {
                _contains[_entities[i].Value] = false;
            }

            _entities.Clear();
        }

        private static int NormalizeCapacity(int capacity)
            => capacity > 0 ? capacity : 1;

        private static void ThrowIfGlobal(EntityRef entity)
        {
            if (entity.IsGlobal)
            {
                throw new InvalidOperationException("Global entity is not valid for dense entity sets.");
            }
        }
    }
}
