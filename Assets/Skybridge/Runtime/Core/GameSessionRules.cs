using System.Linq;

namespace Skybridge.Core
{
    public static class TestTimings
    {
        public const int GoalStableMilliseconds = 500;
        public const int BreakSlowMotionMilliseconds = 350;
        public const int FallDisplayMilliseconds = 1000;
        public const int MaxFailureRecoveryMilliseconds = 2000;
        public const int StallTimeoutMilliseconds = 3000;
    }

    public static class GameSessionRules
    {
        public static BuildSessionState CreateTestSnapshot(BuildSessionState state)
        {
            return CloneBuildState(state);
        }

        public static BuildSessionState RestoreTestSnapshot(BuildSessionState snapshot)
        {
            return CloneBuildState(snapshot);
        }

        public static bool CanEditBridge(GamePhase phase)
        {
            return phase == GamePhase.Build;
        }

        // The first terminal result is locked. Success wins simultaneous goal/fall signals.
        public static TestOutcome ResolveTestOutcome(TestOutcome current, TestOutcomeSignals signals)
        {
            if (current != TestOutcome.Pending)
            {
                return current;
            }

            if (signals.EntireVehicleInGoal)
            {
                return TestOutcome.Success;
            }

            if (signals.FailureZoneEntered)
            {
                return TestOutcome.Failure;
            }

            return signals.StalledForThreeSeconds ? TestOutcome.RuntimeError : TestOutcome.Pending;
        }

        private static BuildSessionState CloneBuildState(BuildSessionState state)
        {
            return new BuildSessionState(
                state.Layout.DeepClone(),
                state.InventoryRemaining.Clone(),
                state.UndoStack.Select(command => command.DeepClone()),
                state.RedoStack.Select(command => command.DeepClone()));
        }
    }
}
