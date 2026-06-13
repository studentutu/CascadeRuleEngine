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
        private readonly List<bool> _destroyed = new List<bool>();

        internal int Count => _destroyed.Count;

        internal EntityRef Create()
        {
            var entity = new EntityRef(_destroyed.Count);
            _destroyed.Add(false);
            return entity;
        }

        internal bool IsKnown(EntityRef entity)
            => !entity.IsGlobal && (uint)entity.Value < _destroyed.Count;

        internal void Validate(EntityRef entity)
        {
            if (entity.IsGlobal)
            {
                throw new InvalidOperationException("Global entity cannot be used for entity state or lifecycle operations.");
            }

            if ((uint)entity.Value >= _destroyed.Count)
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
            return _destroyed[entity.Value];
        }

        internal bool Destroy(EntityRef entity)
        {
            Validate(entity);
            if (_destroyed[entity.Value])
            {
                return false;
            }

            _destroyed[entity.Value] = true;
            return true;
        }

        internal bool IsLive(EntityRef entity)
        {
            Validate(entity);
            return !_destroyed[entity.Value];
        }
    }
}
