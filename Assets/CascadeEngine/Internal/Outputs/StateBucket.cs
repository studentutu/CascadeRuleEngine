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
        private readonly Dictionary<int, TState> _values = new Dictionary<int, TState>();
        private readonly List<StateMutationRecord<TState>> _mutations = new List<StateMutationRecord<TState>>();

        public Type StateType => typeof(TState);
        public int MutationCount => _mutations.Count;

        public bool Has(EntityRef entity)
            => !entity.IsGlobal && _values.ContainsKey(entity.Value);

        internal bool TryGet(EntityRef entity, out TState state)
        {
            if (entity.IsGlobal)
            {
                state = default;
                return false;
            }

            return _values.TryGetValue(entity.Value, out state);
        }

        internal TState Get(EntityRef entity)
        {
            if (TryGet(entity, out var state))
            {
                return state;
            }

            throw new KeyNotFoundException($"Entity '{entity}' has no '{typeof(TState).Name}' output state.");
        }

        internal void Set(EntityRef entity, TState next)
        {
            if (entity.IsGlobal)
            {
                throw new InvalidOperationException("Global facts cannot own output state.");
            }

            if (_values.TryGetValue(entity.Value, out var previous))
            {
                if (EqualityComparer<TState>.Default.Equals(previous, next))
                {
                    return;
                }

                _values[entity.Value] = next;
                _mutations.Add(new StateMutationRecord<TState>(
                    entity,
                    new StateMutation<TState>(true, previous, true, next)));
                return;
            }

            _values.Add(entity.Value, next);
            _mutations.Add(new StateMutationRecord<TState>(
                entity,
                new StateMutation<TState>(false, default, true, next)));
        }

        internal void SetSilently(EntityRef entity, TState next)
        {
            if (entity.IsGlobal)
            {
                throw new InvalidOperationException("Global facts cannot own output state.");
            }

            _values[entity.Value] = next;
        }

        public void Delete(EntityRef entity)
        {
            if (entity.IsGlobal)
            {
                return;
            }

            if (!_values.TryGetValue(entity.Value, out var previous))
            {
                return;
            }

            _values.Remove(entity.Value);
            _mutations.Add(new StateMutationRecord<TState>(
                entity,
                new StateMutation<TState>(true, previous, false, default)));
        }

        public void ClearMutations()
            => _mutations.Clear();

        internal void ForEachMutation(StateMutationHandler<TState> handler)
        {
            for (var i = 0; i < _mutations.Count; i++)
            {
                var mutation = _mutations[i].Mutation;
                handler(_mutations[i].Entity, in mutation);
            }
        }
    }
}
