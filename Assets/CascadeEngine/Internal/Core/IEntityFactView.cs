#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Query-only view of facts accumulated for one entity in the current tick.
    /// </summary>
    public interface IEntityFactView
    {
        bool Has<TFact>()
            where TFact : struct, IFact;

        bool TryGetLatest<TFact>(out TFact fact)
            where TFact : struct, IFact;

        // TODO: if this is only for a single entity for a single fact then remove and just use Has<TFact> instead
        ReadOnlySpan<TFact> All<TFact>()
            where TFact : struct, IFact;
    }
}
