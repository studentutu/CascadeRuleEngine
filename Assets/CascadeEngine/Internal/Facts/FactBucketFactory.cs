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

        public CascadeTypeId Id => CascadeTypeId.FromName(DebugName);
        public string DebugName => typeof(TFact).Name;

        public void Register(CascadeTypeCatalog catalog)
            => catalog.Register<TFact>();

        public void BindRoute(FactFeatureRegistry registry, CascadeTypeId id)
            => FactEmitRouteCache<TFact>.Add(registry, id);

        public void UnbindRoute(FactFeatureRegistry registry)
            => FactEmitRouteCache<TFact>.Remove(registry);

        public IFactBucket Create(
            CascadeTypeId id,
            int entityCapacity,
            int factCapacityPerEntity,
            FactListCapacityMode factListCapacityMode)
            => new FactBucket<TFact>(id, entityCapacity, factCapacityPerEntity, factListCapacityMode);
    }
}
