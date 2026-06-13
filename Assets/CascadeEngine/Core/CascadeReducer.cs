#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Reducer bound to one fact kind: reads committed state via the context, stages values, may produce more facts.
    /// </summary>
    public delegate void CascadeReducer<TPayload>(CascadeReducerContext context, TPayload payload);
}