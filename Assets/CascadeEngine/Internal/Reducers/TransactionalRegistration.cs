#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Entity-scoped transactional reducer registration.
    /// </summary>
    internal sealed class TransactionalRegistration : ITransactionalRegistration
    {
        private readonly ITransactionalReducer _reducer;
        private readonly string _debugName;

        internal TransactionalRegistration(
            int index,
            CascadeTypeId[] requiredFactIds,
            ITransactionalReducer reducer,
            string debugName)
        {
            Index = index;
            RequiredFactIds = requiredFactIds;
            _reducer = reducer;
            _debugName = debugName ?? string.Empty;
        }

        public int Index { get; private set; }
        public string DebugName => _debugName;
        public CascadeTypeId[] RequiredFactIds { get; }

        public void Reindex(int index)
            => Index = index;

        public void Reduce(FactSimulation simulation, EntityRef entity)
            => _reducer.Reduce(simulation, entity);

        public void DisposeRegistration()
        {
            if (_reducer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
