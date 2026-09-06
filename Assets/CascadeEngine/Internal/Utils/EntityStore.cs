#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Bounded generational entity store. Entity values are recyclable sparse slots and generations reject stale handles.
    /// </summary>
    internal sealed class EntityStore
    {
        private const byte Live = 1;
        private const byte PendingCreated = 2;
        private const byte PendingDestroyed = 3;
        private const byte PendingCreatedAndDestroyed = 4;
        private const byte Destroyed = 5;
        private const byte Retired = 6;

        private readonly int _maxEntities;
        private readonly DenseEntitySet _pendingCreated;
        private readonly DenseEntitySet _pendingDestroyed;
        private byte[] _status;
        private uint[] _generations;
        private int[] _freeSlots;
        private int _slotCount;
        private int _activeCount;
        private int _freeSlotCount;

        internal EntityStore(int maxEntities = int.MaxValue)
        {
            if (maxEntities <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEntities));
            }

            _maxEntities = maxEntities;
            var initialCapacity = maxEntities == int.MaxValue ? 64 : maxEntities;
            _status = new byte[initialCapacity];
            _generations = new uint[initialCapacity];
            _freeSlots = new int[initialCapacity];
            _pendingCreated = new DenseEntitySet(initialCapacity);
            _pendingDestroyed = new DenseEntitySet(initialCapacity);
        }

        internal int Count => _slotCount;
        internal int PendingDestroyCount => _pendingDestroyed.Count;

        internal EntityRef Create(bool stageForActiveTick)
        {
            if (_activeCount >= _maxEntities)
            {
                throw new InvalidOperationException(
                    $"Concurrent entity limit '{_maxEntities}' reached. Increase CascadeSettings.MaxEntities.");
            }

            var slot = TakeStorageSlot();
            _status[slot] = stageForActiveTick ? PendingCreated : Live;
            _activeCount++;

            var entity = new EntityRef(slot, _generations[slot]);
            if (stageForActiveTick)
            {
                _pendingCreated.Add(entity);
            }

            return entity;
        }

        internal void Warmup(int entityCapacity)
        {
            if (entityCapacity > _maxEntities)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entityCapacity),
                    $"Warmup entity capacity '{entityCapacity}' exceeds concurrent entity limit '{_maxEntities}'.");
            }

            EnsureCapacity(entityCapacity);
        }

        internal bool IsKnown(EntityRef entity)
            => (uint)entity.Value < _slotCount;

        internal bool TryGetLive(int id, out EntityRef entity)
        {
            if ((uint)id < _slotCount)
            {
                var status = _status[id];
                if (status == Live || status == PendingCreated)
                {
                    entity = new EntityRef(id, _generations[id]);
                    return true;
                }
            }

            entity = default;
            return false;
        }

        internal EntityRef ResolveForStorage(EntityRef entity)
        {
            Validate(entity);
            if (TryResolveActiveSlot(entity, out var slot))
            {
                return new EntityRef(slot, _generations[slot]);
            }

            throw new InvalidOperationException($"Destroyed entity '{entity}' has no active storage slot.");
        }

        internal EntityRef ResolveForEmission(EntityRef entity)
        {
            Validate(entity);
            return TryResolveActiveSlot(entity, out var slot)
                ? new EntityRef(slot, _generations[slot])
                : entity;
        }

        internal bool TryResolveForStorage(EntityRef entity, out EntityRef resolved)
        {
            if (!IsKnown(entity) || !TryResolveActiveSlot(entity, out var slot))
            {
                resolved = default;
                return false;
            }

            resolved = new EntityRef(slot, _generations[slot]);
            return true;
        }

        internal void Validate(EntityRef entity)
        {
            if (!IsKnown(entity))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entity),
                    $"Unknown entity '{entity}'. Create entities through FactSimulation.CreateEntity.");
            }
        }

        internal bool IsDestroyed(EntityRef entity)
        {
            Validate(entity);
            if (!TryResolveActiveSlot(entity, out var slot))
            {
                return true;
            }

            var status = _status[slot];
            return status == PendingDestroyed
                || status == PendingCreatedAndDestroyed;
        }

        internal bool IsRetired(EntityRef entity)
        {
            Validate(entity);
            return !TryResolveActiveSlot(entity, out _);
        }

        internal bool StageDestroy(EntityRef entity)
        {
            var resolved = ResolveForStorage(entity);
            var slot = resolved.StorageIndex;
            var status = _status[slot];
            if (status == PendingDestroyed || status == PendingCreatedAndDestroyed)
            {
                return false;
            }

            _status[slot] = status == PendingCreated
                ? PendingCreatedAndDestroyed
                : PendingDestroyed;
            _pendingDestroyed.Add(resolved);
            return true;
        }

        internal bool IsLive(EntityRef entity)
        {
            Validate(entity);
            return TryResolveActiveSlot(entity, out _);
        }

        internal EntityRef PendingDestroyAt(int index)
            => _pendingDestroyed[index];

        internal void CommitTick()
        {
            for (var i = 0; i < _pendingCreated.Count; i++)
            {
                var entity = _pendingCreated[i];
                if (!TryResolveActiveSlot(entity, out var slot))
                {
                    continue;
                }

                if (_status[slot] == PendingCreated)
                {
                    _status[slot] = Live;
                }
                else if (_status[slot] == PendingCreatedAndDestroyed)
                {
                    ReleaseStorageSlot(slot);
                }
            }

            for (var i = 0; i < _pendingDestroyed.Count; i++)
            {
                var entity = _pendingDestroyed[i];
                if (TryResolveActiveSlot(entity, out var slot) && _status[slot] == PendingDestroyed)
                {
                    ReleaseStorageSlot(slot);
                }
            }

            ClearPending();
        }

        internal void RollbackTick()
        {
            for (var i = 0; i < _pendingCreated.Count; i++)
            {
                var entity = _pendingCreated[i];
                if (TryResolveActiveSlot(entity, out var slot))
                {
                    ReleaseStorageSlot(slot);
                }
            }

            for (var i = 0; i < _pendingDestroyed.Count; i++)
            {
                var entity = _pendingDestroyed[i];
                if (TryResolveActiveSlot(entity, out var slot) && _status[slot] == PendingDestroyed)
                {
                    _status[slot] = Live;
                }
            }

            ClearPending();
        }

        internal void DisposeStore()
        {
            ClearPending();
            _pendingCreated.DisposeStorage();
            _pendingDestroyed.DisposeStorage();
            _status = Array.Empty<byte>();
            _generations = Array.Empty<uint>();
            _freeSlots = Array.Empty<int>();
            _slotCount = 0;
            _activeCount = 0;
            _freeSlotCount = 0;
        }

        private int TakeStorageSlot()
        {
            if (_freeSlotCount > 0)
            {
                _freeSlotCount--;
                return _freeSlots[_freeSlotCount];
            }

            if (_slotCount >= _maxEntities)
            {
                throw new InvalidOperationException(
                    "No reusable entity slots remain because their generation space is exhausted.");
            }

            EnsureCapacity(_slotCount + 1);
            var slot = _slotCount;
            _slotCount++;
            return slot;
        }

        private void ReleaseStorageSlot(int slot)
        {
            _activeCount--;

            if (_generations[slot] == uint.MaxValue)
            {
                _status[slot] = Retired;
                return;
            }

            _generations[slot]++;
            _status[slot] = Destroyed;
            _freeSlots[_freeSlotCount] = slot;
            _freeSlotCount++;
        }

        private bool TryResolveActiveSlot(EntityRef entity, out int slot)
        {
            var candidate = entity.Value;
            if ((uint)candidate < _slotCount
                && _generations[candidate] == entity.Generation
                && IsActiveStatus(_status[candidate]))
            {
                slot = candidate;
                return true;
            }

            slot = -1;
            return false;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _status.Length)
            {
                return;
            }

            if (required > _maxEntities)
            {
                throw new InvalidOperationException(
                    $"Concurrent entity limit '{_maxEntities}' reached. Increase CascadeSettings.MaxEntities.");
            }

            var doubled = _status.Length == 0
                ? 1L
                : (long)_status.Length * 2L;
            var capacity = (int)Math.Min(_maxEntities, Math.Max(required, doubled));
            Array.Resize(ref _status, capacity);
            Array.Resize(ref _generations, capacity);
            Array.Resize(ref _freeSlots, capacity);
            _pendingCreated.EnsureCapacity(capacity);
            _pendingDestroyed.EnsureCapacity(capacity);
        }

        private void ClearPending()
        {
            _pendingCreated.Clear();
            _pendingDestroyed.Clear();
        }

        private static bool IsActiveStatus(byte status)
            => status == Live
                || status == PendingCreated
                || status == PendingDestroyed
                || status == PendingCreatedAndDestroyed;
    }
}
