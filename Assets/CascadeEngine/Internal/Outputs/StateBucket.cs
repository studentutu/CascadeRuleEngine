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
        private StateMutationRecord<TState>[] _mutations = Array.Empty<StateMutationRecord<TState>>();
        private int _mutationCount;
        private static readonly EqualityComparer<TState> Comparer = EqualityComparer<TState>.Default;
        private readonly EntitySparseSet<TState> _storage = new EntitySparseSet<TState>();
        private int _preparedInsertions;
        private int _preparedMutations;
        private bool _fixedCapacity;

        static StateBucket() { }

        internal StateBucket(CascadeTypeId stateId, string debugName)
        {
            _stateId = stateId;
            _debugName = debugName;
            // Mono records cold generic value construction allocations. Initialize these during registration, without callbacks or state writes.
            var emptyMutation = new StateMutation<TState>(false, default, false, default);
            _ = new CommitAction<TState>(this, default, emptyMutation);
        }

        public CascadeTypeId StateId => _stateId;
        public int StateCapacityHint => Math.Min(_storage.SparseCapacity, _storage.DenseCapacity);
        public int MutationCapacity => _mutations.Length;
        public int MutationCount => _mutationCount;
        internal int EntityCount => _storage.Count;

        public bool Has(EntityRef entity)
            => _storage.TryGetIndex(entity, out _);

        internal bool TryGet(EntityRef entity, out TState state)
        {
            if (_storage.TryGetIndex(entity, out var index))
            {
                state = _storage.ValueAt(index);
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

        internal void FreezeCapacity()
        {
            _fixedCapacity = true;
            _storage.FreezeCapacity();
        }

        /// <summary>
        /// [INTEGRATION] Range: one decision per entity/output. Condition: unchanged committed snapshot. Output: equality and capacity checked before any durable write.
        /// </summary>
        internal bool Prepare(EntityRef entity, CommitDecision<TState> decision, out CommitAction<TState> action)
        {
            action = default;
            var hadPrevious = TryGet(entity, out var previous);
            var hasNext = decision.Kind == CommitDecisionKind.Set;
            if (decision.Kind == CommitDecisionKind.Unchanged
                || (!hasNext && !hadPrevious)
                || (hasNext && hadPrevious && Comparer.Equals(previous, decision.Next)))
            {
                return false;
            }

            var insertions = _preparedInsertions + (hasNext && !hadPrevious ? 1 : 0);
            EnsureStorageCapacity(entity.StorageIndex + 1, _storage.Count + insertions);
            _storage.Prepare(entity, _storage.Count + insertions);
            EnsureMutationCapacity(_mutationCount + _preparedMutations + 1);
            _preparedInsertions = insertions;
            _preparedMutations++;
            action = new CommitAction<TState>(this, entity,
                new StateMutation<TState>(hadPrevious, previous, hasNext, decision.Next));
            return true;
        }

        internal void ClearPreparation()
        {
            _preparedInsertions = 0;
            _preparedMutations = 0;
        }

        /// <summary>
        /// [INTEGRATION] Range: validated plan only. Condition: all outputs prepared. Output: array/index writes and reserved journal append, without callbacks or growth.
        /// </summary>
        internal void ApplyPrepared(EntityRef entity, in StateMutation<TState> mutation)
        {
            if (mutation.HasNext)
            {
                _storage.SetPrepared(entity, mutation.Next);
            }
            else
            {
                _storage.RemovePrepared(entity);
            }
            _mutations[_mutationCount++] = new StateMutationRecord<TState>(entity, mutation);
        }

        internal void SetSilently(EntityRef entity, TState next)
        {
            EnsureStorageCapacity(entity.StorageIndex + 1, _storage.Count + (Has(entity) ? 0 : 1));
            _storage.Prepare(entity, _storage.Count + (Has(entity) ? 0 : 1));
            _storage.SetPrepared(entity, next);
        }

        private void EnsureMutationCapacity(int required)
        {
            if (_mutations.Length >= required) return;
            if (_fixedCapacity) throw new InvalidOperationException("Fixed output mutation capacity exceeded.");
            Array.Resize(ref _mutations, GrowCapacity(_mutations.Length, required));
        }

        public void EnsureCapacity(int stateCapacity, int mutationCapacity)
        {
            EnsureStorageCapacity(stateCapacity, stateCapacity);

            EnsureMutationCapacity(mutationCapacity);
        }

        public void ClearMutations()
        {
            Array.Clear(_mutations, 0, _mutationCount);
            _mutationCount = 0;
        }

        public void DisposeBucket()
        {
            var errors = new CleanupErrors();
            for (var i = 0; i < _storage.Count; i++)
            {
                var state = _storage.ValueAt(i);
                _storage.ValueAt(i) = default;
                try
                {
                    DisposeIfNeeded(state);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            _storage.DisposeStorage();
            _mutations = Array.Empty<StateMutationRecord<TState>>();
            _mutationCount = 0;
            errors.ThrowIfAny();
        }

        /// <summary>
        /// [INTEGRATION] Releases the static simulation route independently of the feature registry's lifetime.
        /// </summary>
        public void UnbindStateRoute(FactSimulation simulation)
            => OutputStateRouteCache<TState>.Remove(simulation);

        internal EntityRef EntityAt(int index)
        {
            if ((uint)index >= _storage.Count)
            {
                throw new IndexOutOfRangeException();
            }

            return _storage.EntityAt(index);
        }

        internal void ForEachMutation(StateMutationHandler<TState> handler)
        {
            for (var i = 0; i < _mutationCount; i++)
            {
                var mutation = _mutations[i].Mutation;
                handler(_mutations[i].Entity, in mutation);
            }
        }

        private void EnsureStorageCapacity(int entityCapacity, int stateCapacity)
        {
            var sparse = _storage.SparseCapacity;
            var dense = _storage.DenseCapacity;
            _storage.EnsureCapacity(
                entityCapacity > sparse ? (_fixedCapacity ? entityCapacity : GrowCapacity(sparse, entityCapacity)) : sparse,
                stateCapacity > dense ? (_fixedCapacity ? stateCapacity : GrowCapacity(dense, stateCapacity)) : dense);
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
