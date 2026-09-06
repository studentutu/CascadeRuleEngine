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
        bool UsesPrioritySelection { get; }
        bool HasAbsenceReconciliation { get; }

        void Reindex(int index);

        void QueueCommitAction(FactSimulation simulation, EntityRef entity);

        int StateEntityCount { get; }
        int QueuedActionCount { get; }
        void ValidateAction(EntityStore entities, int index);
        void QueueAbsentCommitAction(FactSimulation simulation, int entityIndex);

        CascadeTypeId SelectPriorityWinner(FactSimulation simulation, EntityRef entity);

        void ApplyQueuedCommitActions();

        void ClearQueuedCommitActions();

        IStateBucket CreateStateBucket();

        void BindStateBucket(FactSimulation simulation, IStateBucket bucket);

        void UnbindStateBucket(FactSimulation simulation);

        void QueueDeleteAction(FactSimulation simulation, EntityRef entity);

        void FreezeCapacity();

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
