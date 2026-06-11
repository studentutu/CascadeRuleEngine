#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Core committed and staged property slots for one Cascade entity.
    /// </summary>
    public sealed class CascadeEntityState
    {
        private readonly ReducerPayload?[] _committed = new ReducerPayload?[Bitmask512.BitCount];
        private readonly ReducerPayload?[] _staged = new ReducerPayload?[Bitmask512.BitCount];
        private readonly int[] _stagedPriorities = new int[Bitmask512.BitCount];
        private readonly CascadePropertyKey[] _stagedProperties = new CascadePropertyKey[Bitmask512.BitCount];
        private Bitmask512 _flags;
        private Bitmask512 _stagedMask;

        public bool IsDestroyed { get; private set; }
        public int StagedPropertyCount { get; private set; }

        public CascadePropertyKey GetStagedProperty(int index)
        {
            if ((uint)index >= StagedPropertyCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _stagedProperties[index];
        }

        public void SetCommitted(CascadePropertyKey property, ReducerPayload value)
        {
            if (IsDestroyed)
            {
                throw new InvalidOperationException("Destroyed entities cannot receive committed state.");
            }

            _committed[property.Index] = value ?? throw new ArgumentNullException(nameof(value));
            ClearStage(property);
        }

        public bool TryGetCommitted<T>(CascadePropertyKey property, out T value)
        {
            var payload = _committed[property.Index];
            if (payload == null)
            {
                value = default!;
                return false;
            }

            value = payload.Unwrap<T>();
            return true;
        }

        public T GetCommittedOrDefault<T>(CascadePropertyKey property)
            => TryGetCommitted<T>(property, out var value) ? value : default!;

        public bool TryGetStaged<T>(CascadePropertyKey property, out T value)
        {
            var payload = _staged[property.Index];
            if (payload == null)
            {
                value = default!;
                return false;
            }

            value = payload.Unwrap<T>();
            return true;
        }

        public T GetStagedOrCommittedOrDefault<T>(CascadePropertyKey property)
            => TryGetStaged<T>(property, out var staged)
                ? staged
                : GetCommittedOrDefault<T>(property);

        public bool HasFlag(CascadeEntityFlagKey flag)
            => _flags.IsSet(flag.Index);

        /// <summary>
        /// Range: flag index 0-511. Condition: live entity only. Output: entity filter flag is enabled.
        /// </summary>
        public void SetFlag(CascadeEntityFlagKey flag)
        {
            ThrowIfDestroyed("Destroyed entities cannot receive flags.");

            _flags.SetDirty(flag.Index);
        }

        public void ClearFlag(CascadeEntityFlagKey flag)
        {
            ThrowIfDestroyed("Destroyed entities cannot receive flags.");

            _flags.Clear(flag.Index);
        }

        public void SetFlag(CascadeEntityFlagKey flag, bool enabled)
        {
            if (enabled)
            {
                SetFlag(flag);
                return;
            }

            ClearFlag(flag);
        }

        /// <summary>
        /// Range: property index 0-511. Condition: reducer stages an entity-local value. Output: property is listed once for commit.
        /// </summary>
        public void Stage(CascadePropertyKey property, ReducerPayload value, int priority = 0)
        {
            if (IsDestroyed)
            {
                return;
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            RegisterStagedProperty(property);
            _staged[property.Index] = value;
            _stagedPriorities[property.Index] = priority;
        }

        public bool StageIfPriorityAtLeast(CascadePropertyKey property, ReducerPayload value, int priority)
        {
            if (IsDestroyed)
            {
                return false;
            }

            if (_stagedMask.IsSet(property.Index) && priority < _stagedPriorities[property.Index])
            {
                return false;
            }

            Stage(property, value, priority);
            return true;
        }

        public bool PublishStagedIfChanged(CascadePropertyKey property)
        {
            var staged = _staged[property.Index];
            if (staged == null)
            {
                throw new InvalidOperationException($"Property '{property.Name}' has no staged value.");
            }

            var committed = _committed[property.Index];
            if (committed != null && committed.ValueEquals(staged))
            {
                return false;
            }

            _committed[property.Index] = staged;
            return true;
        }

        public T GetStaged<T>(CascadePropertyKey property)
        {
            var staged = _staged[property.Index];
            if (staged == null)
            {
                throw new InvalidOperationException($"Property '{property.Name}' has no staged value.");
            }

            return staged.Unwrap<T>();
        }

        public void ClearStage()
        {
            for (var i = 0; i < StagedPropertyCount; i++)
            {
                ClearStage(_stagedProperties[i]);
            }

            _stagedMask.ClearAll();
            StagedPropertyCount = 0;
        }

        public void Destroy()
        {
            IsDestroyed = true;
            ClearStage();
            _flags.ClearAll();

            for (var i = 0; i < Bitmask512.BitCount; i++)
            {
                _committed[i] = null;
            }
        }

        private void ThrowIfDestroyed(string message)
        {
            if (IsDestroyed)
            {
                throw new InvalidOperationException(message);
            }
        }

        private void ClearStage(CascadePropertyKey property)
        {
            _staged[property.Index] = null;
            _stagedPriorities[property.Index] = 0;
        }

        private void RegisterStagedProperty(CascadePropertyKey property)
        {
            if (!_stagedMask.Set(property.Index))
            {
                return;
            }

            _stagedProperties[StagedPropertyCount] = property;
            StagedPropertyCount++;
        }
    }
}
