#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Batch transactional reducer registration.
    /// </summary>
    internal sealed class BatchTransactionalRegistration : IBatchTransactionalRegistration
    {
        private readonly IBatchTransactionalReducer _reducer;

        internal BatchTransactionalRegistration(int index, CascadeTypeId[] requiredFactIds, IBatchTransactionalReducer reducer)
        {
            Index = index;
            RequiredFactIds = requiredFactIds;
            _reducer = reducer;
        }

        public int Index { get; private set; }
        public CascadeTypeId[] RequiredFactIds { get; }

        public void Reindex(int index)
            => Index = index;

        public void ReduceBatch(FactSimulation simulation, ReadOnlySpan<EntityRef> entities)
            => _reducer.ReduceBatch(simulation, entities);

        public void DisposeRegistration()
        {
            if (_reducer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
