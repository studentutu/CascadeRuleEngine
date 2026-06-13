#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal registration for one batch transactional reducer.
    /// </summary>
    internal interface IBatchTransactionalRegistration
    {
        int Index { get; }
        Type[] RequiredFacts { get; }

        void Reindex(int index);

        void ReduceBatch(FactSimulation simulation, ReadOnlySpan<EntityRef> entities);
    }
}
