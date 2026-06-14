#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal typed factory used to warm fact buckets without using System.Type as identity.
    /// </summary>
    internal interface IFactBucketFactory
    {
        CascadeTypeId Id { get; }
        string DebugName { get; }

        IFactBucket Create(int entityCapacity, int factCapacityPerEntity);
    }
}
