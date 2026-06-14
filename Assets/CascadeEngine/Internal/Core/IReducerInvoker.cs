#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal untyped bridge from queued facts to typed reducers.
    /// </summary>
    internal interface IReducerInvoker
    {
        Type FactType { get; }

        void Reduce(FactSimulation simulation, in QueuedFact fact);

        void DisposeRegistration();
    }
}
