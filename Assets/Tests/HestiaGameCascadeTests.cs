#nullable enable

using System;
using Hestia;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class HestiaGameCascadeTests
    {
        [Test]
        public void AmmoSpendDirtiesTextButNotIconWhenAmmoRemainsNonEmpty()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            EnableAmmoHudEntity(cascade, entityId);
            cascade.SetInitialAmmo(entityId, ammo: 2);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(1, cascade.GetAmmo(entityId));
            Assert.IsFalse(cascade.IsAmmoEmpty(entityId));
            Assert.IsTrue(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoText));
            Assert.IsTrue(cascade.IsConsumerDirty(entityId, HestiaGameCascadeSchema.Consumers.HudAmmoText));
            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoIcon));
            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.AudioCue));
        }

        [Test]
        public void AmmoEmptyTransitionDirtiesIconAndAudio()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            EnableAmmoHudEntity(cascade, entityId);
            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.PublishesAudioCues);
            cascade.SetInitialAmmo(entityId, ammo: 1);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(0, cascade.GetAmmo(entityId));
            Assert.IsTrue(cascade.IsAmmoEmpty(entityId));
            Assert.IsTrue(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoText));
            Assert.IsTrue(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoIcon));
            Assert.IsTrue(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.AudioCue));
            Assert.IsTrue(cascade.IsConsumerDirty(entityId, HestiaGameCascadeSchema.Consumers.AudioCue));
            Assert.AreEqual(2, cascade.LastCounters.ProducedFacts);
            Assert.AreEqual(2, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(1, cascade.LastCounters.TouchedEntities);

            cascade.RunTick();

            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoText));
            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoIcon));
            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.AudioCue));
        }

        [Test]
        public void MovementPositionDirtiesOnlyCharacterMotorConsumer()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);
            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.PublishesCharacterMotor);

            cascade.InputMove(entityId, desiredPosition: 12.5f);
            cascade.RunTick();

            Assert.AreEqual(12.5f, cascade.GetPosition(entityId), 0.0001f);
            Assert.IsTrue(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.CharacterMotor));
            Assert.IsTrue(cascade.IsConsumerDirty(entityId, HestiaGameCascadeSchema.Consumers.CharacterMotor));
            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoText));
            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoIcon));
            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.AudioCue));
        }

        [Test]
        public void NonRelevantCueDoesNotProduceFactOrConsumerWork()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(5);

            cascade.InputFootstepCue(entityId, isRelevant: false);
            cascade.RunTick();

            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.AudioCue));
            Assert.AreEqual(0, cascade.LastCounters.ProducedFacts);
            Assert.AreEqual(0, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(1, cascade.LastCounters.SkippedNonRelevant);
            Assert.AreEqual(0, cascade.LastCounters.TouchedEntities);
        }

        [Test]
        public void FactReducerRunsOnlyTouchedEntityWork()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 1000);
            var entityId = new CascadeEntityId(777);
            EnableAmmoHudEntity(cascade, entityId);
            cascade.SetInitialAmmo(entityId, ammo: 2);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(1, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(1, cascade.LastCounters.TouchedEntities);
            Assert.AreEqual(1, cascade.GetAmmo(entityId));
            Assert.IsTrue(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoText));
        }

        [Test]
        public void DuplicateFireFactsRunReducerForEachFactAndCommitOnce()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            EnableAmmoHudEntity(cascade, entityId);
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
            var dirtyConsumers = new CascadeDirtyConsumerSet();
            var committers = new CascadePropertyCommitMap();

            entities.Get(entityId).Stage(property, CascadeValue.From(7));
            touched.Mark(entityId);

            Assert.Throws<InvalidOperationException>(() => entities.CommitTouched(touched, committers, dirtyConsumers));
            Assert.AreEqual(0, entities.Get(entityId).GetCommittedOrDefault<int>(property));
        }

        [Test]
        public void CoreCommitClearsDestroyedEntityWithoutCommitter()
        {
            var entityId = new CascadeEntityId(1);
            var property = new CascadePropertyKey(36, "DestroyedEntityProperty");
            var entities = new CascadeEntityStateStore(entityCapacity: 4);
            var touched = new CascadeTouchedEntitySet(entityCapacity: 4);
            var dirtyConsumers = new CascadeDirtyConsumerSet();
            var committers = new CascadePropertyCommitMap();

            entities.Get(entityId).Stage(property, CascadeValue.From(7));
            touched.Mark(entityId);
            entities.Destroy(entityId);

            Assert.DoesNotThrow(() => entities.CommitTouched(touched, committers, dirtyConsumers));
            Assert.AreEqual(0, entities.Get(entityId).StagedPropertyCount);
            Assert.AreEqual(0, dirtyConsumers.Count);
        }

        [Test]
        public void DestroyedEntityFactsDoNotRunReducersOrStopTick()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            EnableAmmoHudEntity(cascade, entityId);
            cascade.SetInitialAmmo(entityId, ammo: 2);
            cascade.DestroyEntity(entityId);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(1, cascade.LastCounters.ProducedFacts);
            Assert.AreEqual(1, cascade.LastCounters.ProcessedFacts);
            Assert.AreEqual(0, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(0, cascade.GetAmmo(entityId));
            Assert.IsFalse(cascade.HasEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput));
            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoText));
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
            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.PublishesHudAmmo);
            cascade.SetInitialAmmo(entityId, ammo: 2);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(2, cascade.GetAmmo(entityId));
            Assert.AreEqual(1, cascade.LastCounters.ReducerRuns);
            Assert.AreEqual(0, cascade.LastCounters.TouchedEntities);
            Assert.IsFalse(cascade.IsConsumerDirty(entityId, HestiaGameCascadeSchema.Consumers.HudAmmoText));
        }

        [Test]
        public void EntityFlagsFilterConsumerWorkButNotCommittedState()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput);
            cascade.SetInitialAmmo(entityId, ammo: 2);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(1, cascade.GetAmmo(entityId));
            Assert.AreEqual(0, cascade.LastCounters.DirtyConsumers);
            Assert.IsFalse(cascade.IsConsumerDirty(entityId, HestiaGameCascadeSchema.Consumers.HudAmmoText));

            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.PublishesHudAmmo);
            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(0, cascade.GetAmmo(entityId));
            Assert.IsTrue(cascade.IsConsumerDirty(entityId, HestiaGameCascadeSchema.Consumers.HudAmmoText));
        }

        [Test]
        public void DirtyConsumersAreEntityScopedAndExposeCommittedEntity()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var firstEntityId = new CascadeEntityId(3);
            var secondEntityId = new CascadeEntityId(7);
            EnableAmmoHudEntity(cascade, firstEntityId);
            EnableAmmoHudEntity(cascade, secondEntityId);
            cascade.SetInitialAmmo(firstEntityId, ammo: 2);
            cascade.SetInitialAmmo(secondEntityId, ammo: 2);

            cascade.InputFireWeapon(firstEntityId);
            cascade.InputFireWeapon(secondEntityId);
            cascade.RunTick();

            Assert.IsTrue(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoText));
            Assert.IsTrue(cascade.IsConsumerDirty(firstEntityId, HestiaGameCascadeSchema.Consumers.HudAmmoText));
            Assert.IsTrue(cascade.IsConsumerDirty(secondEntityId, HestiaGameCascadeSchema.Consumers.HudAmmoText));
            Assert.AreEqual(2, cascade.DirtyConsumerEntityCount);
            Assert.AreEqual(2, cascade.LastCounters.DirtyConsumers);
            Assert.AreEqual(firstEntityId, cascade.GetDirtyConsumerEntityId(0));
            Assert.AreEqual(1, cascade.GetDirtyConsumerEntity(0).GetCommittedOrDefault<int>(HestiaGameCascadeSchema.Properties.AmmoCurrent));
        }

        [Test]
        public void DirtyConsumerWorkItemsDrainCommittedValues()
        {
            var cascade = new HestiaGameCascade(entityCapacity: 32);
            var entityId = new CascadeEntityId(3);
            var consumer = new RecordingHestiaConsumer();
            EnableAmmoHudEntity(cascade, entityId);
            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.PublishesAudioCues);
            cascade.SetInitialAmmo(entityId, ammo: 1);

            cascade.InputFireWeapon(entityId);
            cascade.RunTick();

            Assert.AreEqual(3, cascade.DirtyConsumerWorkCount);
            Assert.AreEqual(HestiaGameCascadeSchema.Consumers.HudAmmoText, cascade.GetDirtyConsumerWorkItem(0).Consumer);
            Assert.AreEqual(HestiaGameCascadeSchema.Consumers.HudAmmoIcon, cascade.GetDirtyConsumerWorkItem(1).Consumer);
            Assert.AreEqual(HestiaGameCascadeSchema.Consumers.AudioCue, cascade.GetDirtyConsumerWorkItem(2).Consumer);

            cascade.DrainDirtyConsumers(consumer);

            Assert.AreEqual("AmmoText:3:0|AmmoIcon:3:True|Audio:3", consumer.Events);
            Assert.AreEqual(0, cascade.DirtyConsumerWorkCount);
            Assert.IsFalse(cascade.IsConsumerDirty(HestiaGameCascadeSchema.Consumers.HudAmmoText));
        }

        [Test]
        public void CommitPreflightDoesNotPublishAnyPropertyWhenACommitterIsMissing()
        {
            var entityId = new CascadeEntityId(1);
            var committedProperty = new CascadePropertyKey(37, "CommittedProperty");
            var missingProperty = new CascadePropertyKey(38, "MissingProperty");
            var entities = new CascadeEntityStateStore(entityCapacity: 4);
            var touched = new CascadeTouchedEntitySet(entityCapacity: 4);
            var dirtyConsumers = new CascadeDirtyConsumerSet();
            var committers = new CascadePropertyCommitMap();

            entities.Get(entityId).Stage(committedProperty, CascadeValue.From(7));
            entities.Get(entityId).Stage(missingProperty, CascadeValue.From(9));
            touched.Mark(entityId);
            committers.Register(committedProperty, PublishOnlyCommitter);

            Assert.Throws<InvalidOperationException>(() => entities.CommitTouched(touched, committers, dirtyConsumers));
            Assert.AreEqual(0, entities.Get(entityId).GetCommittedOrDefault<int>(committedProperty));
            Assert.AreEqual(0, entities.Get(entityId).GetCommittedOrDefault<int>(missingProperty));
            Assert.AreEqual(0, dirtyConsumers.Count);
        }

        private static void NoopReducer(object context, CascadeFact fact)
        {
        }

        private static void NoopCommitter(CascadePropertyCommitContext context)
        {
        }

        private static void PublishOnlyCommitter(CascadePropertyCommitContext context)
        {
            context.PublishStagedIfChanged();
        }

        private static void EnableAmmoHudEntity(HestiaGameCascade cascade, CascadeEntityId entityId)
        {
            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.AcceptsAmmoInput);
            cascade.SetEntityFlag(entityId, HestiaGameCascadeSchema.EntityFlags.PublishesHudAmmo);
        }

        private sealed class RecordingHestiaConsumer : IHestiaGameCascadeConsumer
        {
            public string Events { get; private set; } = string.Empty;

            public void RefreshHudAmmoText(CascadeEntityId entityId, int ammo)
            {
                Append($"AmmoText:{entityId.Value}:{ammo}");
            }

            public void RefreshHudAmmoIcon(CascadeEntityId entityId, bool isAmmoEmpty)
            {
                Append($"AmmoIcon:{entityId.Value}:{isAmmoEmpty}");
            }

            public void RefreshCharacterMotor(CascadeEntityId entityId, float position)
            {
                Append($"Motor:{entityId.Value}:{position}");
            }

            public void PlayAudioCue(CascadeEntityId entityId)
            {
                Append($"Audio:{entityId.Value}");
            }

            private void Append(string value)
            {
                Events = Events.Length == 0
                    ? value
                    : Events + "|" + value;
            }
        }
    }
}
