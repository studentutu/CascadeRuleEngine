#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Optional fact contract for queue priority. Facts without it use FactPriority.Normal.
    /// </summary>
    public interface IPrioritizedFact
    {
        FactPriority Priority { get; }
    }
}
