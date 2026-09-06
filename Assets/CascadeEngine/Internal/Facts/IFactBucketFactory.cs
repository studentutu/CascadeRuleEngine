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
        IFactReduceRoute ReduceRoute(FactFeatureRegistry registry);

        void Register(CascadeTypeCatalog catalog);

        void BindRoute(FactFeatureRegistry registry, CascadeTypeId id);

        void UnbindRoute(FactFeatureRegistry registry);

        void BindAffectedOutput(FactFeatureRegistry registry, IOutputRegistration output);

        void MarkNegativeConditionInput(FactFeatureRegistry registry);

        IFactBucket Create(
            CascadeTypeId id,
            int entityCapacity,
            int factCapacityPerEntity,
            FactListCapacityMode factListCapacityMode);
    }
}
