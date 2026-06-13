#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Host-provided capacity hints for preparing the simulation before the first gameplay tick.
    /// </summary>
    public sealed class WarmupCapacityHints
    {
        public int EntityCapacity { get; set; } = 64;
        public int FactQueueCapacity { get; set; } = 64;
        public int FactsPerEntityPerTypeCapacity { get; set; } = 4;
        public int QueryEntityCapacity { get; set; } = 64;
        public int TransactionEntityCapacity { get; set; } = 64;
        public int BatchEntityCapacity { get; set; } = 64;
        public int CommitActionCapacity { get; set; } = 64;
        public int OutputStateCapacityPerOutput { get; set; } = 64;
        public int MutationCapacityPerOutput { get; set; } = 64;

        public static WarmupCapacityHints ForEntities(int entityCapacity)
        {
            return new WarmupCapacityHints
            {
                EntityCapacity = entityCapacity,
                FactQueueCapacity = entityCapacity,
                QueryEntityCapacity = entityCapacity,
                TransactionEntityCapacity = entityCapacity,
                BatchEntityCapacity = entityCapacity,
                CommitActionCapacity = entityCapacity,
                OutputStateCapacityPerOutput = entityCapacity,
                MutationCapacityPerOutput = entityCapacity
            };
        }
    }
}
