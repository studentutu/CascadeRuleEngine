#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Built-in lifecycle fact: the entity remains reduction-visible until closure, then all durable output state is deleted.
    /// </summary>
    public readonly struct DeadFact : IFact<DeadFact>
    {
        public bool Equals(DeadFact other)
            => true;

        public void Dispose() { }
    }
}
