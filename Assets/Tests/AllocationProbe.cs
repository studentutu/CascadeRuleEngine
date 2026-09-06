#nullable enable

using System;
using Unity.Profiling;

namespace CascadeEngineApi.Tests
{
    /// <summary>
    /// Synchronous Unity allocation-event measurement. Does not depend on Mono's unsupported per-thread byte counter or Unity lifecycle callbacks.
    /// </summary>
    internal static class AllocationProbe
    {
        internal static long Count(Action action)
        {
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            if (!recorder.Valid) throw new InvalidOperationException("Unity GC.Alloc recorder is unavailable.");
            action();
            recorder.Stop();
            return recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
        }
    }
}
