#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Context for entity-scoped transactional reducers. Same surface as IReduceContext in the MVP.
    /// </summary>
    public interface ITransactionReduceContext : IReduceContext
    {
    }
}
