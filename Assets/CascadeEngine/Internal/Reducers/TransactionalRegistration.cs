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

        internal TransactionalRegistration(int index, Type[] requiredFacts, ITransactionalReducer reducer)
        {
            Index = index;
            RequiredFacts = requiredFacts;
            _reducer = reducer;
        }

        public int Index { get; private set; }
        public Type[] RequiredFacts { get; }

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
