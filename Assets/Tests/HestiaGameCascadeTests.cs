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
            Assert.AreEqual(1, cascade.MutationCount);
            AssertMutation(cascade, 0, entityId, HestiaGameCascadeSchema.Properties.AmmoCurrent);
            Assert.IsTrue(cascade.WasPropertyMutated(entityId, HestiaGameCascadeSchema.Properties.AmmoCurrent));
            Assert.IsFalse(cascade.WasPropertyMutated(entityId, HestiaGameCascadeSchema.Properties.AmmoEmpty));
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
            Assert.AreEqual(3, cascade.MutationCount);
            AssertMutation(cascade, 0, entityId, HestiaGameCascadeSchema.Properties.AmmoCurrent);
            AssertMutation(cascade, 1, entityId, HestiaGameCascadeSchema.Properties.AmmoEmpty);
            AssertMutation(cascade, 2, entityId, HestiaGameCascadeSchema.Properties.PublishedAudioCues);
            Assert.AreEqual(2, cascade.LastCounters.ProducedFacts);
            Assert.AreEqual(2, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(3, cascade.LastCounters.MutatedProperties);
            Assert.AreEqual(1, cascade.LastCounters.TouchedEntities);

            cascade.RunTick();

            Assert.AreEqual(0, cascade.MutationCount);
            Assert.IsFalse(cascade.WasPropertyMutated(entityId, HestiaGameCascadeSchema.Properties.AmmoCurrent));
        }

        [Test]
        public void MovementPositionMutatesOnlyPosition()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);

            cascade.InputMove(entityId, desiredPosition: 12.5f);
            cascade.RunTick();

            Assert.AreEqual(12.5f, cascade.GetPosition(entityId), 0.0001f);
            Assert.AreEqual(1, cascade.MutationCount);
            AssertMutation(cascade, 0, entityId, HestiaGameCascadeSchema.Properties.Position);
            Assert.IsFalse(cascade.WasPropertyMutated(entityId, HestiaGameCascadeSchema.Properties.AmmoCurrent));
        }

        [Test]
        public void NonRelevantCueDoesNotProduceFactOrMutation()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);

            cascade.InputFootstepCue(entityId, isRelevant: false);
            cascade.RunTick();

            Assert.AreEqual(0, cascade.MutationCount);
            Assert.AreEqual(0, cascade.LastCounters.ProducedFacts);
            Assert.AreEqual(0, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(1, cascade.LastCounters.SkippedNonRelevant);
            Assert.AreEqual(0, cascade.LastCounters.TouchedEntities);
        }

        [Test]
        public void RelevantCueMutatesMarkerEachTick()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);

            cascade.InputFootstepCue(entityId, isRelevant: true);
            cascade.RunTick();
            AssertMutation(cascade, 0, entityId, HestiaGameCascadeSchema.Properties.PublishedAudioCues);

            cascade.InputFootstepCue(entityId, isRelevant: true);
            cascade.RunTick();
            AssertMutation(cascade, 0, entityId, HestiaGameCascadeSchema.Properties.PublishedAudioCues);
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

            Assert.AreEqual(1, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(1, cascade.LastCounters.TouchedEntities);
            Assert.AreEqual(1, cascade.GetAmmo(entityId));
            Assert.AreEqual(1, cascade.MutationCount);
            AssertMutation(cascade, 0, entityId, HestiaGameCascadeSchema.Properties.AmmoCurrent);
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
            Assert.AreEqual(2, cascade.LastCounters.ProducedFacts);
            Assert.AreEqual(2, cascade.LastCounters.ProcessedFacts);
            Assert.AreEqual(2, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(4, cascade.LastCounters.RegisteredReducers);
            Assert.AreEqual(1, cascade.LastCounters.TouchedEntities);
            Assert.AreEqual(1, cascade.MutationCount);
            AssertMutation(cascade, 0, entityId, HestiaGameCascadeSchema.Properties.AmmoCurrent);
        }

        [Test]
        public void ReducerMapRejectsDuplicateFactKind()
        {
            var reducers = new CascadeReducerMap<object>();
            var factKind = new CascadeFactKey(31, "DuplicateTestFact");

            reducers.Register(factKind, NoopReducer);

            Assert.Throws<InvalidOperationException>(() => reducers.Register(factKind, NoopReducer));
        }

        [Test]
        public void ReducerMapFailsUnknownFactKind()
        {
            var reducers = new CascadeReducerMap<object>();
            var factKind = new CascadeFactKey(32, "UnknownTestFact");

            Assert.Throws<InvalidOperationException>(() => reducers.GetRequired(factKind));
        }

        [Test]
        public void PropertyCommitMapRejectsDuplicateProperty()
        {
            var committers = new CascadePropertyCommitMap();
            var property = new CascadePropertyKey(33, "DuplicateTestProperty");

            committers.Register(property, NoopCommitter);

            Assert.Throws<InvalidOperationException>(() => committers.Register(property, NoopCommitter));
        }

        [Test]
        public void PropertyCommitMapFailsUnknownProperty()
        {
            var committers = new CascadePropertyCommitMap();
            var property = new CascadePropertyKey(34, "UnknownTestProperty");

            Assert.Throws<InvalidOperationException>(() => committers.GetRequired(property));
        }

        [Test]
        public void CoreCommitRequiresRegisteredPropertyCommitter()
        {
            var entityId = new CascadeEntityId(1);
            var property = new CascadePropertyKey(35, "RequiredCommitterProperty");
            var entities = new CascadeEntityStateStore(entityCapacity: 4);
            var touched = new CascadeTouchedEntitySet(entityCapacity: 4);
            var mutations = new CascadePropertyMutationSet();
            var committers = new CascadePropertyCommitMap();

            entities.Get(entityId).Stage(property, CascadeValue.From(7));
            touched.Mark(entityId);

            Assert.Throws<InvalidOperationException>(() => entities.CommitTouched(touched, committers, mutations));
            Assert.AreEqual(0, entities.Get(entityId).GetCommittedOrDefault<int>(property));
            Assert.AreEqual(0, mutations.Count);
        }

        [Test]
        public void CoreCommitClearsDestroyedEntityWithoutCommitter()
        {
            var entityId = new CascadeEntityId(1);
            var property = new CascadePropertyKey(36, "DestroyedEntityProperty");
            var entities = new CascadeEntityStateStore(entityCapacity: 4);
            var touched = new CascadeTouchedEntitySet(entityCapacity: 4);
            var mutations = new CascadePropertyMutationSet();
            var committers = new CascadePropertyCommitMap();

            entities.Get(entityId).Stage(property, CascadeValue.From(7));
            touched.Mark(entityId);
            entities.Destroy(entityId);

            Assert.DoesNotThrow(() => entities.CommitTouched(touched, committers, mutations));
            Assert.AreEqual(0, entities.Get(entityId).StagedPropertyCount);
            Assert.AreEqual(0, mutations.Count);
        }

        [Test]
        public void DestroyedEntityFactsDoNotRunReducersOrStopTick()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            EnableAmmoInput(cascade, entityId);
            cascade.SetInitialAmmo(entityId, ammo: 2);
            cascade.DestroyEntity(entityId);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(1, cascade.LastCounters.ProducedFacts);
            Assert.AreEqual(1, cascade.LastCounters.ProcessedFacts);
            Assert.AreEqual(0, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(0, cascade.GetAmmo(entityId));
            Assert.IsFalse(cascade.HasEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput));
            Assert.AreEqual(0, cascade.MutationCount);
        }

        [Test]
        public void HestiaCanChangeEntityFlagsBeforeFirstTick()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(9);

            Assert.IsFalse(cascade.HasEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput));

            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput);
            Assert.IsTrue(cascade.HasEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput));

            cascade.ClearEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput);
            Assert.IsFalse(cascade.HasEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput));

            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput, enabled: true);
            Assert.IsTrue(cascade.HasEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput));

            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput, enabled: false);
            Assert.IsFalse(cascade.HasEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput));
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
            Assert.AreEqual(1, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(0, cascade.LastCounters.TouchedEntities);
            Assert.AreEqual(0, cascade.MutationCount);
        }

        [Test]
        public void CommitPreflightDoesNotCommitAnyPropertyWhenACommitterIsMissing()
        {
            var entityId = new CascadeEntityId(1);
            var committedProperty = new CascadePropertyKey(37, "CommittedProperty");
            var missingProperty = new CascadePropertyKey(38, "MissingProperty");
            var entities = new CascadeEntityStateStore(entityCapacity: 4);
            var touched = new CascadeTouchedEntitySet(entityCapacity: 4);
            var mutations = new CascadePropertyMutationSet();
            var committers = new CascadePropertyCommitMap();

            entities.Get(entityId).Stage(committedProperty, CascadeValue.From(7));
            entities.Get(entityId).Stage(missingProperty, CascadeValue.From(9));
            touched.Mark(entityId);
            committers.Register(committedProperty, CommitOnlyCommitter);

            Assert.Throws<InvalidOperationException>(() => entities.CommitTouched(touched, committers, mutations));
            Assert.AreEqual(0, entities.Get(entityId).GetCommittedOrDefault<int>(committedProperty));
            Assert.AreEqual(0, entities.Get(entityId).GetCommittedOrDefault<int>(missingProperty));
            Assert.AreEqual(0, mutations.Count);
        }

        private static void AssertMutation(
            HestiaGameCascade cascade,
            int index,
            CascadeEntityId entityId,
            CascadePropertyKey property)
        {
            var mutation = cascade.GetMutation(index);
            Assert.AreEqual(entityId, mutation.EntityId);
            Assert.AreEqual(property, mutation.Property);
        }

        private static void NoopReducer(object context, CascadeFact fact)
        {
        }

        private static void NoopCommitter(CascadePropertyCommitContext context)
        {
        }

        private static void CommitOnlyCommitter(CascadePropertyCommitContext context)
        {
            context.CommitStagedIfChanged();
        }

        private static void EnableAmmoInput(HestiaGameCascade cascade, CascadeEntityId entityId)
        {
            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput);
        }
    }
}
