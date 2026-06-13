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

        internal BatchTransactionalRegistration(int index, Type[] requiredFacts, IBatchTransactionalReducer reducer)
        {
            Index = index;
            RequiredFacts = requiredFacts;
            _reducer = reducer;
        }

        public int Index { get; private set; }
        public Type[] RequiredFacts { get; }

        public void Reindex(int index)
            => Index = index;

        public void ReduceBatch(FactSimulation simulation, ReadOnlySpan<EntityRef> entities)
            => _reducer.ReduceBatch(simulation, entities);
    }
}
