#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal reducer-facing fact route exposing reducer invokers without fact-id map lookups.
    /// </summary>
    internal interface IFactReduceRoute
    {
        int ReducerCount { get; }

        IReducerInvoker ReducerAt(int index);
    }
}
