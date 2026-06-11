#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Optional subscriber relevance predicate checked during property publish fanout.
    /// </summary>
    public delegate bool CascadeConsumerRelevanceFunction(CascadeEntityState entity);
}
