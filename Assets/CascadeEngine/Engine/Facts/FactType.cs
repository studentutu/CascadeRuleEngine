#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Runtime fact type descriptor for non-generic transactional registration helpers.
    /// </summary>
    public readonly struct FactType
    {
        public FactType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (!typeof(IFact).IsAssignableFrom(type))
            {
                throw new ArgumentException("Fact type must implement IFact.", nameof(type));
            }

            Type = type;
        }

        internal Type Type { get; }

        public static FactType Of<TFact>()
            where TFact : struct, IFact
            => new FactType(typeof(TFact));
    }
}
