#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Fluent registration for output state affected facts and its committer.
    /// </summary>
    public sealed class OutputRegistrationBuilder<TState>
        where TState : struct, IOutputState
    {
        private readonly FactFeatureRegistry _registry;
        private readonly string _name;
        private readonly FactTypeList _affectedFacts = new FactTypeList();
        private readonly List<int> _affectedFactPriorities = new List<int>();
        private CommitConflictPolicy _conflictPolicy = CommitConflictPolicy.FoldAll;

        internal OutputRegistrationBuilder(FactFeatureRegistry registry, string name)
        {
            _registry = registry;
            _name = name;
        }

        /// <summary>
        /// [INTEGRATION] Range: one fact type affecting this output. Condition: priority conflict policy. Output: registration-time commit priority; reducer scheduling is unchanged.
        /// </summary>
        public OutputRegistrationBuilder<TState> AffectedBy<TFact>(int priority)
            where TFact : struct, IFact
        {
            if (!_affectedFacts.Add(FactType.Of<TFact>()))
            {
                throw new InvalidOperationException(
                    $"Fact '{typeof(TFact).Name}' is already registered for output '{_name}'.");
            }

            _affectedFactPriorities.Add(priority);
            return this;
        }

        public OutputRegistrationBuilder<TState> ConflictPolicy(CommitConflictPolicy policy)
        {
            _conflictPolicy = policy;
            return this;
        }

        public OutputState<TState> CommitWith<TCommitter>()
            where TCommitter : IOutputCommitter<TState>, new()
            => _registry.AddOutput<TState, TCommitter>(
                _name,
                _affectedFacts.ToArray(),
                _affectedFactPriorities.ToArray(),
                _conflictPolicy);
    }
}
