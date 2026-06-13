#nullable enable

using System;
using CascadeEngineApi;
using Hestia;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class HestiaGameContextTests
    {
        [Test]
        public void FireWeaponReducesRequestAndCommitsAmmoOnce()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();
            cascade.SetInitialAmmo(entity, ammo: 2);

            cascade.InputFireWeapon(entity);
            var result = cascade.RunTick();

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(1, cascade.GetAmmo(entity).Current);
            Assert.IsFalse(cascade.GetAmmo(entity).IsEmpty);
            Assert.AreEqual(2, result.AcceptedFacts);
            Assert.AreEqual(2, result.ProcessedFacts);
            Assert.AreEqual(1, result.ReducerInvocations);
            Assert.AreEqual(1, result.MutationCount);
        }

        [Test]
        public void AmmoEmptyTransitionPublishesAmmoAndDryFireCueMutations()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();
            cascade.SetInitialAmmo(entity, ammo: 1);

            cascade.InputFireWeapon(entity);
            var result = cascade.RunTick();

            Assert.AreEqual(0, cascade.GetAmmo(entity).Current);
            Assert.IsTrue(cascade.GetAmmo(entity).IsEmpty);
            Assert.AreEqual(2, result.MutationCount);

            var ammoMutations = 0;
            cascade.Simulation.ForEachMutation(
                cascade.Feature.Ammo,
                (EntityRef mutatedEntity, in StateMutation<HestiaAmmoState> mutation) =>
                {
                    ammoMutations++;
                    Assert.AreEqual(entity, mutatedEntity);
                    Assert.IsTrue(mutation.HadPrevious);
                    Assert.AreEqual(1, mutation.Previous.Current);
                    Assert.IsTrue(mutation.HasNext);
                    Assert.AreEqual(0, mutation.Next.Current);
                });

            var audioMutations = 0;
            cascade.Simulation.ForEachMutation(
                cascade.Feature.AudioCue,
                (EntityRef mutatedEntity, in StateMutation<HestiaAudioCueState> mutation) =>
                {
                    audioMutations++;
                    Assert.AreEqual(entity, mutatedEntity);
                    Assert.IsFalse(mutation.HadPrevious);
                    Assert.IsTrue(mutation.HasNext);
                    Assert.AreEqual(HestiaAudioCueKind.DryFire, mutation.Next.Cue);
                    Assert.AreEqual(1, mutation.Next.Version);
                });

            Assert.AreEqual(1, ammoMutations);
            Assert.AreEqual(1, audioMutations);
        }

        [Test]
        public void DuplicateFactsAreDeduplicatedWithinOneTick()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();
            cascade.SetInitialAmmo(entity, ammo: 5);

            cascade.InputFireWeapon(entity);
            cascade.InputFireWeapon(entity);
            var result = cascade.RunTick();

            Assert.AreEqual(4, cascade.GetAmmo(entity).Current);
            Assert.AreEqual(2, result.AcceptedFacts);
            Assert.AreEqual(1, result.DeduplicatedFacts);
            Assert.AreEqual(1, result.ReducerInvocations);
            Assert.AreEqual(1, result.MutationCount);
        }

        [Test]
        public void DistinctAmmoFactsFoldIntoOneOutputMutation()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();
            cascade.SetInitialAmmo(entity, ammo: 5);

            cascade.InputFireWeapon(entity, amount: 1);
            cascade.InputFireWeapon(entity, amount: 2);
            var result = cascade.RunTick();

            Assert.AreEqual(2, cascade.GetAmmo(entity).Current);
            Assert.AreEqual(4, result.AcceptedFacts);
            Assert.AreEqual(2, result.ReducerInvocations);
            Assert.AreEqual(1, result.MutationCount);
        }

        [Test]
        public void MissingAmmoStateSkipsSpendAndCreatesNoDefaultState()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();

            cascade.InputFireWeapon(entity);
            var result = cascade.RunTick();

            Assert.IsFalse(cascade.TryGetAmmo(entity, out _));
            Assert.AreEqual(1, result.AcceptedFacts);
            Assert.AreEqual(1, result.ProcessedFacts);
            Assert.AreEqual(1, result.ReducerInvocations);
            Assert.AreEqual(0, result.MutationCount);
        }

        [Test]
        public void HighestPriorityMoveWinsAndPublishesTypedMutation()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();
            cascade.SetInitialPosition(entity, 0f);

            cascade.InputMove(entity, desiredPosition: 10f, priority: FactPriority.Low);
            cascade.InputMove(entity, desiredPosition: 99f, priority: FactPriority.PlayerVisible);
            var result = cascade.RunTick();

            Assert.AreEqual(99f, cascade.GetPosition(entity).Position, 0.0001f);
            Assert.AreEqual(4, result.AcceptedFacts);
            Assert.AreEqual(2, result.ReducerInvocations);
            Assert.AreEqual(1, result.MutationCount);

            var positionMutations = 0;
            cascade.Simulation.ForEachMutation(
                cascade.Feature.Position,
                (EntityRef mutatedEntity, in StateMutation<HestiaPositionState> mutation) =>
                {
                    positionMutations++;
                    Assert.AreEqual(entity, mutatedEntity);
                    Assert.IsTrue(mutation.HadPrevious);
                    Assert.AreEqual(0f, mutation.Previous.Position, 0.0001f);
                    Assert.AreEqual(99f, mutation.Next.Position, 0.0001f);
                });

            Assert.AreEqual(1, positionMutations);
        }

        [Test]
        public void EqualPriorityMoveConflictThrowsAndDoesNotCommit()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();
            cascade.SetInitialPosition(entity, 0f);

            cascade.InputMove(entity, desiredPosition: 5f, priority: FactPriority.Normal);
            cascade.InputMove(entity, desiredPosition: 7f, priority: FactPriority.Normal);

            Assert.Throws<CommitConflictException>(() => cascade.RunTick());
            Assert.AreEqual(0f, cascade.GetPosition(entity).Position, 0.0001f);
            Assert.AreEqual(0, cascade.Simulation.MutationCount);
        }

        [Test]
        public void PositionWithinEpsilonDoesNotPublishMutation()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();
            cascade.SetInitialPosition(entity, 0f);

            cascade.InputMove(entity, desiredPosition: 0.00005f);
            var result = cascade.RunTick();

            Assert.AreEqual(0f, cascade.GetPosition(entity).Position, 0.0001f);
            Assert.AreEqual(0, result.MutationCount);
        }

        [Test]
        public void RelevantFootstepPublishesMarkerEachTick()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();

            Assert.IsTrue(cascade.InputFootstepCue(entity, isRelevant: true));
            cascade.RunTick();
            AssertAudioCue(cascade, entity, HestiaAudioCueKind.Footstep, version: 1);

            Assert.IsTrue(cascade.InputFootstepCue(entity, isRelevant: true));
            cascade.RunTick();
            AssertAudioCue(cascade, entity, HestiaAudioCueKind.Footstep, version: 2);
        }

        [Test]
        public void NonRelevantFootstepDoesNotEmitFactOrMutation()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();

            Assert.IsFalse(cascade.InputFootstepCue(entity, isRelevant: false));
            var result = cascade.RunTick();

            Assert.AreEqual(0, result.AcceptedFacts);
            Assert.AreEqual(0, result.ProcessedFacts);
            Assert.AreEqual(0, result.MutationCount);
        }

        [Test]
        public void DestroyEntityPublishesDeleteMutationAndRejectsLaterFacts()
        {
            var cascade = new HestiaGameContext();
            var entity = cascade.CreateEntity();
            cascade.SetInitialAmmo(entity, ammo: 3);

            cascade.DestroyEntity(entity);

            var deleteMutations = 0;
            cascade.Simulation.ForEachMutation(
                cascade.Feature.Ammo,
                (EntityRef mutatedEntity, in StateMutation<HestiaAmmoState> mutation) =>
                {
                    deleteMutations++;
                    Assert.AreEqual(entity, mutatedEntity);
                    Assert.IsTrue(mutation.HadPrevious);
                    Assert.AreEqual(3, mutation.Previous.Current);
                    Assert.IsFalse(mutation.HasNext);
                });

            Assert.AreEqual(1, deleteMutations);

            cascade.InputFireWeapon(entity);
            var result = cascade.RunTick();

            Assert.AreEqual(1, result.RejectedDestroyedEntityFacts);
            Assert.AreEqual(0, result.ProcessedFacts);
            Assert.AreEqual(0, result.MutationCount);
            Assert.IsFalse(cascade.TryGetAmmo(entity, out _));
        }

        [Test]
        public void ForeignOutputDescriptorIsRejected()
        {
            var cascade = new HestiaGameContext();
            var foreign = new HestiaGameContext();

            Assert.Throws<InvalidOperationException>(
                () => cascade.Simulation.ForEachMutation(
                    foreign.Feature.Ammo,
                    (EntityRef entity, in StateMutation<HestiaAmmoState> mutation) => { }));
        }

        [Test]
        public void UnknownEntityFactsAreRejectedBeforeEnteringTheQueue()
        {
            var cascade = new HestiaGameContext();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => cascade.InputFireWeapon(new EntityRef(99)));
        }

        [Test]
        public void AcceptedFactsAreDisposedWhenTickFactStoreClears()
        {
            DisposableFact.DisposeCount = 0;

            var simulation = new FactSimulation(new EmptyFactFeature());
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new DisposableFact(7));
            var result = simulation.RunTick(ReduceOptions.Default());

            Assert.AreEqual(1, result.AcceptedFacts);
            Assert.AreEqual(1, DisposableFact.DisposeCount);
        }

        private static void AssertAudioCue(
            HestiaGameContext context,
            EntityRef entity,
            HestiaAudioCueKind expectedCue,
            int version)
        {
            var mutations = 0;
            context.Simulation.ForEachMutation(
                context.Feature.AudioCue,
                (EntityRef mutatedEntity, in StateMutation<HestiaAudioCueState> mutation) =>
                {
                    mutations++;
                    Assert.AreEqual(entity, mutatedEntity);
                    Assert.IsTrue(mutation.HasNext);
                    Assert.AreEqual(expectedCue, mutation.Next.Cue);
                    Assert.AreEqual(version, mutation.Next.Version);
                });

            Assert.AreEqual(1, mutations);
        }

        private sealed class EmptyFactFeature : FactFeature
        {
        }

        private readonly struct DisposableFact : IFact, IEquatable<DisposableFact>
        {
            internal static int DisposeCount;

            internal DisposableFact(int value)
            {
                Value = value;
            }

            private int Value { get; }

            public bool Equals(DisposableFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is DisposableFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
                => DisposeCount++;
        }
    }
}
