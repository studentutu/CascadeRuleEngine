#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Per-tick reduction budget. Use Throw for incomplete commits unless a caller can prove partial closure is safe.
    /// </summary>
    public sealed class ReduceOptions
    {
        public int MaxFacts { get; set; } = 50000;
        public int MaxPasses { get; set; } = 64;
        public int MaxMilliseconds { get; set; } = 8;
        public BudgetMode BudgetMode { get; set; } = BudgetMode.PriorityFirst;
        public IncompleteCommitMode IncompleteCommitMode { get; set; } = IncompleteCommitMode.Throw;
        public FactGuardrails Guardrails { get; set; } = new FactGuardrails();

        public static ReduceOptions Default()
            => new ReduceOptions();
    }
}
