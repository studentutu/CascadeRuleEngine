#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Coarse fact priority used by priority-first budget mode and domain conflict policies.
    /// </summary>
    public enum FactPriority
    {
        Low = 0,
        Normal = 100,
        Relevant = 500,
        PlayerVisible = 1000
    }
}
