#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Transactional reducer registration overloads. Extend arity declaratively with builder And&lt;TFact&gt; extensions.
    /// </summary>
    public abstract partial class FactFeature
    {
        protected TransactionalReducerRegistrationBuilder ReduceWhen<TA, TB>()
            where TA : struct, IFact
            where TB : struct, IFact
            => CreateTransactionalRegistration(FactType.Of<TA>(), FactType.Of<TB>());

        protected TransactionalReducerRegistrationBuilder ReduceWhen<TA, TB, TC>()
            where TA : struct, IFact
            where TB : struct, IFact
            where TC : struct, IFact
            => CreateTransactionalRegistration(FactType.Of<TA>(), FactType.Of<TB>(), FactType.Of<TC>());

        protected TransactionalReducerRegistrationBuilder ReduceWhen<TA, TB, TC, TD>()
            where TA : struct, IFact
            where TB : struct, IFact
            where TC : struct, IFact
            where TD : struct, IFact
            => CreateTransactionalRegistration(
                FactType.Of<TA>(),
                FactType.Of<TB>(),
                FactType.Of<TC>(),
                FactType.Of<TD>());

        protected BatchTransactionalReducerRegistrationBuilder ReduceBatchWhen<TA, TB>()
            where TA : struct, IFact
            where TB : struct, IFact
            => CreateBatchTransactionalRegistration(FactType.Of<TA>(), FactType.Of<TB>());

        protected BatchTransactionalReducerRegistrationBuilder ReduceBatchWhen<TA, TB, TC>()
            where TA : struct, IFact
            where TB : struct, IFact
            where TC : struct, IFact
            => CreateBatchTransactionalRegistration(FactType.Of<TA>(), FactType.Of<TB>(), FactType.Of<TC>());

        protected BatchTransactionalReducerRegistrationBuilder ReduceBatchWhen<TA, TB, TC, TD>()
            where TA : struct, IFact
            where TB : struct, IFact
            where TC : struct, IFact
            where TD : struct, IFact
            => CreateBatchTransactionalRegistration(
                FactType.Of<TA>(),
                FactType.Of<TB>(),
                FactType.Of<TC>(),
                FactType.Of<TD>());

        private TransactionalReducerRegistrationBuilder CreateTransactionalRegistration(
            params FactType[] requiredFacts)
        {
            ThrowIfNotMutable();
            return new TransactionalReducerRegistrationBuilder(Registry, requiredFacts);
        }

        private BatchTransactionalReducerRegistrationBuilder CreateBatchTransactionalRegistration(
            params FactType[] requiredFacts)
        {
            ThrowIfNotMutable();
            return new BatchTransactionalReducerRegistrationBuilder(Registry, requiredFacts);
        }
    }
}
