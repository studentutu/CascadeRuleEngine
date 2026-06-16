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
        private readonly Dictionary<CascadeTypeId, IFactBucket> _buckets = new Dictionary<CascadeTypeId, IFactBucket>();
        private readonly List<QueuedFact> _queue = new List<QueuedFact>();
        private readonly DenseEntitySet _touchedEntities = new DenseEntitySet(64);
        private readonly DenseEntityCounter _factCountsByEntity = new DenseEntityCounter(64);
        private readonly DenseEntityObjectStore<EntityFactRouteList> _factRoutesByEntity;
        private int _entityCapacity = 64;
        private int _factCapacityPerEntity = 4;
        private int _factRouteCapacityPerEntity = 4;
        private FactListCapacityMode _factListCapacityMode = FactListCapacityMode.GrowOnDemand;
        private long _nextSequence;

        internal FactStore()
        {
            _factRoutesByEntity = new DenseEntityObjectStore<EntityFactRouteList>(
                CreateFactRouteList,
                _entityCapacity);
        }

        internal int AcceptedFacts { get; private set; }
        internal int DeduplicatedFacts { get; private set; }
        internal int RejectedDestroyedEntityFacts { get; private set; }
        internal int TouchedEntityCount => _touchedEntities.Count;
        internal int QueueCapacity => _queue.Capacity;
        internal int TouchedEntityCapacity => _touchedEntities.Capacity;
        internal int FactCounterEntityCapacity => _factCountsByEntity.Capacity;
        internal int BucketCount => _buckets.Count;

        internal bool HasQueuedFacts => _queue.Count > 0;

        internal void Warmup(
            int entityCapacity,
            int factQueueCapacity,
            int factCapacityPerEntity,
            FactListCapacityMode factListCapacityMode,
            FactType[] knownFactTypes)
        {
            var normalizedEntityCapacity = NormalizeCapacity(entityCapacity);
            var normalizedFactCapacity = NormalizeCapacity(factCapacityPerEntity);

            EnsureEntityCapacity(normalizedEntityCapacity);
            WarmupFactRouteLists(normalizedEntityCapacity, NormalizeCapacity(knownFactTypes.Length));
            if (_queue.Capacity < factQueueCapacity)
            {
                _queue.Capacity = factQueueCapacity;
            }

            if (normalizedFactCapacity > _factCapacityPerEntity)
            {
                _factCapacityPerEntity = normalizedFactCapacity;
            }

            _factListCapacityMode = factListCapacityMode;

            for (var i = 0; i < knownFactTypes.Length; i++)
            {
                if (!knownFactTypes[i].CanCreateBucket)
                {
                    continue;
                }

                GetOrCreateBucket(knownFactTypes[i]).Warmup(
                    normalizedEntityCapacity,
                    _factCapacityPerEntity,
                    _factListCapacityMode);
            }
        }

        internal void EnsureEntityCapacity(int entityCapacity)
        {
            if (entityCapacity <= _entityCapacity)
            {
                return;
            }

            _entityCapacity = entityCapacity;
            _touchedEntities.EnsureCapacity(entityCapacity);
            _factCountsByEntity.EnsureCapacity(entityCapacity);
            _factRoutesByEntity.EnsureCapacity(entityCapacity);

            foreach (var bucket in _buckets.Values)
            {
                bucket.EnsureEntityCapacity(entityCapacity);
            }
        }

        internal int MinimumBucketEntityCapacity()
        {
            var minimum = int.MaxValue;
            foreach (var bucket in _buckets.Values)
            {
                if (bucket.EntityCapacity < minimum)
                {
                    minimum = bucket.EntityCapacity;
                }
            }

            return minimum == int.MaxValue ? 0 : minimum;
        }

        internal int MinimumBucketTouchedEntityCapacity()
        {
            var minimum = int.MaxValue;
            foreach (var bucket in _buckets.Values)
            {
                if (bucket.TouchedEntityCapacity < minimum)
                {
                    minimum = bucket.TouchedEntityCapacity;
                }
            }

            return minimum == int.MaxValue ? 0 : minimum;
        }

        internal int MinimumFactListCapacity(int entityCapacity)
        {
            var minimum = int.MaxValue;
            foreach (var bucket in _buckets.Values)
            {
                var bucketMinimum = bucket.MinimumFactListCapacity(entityCapacity);
                if (bucketMinimum < minimum)
                {
                    minimum = bucketMinimum;
                }
            }

            return minimum == int.MaxValue ? 0 : minimum;
        }

        internal bool Emit<TFact>(
            EntityStore entities,
            EntityRef entity,
            FactEmitRoute<TFact> route,
            in TFact fact,
            FactPriority priority,
            int depth,
            FactGuardrails guardrails)
            where TFact : struct, IFact
        {
            var factId = route.FactId;
            if (entities.IsDestroyed(entity))
            {
                RejectedDestroyedEntityFacts++;
                return false;
            }

            if (depth > guardrails.MaxCausalDepth)
            {
                throw new InvalidOperationException($"Fact causal depth '{depth}' exceeded limit '{guardrails.MaxCausalDepth}'.");
            }

            var bucket = GetOrCreateBucket<TFact>(factId);
            if (bucket.Contains(entity, in fact))
            {
                DeduplicatedFacts++;
                return false;
            }

            var factCountForType = bucket.CountFor(entity);
            if (factCountForType >= guardrails.MaxFactsPerTypePerEntity)
            {
                throw new InvalidOperationException($"Fact type id '{factId}' exceeded per-entity limit '{guardrails.MaxFactsPerTypePerEntity}' for entity '{entity}'.");
            }

            var factIndex = bucket.Add(entity, in fact);
            AcceptedFacts++;

            TrackTouchedEntity(entity);
            if (factCountForType == 0)
            {
                TrackTouchedFactRoute(entity, route);
            }

            if (IncrementFactCount(entity) > guardrails.MaxFactsPerEntity)
            {
                throw new InvalidOperationException($"Entity '{entity}' exceeded per-tick fact limit '{guardrails.MaxFactsPerEntity}'.");
            }

            _queue.Add(new QueuedFact(
                entity,
                factId,
                route,
                bucket,
                factIndex,
                priority,
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

        internal bool Has(EntityRef entity, CascadeTypeId factId)
            => _buckets.TryGetValue(factId, out var bucket) && bucket.Has(entity);

        internal bool TryGetLatest<TFact>(EntityRef entity, CascadeTypeId factId, out TFact fact)
            where TFact : struct, IFact
        {
            if (_buckets.TryGetValue(factId, out var bucket))
            {
                return ((FactBucket<TFact>)bucket).TryGetLatest(entity, out fact);
            }

            fact = default;
            return false;
        }

        internal ReadOnlySpan<TFact> All<TFact>(EntityRef entity, CascadeTypeId factId)
            where TFact : struct, IFact
        {
            if (_buckets.TryGetValue(factId, out var bucket))
            {
                return ((FactBucket<TFact>)bucket).All(entity);
            }

            return ReadOnlySpan<TFact>.Empty;
        }

        internal bool HasAll(EntityRef entity, CascadeTypeId[] requiredFacts)
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

        internal void CopyTouchedEntities(EntityRefBuffer destination, out int count)
            => _touchedEntities.CopyTo(destination, out count);

        internal ReadOnlySpan<IFactCommitRoute> FactRoutes(EntityRef entity)
        {
            if (_factRoutesByEntity.TryGet(entity, out var routes))
            {
                return routes.AsSpan();
            }

            return ReadOnlySpan<IFactCommitRoute>.Empty;
        }

        internal void Clear()
        {
            foreach (var bucket in _buckets.Values)
            {
                bucket.Clear();
            }

            _queue.Clear();
            _factCountsByEntity.Clear(_touchedEntities);
            ClearTouchedFactRoutes();
            _touchedEntities.Clear();
            _nextSequence = 0;
            AcceptedFacts = 0;
            DeduplicatedFacts = 0;
            RejectedDestroyedEntityFacts = 0;
        }

        internal void DisposeStore()
        {
            Clear();
            _buckets.Clear();
            _queue.Clear();
            _queue.Capacity = 0;
        }

        private FactBucket<TFact> GetOrCreateBucket<TFact>(CascadeTypeId factId)
            where TFact : struct, IFact
        {
            if (_buckets.TryGetValue(factId, out var bucket))
            {
                return (FactBucket<TFact>)bucket;
            }

            var typedBucket = new FactBucket<TFact>(
                factId,
                _entityCapacity,
                _factCapacityPerEntity,
                _factListCapacityMode);
            _buckets.Add(factId, typedBucket);
            return typedBucket;
        }

        private IFactBucket GetOrCreateBucket(FactType factType)
        {
            if (_buckets.TryGetValue(factType.Id, out var bucket))
            {
                return bucket;
            }

            var typedBucket = factType.CreateBucket(
                _entityCapacity,
                _factCapacityPerEntity,
                _factListCapacityMode);
            _buckets.Add(factType.Id, typedBucket);
            return typedBucket;
        }

        private void TrackTouchedEntity(EntityRef entity)
        {
            _touchedEntities.Add(entity);
        }

        private void TrackTouchedFactRoute(EntityRef entity, IFactCommitRoute route)
        {
            _factRoutesByEntity.GetOrCreate(entity).Add(route);
        }

        private int IncrementFactCount(EntityRef entity)
        {
            return _factCountsByEntity.Increment(entity);
        }

        private void WarmupFactRouteLists(int entityCapacity, int factTypeCapacity)
        {
            if (factTypeCapacity > _factRouteCapacityPerEntity)
            {
                _factRouteCapacityPerEntity = factTypeCapacity;
            }

            for (var i = 0; i < entityCapacity; i++)
            {
                _factRoutesByEntity.GetOrCreate(new EntityRef(i)).EnsureCapacity(_factRouteCapacityPerEntity);
            }
        }

        private void ClearTouchedFactRoutes()
        {
            for (var i = 0; i < _touchedEntities.Count; i++)
            {
                if (_factRoutesByEntity.TryGet(_touchedEntities[i], out var routes))
                {
                    routes.Clear();
                }
            }
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

        private static int NormalizeCapacity(int capacity)
            => Math.Max(capacity, 1);

        private EntityFactRouteList CreateFactRouteList()
            => new EntityFactRouteList(_factRouteCapacityPerEntity);
    }
}
