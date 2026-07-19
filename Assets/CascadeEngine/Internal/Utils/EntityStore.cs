#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Entity lifecycle store with monotonic public ids and reusable bounded internal storage slots.
    /// </summary>
    internal sealed class EntityStore
    {
        private const byte Live = 1;
        private const byte PendingCreated = 2;
        private const byte PendingDestroyed = 3;
        private const byte PendingCreatedAndDestroyed = 4;
        private const byte Destroyed = 5;

        private readonly int _maxEntities;
        private readonly DenseEntitySet _pendingCreated;
        private readonly DenseEntitySet _pendingDestroyed;
        private byte[] _status;
        private int[] _ownerIds;
        private int[] _freeSlots;
        private int _slotCount;
        private int _activeCount;
        private int _freeSlotCount;
        private int _nextEntityId;

        internal EntityStore(int maxEntities = int.MaxValue)
        {
            if (maxEntities <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEntities));
            }

            _maxEntities = maxEntities;
            var initialCapacity = maxEntities == int.MaxValue ? 64 : maxEntities;
            _status = new byte[initialCapacity];
            _ownerIds = new int[initialCapacity];
            _freeSlots = new int[initialCapacity];
            FillOwnerIds(0, _ownerIds.Length);
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

            if (_nextEntityId == int.MaxValue)
            {
                throw new InvalidOperationException("Entity id space exhausted. Public entity ids are never reused.");
            }

            var slot = TakeStorageSlot();
            var id = _nextEntityId;
            _nextEntityId++;
            _ownerIds[slot] = id;
            _status[slot] = stageForActiveTick ? PendingCreated : Live;
            _activeCount++;

            var entity = new EntityRef(id, slot);
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
            => (uint)entity.Value < _nextEntityId;

        internal bool TryGetLive(int id, out EntityRef entity)
        {
            var slot = FindSlotById(id);
            if (slot >= 0)
            {
                var status = _status[slot];
                if (status == Live || status == PendingCreated)
                {
                    entity = new EntityRef(id, slot);
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
                return new EntityRef(entity.Value, slot);
            }

            throw new InvalidOperationException($"Destroyed entity '{entity}' has no active storage slot.");
        }

        internal EntityRef ResolveForEmission(EntityRef entity)
        {
            Validate(entity);
            return TryResolveActiveSlot(entity, out var slot)
                ? new EntityRef(entity.Value, slot)
                : entity;
        }

        internal bool TryResolveForStorage(EntityRef entity, out EntityRef resolved)
        {
            if (!IsKnown(entity) || !TryResolveActiveSlot(entity, out var slot))
            {
                resolved = default;
                return false;
            }

            resolved = new EntityRef(entity.Value, slot);
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
                || status == PendingCreatedAndDestroyed
                || status == Destroyed;
        }

        internal bool IsRetired(EntityRef entity)
        {
            Validate(entity);
            return !TryResolveActiveSlot(entity, out var slot) || _status[slot] == Destroyed;
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
            return TryResolveActiveSlot(entity, out var slot) && _status[slot] != Destroyed;
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
                    ReleaseStorageSlot(entity, slot);
                }
            }

            for (var i = 0; i < _pendingDestroyed.Count; i++)
            {
                var entity = _pendingDestroyed[i];
                if (TryResolveActiveSlot(entity, out var slot) && _status[slot] == PendingDestroyed)
                {
                    ReleaseStorageSlot(entity, slot);
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
                    ReleaseStorageSlot(entity, slot);
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
            _status = Array.Empty<byte>();
            _ownerIds = Array.Empty<int>();
            _freeSlots = Array.Empty<int>();
            _slotCount = 0;
            _activeCount = 0;
            _freeSlotCount = 0;
            _nextEntityId = 0;
        }

        private int TakeStorageSlot()
        {
            if (_freeSlotCount > 0)
            {
                _freeSlotCount--;
                return _freeSlots[_freeSlotCount];
            }

            EnsureCapacity(_slotCount + 1);
            var slot = _slotCount;
            _slotCount++;
            return slot;
        }

        private void ReleaseStorageSlot(EntityRef entity, int slot)
        {
            _status[slot] = Destroyed;
            _ownerIds[slot] = -1;
            _activeCount--;
            _freeSlots[_freeSlotCount] = slot;
            _freeSlotCount++;
        }

        private bool TryResolveActiveSlot(EntityRef entity, out int slot)
        {
            var candidate = entity.StorageIndex;
            if ((uint)candidate < _slotCount && _ownerIds[candidate] == entity.Value)
            {
                slot = candidate;
                return true;
            }

            slot = FindSlotById(entity.Value);
            return slot >= 0;
        }

        private int FindSlotById(int id)
        {
            for (var slot = 0; slot < _slotCount; slot++)
            {
                if (_ownerIds[slot] == id)
                {
                    return slot;
                }
            }

            return -1;
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
            var previousCapacity = _ownerIds.Length;
            Array.Resize(ref _status, capacity);
            Array.Resize(ref _ownerIds, capacity);
            Array.Resize(ref _freeSlots, capacity);
            FillOwnerIds(previousCapacity, capacity);
            _pendingCreated.EnsureCapacity(capacity);
            _pendingDestroyed.EnsureCapacity(capacity);
        }

        private void ClearPending()
        {
            _pendingCreated.Clear();
            _pendingDestroyed.Clear();
        }

        private void FillOwnerIds(int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                _ownerIds[i] = -1;
            }
        }
    }
}
