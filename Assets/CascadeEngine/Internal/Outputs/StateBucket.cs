#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed durable state and last-tick mutation storage for one output state.
    /// </summary>
    internal sealed class StateBucket<TState> : IStateBucket
        where TState : struct, IOutputState
    {
        private readonly CascadeTypeId _stateId;
        private readonly string _debugName;
        private readonly List<StateMutationRecord<TState>> _mutations = new List<StateMutationRecord<TState>>();
        private int[] _sparse = Array.Empty<int>();
        private EntityRef[] _entities = Array.Empty<EntityRef>();
        private TState[] _values = Array.Empty<TState>();
        private int _count;

        internal StateBucket(CascadeTypeId stateId, string debugName)
        {
            _stateId = stateId;
            _debugName = debugName;
        }

        public CascadeTypeId StateId => _stateId;
        public int StateCapacityHint => Math.Min(_sparse.Length, _values.Length);
        public int MutationCapacity => _mutations.Capacity;
        public int MutationCount => _mutations.Count;
        internal int EntityCount => _count;

        public bool Has(EntityRef entity)
            => TryGetDenseIndex(entity, out _);

        internal bool TryGet(EntityRef entity, out TState state)
        {
            if (TryGetDenseIndex(entity, out var index))
            {
                state = _values[index];
                return true;
            }

            state = default;
            return false;
        }

        internal TState Get(EntityRef entity)
        {
            if (TryGet(entity, out var state))
            {
                return state;
            }

            throw new KeyNotFoundException($"Entity '{entity}' has no '{_debugName}' output state.");
        }

        internal void Set(EntityRef entity, TState next)
        {
            EnsureStorageCapacity(entity.Value + 1, _count + 1);
            if (TryGetDenseIndex(entity, out var index))
            {
                var previous = _values[index];
                if (EqualityComparer<TState>.Default.Equals(previous, next))
                {
                    return;
                }

                _values[index] = next;
                _mutations.Add(new StateMutationRecord<TState>(
                    entity,
                    new StateMutation<TState>(true, previous, true, next)));
                return;
            }

            AddNew(entity, next);
            _mutations.Add(new StateMutationRecord<TState>(
                entity,
                new StateMutation<TState>(false, default, true, next)));
        }

        internal void SetSilently(EntityRef entity, TState next)
        {
            EnsureStorageCapacity(entity.Value + 1, _count + 1);
            if (TryGetDenseIndex(entity, out var index))
            {
                _values[index] = next;
                return;
            }

            AddNew(entity, next);
        }

        public void Delete(EntityRef entity)
        {
            if (!TryGetDenseIndex(entity, out var index))
            {
                return;
            }

            var previous = _values[index];
            var lastIndex = _count - 1;
            var lastEntity = _entities[lastIndex];

            if (index != lastIndex)
            {
                _entities[index] = lastEntity;
                _values[index] = _values[lastIndex];
                _sparse[lastEntity.Value] = index + 1;
            }

            _sparse[entity.Value] = 0;
            _entities[lastIndex] = default;
            _values[lastIndex] = default;
            _count--;

            _mutations.Add(new StateMutationRecord<TState>(
                entity,
                new StateMutation<TState>(true, previous, false, default)));
        }

        public void EnsureCapacity(int stateCapacity, int mutationCapacity)
        {
            EnsureStorageCapacity(stateCapacity, stateCapacity);

            if (_mutations.Capacity < mutationCapacity)
            {
                _mutations.Capacity = mutationCapacity;
            }
        }

        public void ClearMutations()
            => _mutations.Clear();

        public void DisposeBucket()
        {
            for (var i = 0; i < _count; i++)
            {
                DisposeIfNeeded(_values[i]);
            }

            _sparse = Array.Empty<int>();
            _entities = Array.Empty<EntityRef>();
            _values = Array.Empty<TState>();
            _count = 0;
            _mutations.Clear();
            _mutations.Capacity = 0;
        }

        internal EntityRef EntityAt(int index)
        {
            if ((uint)index >= _count)
            {
                throw new IndexOutOfRangeException();
            }

            return _entities[index];
        }

        internal void ForEachMutation(StateMutationHandler<TState> handler)
        {
            for (var i = 0; i < _mutations.Count; i++)
            {
                var mutation = _mutations[i].Mutation;
                handler(_mutations[i].Entity, in mutation);
            }
        }

        private void AddNew(EntityRef entity, TState state)
        {
            var index = _count;
            _entities[index] = entity;
            _values[index] = state;
            _sparse[entity.Value] = index + 1;
            _count++;
        }

        private bool TryGetDenseIndex(EntityRef entity, out int index)
        {
            if ((uint)entity.Value < _sparse.Length)
            {
                var stored = _sparse[entity.Value];
                if (stored != 0)
                {
                    index = stored - 1;
                    return index < _count && _entities[index].Equals(entity);
                }
            }

            index = -1;
            return false;
        }

        private void EnsureStorageCapacity(int entityCapacity, int stateCapacity)
        {
            if (entityCapacity > _sparse.Length)
            {
                Array.Resize(ref _sparse, GrowCapacity(_sparse.Length, entityCapacity));
            }

            if (stateCapacity <= _values.Length)
            {
                return;
            }

            var capacity = GrowCapacity(_values.Length, stateCapacity);
            Array.Resize(ref _entities, capacity);
            Array.Resize(ref _values, capacity);
        }

        private static void DisposeIfNeeded(TState state)
        {
            if (state is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private static int GrowCapacity(int current, int required)
        {
            var doubled = current == 0 ? 1 : current * 2;
            return Math.Max(required, doubled);
        }
    }
}
