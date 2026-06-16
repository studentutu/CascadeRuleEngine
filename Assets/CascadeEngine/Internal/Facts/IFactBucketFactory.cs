#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal typed factory used to warm fact buckets without object identity routing.
    /// </summary>
    internal interface IFactBucketFactory
    {
        CascadeTypeId Id { get; }
        string DebugName { get; }

        void Register(CascadeTypeCatalog catalog);

        void BindRoute(FactFeatureRegistry registry, CascadeTypeId id);

        void UnbindRoute(FactFeatureRegistry registry);

        void BindAffectedOutput(FactFeatureRegistry registry, IOutputRegistration output);

        IFactBucket Create(
            CascadeTypeId id,
            int entityCapacity,
            int factCapacityPerEntity,
            FactListCapacityMode factListCapacityMode);
    }
}
