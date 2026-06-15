#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Diagnostics for one RunTick call.
    /// </summary>
    public readonly struct SimulationResult
    {
        public SimulationResult(
            SimulationTick tick,
            bool complete,
            int acceptedFacts,
            int processedFacts,
            int deduplicatedFacts,
            int rejectedDestroyedEntityFacts,
            int reducerInvocations,
            int transactionalReducerInvocations,
            int touchedEntities,
            int mutationCount)
            : this(
                tick,
                complete,
                new SimulationResultCounters(
                    acceptedFacts,
                    processedFacts,
                    deduplicatedFacts,
                    rejectedDestroyedEntityFacts,
                    reducerInvocations,
                    transactionalReducerInvocations,
                    touchedEntities,
                    mutationCount),
                SimulationResultDiagnostics.Empty)
        {
        }

        public SimulationResult(
            SimulationTick tick,
            bool complete,
            int acceptedFacts,
            int processedFacts,
            int deduplicatedFacts,
            int rejectedDestroyedEntityFacts,
            int reducerInvocations,
            int transactionalReducerInvocations,
            int touchedEntities,
            int mutationCount,
            string budgetReason,
            CascadeTypeId currentFactId,
            string currentFactName,
            EntityRef currentEntity,
            int causalDepth,
            string reducerName)
            : this(
                tick,
                complete,
                new SimulationResultCounters(
                    acceptedFacts,
                    processedFacts,
                    deduplicatedFacts,
                    rejectedDestroyedEntityFacts,
                    reducerInvocations,
                    transactionalReducerInvocations,
                    touchedEntities,
                    mutationCount),
                new SimulationResultDiagnostics(
                    budgetReason,
                    currentFactId,
                    currentFactName,
                    currentEntity,
                    causalDepth,
                    reducerName))
        {
        }

        public SimulationResult(
            SimulationTick tick,
            bool complete,
            SimulationResultCounters counters,
            SimulationResultDiagnostics diagnostics)
        {
            Tick = tick;
            Complete = complete;
            Counters = counters;
            Diagnostics = diagnostics;
        }

        public SimulationTick Tick { get; }
        public bool Complete { get; }
        public SimulationResultCounters Counters { get; }
        public SimulationResultDiagnostics Diagnostics { get; }
        public int AcceptedFacts => Counters.AcceptedFacts;
        public int ProcessedFacts => Counters.ProcessedFacts;
        public int DeduplicatedFacts => Counters.DeduplicatedFacts;
        public int RejectedDestroyedEntityFacts => Counters.RejectedDestroyedEntityFacts;
        public int ReducerInvocations => Counters.ReducerInvocations;
        public int TransactionalReducerInvocations => Counters.TransactionalReducerInvocations;
        public int TouchedEntities => Counters.TouchedEntities;
        public int MutationCount => Counters.MutationCount;
        public string BudgetReason => Diagnostics.BudgetReason;
        public CascadeTypeId CurrentFactId => Diagnostics.CurrentFactId;
        public string CurrentFactName => Diagnostics.CurrentFactName;
        public EntityRef CurrentEntity => Diagnostics.CurrentEntity;
        public int CausalDepth => Diagnostics.CausalDepth;
        public string ReducerName => Diagnostics.ReducerName;
    }
}
