#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// One property subscriber and optional entity relevance predicate.
    /// </summary>
    public readonly struct CascadeConsumerSubscription
    {
        public CascadeConsumerSubscription(
            CascadeConsumerKey consumer,
            CascadeConsumerRelevanceFunction? isRelevant)
        {
            Consumer = consumer;
            IsRelevant = isRelevant;
        }

        public CascadeConsumerKey Consumer { get; }
        public CascadeConsumerRelevanceFunction? IsRelevant { get; }
    }
}
