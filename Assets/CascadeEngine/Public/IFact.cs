#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Transient tick-local input or derived consequence. Accepted facts are disposed when tick-local storage clears.
    /// </summary>
    public interface IFact : IDisposable
    {
    }
}
