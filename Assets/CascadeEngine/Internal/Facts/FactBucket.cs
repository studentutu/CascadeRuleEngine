#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed tick-local fact slab: touched entities own contiguous slices without per-entity list objects.
    /// </summary>
    internal sealed class FactBucket<TFact> : IFactBucket
        where TFact : struct, IFact
    {
        private static readonly EqualityComparer<TFact> Comparer = EqualityComparer<TFact>.Default;

        private readonly CascadeTypeId _factId;
        private int[] _slabByEntity;
        private EntityRef[] _entitiesBySlab;
        private int[] _countsBySlab;
        private TFact[] _items;
        private int _factCapacityPerEntity;
        private int _slabCount;
        private FactListCapacityMode _factListCapacityMode;

        public FactBucket(
            CascadeTypeId factId,
            int entityCapacity,
            int factCapacityPerEntity,
            FactListCapacityMode factListCapacityMode)
        {
            _factId = factId;
            var normalizedEntityCapacity = NormalizeCapacity(entityCapacity);
            _factCapacityPerEntity = NormalizeCapacity(factCapacityPerEntity);
            _factListCapacityMode = factListCapacityMode;
            _slabByEntity = new int[normalizedEntityCapacity];
            _entitiesBySlab = new EntityRef[normalizedEntityCapacity];
            _countsBySlab = new int[normalizedEntityCapacity];
            _items = new TFact[PayloadCapacity(normalizedEntityCapacity, _factCapacityPerEntity)];
        }

        public CascadeTypeId FactId => _factId;
        public int EntityCapacity => _slabByEntity.Length;
        public int TouchedEntityCapacity => _entitiesBySlab.Length;
        internal int ActiveSlabCount => _slabCount;

        internal bool Contains(EntityRef entity, in TFact fact)
        {
            if (!TryGetSlab(entity, out var slab))
            {
                return false;
            }

            var count = _countsBySlab[slab];
            var offset = Offset(slab);
            for (var i = 0; i < count; i++)
            {
                if (Comparer.Equals(_items[offset + i], fact))
                {
                    return true;
                }
            }

            return false;
        }

        internal int Add(EntityRef entity, in TFact fact)
        {
            var slab = GetOrCreateSlab(entity);
            var count = _countsBySlab[slab];
            if (count == _factCapacityPerEntity)
            {
                GrowForAdd();
            }

            _items[Offset(slab) + count] = fact;
            _countsBySlab[slab] = count + 1;
            return count;
        }

        public bool Has(EntityRef entity)
            => TryGetSlab(entity, out var slab) && _countsBySlab[slab] > 0;

        public int CountFor(EntityRef entity)
            => TryGetSlab(entity, out var slab) ? _countsBySlab[slab] : 0;

        internal bool TryGetLatest(EntityRef entity, out TFact fact)
        {
            if (TryGetSlab(entity, out var slab))
            {
                var count = _countsBySlab[slab];
                if (count > 0)
                {
                    fact = _items[Offset(slab) + count - 1];
                    return true;
                }
            }

            fact = default;
            return false;
        }

        internal ReadOnlySpan<TFact> All(EntityRef entity)
        {
            return TryGetSlab(entity, out var slab)
                ? new ReadOnlySpan<TFact>(_items, Offset(slab), _countsBySlab[slab])
                : ReadOnlySpan<TFact>.Empty;
        }

        internal ref readonly TFact Get(EntityRef entity, int index)
        {
            if (!TryGetSlab(entity, out var slab) || (uint)index >= _countsBySlab[slab])
            {
                throw new InvalidOperationException($"Queued fact storage is missing for entity '{entity}'.");
            }

            return ref _items[Offset(slab) + index];
        }

        public void EnsureEntityCapacity(int entityCapacity)
        {
            var normalized = NormalizeCapacity(entityCapacity);
            if (normalized <= EntityCapacity)
            {
                return;
            }

            ResizeStorage(normalized, _factCapacityPerEntity);
        }

        public void Warmup(
            int entityCapacity,
            int factCapacityPerEntity,
            FactListCapacityMode factListCapacityMode)
        {
            var normalizedEntityCapacity = Math.Max(EntityCapacity, NormalizeCapacity(entityCapacity));
            var normalizedFactCapacity = Math.Max(
                _factCapacityPerEntity,
                NormalizeCapacity(factCapacityPerEntity));
            if (normalizedEntityCapacity != EntityCapacity
                || normalizedFactCapacity != _factCapacityPerEntity)
            {
                ResizeStorage(normalizedEntityCapacity, normalizedFactCapacity);
            }

            _factListCapacityMode = factListCapacityMode;
        }

        public int MinimumFactListCapacity(int entityCapacity)
        {
            var normalized = NormalizeCapacity(entityCapacity);
            return normalized <= EntityCapacity
                && _items.Length >= PayloadCapacity(normalized, _factCapacityPerEntity)
                    ? _factCapacityPerEntity
                    : 0;
        }

        public void Clear()
        {
            for (var slab = 0; slab < _slabCount; slab++)
            {
                var entity = _entitiesBySlab[slab];
                var count = _countsBySlab[slab];
                var offset = Offset(slab);
                for (var i = 0; i < count; i++)
                {
                    _items[offset + i].Dispose();
                }

                if (count > 0)
                {
                    Array.Clear(_items, offset, count);
                }

                if ((uint)entity.StorageIndex < _slabByEntity.Length
                    && _slabByEntity[entity.StorageIndex] == slab + 1)
                {
                    _slabByEntity[entity.StorageIndex] = 0;
                }

                _entitiesBySlab[slab] = default;
                _countsBySlab[slab] = 0;
            }

            _slabCount = 0;
        }

        private int GetOrCreateSlab(EntityRef entity)
        {
            EnsureEntityCapacity(entity.StorageIndex + 1);
            var stored = _slabByEntity[entity.StorageIndex];
            if (stored != 0)
            {
                var existing = stored - 1;
                if (_entitiesBySlab[existing].Equals(entity))
                {
                    return existing;
                }

                throw new InvalidOperationException(
                    $"Fact slab slot '{entity.StorageIndex}' is still owned by entity '{_entitiesBySlab[existing]}'.");
            }

            var slab = _slabCount;
            if (slab >= _entitiesBySlab.Length)
            {
                EnsureEntityCapacity(slab + 1);
            }

            _slabCount++;
            _slabByEntity[entity.StorageIndex] = slab + 1;
            _entitiesBySlab[slab] = entity;
            return slab;
        }

        private bool TryGetSlab(EntityRef entity, out int slab)
        {
            if ((uint)entity.StorageIndex < _slabByEntity.Length)
            {
                var stored = _slabByEntity[entity.StorageIndex];
                if (stored != 0)
                {
                    var candidate = stored - 1;
                    if (candidate < _slabCount && _entitiesBySlab[candidate].Equals(entity))
                    {
                        slab = candidate;
                        return true;
                    }
                }
            }

            slab = -1;
            return false;
        }

        private void GrowForAdd()
        {
            if (_factListCapacityMode == FactListCapacityMode.Fixed)
            {
                throw new InvalidOperationException(
                    $"Fact slab for '{typeof(TFact).Name}' exceeded fixed per-entity capacity '{_factCapacityPerEntity}'. Increase CascadeSettings maxFactsPerTypePerEntity or WarmupCapacityHints.FactsPerEntityPerTypeCapacity.");
            }

            if (_factCapacityPerEntity > int.MaxValue / 2)
            {
                throw new InvalidOperationException($"Fact slab for '{typeof(TFact).Name}' cannot grow further.");
            }

            ResizeStorage(EntityCapacity, _factCapacityPerEntity * 2);
        }

        private void ResizeStorage(int entityCapacity, int factCapacityPerEntity)
        {
            var nextItems = new TFact[PayloadCapacity(entityCapacity, factCapacityPerEntity)];
            for (var slab = 0; slab < _slabCount; slab++)
            {
                Array.Copy(
                    _items,
                    slab * _factCapacityPerEntity,
                    nextItems,
                    slab * factCapacityPerEntity,
                    _countsBySlab[slab]);
            }

            if (entityCapacity > EntityCapacity)
            {
                Array.Resize(ref _slabByEntity, entityCapacity);
                Array.Resize(ref _entitiesBySlab, entityCapacity);
                Array.Resize(ref _countsBySlab, entityCapacity);
            }

            _items = nextItems;
            _factCapacityPerEntity = factCapacityPerEntity;
        }

        private int Offset(int slab)
            => slab * _factCapacityPerEntity;

        private static int PayloadCapacity(int entityCapacity, int factCapacityPerEntity)
        {
            var capacity = (long)entityCapacity * factCapacityPerEntity;
            if (capacity > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Typed fact slab for '{typeof(TFact).Name}' exceeds supported array capacity.");
            }

            return (int)capacity;
        }

        private static int NormalizeCapacity(int capacity)
            => Math.Max(capacity, 1);
    }
}
