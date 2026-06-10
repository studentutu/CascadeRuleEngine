#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Reducer function mapped from a fact kind.
    /// </summary>
    public delegate void CascadeReducerFunction<in TContext>(TContext context, CascadeFact fact);
}
