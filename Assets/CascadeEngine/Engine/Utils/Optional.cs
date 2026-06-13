#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Value-type optional used so missing output state cannot be confused with default struct state.
    /// </summary>
    public readonly struct Optional<T>
        where T : struct
    {
        private readonly T _value;

        public Optional(T value)
        {
            _value = value;
            HasValue = true;
        }

        public bool HasValue { get; }

        public T Value
        {
            get
            {
                if (!HasValue)
                {
                    throw new InvalidOperationException("Optional has no value.");
                }

                return _value;
            }
        }
    }
}
