#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Dense per-registration fired marker that avoids per-tick HashSet churn.
    /// </summary>
    internal sealed class FiredReducerTracker
    {
        private int[][] _stampsByRegistration;
        private int _entityCapacity;
        private int _currentStamp = 1;
        private bool _fixedCapacity;
        internal void FreezeCapacity() => _fixedCapacity = true;

        internal FiredReducerTracker(int registrationCapacity, int entityCapacity)
        {
            _stampsByRegistration = new int[NormalizeRegistrationCapacity(registrationCapacity)][];
            _entityCapacity = NormalizeCapacity(entityCapacity);
            EnsureRegistrationArrays();
        }

        internal int RegistrationCapacity => _stampsByRegistration.Length;
        internal int EntityCapacity => _entityCapacity;

        internal void Warmup(int registrationCapacity, int entityCapacity)
        {
            EnsureRegistrationCapacity(registrationCapacity);
            EnsureEntityCapacity(entityCapacity);
        }

        internal void BeginTick()
        {
            if (_currentStamp == int.MaxValue)
            {
                ClearAllStamps();
                _currentStamp = 1;
                return;
            }

            _currentStamp++;
        }

        internal bool HasFired(int registrationIndex, EntityRef entity)
            => _stampsByRegistration[registrationIndex][entity.StorageIndex] == _currentStamp;

        internal bool MarkIfNew(int registrationIndex, EntityRef entity)
        {
            EnsureRegistrationCapacity(registrationIndex + 1);
            EnsureEntityCapacity(entity.StorageIndex + 1);

            var stamps = _stampsByRegistration[registrationIndex];
            if (stamps[entity.StorageIndex] == _currentStamp)
            {
                return false;
            }

            stamps[entity.StorageIndex] = _currentStamp;
            return true;
        }

        internal void DisposeTracker()
        {
            _stampsByRegistration = Array.Empty<int[]>();
            _entityCapacity = 0;
            _currentStamp = 1;
        }

        private void EnsureRegistrationCapacity(int required)
        {
            var normalized = NormalizeRegistrationCapacity(required);
            if (normalized <= _stampsByRegistration.Length)
            {
                return;
            }

            if (_fixedCapacity) throw new InvalidOperationException("Fixed fired registration capacity exceeded.");
            var oldLength = _stampsByRegistration.Length;
            Array.Resize(ref _stampsByRegistration, normalized);
            for (var i = oldLength; i < _stampsByRegistration.Length; i++)
            {
                _stampsByRegistration[i] = new int[_entityCapacity];
            }
        }

        private void EnsureEntityCapacity(int required)
        {
            var normalized = NormalizeCapacity(required);
            if (normalized <= _entityCapacity)
            {
                return;
            }

            if (_fixedCapacity) throw new InvalidOperationException("Fixed fired entity capacity exceeded.");
            var next = new int[_stampsByRegistration.Length][];
            for (var i = 0; i < next.Length; i++)
            {
                next[i] = new int[normalized];
                Array.Copy(_stampsByRegistration[i], next[i], _entityCapacity);
            }
            _stampsByRegistration = next;
            _entityCapacity = normalized;
        }

        private void EnsureRegistrationArrays()
        {
            for (var i = 0; i < _stampsByRegistration.Length; i++)
            {
                _stampsByRegistration[i] = new int[_entityCapacity];
            }
        }

        private void ClearAllStamps()
        {
            for (var i = 0; i < _stampsByRegistration.Length; i++)
            {
                Array.Clear(_stampsByRegistration[i], 0, _stampsByRegistration[i].Length);
            }
        }

        private static int NormalizeCapacity(int capacity)
            => Math.Max(capacity, 1);

        private static int NormalizeRegistrationCapacity(int capacity)
            => Math.Max(capacity, 0);
    }
}
