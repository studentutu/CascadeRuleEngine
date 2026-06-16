#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Coarse domain priority used by committers when resolving closed fact conflicts.
    /// </summary>
    public enum FactPriority
    {
        Low = 0,
        Normal = 100,
        Relevant = 500,
        PlayerVisible = 1000
    }
}
