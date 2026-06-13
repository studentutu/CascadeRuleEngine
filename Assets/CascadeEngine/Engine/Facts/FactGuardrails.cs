#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Hard limits that turn fact cycles and runaway reducers into explicit failures.
    /// </summary>
    public sealed class FactGuardrails
    {
        public int MaxFactsPerEntity { get; set; } = 512;
        public int MaxFactsPerTypePerEntity { get; set; } = 64;
        public int MaxReducerInvocationsPerTick { get; set; } = 100000;
        public int MaxTransactionalReducerInvocationsPerTick { get; set; } = 50000;
        public int MaxCausalDepth { get; set; } = 32;
        public bool DetectCycles { get; set; } = true;
        public bool FailOnEqualPriorityConflict { get; set; } = true;
        public bool CountDeduplicatedFacts { get; set; } = true;
        public bool CountRejectedDestroyedEntityFacts { get; set; } = true;
    }
}
