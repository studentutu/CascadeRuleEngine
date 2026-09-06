#nullable enable

using System;

namespace CascadeEngineApi.Tests
{
    /// <summary>
    /// Compile-only consumer and adapter: keeps the pre-overhaul public entry points source compatible.
    /// </summary>
    internal sealed class PublicContractFixture : IFactSimulation
    {
        private readonly FactSimulation _simulation;
        internal PublicContractFixture(FactFeature feature) => _simulation = new FactSimulation(feature);
        internal PublicContractFixture(FactFeature feature, CascadeSettings settings) => _simulation = new FactSimulation(feature, settings);
        public ICommittedStateStore State => _simulation.State;
        public EntityRef CreateEntity() => _simulation.CreateEntity();
        public void DestroyEntity(EntityRef entity) => _simulation.DestroyEntity(entity);
        public void Emit<TFact>(EntityRef entity, in TFact fact) where TFact : struct, IFact => _simulation.Emit(entity, in fact);
        public SimulationResult RunTick(ReduceOptions options) => _simulation.RunTick(options);
        public void ForEachMutation<TState>(OutputState<TState> output, StateMutationHandler<TState> handler)
            where TState : struct, IOutputState => _simulation.ForEachMutation(output, handler);

        internal void CompileHost<TFact, TState>(EntityRef entity, TFact fact, TState state)
            where TFact : struct, IFact where TState : struct, IOutputState
        {
            _simulation.Warmup(new WarmupCapacityHints());
            _simulation.SetStateSilently(entity, in state);
            _simulation.TryGetEntity(entity.Value, out entity);
            _simulation.Has<TState>(entity);
            _simulation.Get<TState>(entity);
            _simulation.TryGet<TState>(entity, out state);
            _simulation.HasState<TState>(entity);
            _simulation.GetState<TState>(entity);
            _simulation.TryGetState<TState>(entity, out state);
            _simulation.Facts(entity).All<TFact>();
            _simulation.Query.With<TState>();
            _simulation.Query.With<TState, TState>();
            _simulation.Query.WithFact<TFact>();
            _simulation.RunTick();
            _simulation.RunTickIncremental(out _);
            _simulation.RunTickIncremental(ReduceOptions.Default(), out _);
            IReduceContext reducer = _simulation;
            ICommitContext committer = _simulation;
            reducer.Emit(entity, in fact);
            committer.Facts(entity);
            _ = _simulation.Tick;
            _ = _simulation.LastResult;
            _ = _simulation.MutationCount;
            _ = FactType.Of<TFact>();
            _ = new FactType(CascadeTypeId.FromName("fixture"));
            _ = CommitDecision<TState>.Set(state);
            _ = CommitDecision<TState>.Delete();
            _ = CommitDecision<TState>.Unchanged();
            _simulation.Dispose();
        }
    }
}
