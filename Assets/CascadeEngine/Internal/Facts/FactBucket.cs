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
        private readonly EntityFactList<TFact> _globalFacts = new EntityFactList<TFact>();
        private bool _globalTouched;

        internal FactBucket(int entityCapacity)
        {
            _factsByEntity = new DenseEntityObjectStore<EntityFactList<TFact>>(
                CreateFactList,
                entityCapacity);
            _touchedEntities = new DenseEntitySet(entityCapacity);
        }

        public Type FactType => typeof(TFact);

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

        private static EntityFactList<TFact> CreateFactList()
            => new EntityFactList<TFact>();
    }
}
