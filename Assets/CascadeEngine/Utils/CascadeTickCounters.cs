#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Per-tick counters used to validate fanout and skipped work.
    /// </summary>
    public readonly struct CascadeTickCounters
    {
        public CascadeTickCounters(
            int producedFacts,
            int processedFacts,
            int reducerRuns,
            int dirtyConsumers,
            int skippedNonRelevant,
            int registeredReducers,
            int touchedEntities)
        {
            ProducedFacts = producedFacts;
            ProcessedFacts = processedFacts;
            ReducerRuns = reducerRuns;
            DirtyConsumers = dirtyConsumers;
            SkippedNonRelevant = skippedNonRelevant;
            RegisteredReducers = registeredReducers;
            TouchedEntities = touchedEntities;
        }

        public int ProducedFacts { get; }
        public int ProcessedFacts { get; }
        public int ReducerRuns { get; }
        public int DirtyConsumers { get; }
        public int SkippedNonRelevant { get; }
        public int RegisteredReducers { get; }
        public int TouchedEntities { get; }
    }
}
