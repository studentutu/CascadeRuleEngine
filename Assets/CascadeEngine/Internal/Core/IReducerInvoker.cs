#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal untyped bridge from queued facts to typed reducers.
    /// </summary>
    internal interface IReducerInvoker
    {
        string DebugName { get; }

        void BindRoute(FactFeatureRegistry registry);

        void Reduce(FactSimulation simulation, in QueuedFact fact);

        void DisposeRegistration();
    }
}
