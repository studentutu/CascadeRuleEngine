#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Dense entity set optimized for per-tick add-once and clear-by-touched operations.
    /// </summary>
    internal sealed class DenseEntitySet
    {
        private readonly List<EntityRef> _entities;
        private int[] _contains;

        private bool _fixedCapacity;
        internal void FreezeCapacity() => _fixedCapacity = true;

        internal DenseEntitySet(int initialEntityCapacity)
        {
            var capacity = NormalizeCapacity(initialEntityCapacity);
            _entities = new List<EntityRef>(capacity);
            _contains = new int[capacity];
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

            if (_fixedCapacity) throw new InvalidOperationException("Fixed runtime buffer capacity exceeded.");
            if (_entities.Capacity < entityCapacity) _entities.Capacity = entityCapacity;
            Array.Resize(ref _contains, entityCapacity);
        }

        internal bool Add(EntityRef entity)
        {
            EnsureCapacity(entity.StorageIndex + 1);

            var stored = _contains[entity.StorageIndex];
            if (stored != 0)
            {
                if (_entities[stored - 1] != entity)
                    throw new InvalidOperationException("Entity membership is owned by another generation.");
                return false;
            }

            _contains[entity.StorageIndex] = _entities.Count + 1;
            _entities.Add(entity);
            return true;
        }

        internal int IndexOf(EntityRef entity)
        {
            if ((uint)entity.StorageIndex >= _contains.Length) return -1;
            var stored = _contains[entity.StorageIndex];
            return stored != 0 && _entities[stored - 1] == entity ? stored - 1 : -1;
        }

        internal bool Contains(EntityRef entity) => IndexOf(entity) >= 0;

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
                _contains[_entities[i].StorageIndex] = 0;
            }

            _entities.Clear();
        }

        private static int NormalizeCapacity(int capacity)
            => capacity > 0 ? capacity : 1;

        internal void DisposeStorage()
        {
            _entities.Clear();
            _entities.Capacity = 0;
            _contains = Array.Empty<int>();
        }
    }
}
