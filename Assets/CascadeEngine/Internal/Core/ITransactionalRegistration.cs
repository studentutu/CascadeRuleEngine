#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal registration for one entity-scoped transactional reducer.
    /// </summary>
    internal interface ITransactionalRegistration
    {
        int Index { get; }
        Type[] RequiredFacts { get; }

        void Reindex(int index);

        void Reduce(FactSimulation simulation, EntityRef entity);

        void DisposeRegistration();
    }
}
