#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Core committed and staged property slots for one Cascade entity.
    /// </summary>
    public sealed class CascadeEntityState
    {
        private readonly CascadeValue?[] _committed = new CascadeValue?[Bitmask512.BitCount];
        private readonly CascadeValue?[] _staged = new CascadeValue?[Bitmask512.BitCount];
        private readonly int[] _stagedPriorities = new int[Bitmask512.BitCount];
        private readonly CascadePropertyKey[] _stagedProperties = new CascadePropertyKey[Bitmask512.BitCount];
        private Bitmask512 _flags;
        private Bitmask512 _stagedMask;

        public bool IsDestroyed { get; private set; }
        public int StagedPropertyCount { get; private set; }

        /// <summary>
        /// Range: staged property index. Condition: commit scans touched entity state. Output: property key staged once for commit.
        /// </summary>
        public CascadePropertyKey GetStagedProperty(int index)
        {
            if ((uint)index >= StagedPropertyCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _stagedProperties[index];
        }

        /// <summary>
        /// Range: live entity property. Condition: initialization or authoritative external load. Output: committed value is set and any staged value is cleared.
        /// </summary>
        public void SetCommitted(CascadePropertyKey property, CascadeValue value)
        {
            if (IsDestroyed)
            {
                throw new InvalidOperationException("Destroyed entities cannot receive committed state.");
            }

            _committed[property.Index] = value ?? throw new ArgumentNullException(nameof(value));
            ClearStage(property);
        }

        /// <summary>
        /// Range: committed property slot. Condition: query needs to know whether value exists. Output: typed committed value and true, or default and false.
        /// </summary>
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

        /// <summary>
        /// Range: committed property slot. Condition: query accepts default for missing value. Output: typed committed value or default.
        /// </summary>
        public T GetCommittedOrDefault<T>(CascadePropertyKey property)
            => TryGetCommitted<T>(property, out var value) ? value : default!;

        /// <summary>
        /// Range: staged property slot. Condition: query needs to know whether reducer staged a value. Output: typed staged value and true, or default and false.
        /// </summary>
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

        /// <summary>
        /// Range: entity property slot. Condition: reducer reads current working value. Output: staged value wins, otherwise committed value or default.
        /// </summary>
        public T GetStagedOrCommittedOrDefault<T>(CascadePropertyKey property)
            => TryGetStaged<T>(property, out var staged)
                ? staged
                : GetCommittedOrDefault<T>(property);

        /// <summary>
        /// Range: entity-local flag. Condition: reducer or commit policy filters work. Output: true when flag is set.
        /// </summary>
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

        /// <summary>
        /// Range: flag index 0-511. Condition: live entity only. Output: entity filter flag is disabled.
        /// </summary>
        public void ClearFlag(CascadeEntityFlagKey flag)
        {
            ThrowIfDestroyed("Destroyed entities cannot receive flags.");

            _flags.Clear(flag.Index);
        }

        /// <summary>
        /// Range: flag index 0-511. Condition: live entity only. Output: entity filter flag matches enabled.
        /// </summary>
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
        public void Stage(CascadePropertyKey property, CascadeValue value, int priority = 0)
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

        public bool StageIfPriorityAtLeast(CascadePropertyKey property, CascadeValue value, int priority)
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

        /// <summary>
        /// Range: staged property slot. Condition: commit policy accepts staged value. Output: committed value is changed only when staged differs.
        /// </summary>
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

        /// <summary>
        /// Range: staged property slot. Condition: commit policy requires staged value. Output: typed staged value or throws when missing.
        /// </summary>
        public T GetStaged<T>(CascadePropertyKey property)
        {
            var staged = _staged[property.Index];
            if (staged == null)
            {
                throw new InvalidOperationException($"Property '{property.Name}' has no staged value.");
            }

            return staged.Unwrap<T>();
        }

        /// <summary>
        /// Range: all staged properties. Condition: tick cleanup or destroyed entity cleanup. Output: staged values and staged property list are cleared.
        /// </summary>
        public void ClearStage()
        {
            for (var i = 0; i < StagedPropertyCount; i++)
            {
                ClearStage(_stagedProperties[i]);
            }

            _stagedMask.ClearAll();
            StagedPropertyCount = 0;
        }

        /// <summary>
        /// Range: this entity state. Condition: entity lifetime ends. Output: committed values, staged values, and flags are cleared.
        /// </summary>
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
