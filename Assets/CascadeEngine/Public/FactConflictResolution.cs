#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Commit-stage fact selection helpers. They read closed facts and never mutate output state directly.
    /// </summary>
    public static class FactConflictResolution
    {
        /// <summary>
        /// [INTEGRATION] Range: closed same-type facts for one entity. Condition: committer priority conflict. Output: highest-priority winner or throws on equal-priority conflict.
        /// </summary>
        public static bool TrySelectHighestPriority<TFact, TState, TConflictComparer>(
            ICommitContext ctx,
            EntityRef entity,
            TConflictComparer conflictComparer,
            out TFact winner)
            where TFact : struct, IFact, IPrioritizedFact
            where TState : struct, IOutputState
            where TConflictComparer : struct, IFactConflictComparer<TFact>
        {
            if (ctx == null)
            {
                throw new ArgumentNullException(nameof(ctx));
            }

            var facts = ctx.Facts(entity).All<TFact>();
            if (facts.Length == 0)
            {
                winner = default;
                return false;
            }

            winner = facts[0];
            var winnerPriority = winner.Priority;

            for (var i = 1; i < facts.Length; i++)
            {
                var candidate = facts[i];
                var candidatePriority = candidate.Priority;
                if (candidatePriority > winnerPriority)
                {
                    winner = candidate;
                    winnerPriority = candidatePriority;
                    continue;
                }

                if (candidatePriority == winnerPriority
                    && conflictComparer.Conflicts(in winner, in candidate))
                {
                    throw new CommitConflictException(
                        entity,
                        typeof(TState),
                        $"Fact '{typeof(TFact).Name}' has equal priority with conflicting payloads.");
                }
            }

            return true;
        }
    }
}
