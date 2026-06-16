#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Per-tick reduction budget. Full ticks throw if reduction cannot close inside the configured budget.
    /// </summary>
    public sealed class ReduceOptions
    {
        public int MaxFacts { get; set; } = 50000;
        public int MaxPasses { get; set; } = 64;
        public int MaxMilliseconds { get; set; } = 8;
        public BudgetMode BudgetMode { get; set; } = BudgetMode.PriorityFirst;
        public FactGuardrails Guardrails { get; set; } = new FactGuardrails();

        public static ReduceOptions Default()
            => new ReduceOptions();
    }
}
