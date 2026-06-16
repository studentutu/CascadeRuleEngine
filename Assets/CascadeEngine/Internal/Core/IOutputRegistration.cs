#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal output registration contract used by the commit loop.
    /// </summary>
    internal interface IOutputRegistration
    {
        CascadeTypeId StateId { get; }
        int Index { get; }
        string Name { get; }
        FactType[] AffectedFacts { get; }

        void Reindex(int index);

        void QueueCommitAction(FactSimulation simulation, EntityRef entity);

        void ApplyQueuedCommitActions();

        void ClearQueuedCommitActions();

        IStateBucket CreateStateBucket();

        void BindStateBucket(FactSimulation simulation, IStateBucket bucket);

        void UnbindStateBucket(FactSimulation simulation);

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
