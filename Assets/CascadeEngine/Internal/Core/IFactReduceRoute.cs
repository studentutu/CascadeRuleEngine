#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal reducer-facing fact route exposing reducer invokers without fact-id map lookups.
    /// </summary>
    internal interface IFactReduceRoute
    {
        int ReducerCount { get; }
        int[] TransactionalWaiters { get; }
        int[] BatchWaiters { get; }
        void BindWaiters(int[] transactional, int[] batch);

        IReducerInvoker ReducerAt(int index);
    }
}
