#nullable enable

using System;
using Hestia;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class HestiaGameCascadeTests
    {
        [Test]
        public void AmmoSpendMutatesCurrentAmmoOnlyWhenAmmoRemainsNonEmpty()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            EnableAmmoInput(cascade, entityId);
            cascade.SetInitialAmmo(entityId, ammo: 2);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(1, cascade.GetAmmo(entityId));
            Assert.IsFalse(cascade.IsAmmoEmpty(entityId));
            Assert.AreEqual(1, cascade.Engine.MutationCount);
            AssertMutation(cascade, 0, entityId, cascade.Schema.AmmoCurrent);
            Assert.IsTrue(cascade.Engine.WasPropertyMutated(entityId, cascade.Schema.AmmoCurrent));
            Assert.IsFalse(cascade.Engine.WasPropertyMutated(entityId, cascade.Schema.AmmoEmpty));
        }

        [Test]
        public void AmmoEmptyTransitionMutatesCurrentEmptyAndAudioCue()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            EnableAmmoInput(cascade, entityId);
            cascade.SetInitialAmmo(entityId, ammo: 1);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(0, cascade.GetAmmo(entityId));
            Assert.IsTrue(cascade.IsAmmoEmpty(entityId));
            Assert.AreEqual(3, cascade.Engine.MutationCount);
            AssertMutation(cascade, 0, entityId, cascade.Schema.AmmoCurrent);
            AssertMutation(cascade, 1, entityId, cascade.Schema.AmmoEmpty);
            AssertMutation(cascade, 2, entityId, cascade.Schema.PublishedAudioCues);
            Assert.AreEqual(2, cascade.Engine.LastCounters.ProducedFacts);
            Assert.AreEqual(2, cascade.Engine.LastCounters.ReducerRuns);
            Assert.AreEqual(3, cascade.Engine.LastCounters.MutatedProperties);
            Assert.AreEqual(1, cascade.Engine.LastCounters.TouchedEntities);

            cascade.RunTick();

            Assert.AreEqual(0, cascade.Engine.MutationCount);
            Assert.IsFalse(cascade.Engine.WasPropertyMutated(entityId, cascade.Schema.AmmoCurrent));
        }

        [Test]
        public void TypedMutationOutputCarriesPreviousAndNextValues()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            EnableAmmoInput(cascade, entityId);
            cascade.SetInitialAmmo(entityId, ammo: 2);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            var handlerRuns = 0;
            cascade.Engine.ForEachMutation(cascade.Schema.AmmoCurrent, (mutatedId, previous, next) =>
            {
                handlerRuns++;
                Assert.AreEqual(entityId, mutatedId);
                Assert.AreEqual(2, previous);
                Assert.AreEqual(1, next);
            });

            Assert.AreEqual(1, handlerRuns);
        }

        [Test]
        public void MovementPositionMutatesOnlyPosition()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);

            cascade.InputMove(entityId, desiredPosition: 12.5f);
            cascade.RunTick();

            Assert.AreEqual(12.5f, cascade.GetPosition(entityId), 0.0001f);
            Assert.AreEqual(1, cascade.Engine.MutationCount);
            AssertMutation(cascade, 0, entityId, cascade.Schema.Position);
            Assert.IsFalse(cascade.Engine.WasPropertyMutated(entityId, cascade.Schema.AmmoCurrent));
        }

        [Test]
        public void HigherPriorityMoveWinsSamePropertyConflict()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);

            cascade.InputMove(entityId, desiredPosition: 10f, priority: 5);
            cascade.InputMove(entityId, desiredPosition: 99f, priority: 1);
            cascade.RunTick();

            Assert.AreEqual(10f, cascade.GetPosition(entityId), 0.0001f);
            Assert.AreEqual(1, cascade.Engine.MutationCount);
        }

        [Test]
        public void EqualPriorityMoveOverwritesEarlierStagedValue()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);

            cascade.InputMove(entityId, desiredPosition: 5f, priority: 0);
            cascade.InputMove(entityId, desiredPosition: 7f, priority: 0);
            cascade.RunTick();

            Assert.AreEqual(7f, cascade.GetPosition(entityId), 0.0001f);
            Assert.AreEqual(1, cascade.Engine.MutationCount);
        }

        [Test]
        public void PositionWithinEpsilonDoesNotMutate()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);

            cascade.InputMove(entityId, desiredPosition: 0.00005f);
            cascade.RunTick();

            Assert.AreEqual(0f, cascade.GetPosition(entityId), 0.000001f);
            Assert.AreEqual(0, cascade.Engine.MutationCount);
        }

        [Test]
        public void NonRelevantCueDoesNotProduceFactOrMutation()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);

            cascade.InputFootstepCue(entityId, isRelevant: false);
            cascade.RunTick();

            Assert.AreEqual(0, cascade.Engine.MutationCount);
            Assert.AreEqual(0, cascade.Engine.LastCounters.ProducedFacts);
            Assert.AreEqual(0, cascade.Engine.LastCounters.ReducerRuns);
            Assert.AreEqual(1, cascade.Engine.LastCounters.SkippedNonRelevant);
            Assert.AreEqual(0, cascade.Engine.LastCounters.TouchedEntities);
        }

        [Test]
        public void RelevantCueMutatesMarkerEachTick()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);

            cascade.InputFootstepCue(entityId, isRelevant: true);
            cascade.RunTick();
            AssertMutation(cascade, 0, entityId, cascade.Schema.PublishedAudioCues);

            cascade.InputFootstepCue(entityId, isRelevant: true);
            cascade.RunTick();
            AssertMutation(cascade, 0, entityId, cascade.Schema.PublishedAudioCues);
        }

        [Test]
        public void FactReducerRunsOnlyTouchedEntityWork()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 1000);
            var entityId = new CascadeEntityId(777);
            EnableAmmoInput(cascade, entityId);
            cascade.SetInitialAmmo(entityId, ammo: 2);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(1, cascade.Engine.LastCounters.ReducerRuns);
            Assert.AreEqual(1, cascade.Engine.LastCounters.TouchedEntities);
            Assert.AreEqual(1, cascade.GetAmmo(entityId));
            Assert.AreEqual(1, cascade.Engine.MutationCount);
            AssertMutation(cascade, 0, entityId, cascade.Schema.AmmoCurrent);
        }

        [Test]
        public void DuplicateFireFactsRunReducerForEachFactAndCommitOnce()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            EnableAmmoInput(cascade, entityId);
            cascade.SetInitialAmmo(entityId, ammo: 5);

            cascade.InputFireWeapon(entityId);
            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(3, cascade.GetAmmo(entityId));
            Assert.AreEqual(2, cascade.Engine.LastCounters.ProducedFacts);
            Assert.AreEqual(2, cascade.Engine.LastCounters.ProcessedFacts);
            Assert.AreEqual(2, cascade.Engine.LastCounters.ReducerRuns);
            Assert.AreEqual(4, cascade.Engine.LastCounters.RegisteredReducers);
            Assert.AreEqual(1, cascade.Engine.LastCounters.TouchedEntities);
            Assert.AreEqual(1, cascade.Engine.MutationCount);
            AssertMutation(cascade, 0, entityId, cascade.Schema.AmmoCurrent);
        }

        [Test]
        public void SchemaRejectsDuplicatePropertyName()
        {
            var schema = new CascadeSchema(entityCapacity: 4);
            schema.AddProperty<int>("Hp");

            Assert.Throws<InvalidOperationException>(() => schema.AddProperty<int>("Hp"));
        }

        [Test]
        public void SchemaRejectsDuplicateFactName()
        {
            var schema = new CascadeSchema(entityCapacity: 4);
            schema.AddFact<int>("Hit", NoopReducer);

            Assert.Throws<InvalidOperationException>(() => schema.AddFact<int>("Hit", NoopReducer));
        }

        [Test]
        public void SchemaSealsWhenEngineIsConstructed()
        {
            var schema = new CascadeSchema(entityCapacity: 4);
            var unused = new CascadeEngine(schema);

            Assert.Throws<InvalidOperationException>(() => schema.AddProperty<int>("LateProperty"));
            Assert.Throws<InvalidOperationException>(() => new CascadeEngine(schema));
        }

        [Test]
        public void EngineRejectsKeysFromAnotherSchema()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 4);
            var foreignSchema = new HestiaGameCascadeSchema(entityCapacity: 4);
            var entityId = new CascadeEntityId(1);

            Assert.Throws<InvalidOperationException>(
                () => cascade.Engine.ReadCommitted(entityId, foreignSchema.AmmoCurrent));
            Assert.Throws<InvalidOperationException>(
                () => cascade.Engine.EnqueueFact(entityId, foreignSchema.AmmoSpendRequested, 1));
        }

        [Test]
        public void ReducerFactCycleFailsTickAndAbortsStagedWork()
        {
            var schema = new CascadeSchema(entityCapacity: 4);
            var hp = schema.AddProperty<int>("Hp");
            CascadeFact<CascadeSignal> loop = null!;
            loop = schema.AddFact<CascadeSignal>("Loop", (context, signal) =>
            {
                context.Stage(hp, 99);
                context.Produce(loop);
            });
            var engine = new CascadeEngine(schema, maxReducerRunsPerTick: 8);
            var entityId = new CascadeEntityId(1);

            engine.EnqueueFact(entityId, loop);

            Assert.Throws<InvalidOperationException>(() => engine.RunTick());
            Assert.AreEqual(0, engine.ReadCommitted(entityId, hp));
            Assert.AreEqual(0, engine.MutationCount);
        }

        [Test]
        public void FailedReducerAbortsAllStagedWorkAndNextTickRecovers()
        {
            var schema = new CascadeSchema(entityCapacity: 4);
            var hp = schema.AddProperty<int>("Hp");
            var stageThenFail = schema.AddFact<int>("StageThenFail", (context, value) =>
            {
                context.Stage(hp, value);
                throw new InvalidOperationException("Reducer failed.");
            });
            var setHp = schema.AddFact<int>("SetHp", (context, value) => context.Stage(hp, value));
            var engine = new CascadeEngine(schema);
            var entityId = new CascadeEntityId(1);

            engine.EnqueueFact(entityId, stageThenFail, 50);
            Assert.Throws<InvalidOperationException>(() => engine.RunTick());
            Assert.AreEqual(0, engine.ReadCommitted(entityId, hp));
            Assert.AreEqual(0, engine.MutationCount);

            engine.EnqueueFact(entityId, setHp, 7);
            engine.RunTick();
            Assert.AreEqual(7, engine.ReadCommitted(entityId, hp));
            Assert.AreEqual(1, engine.MutationCount);
        }

        [Test]
        public void DestroyedEntityFactsDoNotRunReducersOrStopTick()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            EnableAmmoInput(cascade, entityId);
            cascade.SetInitialAmmo(entityId, ammo: 2);
            cascade.Engine.DestroyEntity(entityId);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(1, cascade.Engine.LastCounters.ProducedFacts);
            Assert.AreEqual(1, cascade.Engine.LastCounters.ProcessedFacts);
            Assert.AreEqual(0, cascade.Engine.LastCounters.ReducerRuns);
            Assert.AreEqual(0, cascade.GetAmmo(entityId));
            Assert.IsFalse(cascade.HasEntityFlag(entityId, cascade.Schema.AcceptsAmmoInput));
            Assert.AreEqual(0, cascade.Engine.MutationCount);
        }

        [Test]
        public void HestiaCanChangeEntityFlagsBeforeFirstTick()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(9);
            var flag = cascade.Schema.AcceptsAmmoInput;

            Assert.IsFalse(cascade.HasEntityFlag(entityId, flag));

            cascade.SetEntityFlag(entityId, flag);
            Assert.IsTrue(cascade.HasEntityFlag(entityId, flag));

            cascade.ClearEntityFlag(entityId, flag);
            Assert.IsFalse(cascade.HasEntityFlag(entityId, flag));

            cascade.SetEntityFlag(entityId, flag, enabled: true);
            Assert.IsTrue(cascade.HasEntityFlag(entityId, flag));

            cascade.SetEntityFlag(entityId, flag, enabled: false);
            Assert.IsFalse(cascade.HasEntityFlag(entityId, flag));
        }

        [Test]
        public void ReducerSkipsAmmoSpendWhenEntityLacksFlag()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            cascade.SetInitialAmmo(entityId, ammo: 2);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(2, cascade.GetAmmo(entityId));
            Assert.AreEqual(1, cascade.Engine.LastCounters.ReducerRuns);
            Assert.AreEqual(0, cascade.Engine.LastCounters.TouchedEntities);
            Assert.AreEqual(0, cascade.Engine.MutationCount);
        }

        private static void AssertMutation(
            HestiaGameCascade cascade,
            int index,
            CascadeEntityId entityId,
            CascadePropertyKey property)
        {
            var mutation = cascade.Engine.GetMutation(index);
            Assert.AreEqual(entityId, mutation.EntityId);
            Assert.AreSame(property, mutation.Property);
        }

        private static void NoopReducer(CascadeReducerContext context, int payload)
        {
        }

        private static void EnableAmmoInput(HestiaGameCascade cascade, CascadeEntityId entityId)
        {
            cascade.SetEntityFlag(entityId, cascade.Schema.AcceptsAmmoInput);
        }
    }
}