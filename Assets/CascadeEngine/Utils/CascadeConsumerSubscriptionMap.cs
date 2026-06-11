#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Property-to-consumer subscription table used after commit to fan out published properties.
    /// </summary>
    public sealed class CascadeConsumerSubscriptionMap
    {
        private readonly Dictionary<int, List<CascadeConsumerSubscription>> _subscriptionsByProperty =
            new Dictionary<int, List<CascadeConsumerSubscription>>();

        private readonly HashSet<long> _registeredPairs = new HashSet<long>();

        public int Count { get; private set; }

        /// <summary>
        /// Range: property key and consumer key. Condition: one subscription pair per property. Output: consumer receives work when property is published.
        /// </summary>
        public void Register(
            CascadePropertyKey property,
            CascadeConsumerKey consumer,
            CascadeConsumerRelevanceFunction? isRelevant = null)
        {
            if (!_registeredPairs.Add(CreatePairKey(property, consumer)))
            {
                throw new InvalidOperationException(
                    $"Consumer '{consumer.Name}' already subscribed to property '{property.Name}'.");
            }

            if (!_subscriptionsByProperty.TryGetValue(property.Index, out var subscriptions))
            {
                subscriptions = new List<CascadeConsumerSubscription>();
                _subscriptionsByProperty.Add(property.Index, subscriptions);
            }

            subscriptions.Add(new CascadeConsumerSubscription(consumer, isRelevant));
            Count++;
        }

        /// <summary>
        /// Range: published properties from one tick. Condition: commit phase completed. Output: dirty consumer work for every relevant subscriber.
        /// </summary>
        public void Publish(
            CascadePublishedPropertySet publishedProperties,
            CascadeEntityStateStore entities,
            CascadeDirtyConsumerSet dirtyConsumers)
        {
            for (var i = 0; i < publishedProperties.Count; i++)
            {
                var publishedProperty = publishedProperties[i];
                if (!_subscriptionsByProperty.TryGetValue(publishedProperty.Property.Index, out var subscriptions))
                {
                    continue;
                }

                var entity = entities.Get(publishedProperty.EntityId);
                for (var subscriptionIndex = 0; subscriptionIndex < subscriptions.Count; subscriptionIndex++)
                {
                    var subscription = subscriptions[subscriptionIndex];
                    if (subscription.IsRelevant != null && !subscription.IsRelevant(entity))
                    {
                        continue;
                    }

                    dirtyConsumers.Mark(subscription.Consumer, publishedProperty.EntityId);
                }
            }
        }

        private static long CreatePairKey(CascadePropertyKey property, CascadeConsumerKey consumer)
            => ((long)property.Index << 32) ^ (uint)consumer.Index;
    }
}
