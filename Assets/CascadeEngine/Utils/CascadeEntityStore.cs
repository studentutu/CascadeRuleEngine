#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Dense per-entity lifecycle and flag store. Property values live in their CascadeProperty columns.
    /// </summary>
    internal sealed class CascadeEntityStore
    {
        private readonly bool[] _destroyed;
        private readonly Bitmask512[] _flags;

        /// <summary>
        /// Range: positive entity capacity. Condition: engine bootstrap. Output: dense store with all entities live and flag-free.
        /// </summary>
        internal CascadeEntityStore(int entityCapacity)
        {
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            _destroyed = new bool[entityCapacity];
            _flags = new Bitmask512[entityCapacity];
        }

        /// <summary>
        /// Range: any entity id. Condition: caller passes external input. Output: throws when the id is outside capacity.
        /// </summary>
        internal void ValidateEntity(CascadeEntityId entityId)
        {
            if ((uint)entityId.Value >= _destroyed.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }
        }

        /// <summary>
        /// Range: entity in capacity. Condition: query, no side effects. Output: true when destroyed.
        /// </summary>
        internal bool IsDestroyed(CascadeEntityId entityId)
        {
            ValidateEntity(entityId);
            return _destroyed[entityId.Value];
        }

        /// <summary>
        /// Range: entity in capacity. Condition: entity lifetime ends. Output: entity destroyed and all flags cleared.
        /// </summary>
        internal void Destroy(CascadeEntityId entityId)
        {
            ValidateEntity(entityId);
            _destroyed[entityId.Value] = true;
            _flags[entityId.Value].ClearAll();
        }

        /// <summary>
        /// Range: entity in capacity. Condition: query, no side effects. Output: true when the flag is set.
        /// </summary>
        internal bool HasFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
        {
            ValidateEntity(entityId);
            return _flags[entityId.Value].IsSet(flag.Index);
        }

        /// <summary>
        /// Range: live entity in capacity. Condition: host configuration. Output: flag set; throws on destroyed entity.
        /// </summary>
        internal void SetFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
        {
            ThrowIfDestroyed(entityId);
            _flags[entityId.Value].SetDirty(flag.Index);
        }

        /// <summary>
        /// Range: live entity in capacity. Condition: host configuration. Output: flag cleared; throws on destroyed entity.
        /// </summary>
        internal void ClearFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
        {
            ThrowIfDestroyed(entityId);
            _flags[entityId.Value].Clear(flag.Index);
        }

        private void ThrowIfDestroyed(CascadeEntityId entityId)
        {
            if (IsDestroyed(entityId))
            {
                throw new InvalidOperationException("Destroyed entities cannot receive flags.");
            }
        }
    }
}