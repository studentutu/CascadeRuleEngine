#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Entity lifecycle store with tick-local creation and destruction staging. Entity ids are never reused.
    /// </summary>
    internal sealed class EntityStore
    {
        private const byte Live = 1;
        private const byte PendingCreated = 2;
        private const byte PendingDestroyed = 3;
        private const byte PendingCreatedAndDestroyed = 4;
        private const byte Destroyed = 5;

        private readonly DenseEntitySet _pendingCreated = new DenseEntitySet(64);
        private readonly DenseEntitySet _pendingDestroyed = new DenseEntitySet(64);
        private byte[] _status = new byte[64];
        private int _createdCount;

        internal int Count => _createdCount;
        internal int PendingDestroyCount => _pendingDestroyed.Count;

        internal EntityRef Create(bool stageForActiveTick)
        {
            EnsureCapacity(_createdCount + 1);
            var entity = new EntityRef(_createdCount);
            _createdCount++;

            if (stageForActiveTick)
            {
                _status[entity.Value] = PendingCreated;
                _pendingCreated.Add(entity);
            }
            else
            {
                _status[entity.Value] = Live;
            }

            return entity;
        }

        internal void Warmup(int entityCapacity)
            => EnsureCapacity(entityCapacity);

        internal bool IsKnown(EntityRef entity)
            => (uint)entity.Value < _createdCount;

        internal bool TryGetLive(int id, out EntityRef entity)
        {
            if ((uint)id < _createdCount)
            {
                var status = _status[id];
                if (status == Live || status == PendingCreated)
                {
                    entity = new EntityRef(id);
                    return true;
                }
            }

            entity = default;
            return false;
        }

        internal void Validate(EntityRef entity)
        {
            if ((uint)entity.Value >= _createdCount)
            {
                throw new ArgumentOutOfRangeException(nameof(entity), $"Unknown entity '{entity}'. Create entities through FactSimulation.CreateEntity.");
            }
        }

        internal bool IsDestroyed(EntityRef entity)
        {
            Validate(entity);
            var status = _status[entity.Value];
            return status == PendingDestroyed
                || status == PendingCreatedAndDestroyed
                || status == Destroyed;
        }

        internal bool IsRetired(EntityRef entity)
        {
            Validate(entity);
            return _status[entity.Value] == Destroyed;
        }

        internal bool StageDestroy(EntityRef entity)
        {
            Validate(entity);
            var status = _status[entity.Value];
            if (status == Destroyed
                || status == PendingDestroyed
                || status == PendingCreatedAndDestroyed)
            {
                return false;
            }

            _status[entity.Value] = status == PendingCreated
                ? PendingCreatedAndDestroyed
                : PendingDestroyed;
            _pendingDestroyed.Add(entity);
            return true;
        }

        internal bool IsLive(EntityRef entity)
        {
            Validate(entity);
            var status = _status[entity.Value];
            return status != Destroyed;
        }

        internal EntityRef PendingDestroyAt(int index)
            => _pendingDestroyed[index];

        internal void CommitTick()
        {
            for (var i = 0; i < _pendingCreated.Count; i++)
            {
                var entity = _pendingCreated[i];
                _status[entity.Value] = _status[entity.Value] == PendingCreated
                    ? Live
                    : Destroyed;
            }

            for (var i = 0; i < _pendingDestroyed.Count; i++)
            {
                var entity = _pendingDestroyed[i];
                if (_status[entity.Value] == PendingDestroyed)
                {
                    _status[entity.Value] = Destroyed;
                }
            }

            ClearPending();
        }

        internal void RollbackTick()
        {
            for (var i = 0; i < _pendingCreated.Count; i++)
            {
                _status[_pendingCreated[i].Value] = Destroyed;
            }

            for (var i = 0; i < _pendingDestroyed.Count; i++)
            {
                var entity = _pendingDestroyed[i];
                if (_status[entity.Value] == PendingDestroyed)
                {
                    _status[entity.Value] = Live;
                }
            }

            ClearPending();
        }

        internal void DisposeStore()
        {
            ClearPending();
            _status = Array.Empty<byte>();
            _createdCount = 0;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _status.Length)
            {
                return;
            }

            var doubled = _status.Length == 0 ? 1 : _status.Length * 2;
            var capacity = Math.Max(required, doubled);
            Array.Resize(ref _status, capacity);
            _pendingCreated.EnsureCapacity(capacity);
            _pendingDestroyed.EnsureCapacity(capacity);
        }

        private void ClearPending()
        {
            _pendingCreated.Clear();
            _pendingDestroyed.Clear();
        }
    }
}
