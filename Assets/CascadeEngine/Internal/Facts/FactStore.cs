#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Tick-local fact set, per-entity fact view backing store, and reducer work queue.
    /// </summary>
    internal sealed class FactStore
    {
        private readonly Dictionary<Type, IFactBucket> _buckets = new Dictionary<Type, IFactBucket>();
        private readonly HashSet<FactIdentity> _dedupe = new HashSet<FactIdentity>();
        private readonly List<QueuedFact> _queue = new List<QueuedFact>();
        private readonly List<EntityRef> _touchedEntities = new List<EntityRef>();
        private readonly HashSet<EntityRef> _touchedEntitySet = new HashSet<EntityRef>();
        private long _nextSequence;

        internal int AcceptedFacts { get; private set; }
        internal int DeduplicatedFacts { get; private set; }
        internal int RejectedDestroyedEntityFacts { get; private set; }
        internal int TouchedEntityCount => _touchedEntities.Count;

        internal bool HasQueuedFacts => _queue.Count > 0;

        internal bool Emit<TFact>(
            EntityStore entities,
            EntityRef entity,
            in TFact fact,
            int depth,
            FactGuardrails guardrails)
            where TFact : struct, IFact
        {
            if (!entity.IsGlobal && entities.IsDestroyed(entity))
            {
                RejectedDestroyedEntityFacts++;
                return false;
            }

            if (depth > guardrails.MaxCausalDepth)
            {
                throw new InvalidOperationException($"Fact causal depth '{depth}' exceeded limit '{guardrails.MaxCausalDepth}'.");
            }

            var payload = (object)fact;
            var factType = typeof(TFact);
            var identity = new FactIdentity(entity, factType, payload);
            if (!_dedupe.Add(identity))
            {
                DeduplicatedFacts++;
                return false;
            }

            var bucket = GetOrCreateBucket<TFact>();
            if (bucket.CountFor(entity) >= guardrails.MaxFactsPerTypePerEntity)
            {
                throw new InvalidOperationException($"Fact type '{factType.Name}' exceeded per-entity limit '{guardrails.MaxFactsPerTypePerEntity}' for entity '{entity}'.");
            }

            bucket.Add(entity, in fact);
            AcceptedFacts++;

            if (!entity.IsGlobal)
            {
                TrackTouchedEntity(entity);
                if (CountFactsForEntity(entity) > guardrails.MaxFactsPerEntity)
                {
                    throw new InvalidOperationException($"Entity '{entity}' exceeded per-tick fact limit '{guardrails.MaxFactsPerEntity}'.");
                }
            }

            _queue.Add(new QueuedFact(
                entity,
                factType,
                payload,
                ResolvePriority(payload),
                depth,
                _nextSequence));
            _nextSequence++;
            return true;
        }

        internal bool TryPop(BudgetMode mode, out QueuedFact fact)
        {
            if (_queue.Count == 0)
            {
                fact = default;
                return false;
            }

            var index = SelectIndex(mode);
            fact = _queue[index];
            _queue.RemoveAt(index);
            return true;
        }

        internal bool Has(EntityRef entity, Type factType)
            => _buckets.TryGetValue(factType, out var bucket) && bucket.Has(entity);

        internal bool TryGetLatest<TFact>(EntityRef entity, out TFact fact)
            where TFact : struct, IFact
        {
            if (_buckets.TryGetValue(typeof(TFact), out var bucket))
            {
                return ((FactBucket<TFact>)bucket).TryGetLatest(entity, out fact);
            }

            fact = default;
            return false;
        }

        internal ReadOnlySpan<TFact> All<TFact>(EntityRef entity)
            where TFact : struct, IFact
        {
            if (_buckets.TryGetValue(typeof(TFact), out var bucket))
            {
                return ((FactBucket<TFact>)bucket).All(entity);
            }

            return ReadOnlySpan<TFact>.Empty;
        }

        internal bool HasAll(EntityRef entity, Type[] requiredFacts)
        {
            for (var i = 0; i < requiredFacts.Length; i++)
            {
                if (!Has(entity, requiredFacts[i]))
                {
                    return false;
                }
            }

            return true;
        }

        internal void CopyTouchedEntities(EntityRef[] destination, out int count)
        {
            count = _touchedEntities.Count;
            for (var i = 0; i < count; i++)
            {
                destination[i] = _touchedEntities[i];
            }
        }

        internal void Clear()
        {
            foreach (var bucket in _buckets.Values)
            {
                bucket.Clear();
            }

            _dedupe.Clear();
            _queue.Clear();
            _touchedEntities.Clear();
            _touchedEntitySet.Clear();
            _nextSequence = 0;
            AcceptedFacts = 0;
            DeduplicatedFacts = 0;
            RejectedDestroyedEntityFacts = 0;
        }

        private FactBucket<TFact> GetOrCreateBucket<TFact>()
            where TFact : struct, IFact
        {
            var factType = typeof(TFact);
            if (_buckets.TryGetValue(factType, out var bucket))
            {
                return (FactBucket<TFact>)bucket;
            }

            var typedBucket = new FactBucket<TFact>();
            _buckets.Add(factType, typedBucket);
            return typedBucket;
        }

        private void TrackTouchedEntity(EntityRef entity)
        {
            if (!_touchedEntitySet.Add(entity))
            {
                return;
            }

            _touchedEntities.Add(entity);
        }

        private int CountFactsForEntity(EntityRef entity)
        {
            var count = 0;
            foreach (var bucket in _buckets.Values)
            {
                count += bucket.CountFor(entity);
            }

            return count;
        }

        private int SelectIndex(BudgetMode mode)
        {
            if (mode == BudgetMode.Fifo)
            {
                return 0;
            }

            if (mode != BudgetMode.PriorityFirst)
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported budget mode.");
            }

            var bestIndex = 0;
            var bestPriority = _queue[0].Priority;
            var bestSequence = _queue[0].Sequence;
            for (var i = 1; i < _queue.Count; i++)
            {
                var queued = _queue[i];
                if (queued.Priority > bestPriority
                    || queued.Priority == bestPriority && queued.Sequence < bestSequence)
                {
                    bestIndex = i;
                    bestPriority = queued.Priority;
                    bestSequence = queued.Sequence;
                }
            }

            return bestIndex;
        }

        private static FactPriority ResolvePriority(object payload)
        {
            if (payload is IPrioritizedFact prioritized)
            {
                return prioritized.Priority;
            }

            return FactPriority.Normal;
        }
    }
}
