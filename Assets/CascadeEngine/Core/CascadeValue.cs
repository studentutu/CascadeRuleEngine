#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Core value carried by facts, staged properties, and committed properties.
    /// </summary>
    public abstract class CascadeValue
    {
        public static readonly CascadeValue Empty = new EmptyCascadeValue();

        /// <summary>
        /// Range: any payload or property value. Condition: caller accepts boxed wrapper allocation. Output: typed Cascade value.
        /// </summary>
        public static CascadeValue From<T>(T value)
            => new TypedCascadeValue<T>(value);

        /// <summary>
        /// Range: non-null Cascade value. Condition: requested type matches stored type. Output: unwrapped typed value or throws.
        /// </summary>
        public static T Unwrap<T>(CascadeValue value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return value.Unwrap<T>();
        }

        /// <summary>
        /// Range: this Cascade value. Condition: requested type matches stored type. Output: unwrapped typed value or throws.
        /// </summary>
        public T Unwrap<T>()
        {
            if (this is TypedCascadeValue<T> typedValue)
            {
                return typedValue.Value;
            }

            throw new InvalidOperationException($"Value is not '{typeof(T).Name}'.");
        }

        /// <summary>
        /// Range: same Cascade value family. Condition: commit compares staged and committed values. Output: true when stored values are equal.
        /// </summary>
        public abstract bool ValueEquals(CascadeValue other);

        private sealed class EmptyCascadeValue : CascadeValue
        {
            public override bool ValueEquals(CascadeValue other)
                => other is EmptyCascadeValue;
        }

        private sealed class TypedCascadeValue<T> : CascadeValue
        {
            internal TypedCascadeValue(T value)
            {
                Value = value;
            }

            internal T Value { get; }

            public override bool ValueEquals(CascadeValue other)
                => other is TypedCascadeValue<T> typedValue &&
                   Equals(Value, typedValue.Value);
        }
    }
}
