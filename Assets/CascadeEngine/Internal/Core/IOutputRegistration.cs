#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal output registration contract used by the commit loop.
    /// </summary>
    internal interface IOutputRegistration
    {
        CascadeTypeId StateId { get; }
        string Name { get; }

        void Reindex(int index);

        bool IsAffectedBy(FactStore facts, EntityRef entity);

        ICommitAction? CreateCommitAction(FactSimulation simulation, EntityRef entity);

        IStateBucket CreateStateBucket();

        void DeleteState(FactSimulation simulation, EntityRef entity);

        void Warmup(FactSimulation simulation, int stateCapacity, int mutationCapacity);

        void ClearMutations(FactSimulation simulation);

        int MutationCount(FactSimulation simulation);

        void DisposeRegistration();
    }
}
