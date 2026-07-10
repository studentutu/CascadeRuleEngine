#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed bridge from committed state membership to an entity-scoped transactional reducer.
    /// </summary>
    internal sealed class StateReducerRegistration<TState> : IStateReducerRegistration
        where TState : struct, IOutputState
    {
        private readonly ITransactionalReducer _reducer;
        private StateBucket<TState>? _bucket;

        internal StateReducerRegistration(
            CascadeTypeId stateId,
            ITransactionalReducer reducer,
            string debugName)
        {
            StateId = stateId;
            _reducer = reducer;
            DebugName = debugName;
        }

        public CascadeTypeId StateId { get; }
        public string DebugName { get; }
        public int EntityCount => RequireBucket().EntityCount;

        public EntityRef EntityAt(int index)
            => RequireBucket().EntityAt(index);

        public void BindStateBucket(FactSimulation simulation)
            => _bucket = simulation.GetStateBucket<TState>();

        public void Reduce(FactSimulation simulation, EntityRef entity)
            => _reducer.Reduce(simulation, entity);

        public void DisposeRegistration()
        {
            _bucket = null;
            if (_reducer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private StateBucket<TState> RequireBucket()
        {
            if (_bucket == null)
            {
                throw new InvalidOperationException(
                    $"State reducer '{DebugName}' is not bound to output state '{StateId}'.");
            }

            return _bucket;
        }
    }
}
