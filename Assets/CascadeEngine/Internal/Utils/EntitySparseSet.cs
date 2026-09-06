#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Generational sparse membership and compact values. Owns indexing and capacity only; payload lifetime belongs to its caller.
    /// </summary>
    internal sealed class EntitySparseSet<TValue>
    {
        private int[] _sparse = Array.Empty<int>();
        private EntityRef[] _entities = Array.Empty<EntityRef>();
        private TValue[] _values = Array.Empty<TValue>();
        private bool _fixedCapacity;
        internal int Count { get; private set; }
        internal int SparseCapacity => _sparse.Length;
        internal int DenseCapacity => _values.Length;
        internal EntityRef EntityAt(int index) => _entities[index];
        internal ref TValue ValueAt(int index) => ref _values[index];
        internal void FreezeCapacity() => _fixedCapacity = true;

        internal bool TryGetIndex(EntityRef entity, out int index)
        {
            if ((uint)entity.StorageIndex < _sparse.Length)
            {
                index = _sparse[entity.StorageIndex] - 1;
                if ((uint)index < Count && _entities[index] == entity) return true;
            }
            index = -1;
            return false;
        }

        /// <summary>
        /// Range: reserved insertion count includes every unpublished add. Condition: no ownership change. Output: capacity and slot-owner validation.
        /// </summary>
        internal void Prepare(EntityRef entity, int requiredCount)
        {
            EnsureCapacity(entity.StorageIndex + 1, requiredCount);
            var index = _sparse[entity.StorageIndex] - 1;
            if (index >= 0 && _entities[index] != entity)
                throw new InvalidOperationException("Sparse slot is still owned by another entity generation.");
        }

        internal void EnsureCapacity(int sparseCapacity, int denseCapacity)
        {
            if (sparseCapacity <= _sparse.Length && denseCapacity <= _values.Length) return;
            if (_fixedCapacity) throw new InvalidOperationException("Fixed sparse storage capacity exceeded.");
            // Complete all allocations before replacing backing arrays; failure leaves the original shape usable.
            var sparse = _sparse;
            var entities = _entities;
            var values = _values;
            if (sparseCapacity > sparse.Length)
            {
                sparse = new int[sparseCapacity];
                Array.Copy(_sparse, sparse, _sparse.Length);
            }
            if (denseCapacity > values.Length)
            {
                entities = new EntityRef[denseCapacity];
                values = new TValue[denseCapacity];
                Array.Copy(_entities, entities, Count);
                Array.Copy(_values, values, Count);
            }
            _sparse = sparse;
            _entities = entities;
            _values = values;
        }

        /// <summary>Range: preflighted slot and count. Condition: no callbacks/growth. Output: one inserted or replaced value.</summary>
        internal int SetPrepared(EntityRef entity, TValue value)
        {
            if (!TryGetIndex(entity, out var index))
            {
                index = Count++;
                _entities[index] = entity;
                _sparse[entity.StorageIndex] = index + 1;
            }
            _values[index] = value;
            return index;
        }

        internal void RemovePrepared(EntityRef entity)
        {
            if (!TryGetIndex(entity, out var index)) return;
            var last = --Count;
            if (index != last)
            {
                _entities[index] = _entities[last];
                _values[index] = _values[last];
                _sparse[_entities[index].StorageIndex] = index + 1;
            }
            _sparse[entity.StorageIndex] = 0;
            _entities[last] = default;
            _values[last] = default!;
        }

        internal void Clear()
        {
            for (var i = 0; i < Count; i++)
            {
                _sparse[_entities[i].StorageIndex] = 0;
                _entities[i] = default;
                _values[i] = default!;
            }
            Count = 0;
        }

        internal void DisposeStorage()
        {
            _sparse = Array.Empty<int>();
            _entities = Array.Empty<EntityRef>();
            _values = Array.Empty<TValue>();
            Count = 0;
        }
    }
}
