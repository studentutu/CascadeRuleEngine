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
        bool CanCreatePriorityResolver { get; }

        void Register(CascadeTypeCatalog catalog);

        object CreatePriorityResolver();

        IFactBucket Create(
            CascadeTypeId id,
            int entityCapacity,
            int factCapacityPerEntity,
            FactListCapacityMode factListCapacityMode);
    }
}
