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

        static FactBucket() { }

        private readonly CascadeTypeId _factId;
        private readonly EntitySparseSet<int> _rows = new EntitySparseSet<int>();
        private TFact[] _items;
        private int _factCapacityPerEntity;
        private FactListCapacityMode _factListCapacityMode;
        private bool _fixedStorage;
        public void FreezeCapacity()
        {
            _fixedStorage = true;
            _rows.FreezeCapacity();
        }

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
            _rows.EnsureCapacity(normalizedEntityCapacity, normalizedEntityCapacity);
            _items = new TFact[PayloadCapacity(normalizedEntityCapacity, _factCapacityPerEntity)];
        }

        public CascadeTypeId FactId => _factId;
        public int EntityCapacity => _rows.SparseCapacity;
        public int TouchedEntityCapacity => _rows.DenseCapacity;
        internal int ActiveSlabCount => _rows.Count;

        internal bool Contains(EntityRef entity, in TFact fact)
        {
            if (!TryGetSlab(entity, out var slab))
            {
                return false;
            }

            var count = _rows.ValueAt(slab);
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
            PrepareAdd(entity);
            return AddPrepared(entity, in fact);
        }

        internal int AddPrepared(EntityRef entity, in TFact fact)
        {
            var slab = GetOrCreateSlab(entity);
            var count = _rows.ValueAt(slab);

            _items[Offset(slab) + count] = fact;
            _rows.ValueAt(slab) = count + 1;
            return count;
        }

        /// <summary>
        /// [INTEGRATION] Range: validated, nonduplicate payload. Condition: caller retains ownership. Output: reserve all slab storage without acquiring a row or payload.
        /// </summary>
        internal void PrepareAdd(EntityRef entity)
        {
            EnsureEntityCapacity(entity.StorageIndex + 1);
            if (TryGetSlab(entity, out var slab))
            {
                if (_rows.ValueAt(slab) == _factCapacityPerEntity) GrowForAdd();
            }
            else
            {
                EnsureEntityCapacity(_rows.Count + 1);
                _rows.Prepare(entity, _rows.Count + 1);
            }
        }

        public bool Has(EntityRef entity)
            => TryGetSlab(entity, out var slab) && _rows.ValueAt(slab) > 0;

        public int CountFor(EntityRef entity)
            => TryGetSlab(entity, out var slab) ? _rows.ValueAt(slab) : 0;

        internal bool TryGetLatest(EntityRef entity, out TFact fact)
        {
            if (TryGetSlab(entity, out var slab))
            {
                var count = _rows.ValueAt(slab);
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
                ? new ReadOnlySpan<TFact>(_items, Offset(slab), _rows.ValueAt(slab))
                : ReadOnlySpan<TFact>.Empty;
        }

        internal ref readonly TFact Get(EntityRef entity, int index)
        {
            if (!TryGetSlab(entity, out var slab) || (uint)index >= _rows.ValueAt(slab))
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

            if (_fixedStorage) throw new InvalidOperationException("Fixed fact entity capacity exceeded.");
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
            // Range: accepted payloads only. Remove ownership before invoking user disposal; visit every payload once.
            var errors = new CleanupErrors();
            for (var slab = 0; slab < _rows.Count; slab++)
            {
                var count = _rows.ValueAt(slab);
                var offset = Offset(slab);
                for (var i = 0; i < count; i++)
                {
                    var fact = _items[offset + i];
                    _items[offset + i] = default;
                    try
                    {
                        fact.Dispose();
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
                }
            }

            _rows.Clear();
            errors.ThrowIfAny();
        }

        private int GetOrCreateSlab(EntityRef entity)
            => _rows.TryGetIndex(entity, out var slab) ? slab : _rows.SetPrepared(entity, 0);

        private bool TryGetSlab(EntityRef entity, out int slab)
            => _rows.TryGetIndex(entity, out slab);

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
            if (_fixedStorage) throw new InvalidOperationException("Fixed fact slab capacity exceeded.");
            var nextItems = new TFact[PayloadCapacity(entityCapacity, factCapacityPerEntity)];
            for (var slab = 0; slab < _rows.Count; slab++)
            {
                Array.Copy(
                    _items,
                    slab * _factCapacityPerEntity,
                    nextItems,
                    slab * factCapacityPerEntity,
                    _rows.ValueAt(slab));
            }

            if (entityCapacity > EntityCapacity)
            {
                _rows.EnsureCapacity(entityCapacity, entityCapacity);
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
