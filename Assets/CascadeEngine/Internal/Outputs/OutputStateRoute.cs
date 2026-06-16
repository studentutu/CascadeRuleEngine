#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed output route for one simulation-owned state bucket.
    /// </summary>
    internal sealed class OutputStateRoute<TState>
        where TState : struct, IOutputState
    {
        internal OutputStateRoute(OutputState<TState> output, StateBucket<TState> bucket)
        {
            Output = output;
            Bucket = bucket;
        }

        internal OutputState<TState> Output { get; }
        internal StateBucket<TState> Bucket { get; }
    }
}
