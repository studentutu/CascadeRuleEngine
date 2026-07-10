#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Entity-scoped reducer invoked once per tick when its registered fact or committed-state eligibility is satisfied.
    /// </summary>
    public interface ITransactionalReducer
    {
        void Reduce(IReduceContext ctx, EntityRef entity);
    }
}
