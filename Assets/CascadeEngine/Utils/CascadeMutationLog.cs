#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Untyped append-only log of mutations published by the last tick, in commit order. Grows on demand, then stays allocation-free.
    /// </summary>
    internal sealed class CascadeMutationLog
    {
        private CascadePropertyMutation[] _mutations;
        private int _count;

        /// <summary>
        /// Range: positive initial capacity. Condition: engine bootstrap. Output: empty preallocated log.
        /// </summary>
        internal CascadeMutationLog(int initialCapacity)
        {
            _mutations = new CascadePropertyMutation[Math.Max(initialCapacity, 1)];
        }

        internal int Count
            => _count;

        internal CascadePropertyMutation this[int index]
        {
            get
            {
                if ((uint)index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _mutations[index];
            }
        }

        /// <summary>
        /// Range: commit phase only. Condition: a property committed a publishable change. Output: mutation appended; array doubles when full.
        /// </summary>
        internal void Record(CascadePropertyMutation mutation)
        {
            if (_count == _mutations.Length)
            {
                Array.Resize(ref _mutations, _mutations.Length * 2);
            }

            _mutations[_count] = mutation;
            _count++;
        }

        /// <summary>
        /// Range: all recorded mutations. Condition: tick starts or fails. Output: log emptied.
        /// </summary>
        internal void Clear()
        {
            _count = 0;
        }
    }
}