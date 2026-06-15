#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Last actionable reduction context for incomplete ticks and guardrail failures.
    /// </summary>
    public readonly struct SimulationResultDiagnostics
    {
        public SimulationResultDiagnostics(
            string budgetReason,
            CascadeTypeId currentFactId,
            string currentFactName,
            EntityRef currentEntity,
            int causalDepth,
            string reducerName)
        {
            _budgetReason = budgetReason ?? string.Empty;
            CurrentFactId = currentFactId;
            _currentFactName = currentFactName ?? string.Empty;
            CurrentEntity = currentEntity;
            CausalDepth = causalDepth;
            _reducerName = reducerName ?? string.Empty;
        }

        private readonly string _budgetReason;
        private readonly string _currentFactName;
        private readonly string _reducerName;

        public static SimulationResultDiagnostics Empty => default;

        public string BudgetReason => _budgetReason ?? string.Empty;
        public CascadeTypeId CurrentFactId { get; }
        public string CurrentFactName => _currentFactName ?? string.Empty;
        public EntityRef CurrentEntity { get; }
        public int CausalDepth { get; }
        public string ReducerName => _reducerName ?? string.Empty;
    }
}
