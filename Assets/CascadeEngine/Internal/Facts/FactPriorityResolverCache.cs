#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Per-fact typed priority resolver cache used by emit without erased resolver casts.
    /// </summary>
    internal static class FactPriorityResolverCache<TFact>
        where TFact : struct, IFact
    {
        private static Dictionary<FactFeatureRegistry, IFactPriorityResolver<TFact>>? Resolvers;

        internal static void Add(FactFeatureRegistry registry, IFactPriorityResolver<TFact> resolver)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            var resolvers = Resolvers;
            if (resolvers == null)
            {
                resolvers = new Dictionary<FactFeatureRegistry, IFactPriorityResolver<TFact>>();
                Resolvers = resolvers;
            }

            resolvers.Add(registry, resolver);
        }

        internal static void Remove(FactFeatureRegistry registry)
        {
            var resolvers = Resolvers;
            if (resolvers == null)
            {
                return;
            }

            resolvers.Remove(registry);
            if (resolvers.Count == 0)
            {
                Resolvers = null;
            }
        }

        internal static FactPriority Resolve(FactFeatureRegistry registry, in TFact fact)
        {
            var resolvers = Resolvers;
            if (resolvers != null && resolvers.TryGetValue(registry, out var resolver))
            {
                return resolver.Resolve(in fact);
            }

            return FactPriority.Normal;
        }
    }
}
