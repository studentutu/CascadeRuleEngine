#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Per-fact typed emit route cache used by emit without type-catalog lookup or erased resolver casts.
    /// </summary>
    internal static class FactEmitRouteCache<TFact>
        where TFact : struct, IFact
    {
        private static Dictionary<FactFeatureRegistry, FactEmitRoute<TFact>>? Routes;

        internal static void Add(FactFeatureRegistry registry, CascadeTypeId factId)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var routes = Routes;
            if (routes == null)
            {
                routes = new Dictionary<FactFeatureRegistry, FactEmitRoute<TFact>>();
                Routes = routes;
            }

            routes.Add(registry, new FactEmitRoute<TFact>(factId));
        }

        internal static void Remove(FactFeatureRegistry registry)
        {
            var routes = Routes;
            if (routes == null)
            {
                return;
            }

            routes.Remove(registry);
            if (routes.Count == 0)
            {
                Routes = null;
            }
        }

        internal static FactEmitRoute<TFact> Require(FactFeatureRegistry registry)
        {
            var routes = Routes;
            if (routes != null && routes.TryGetValue(registry, out var route))
            {
                return route;
            }

            throw new InvalidOperationException(
                $"Fact '{typeof(TFact).Name}' is not registered in this feature. Register it with Reduce<TFact>(), AffectedBy<TFact>(), or ReduceWhen(...) before using it.");
        }
    }
}
