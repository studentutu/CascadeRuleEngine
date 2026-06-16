#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// One accepted fact waiting for immediate reducer dispatch.
    /// </summary>
    internal readonly struct QueuedFact
    {
        internal QueuedFact(
            EntityRef entity,
            CascadeTypeId factId,
            IFactReduceRoute reduceRoute,
            IFactBucket bucket,
            int factIndex,
            int depth)
        {
            Entity = entity;
            FactId = factId;
            ReduceRoute = reduceRoute;
            Bucket = bucket;
            FactIndex = factIndex;
            Depth = depth;
        }

        internal EntityRef Entity { get; }
        internal CascadeTypeId FactId { get; }
        internal IFactReduceRoute ReduceRoute { get; }
        internal IFactBucket Bucket { get; }
        internal int FactIndex { get; }
        internal int Depth { get; }
    }
}
