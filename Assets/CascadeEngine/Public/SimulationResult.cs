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
        {
            Tick = tick;
            Complete = complete;
            AcceptedFacts = acceptedFacts;
            ProcessedFacts = processedFacts;
            DeduplicatedFacts = deduplicatedFacts;
            RejectedDestroyedEntityFacts = rejectedDestroyedEntityFacts;
            ReducerInvocations = reducerInvocations;
            TransactionalReducerInvocations = transactionalReducerInvocations;
            TouchedEntities = touchedEntities;
            MutationCount = mutationCount;
        }

        public SimulationTick Tick { get; }
        public bool Complete { get; }
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
