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

        /// <summary>
        /// Returns all distinct accepted facts of one type visible to the current reducer or committer context.
        /// </summary>
        ReadOnlySpan<TFact> All<TFact>()
            where TFact : struct, IFact;
    }
}
