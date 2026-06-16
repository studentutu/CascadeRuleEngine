#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Per-output typed state route cache used by state access without type-catalog lookup.
    /// </summary>
    internal static class OutputStateRouteCache<TState>
        where TState : struct, IOutputState
    {
        private static Dictionary<FactSimulation, OutputStateRoute<TState>>? Routes;

        internal static void Add(
            FactSimulation simulation,
            OutputState<TState> output,
            StateBucket<TState> bucket)
        {
            if (simulation == null)
            {
                throw new ArgumentNullException(nameof(simulation));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (bucket == null)
            {
                throw new ArgumentNullException(nameof(bucket));
            }

            var routes = Routes;
            if (routes == null)
            {
                routes = new Dictionary<FactSimulation, OutputStateRoute<TState>>();
                Routes = routes;
            }

            routes.Add(simulation, new OutputStateRoute<TState>(output, bucket));
        }

        internal static void Remove(FactSimulation simulation)
        {
            var routes = Routes;
            if (routes == null)
            {
                return;
            }

            routes.Remove(simulation);
            if (routes.Count == 0)
            {
                Routes = null;
            }
        }

        internal static OutputStateRoute<TState> Require(FactSimulation simulation)
        {
            var routes = Routes;
            if (routes != null && routes.TryGetValue(simulation, out var route))
            {
                return route;
            }

            throw new InvalidOperationException(
                $"Output state '{typeof(TState).Name}' is not registered in this simulation feature.");
        }
    }
}
