#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Transient tick-local input or derived consequence. The store owns accepted payloads through commit and disposes them once at closure, failure, or teardown.
    /// Fact copies and views borrow that payload; they must not dispose it or retain it in output state.
    /// </summary>
    public interface IFact : IDisposable
    {
        void IDisposable.Dispose()
        {
        }
    }
}
