#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Fact queue processing mode. MVP implements FIFO and priority-first; relevance modes currently fall back to priority-first.
    /// </summary>
    public enum BudgetMode
    {
        Fifo,
        PriorityFirst,
        RelevantEntitiesFirst,
        VisibleEntitiesFirst
    }
}
