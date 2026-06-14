#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed fact bucket factory for one fact id.
    /// </summary>
    internal sealed class FactBucketFactory<TFact> : IFactBucketFactory
        where TFact : struct, IFact
    {
        internal static readonly FactBucketFactory<TFact> Instance = new FactBucketFactory<TFact>();

        private FactBucketFactory()
        {
        }

        public CascadeTypeId Id => CascadeTypeIdentity.RequireId<TFact>();
        public string DebugName => CascadeTypeIdentity<TFact>.DebugName;

        public IFactBucket Create(int entityCapacity, int factCapacityPerEntity)
            => new FactBucket<TFact>(entityCapacity, factCapacityPerEntity);
    }
}
