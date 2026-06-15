#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Numeric counters collected during one simulation tick.
    /// </summary>
    public readonly struct SimulationResultCounters
    {
        public SimulationResultCounters(
            int acceptedFacts,
            int processedFacts,
            int deduplicatedFacts,
            int rejectedDestroyedEntityFacts,
            int reducerInvocations,
            int transactionalReducerInvocations,
            int touchedEntities,
            int mutationCount)
        {
            AcceptedFacts = acceptedFacts;
            ProcessedFacts = processedFacts;
            DeduplicatedFacts = deduplicatedFacts;
            RejectedDestroyedEntityFacts = rejectedDestroyedEntityFacts;
            ReducerInvocations = reducerInvocations;
            TransactionalReducerInvocations = transactionalReducerInvocations;
            TouchedEntities = touchedEntities;
            MutationCount = mutationCount;
        }

        public int AcceptedFacts { get; }
        public int ProcessedFacts { get; }
        public int DeduplicatedFacts { get; }
        public int RejectedDestroyedEntityFacts { get; }
        public int ReducerInvocations { get; }
        public int TransactionalReducerInvocations { get; }
        public int TouchedEntities { get; }
        public int MutationCount { get; }
    }
}
