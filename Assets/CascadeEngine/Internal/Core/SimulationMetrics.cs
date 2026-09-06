#nullable enable

namespace CascadeEngineApi
{
    /// <summary>Internal tick timings in Stopwatch ticks; independent of public reducer-work accounting.</summary>
    internal sealed class SimulationMetrics
    {
        internal long MaximumReducerTicks;
        internal long MaximumCommitterTicks;
        internal long PlanningTicks;
        internal long ValidationTicks;
        internal long ApplyTicks;
        internal long CleanupTicks;
        internal int PlanInvalidations;

        internal void Clear()
        {
            MaximumReducerTicks = MaximumCommitterTicks = PlanningTicks = ValidationTicks = ApplyTicks = CleanupTicks = 0;
            PlanInvalidations = 0;
        }
    }
}
