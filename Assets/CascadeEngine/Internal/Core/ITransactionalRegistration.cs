#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal registration for one entity-scoped transactional reducer.
    /// </summary>
    internal interface ITransactionalRegistration
    {
        int Index { get; }
        CascadeTypeId[] RequiredFactIds { get; }

        void Reindex(int index);

        void Reduce(FactSimulation simulation, EntityRef entity);

        void DisposeRegistration();
    }
}
