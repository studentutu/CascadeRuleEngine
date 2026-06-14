#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal untyped bridge from queued facts to typed reducers.
    /// </summary>
    internal interface IReducerInvoker
    {
        CascadeTypeId FactId { get; }

        void Reduce(FactSimulation simulation, in QueuedFact fact);

        void DisposeRegistration();
    }
}
