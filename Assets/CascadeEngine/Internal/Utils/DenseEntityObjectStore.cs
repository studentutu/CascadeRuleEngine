#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Dense non-global entity indexed object slots with explicit capacity ownership.
    /// </summary>
    internal sealed class DenseEntityObjectStore<TValue>
        where TValue : class
    {
        private readonly Func<TValue> _factory;
        private TValue?[] _values;

        internal DenseEntityObjectStore(Func<TValue> factory, int initialEntityCapacity)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _values = new TValue?[NormalizeCapacity(initialEntityCapacity)];
        }

        internal int Capacity => _values.Length;

        internal void EnsureCapacity(int entityCapacity)
        {
            if (entityCapacity <= _values.Length)
            {
                return;
            }

            Array.Resize(ref _values, entityCapacity);
        }

        internal TValue GetOrCreate(EntityRef entity)
        {
            ThrowIfGlobal(entity);
            EnsureCapacity(entity.Value + 1);

            var value = _values[entity.Value];
            if (value != null)
            {
                return value;
            }

            value = _factory();
            _values[entity.Value] = value;
            return value;
        }

        internal bool TryGet(EntityRef entity, out TValue value)
        {
            ThrowIfGlobal(entity);
            if ((uint)entity.Value >= _values.Length)
            {
                value = null!;
                return false;
            }

            value = _values[entity.Value]!;
            return value != null;
        }

        private static int NormalizeCapacity(int capacity)
            => capacity > 0 ? capacity : 1;

        private static void ThrowIfGlobal(EntityRef entity)
        {
            if (entity.IsGlobal)
            {
                throw new InvalidOperationException("Global entity is not valid for dense entity object storage.");
            }
        }
    }
}
