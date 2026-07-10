#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal state-presence registration for one entity-scoped reducer.
    /// </summary>
    internal interface IStateReducerRegistration
    {
        CascadeTypeId StateId { get; }
        string DebugName { get; }
        int EntityCount { get; }

        EntityRef EntityAt(int index);

        void BindStateBucket(FactSimulation simulation);

        void Reduce(FactSimulation simulation, EntityRef entity);

        void DisposeRegistration();
    }
}
