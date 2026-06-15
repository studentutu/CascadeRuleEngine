#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Convenience priority registration helpers for standard fact contracts.
    /// </summary>
    public static class FactPriorityRegistrationBuilderExtensions
    {
        public static FactPriorityRegistrationBuilder<TFact> FromFact<TFact>(
            this FactPriorityRegistrationBuilder<TFact> builder)
            where TFact : struct, IFact, IPrioritizedFact
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.With<PrioritizedFactPriorityResolver<TFact>>();
        }
    }
}
