#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Entity lifecycle store. Entity ids are created by the simulation and never reused by this MVP.
    /// </summary>
    internal sealed class EntityStore
    {
        private readonly HashSet<int> _destroyed = new HashSet<int>();
        private int _createdCount;

        internal int Count => _createdCount;

        internal EntityRef Create()
        {
            var entity = new EntityRef(_createdCount);
            _createdCount++;
            return entity;
        }

        internal bool IsKnown(EntityRef entity)
            => !entity.IsGlobal && (uint)entity.Value < _createdCount;

        internal void Validate(EntityRef entity)
        {
            if (entity.IsGlobal)
            {
                throw new InvalidOperationException("Global entity cannot be used for entity state or lifecycle operations.");
            }

            if ((uint)entity.Value >= _createdCount)
            {
                throw new ArgumentOutOfRangeException(nameof(entity), $"Unknown entity '{entity}'. Create entities through FactSimulation.CreateEntity.");
            }
        }

        internal bool IsDestroyed(EntityRef entity)
        {
            if (entity.IsGlobal)
            {
                return false;
            }

            Validate(entity);
            return _destroyed.Contains(entity.Value);
        }

        internal bool Destroy(EntityRef entity)
        {
            Validate(entity);
            return _destroyed.Add(entity.Value);
        }

        internal bool IsLive(EntityRef entity)
        {
            Validate(entity);
            return !_destroyed.Contains(entity.Value);
        }

        internal void DisposeStore()
        {
            _destroyed.Clear();
            _createdCount = 0;
        }
    }
}
