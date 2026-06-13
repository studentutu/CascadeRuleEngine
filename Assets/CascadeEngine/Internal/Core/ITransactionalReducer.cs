#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Entity-scoped reducer that fires after its required fact set exists for the entity.
    /// </summary>
    public interface ITransactionalReducer
    {
        void Reduce(ITransactionReduceContext ctx, EntityRef entity);
    }
}
