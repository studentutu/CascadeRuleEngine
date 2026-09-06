#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed payload equality. Implement Dispose explicitly for allocation-free cleanup on Mono; the inherited no-op remains supported but boxes.
    /// </summary>
    public interface IFact<TFact> : IFact, IEquatable<TFact>
        where TFact : struct, IFact<TFact>
    {
    }
}
