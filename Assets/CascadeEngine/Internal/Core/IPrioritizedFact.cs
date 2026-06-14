#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Optional fact contract for queue priority. Registered prioritized structs use an internal typed resolver.
    /// </summary>
    public interface IPrioritizedFact
    {
        FactPriority Priority { get; }
    }
}
