#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Consumer callback for typed mutation output: one mutated entity with previous and next committed values.
    /// </summary>
    public delegate void CascadeMutationHandler<T>(CascadeEntityId entityId, T previous, T next);
}