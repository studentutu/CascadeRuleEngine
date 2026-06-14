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

        void QueueCommitAction(FactSimulation simulation, EntityRef entity);

        void ApplyQueuedCommitActions();

        void ClearQueuedCommitActions();

        IStateBucket CreateStateBucket();

        void DeleteState(FactSimulation simulation, EntityRef entity);

        void Warmup(
            FactSimulation simulation,
            int stateCapacity,
            int mutationCapacity,
            int commitActionCapacity);

        void ClearMutations(FactSimulation simulation);

        int MutationCount(FactSimulation simulation);
        int CommitActionCapacity { get; }

        void DisposeRegistration();
    }
}
