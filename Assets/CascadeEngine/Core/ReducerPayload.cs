#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Base payload carried by a fact and unwrapped by reducer functions.
    /// </summary>
    public abstract class ReducerPayload
    {
        public static readonly ReducerPayload Empty = new EmptyReducerPayload();

        public static ReducerPayload From<T>(T value)
            => new TypedReducerPayload<T>(value);

        public static T Unwrap<T>(ReducerPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return payload.Unwrap<T>();
        }

        public T Unwrap<T>()
        {
            if (this is TypedReducerPayload<T> typedPayload)
            {
                return typedPayload.Value;
            }

            throw new InvalidOperationException($"Payload is not '{typeof(T).Name}'.");
        }

        public abstract bool ValueEquals(ReducerPayload other);

        private sealed class EmptyReducerPayload : ReducerPayload
        {
            public override bool ValueEquals(ReducerPayload other)
                => other is EmptyReducerPayload;
        }

        private sealed class TypedReducerPayload<T> : ReducerPayload
        {
            internal TypedReducerPayload(T value)
            {
                Value = value;
            }

            internal T Value { get; }

            public override bool ValueEquals(ReducerPayload other)
                => other is TypedReducerPayload<T> typedPayload &&
                   Equals(Value, typedPayload.Value);
        }
    }
}
