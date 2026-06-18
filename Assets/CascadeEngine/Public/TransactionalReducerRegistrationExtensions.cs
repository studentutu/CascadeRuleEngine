#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Declarative required-fact extensions for transactional reducer registration builders.
    /// </summary>
    public static class TransactionalReducerRegistrationExtensions
    {
        /// <summary>
        /// [INTEGRATION] Appends one required fact to an entity-scoped transactional reducer registration.
        /// </summary>
        public static TransactionalReducerRegistrationBuilder And<TFact>(
            this TransactionalReducerRegistrationBuilder registration)
            where TFact : struct, IFact
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            return registration.Append<TFact>();
        }

        /// <summary>
        /// [INTEGRATION] Appends one required fact to a batch transactional reducer registration.
        /// </summary>
        public static BatchTransactionalReducerRegistrationBuilder And<TFact>(
            this BatchTransactionalReducerRegistrationBuilder registration)
            where TFact : struct, IFact
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            return registration.Append<TFact>();
        }
    }
}
