#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Compatibility shim for older call sites. Use <see cref="CascadeValue"/> for new code.
    /// </summary>
    public static class ReducerPayload
    {
        public static CascadeValue Empty
            => CascadeValue.Empty;

        public static CascadeValue From<T>(T value)
            => CascadeValue.From(value);

        public static T Unwrap<T>(CascadeValue value)
            => CascadeValue.Unwrap<T>(value);
    }
}
