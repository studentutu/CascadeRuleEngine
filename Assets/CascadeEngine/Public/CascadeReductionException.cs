#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Actionable reduction failure with the current fact, entity, causal depth, reducer, and budget reason.
    /// </summary>
    public sealed class CascadeReductionException : InvalidOperationException
    {
        public CascadeReductionException(
            string message,
            SimulationTick tick,
            CascadeTypeId factId,
            string factName,
            EntityRef entity,
            int causalDepth,
            string reducerName,
            string budgetReason,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Tick = tick;
            FactId = factId;
            FactName = factName ?? string.Empty;
            Entity = entity;
            CausalDepth = causalDepth;
            ReducerName = reducerName ?? string.Empty;
            BudgetReason = budgetReason ?? string.Empty;
        }

        public SimulationTick Tick { get; }
        public CascadeTypeId FactId { get; }
        public string FactName { get; }
        public EntityRef Entity { get; }
        public int CausalDepth { get; }
        public string ReducerName { get; }
        public string BudgetReason { get; }
    }
}
