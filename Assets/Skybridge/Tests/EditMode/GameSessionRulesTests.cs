using System;
using System.Linq;
using NUnit.Framework;
using Skybridge.Core;

namespace Skybridge.Tests
{
    public sealed class GameSessionRulesTests
    {
        [Test]
        public void BridgeEditsAreAllowedOnlyInBuildState()
        {
            var phases = new[]
            {
                GamePhase.LevelLoading,
                GamePhase.Build,
                GamePhase.Validating,
                GamePhase.TestPreparing,
                GamePhase.VehicleRunning,
                GamePhase.Breaking,
                GamePhase.VehicleFalling,
                GamePhase.RestoreSnapshot,
                GamePhase.Success,
            };

            CollectionAssert.AreEqual(
                new[] { GamePhase.Build },
                phases.Where(GameSessionRules.CanEditBridge));
        }

        [Test]
        public void SnapshotRestoresPositionsInventoryUndoAndRedoAsIsolatedCopies()
        {
            var original = MakeBuildState();
            var snapshot = GameSessionRules.CreateTestSnapshot(original);
            var restored = GameSessionRules.RestoreTestSnapshot(snapshot);

            Assert.That(restored, Is.Not.SameAs(snapshot));
            Assert.That(restored.Layout, Is.Not.SameAs(snapshot.Layout));
            Assert.That(restored.Layout.Roads[0], Is.Not.SameAs(snapshot.Layout.Roads[0]));
            Assert.That(restored.InventoryRemaining, Is.Not.SameAs(snapshot.InventoryRemaining));
            Assert.That(restored.UndoStack, Is.Not.SameAs(snapshot.UndoStack));
            Assert.That(restored.Layout.Roads.Select(road => road.Id), Is.EqualTo(original.Layout.Roads.Select(road => road.Id)));
            Assert.That(restored.Layout.Piers.Select(pier => pier.Id), Is.EqualTo(original.Layout.Piers.Select(pier => pier.Id)));
            Assert.That(restored.InventoryRemaining, Is.EqualTo(original.InventoryRemaining));
            Assert.That(restored.UndoStack[0].PieceId, Is.EqualTo(original.UndoStack[0].PieceId));
            Assert.That(restored.RedoStack, Is.Empty);
        }

        [Test]
        public void SuccessLocksAndLateFallCannotOverwriteIt()
        {
            var simultaneous = GameSessionRules.ResolveTestOutcome(
                TestOutcome.Pending,
                new TestOutcomeSignals(true, true));

            Assert.That(simultaneous, Is.EqualTo(TestOutcome.Success));
            Assert.That(
                GameSessionRules.ResolveTestOutcome(
                    simultaneous,
                    new TestOutcomeSignals(false, true)),
                Is.EqualTo(TestOutcome.Success));
        }

        [Test]
        public void FailurePresentationFitsTwoSecondTarget()
        {
            Assert.That(
                TestTimings.BreakSlowMotionMilliseconds + TestTimings.FallDisplayMilliseconds,
                Is.LessThan(TestTimings.MaxFailureRecoveryMilliseconds));
        }

        private static BuildSessionState MakeBuildState()
        {
            var layout = LevelCatalog.Get(LevelId.Level2).ReferenceSolution.Layout;
            var firstRoad = layout.Roads.FirstOrDefault();
            if (firstRoad == null)
            {
                throw new InvalidOperationException("Level 2 reference must contain a road.");
            }

            return new BuildSessionState(
                layout,
                new PieceInventory(3, 0, 0, 0),
                new[]
                {
                    new BuildCommand(BuildCommandKind.Place, firstRoad.Id, null, firstRoad),
                },
                Array.Empty<BuildCommand>());
        }
    }
}
