#nullable enable

using System;

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
        public bool CanCreatePriorityResolver => typeof(IPrioritizedFact).IsAssignableFrom(typeof(TFact));

        public void Register(CascadeTypeCatalog catalog)
            => catalog.Register<TFact>();

        public object CreatePriorityResolver()
        {
            if (!CanCreatePriorityResolver)
            {
                throw new InvalidOperationException($"Fact '{DebugName}' does not implement '{nameof(IPrioritizedFact)}'.");
            }

            var resolverType = typeof(PrioritizedFactPriorityResolver<>).MakeGenericType(typeof(TFact));
            return Activator.CreateInstance(resolverType);
        }

        public IFactBucket Create(
            CascadeTypeId id,
            int entityCapacity,
            int factCapacityPerEntity,
            FactListCapacityMode factListCapacityMode)
            => new FactBucket<TFact>(id, entityCapacity, factCapacityPerEntity, factListCapacityMode);
    }
}
