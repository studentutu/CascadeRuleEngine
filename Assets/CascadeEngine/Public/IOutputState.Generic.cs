#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Ergonomic allocation-free output-state contract. Implement only typed value equality.
    /// </summary>
    public interface IOutputState<TState> : IOutputState, IEquatable<TState>
        where TState : struct, IOutputState<TState>
    {
    }
}
