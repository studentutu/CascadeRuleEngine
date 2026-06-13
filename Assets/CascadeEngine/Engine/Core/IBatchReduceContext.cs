#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Context for batch transactional reducers over the current eligible entity slice.
    /// </summary>
    public interface IBatchReduceContext : IReduceContext
    {
    }
}
