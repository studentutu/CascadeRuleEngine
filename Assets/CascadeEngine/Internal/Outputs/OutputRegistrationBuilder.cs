#nullable enable

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
        private CommitConflictPolicy _conflictPolicy;

        internal OutputRegistrationBuilder(FactFeatureRegistry registry, string name)
        {
            _registry = registry;
            _name = name;
        }

        public OutputRegistrationBuilder<TState> AffectedBy<TFact>()
            where TFact : struct, IFact
        {
            _affectedFacts.Add(FactType.Of<TFact>());
            return this;
        }

        public OutputRegistrationBuilder<TState> ConflictPolicy(CommitConflictPolicy policy)
        {
            _conflictPolicy = policy;
            return this;
        }

        public OutputState<TState> CommitWith<TCommitter>()
            where TCommitter : IOutputCommitter<TState>
            => _registry.AddOutput<TState, TCommitter>(_name, _affectedFacts.ToArray(), _conflictPolicy);
    }
}
