#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Ergonomic allocation-free fact contract. Implement only typed payload equality; no-op disposal is inherited from IFact.
    /// </summary>
    public interface IFact<TFact> : IFact, IEquatable<TFact>
        where TFact : struct, IFact<TFact>
    {
    }
}
