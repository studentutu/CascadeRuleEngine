#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Fact payload that exposes durable conflict priority for commit-stage selection.
    /// </summary>
    public interface IPrioritizedFact
    {
        int Priority { get; }
    }
}
