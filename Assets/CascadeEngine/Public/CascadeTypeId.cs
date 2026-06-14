#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Lightweight identity for fact and output-state types.
    /// </summary>
    public readonly struct CascadeTypeId : IEquatable<CascadeTypeId>
    {
        private const uint FnvOffsetBasis = 2166136261;
        private const uint FnvPrime = 16777619;
        private readonly int _value;

        public CascadeTypeId(int value)
        {
            _value = value;
        }

        public bool IsEmpty => _value == 0;

        public static CascadeTypeId FromName(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length == 0)
            {
                throw new ArgumentException("Type id source cannot be empty.", nameof(value));
            }

            return FromNameToken(value);
        }

        public int ToInt()
            => _value;

        public bool Equals(CascadeTypeId other)
            => _value.Equals(other._value);

        public override bool Equals(object? obj)
            => obj is CascadeTypeId other && Equals(other);

        public override int GetHashCode()
            => _value.GetHashCode();

        public override string ToString()
            => _value.ToString();

        public static bool operator ==(CascadeTypeId left, CascadeTypeId right)
            => left.Equals(right);

        public static bool operator !=(CascadeTypeId left, CascadeTypeId right)
            => !left.Equals(right);

        private static CascadeTypeId FromNameToken(string value)
        {
            var hash = FnvOffsetBasis;
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= FnvPrime;
            }

            var id = (int)hash;
            return new CascadeTypeId(id == 0 ? 1 : id);
        }
    }
}
