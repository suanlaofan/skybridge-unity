using NUnit.Framework;
using Skybridge.Core;
using Skybridge.Presentation;

namespace Skybridge.Tests
{
    public sealed class SkybridgeVisualParityTests
    {
        [Test]
        public void IslandVariantsMatchAuthoredThreeJsMapping()
        {
            var expectedStart = new[] { 0, 1, 2, 3, 0 };
            var expectedGoal = new[] { 1, 2, 3, 0, 3 };

            for (var index = 0; index < LevelCatalog.LevelIds.Count; index++)
            {
                var levelId = LevelCatalog.LevelIds[index];
                Assert.That(
                    SkybridgeWorldView.GetStartIslandVariant(levelId),
                    Is.EqualTo(expectedStart[index]),
                    $"{levelId} start island variant");
                Assert.That(
                    SkybridgeWorldView.GetGoalIslandVariant(levelId),
                    Is.EqualTo(expectedGoal[index]),
                    $"{levelId} goal island variant");
            }
        }
    }
}
