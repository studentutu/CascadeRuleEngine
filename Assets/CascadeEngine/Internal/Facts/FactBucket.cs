#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed tick-local fact storage for one fact type.
    /// </summary>
    internal sealed class FactBucket<TFact> : IFactBucket
        where TFact : struct, IFact
    {
        private readonly DenseEntityObjectStore<EntityFactList<TFact>> _factsByEntity;
        private readonly DenseEntitySet _touchedEntities;
        private readonly EntityFactList<TFact> _globalFacts;
        private int _factCapacityPerEntity;
        private bool _globalTouched;

        public FactBucket(int entityCapacity, int factCapacityPerEntity)
        {
            _factCapacityPerEntity = NormalizeCapacity(factCapacityPerEntity);
            _globalFacts = new EntityFactList<TFact>(_factCapacityPerEntity);
            _factsByEntity = new DenseEntityObjectStore<EntityFactList<TFact>>(
                CreateFactList,
                entityCapacity);
            _touchedEntities = new DenseEntitySet(entityCapacity);
        }

        public CascadeTypeId FactId => CascadeTypeIdentity.RequireId<TFact>();
        public int EntityCapacity => _factsByEntity.Capacity;
        public int TouchedEntityCapacity => _touchedEntities.Capacity;

        internal bool Contains(EntityRef entity, in TFact fact)
        {
            return TryGetList(entity, out var facts) && facts.Contains(in fact);
        }

        internal int Add(EntityRef entity, in TFact fact)
        {
            var facts = GetOrCreateList(entity);
            if (facts.Count == 0)
            {
                TrackTouched(entity);
            }

            return facts.Add(in fact);
        }

        public bool Has(EntityRef entity)
            => TryGetList(entity, out var facts) && facts.Count > 0;

        public int CountFor(EntityRef entity)
            => TryGetList(entity, out var facts) ? facts.Count : 0;

        internal bool TryGetLatest(EntityRef entity, out TFact fact)
        {
            if (TryGetList(entity, out var facts))
            {
                return facts.TryGetLatest(out fact);
            }

            fact = default;
            return false;
        }

        internal ReadOnlySpan<TFact> All(EntityRef entity)
        {
            if (TryGetList(entity, out var facts))
            {
                return facts.AsSpan();
            }

            return ReadOnlySpan<TFact>.Empty;
        }

        internal ref readonly TFact Get(EntityRef entity, int index)
            => ref GetRequiredList(entity).Get(index);

        public void EnsureEntityCapacity(int entityCapacity)
        {
            _factsByEntity.EnsureCapacity(entityCapacity);
            _touchedEntities.EnsureCapacity(entityCapacity);
        }

        public void Warmup(int entityCapacity, int factCapacityPerEntity)
        {
            EnsureEntityCapacity(entityCapacity);
            var normalizedFactCapacity = NormalizeCapacity(factCapacityPerEntity);
            if (normalizedFactCapacity > _factCapacityPerEntity)
            {
                _factCapacityPerEntity = normalizedFactCapacity;
                _globalFacts.EnsureCapacity(_factCapacityPerEntity);
            }

            for (var i = 0; i < entityCapacity; i++)
            {
                _factsByEntity.GetOrCreate(new EntityRef(i)).EnsureCapacity(_factCapacityPerEntity);
            }
        }

        public int MinimumFactListCapacity(int entityCapacity)
        {
            var minimum = _globalFacts.Capacity;
            for (var i = 0; i < entityCapacity; i++)
            {
                if (!_factsByEntity.TryGet(new EntityRef(i), out var facts))
                {
                    return 0;
                }

                if (facts.Capacity < minimum)
                {
                    minimum = facts.Capacity;
                }
            }

            return minimum;
        }

        public void Clear()
        {
            for (var i = 0; i < _touchedEntities.Count; i++)
            {
                if (_factsByEntity.TryGet(_touchedEntities[i], out var facts))
                {
                    facts.Clear();
                }
            }

            _touchedEntities.Clear();

            if (_globalTouched)
            {
                _globalFacts.Clear();
                _globalTouched = false;
            }
        }

        private EntityFactList<TFact> GetOrCreateList(EntityRef entity)
        {
            if (entity.IsGlobal)
            {
                return _globalFacts;
            }

            return _factsByEntity.GetOrCreate(entity);
        }

        private bool TryGetList(EntityRef entity, out EntityFactList<TFact> facts)
        {
            if (entity.IsGlobal)
            {
                facts = _globalFacts;
                return facts.Count > 0;
            }

            return _factsByEntity.TryGet(entity, out facts) && facts.Count > 0;
        }

        private EntityFactList<TFact> GetRequiredList(EntityRef entity)
        {
            if (entity.IsGlobal)
            {
                return _globalFacts;
            }

            if (_factsByEntity.TryGet(entity, out var facts))
            {
                return facts;
            }

            throw new InvalidOperationException($"Queued fact storage is missing for entity '{entity}'.");
        }

        private void TrackTouched(EntityRef entity)
        {
            if (entity.IsGlobal)
            {
                _globalTouched = true;
                return;
            }

            _touchedEntities.Add(entity);
        }

        private EntityFactList<TFact> CreateFactList()
            => new EntityFactList<TFact>(_factCapacityPerEntity);

        private static int NormalizeCapacity(int capacity)
            => Math.Max(capacity, 1);
    }
}
