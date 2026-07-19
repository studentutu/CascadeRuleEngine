#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Project-level capacity and reduction policy captured when FactSimulation is created.
    /// </summary>
    public sealed class CascadeSettings
    {
        public CascadeSettings(
            int maxEntities,
            int maxFactsPerEntity,
            int maxFactsPerTypePerEntity)
        {
            MaxEntities = maxEntities;
            MaxFactsPerEntity = maxFactsPerEntity;
            MaxFactsPerTypePerEntity = maxFactsPerTypePerEntity;
        }

        public int MaxEntities { get; }
        public int MaxFactsPerEntity { get; }
        public int MaxFactsPerTypePerEntity { get; }
        public int MaxWorkItemsPerStep { get; set; } = 50000;
        public int MaxWorkItemsPerTick { get; set; } = 100000;
        public int MaxPasses { get; set; } = 64;
        public int MaxMillisecondsPerStep { get; set; } = 8;
        public int MaxCausalDepth { get; set; } = 32;

        internal void Validate()
        {
            RequirePositive(MaxEntities, nameof(MaxEntities));
            RequirePositive(MaxFactsPerEntity, nameof(MaxFactsPerEntity));
            RequirePositive(MaxFactsPerTypePerEntity, nameof(MaxFactsPerTypePerEntity));
            RequirePositive(MaxWorkItemsPerStep, nameof(MaxWorkItemsPerStep));
            RequirePositive(MaxWorkItemsPerTick, nameof(MaxWorkItemsPerTick));
            RequirePositive(MaxPasses, nameof(MaxPasses));

            if (MaxMillisecondsPerStep < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxMillisecondsPerStep));
            }

            if (MaxCausalDepth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxCausalDepth));
            }

            if (MaxFactsPerTypePerEntity > MaxFactsPerEntity)
            {
                throw new ArgumentException(
                    $"{nameof(MaxFactsPerTypePerEntity)} cannot exceed {nameof(MaxFactsPerEntity)}.");
            }

            FactQueueCapacity();
        }

        internal WarmupCapacityHints CreateWarmupHints()
        {
            return new WarmupCapacityHints
            {
                EntityCapacity = MaxEntities,
                FactQueueCapacity = FactQueueCapacity(),
                FactsPerEntityPerTypeCapacity = MaxFactsPerTypePerEntity,
                QueryEntityCapacity = MaxEntities,
                TransactionEntityCapacity = MaxEntities,
                BatchEntityCapacity = MaxEntities,
                CommitActionCapacity = MaxEntities,
                OutputStateCapacityPerOutput = MaxEntities,
                MutationCapacityPerOutput = MaxEntities,
                FactListCapacityMode = FactListCapacityMode.Fixed
            };
        }

        internal ReduceOptions CreateReduceOptions()
        {
            return new ReduceOptions
            {
                MaxFacts = FactQueueCapacity(),
                MaxWorkItems = MaxWorkItemsPerStep,
                MaxPasses = MaxPasses,
                MaxMilliseconds = MaxMillisecondsPerStep,
                Guardrails = new FactGuardrails
                {
                    MaxFactsPerEntity = MaxFactsPerEntity,
                    MaxFactsPerTypePerEntity = MaxFactsPerTypePerEntity,
                    MaxReducerInvocationsPerTick = MaxWorkItemsPerTick,
                    MaxTransactionalReducerInvocationsPerTick = MaxWorkItemsPerTick,
                    MaxWorkItemsPerTick = MaxWorkItemsPerTick,
                    MaxCausalDepth = MaxCausalDepth
                }
            };
        }

        private int FactQueueCapacity()
        {
            var capacity = (long)MaxEntities * MaxFactsPerEntity;
            if (capacity > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxFactsPerEntity),
                    "MaxEntities multiplied by MaxFactsPerEntity exceeds supported queue capacity.");
            }

            return (int)capacity;
        }

        private static void RequirePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}
