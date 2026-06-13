#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Batch reducer that receives only entities made eligible by facts in the current tick.
    /// </summary>
    public interface IBatchTransactionalReducer
    {
        void ReduceBatch(IBatchReduceContext ctx, ReadOnlySpan<EntityRef> entities);
    }
}
