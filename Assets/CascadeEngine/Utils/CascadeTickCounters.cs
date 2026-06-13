#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Per-tick counters used to validate reduction, commit work, mutation output, and skipped work.
    /// </summary>
    public readonly struct CascadeTickCounters
    {
        public CascadeTickCounters(
            int producedFacts,
            int processedFacts,
            int reducerRuns,
            int mutatedProperties,
            int skippedNonRelevant,
            int registeredReducers,
            int touchedEntities)
        {
            ProducedFacts = producedFacts;
            ProcessedFacts = processedFacts;
            ReducerRuns = reducerRuns;
            MutatedProperties = mutatedProperties;
            SkippedNonRelevant = skippedNonRelevant;
            RegisteredReducers = registeredReducers;
            TouchedEntities = touchedEntities;
        }

        public int ProducedFacts { get; }
        public int ProcessedFacts { get; }
        public int ReducerRuns { get; }
        public int MutatedProperties { get; }
        public int SkippedNonRelevant { get; }
        public int RegisteredReducers { get; }
        public int TouchedEntities { get; }
    }
}
