#nullable enable

using System.Diagnostics;

namespace CascadeEngineApi
{
    /// <summary>
    /// Unpublished reconciliation cursors. Each step prepares or validates one entity/output; typed registrations own actions.
    /// </summary>
    internal sealed class ReconciliationPlan
    {
        private readonly FactSimulation _simulation;
        private readonly FactFeatureRegistry _registry;
        private readonly FactStore _facts;
        private readonly EntityStore _entities;
        private ReconciliationStage _stage;
        private int _entity;
        private int _fact;
        private int _output;

        internal ReconciliationPlan(FactSimulation simulation, FactFeatureRegistry registry, FactStore facts, EntityStore entities)
        {
            _simulation = simulation;
            _registry = registry;
            _facts = facts;
            _entities = entities;
        }

        internal void Clear()
        {
            _stage = ReconciliationStage.Affected;
            _entity = _fact = _output = 0;
        }

        /// <summary>
        /// [INTEGRATION] Range: closed facts and unchanged input revision. Condition: cooperative time slice. Output: fully prepared transaction or exact suspension.
        /// </summary>
        internal bool TryPrepare(ReduceOptions options, long startTimestamp)
        {
            while (true)
            {
                if (options.MaxMilliseconds > 0
                    && (Stopwatch.GetTimestamp() - startTimestamp) * 1000L / Stopwatch.Frequency > options.MaxMilliseconds)
                    return false;
                var started = Stopwatch.GetTimestamp();
                var validating = _stage == ReconciliationStage.Validate;
                switch (_stage)
                {
                    case ReconciliationStage.Affected:
                        if (_entity == _facts.TouchedEntityCount) { Advance(ReconciliationStage.Absent); break; }
                        var entity = _facts.TouchedEntityAt(_entity);
                        var routes = _facts.FactRoutes(entity);
                        if (_entities.IsDestroyed(entity) || _fact == routes.Length)
                        { _entity++; _fact = _output = 0; break; }
                        var route = routes[_fact];
                        if (_output == route.AffectedOutputCount) { _fact++; _output = 0; break; }
                        route.AffectedOutputAt(_output++).QueueCommitAction(_simulation, entity);
                        break;
                    case ReconciliationStage.Absent:
                        if (_output == _registry.AbsenceOutputs.Count) { Advance(ReconciliationStage.Lifecycle); break; }
                        var output = _registry.AbsenceOutputs[_output];
                        if (_entity == output.StateEntityCount) { _output++; _entity = 0; break; }
                        output.QueueAbsentCommitAction(_simulation, _entity++);
                        break;
                    case ReconciliationStage.Lifecycle:
                        if (_entity == _entities.PendingDestroyCount) { Advance(ReconciliationStage.Validate); break; }
                        if (_output == _registry.Outputs.Count) { _entity++; _output = 0; break; }
                        _registry.Outputs[_output++].QueueDeleteAction(_simulation, _entities.PendingDestroyAt(_entity));
                        break;
                    case ReconciliationStage.Validate:
                        if (_output == _registry.Outputs.Count) { _stage = ReconciliationStage.Ready; break; }
                        var registration = _registry.Outputs[_output];
                        if (_entity == registration.QueuedActionCount) { _output++; _entity = 0; break; }
                        registration.ValidateAction(_entities, _entity++);
                        break;
                    default:
                        return true;
                }
                var elapsed = Stopwatch.GetTimestamp() - started;
                if (validating) _simulation.Metrics.ValidationTicks += elapsed;
                else _simulation.Metrics.PlanningTicks += elapsed;
            }
        }

        private void Advance(ReconciliationStage stage)
        {
            _stage = stage;
            _entity = _fact = _output = 0;
        }
    }
}
