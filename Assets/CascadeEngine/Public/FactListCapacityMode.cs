#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Capacity behavior for per-entity fact lists after warmup.
    /// </summary>
    public enum FactListCapacityMode
    {
        GrowOnDemand = 0,
        Fixed = 1
    }
}
