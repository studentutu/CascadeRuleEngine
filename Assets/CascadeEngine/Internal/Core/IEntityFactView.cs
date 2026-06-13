#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Query-only view of facts accumulated for one entity in the current tick.
    /// </summary>
    public interface IEntityFactView
    {
        bool Has(FactType factType);

        bool Has<TFact>()
            where TFact : struct, IFact;

        bool TryGetLatest<TFact>(out TFact fact)
            where TFact : struct, IFact;

        ReadOnlySpan<TFact> All<TFact>()
            where TFact : struct, IFact;

        bool HasAll(FactMask requiredFacts);
    }
}
