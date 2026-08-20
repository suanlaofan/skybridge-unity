using System.Collections.Generic;
using NUnit.Framework;
using Skybridge.Core;

namespace Skybridge.Tests
{
    public sealed class SaveDataTests
    {
        [Test]
        public void HighestRatingAndLowestCostPersistAcrossReload()
        {
            var storage = new MemoryStorage();
            var data = SaveDataService.CreateDefaultSaveData();
            data = SaveDataService.RecordLevelCompletion(data, LevelId.Level1, Medal.Silver, 4);
            data = SaveDataService.RecordLevelCompletion(data, LevelId.Level1, Medal.Bronze, 3);
            data = SaveDataService.RecordLevelCompletion(data, LevelId.Level1, Medal.Gold, 5);

            Assert.That(SaveDataService.SaveSaveData(data, storage), Is.True);
            var reloaded = SaveDataService.LoadSaveData(storage);
            AssertProgress(reloaded, LevelId.Level1, Medal.Gold, 3);
            Assert.That(SaveDataService.IsLevelUnlocked(reloaded, LevelId.Level2), Is.True);
            Assert.That(SaveDataService.IsLevelUnlocked(reloaded, LevelId.Level3), Is.False);
        }

        [Test]
        public void MalformedOrForeignSchemaFallsBackSafely()
        {
            var storage = new MemoryStorage();
            storage.SetItem(SaveDataService.SaveDataKey, "not-json");
            AssertDefault(SaveDataService.LoadSaveData(storage));

            storage.SetItem(SaveDataService.SaveDataKey, "{\"version\":99,\"levels\":{}}");
            AssertDefault(SaveDataService.LoadSaveData(storage));
        }

        [Test]
        public void ExistingFourLevelV1SaveMigratesWithEmptyLevelFive()
        {
            const string legacy = "{\"version\":1,\"levels\":{" +
                                  "\"level-1\":{\"highestMedal\":\"gold\",\"lowestCost\":3}," +
                                  "\"level-2\":{\"highestMedal\":\"silver\",\"lowestCost\":9}," +
                                  "\"level-3\":{\"highestMedal\":\"bronze\",\"lowestCost\":10}," +
                                  "\"level-4\":{\"highestMedal\":\"gold\",\"lowestCost\":10}}}";

            var migrated = SaveDataService.ParseSaveData(legacy);

            AssertProgress(migrated, LevelId.Level1, Medal.Gold, 3);
            AssertProgress(migrated, LevelId.Level5, Medal.None, null);
            Assert.That(SaveDataService.IsLevelUnlocked(migrated, LevelId.Level5), Is.True);
        }

        [Test]
        public void LevelFiveUnlocksAfterLevelFourAndRecordsResult()
        {
            var data = SaveDataService.CreateDefaultSaveData();
            Assert.That(SaveDataService.IsLevelUnlocked(data, LevelId.Level5), Is.False);

            data = SaveDataService.RecordLevelCompletion(data, LevelId.Level4, Medal.Bronze, 14);
            Assert.That(SaveDataService.IsLevelUnlocked(data, LevelId.Level5), Is.True);

            data = SaveDataService.RecordLevelCompletion(data, LevelId.Level5, Medal.Gold, 32);
            AssertProgress(data, LevelId.Level5, Medal.Gold, 32);
        }

        [Test]
        public void SavedProgressCanBeCleared()
        {
            var storage = new MemoryStorage();
            var completed = SaveDataService.RecordLevelCompletion(
                SaveDataService.CreateDefaultSaveData(),
                LevelId.Level1,
                Medal.Gold,
                3);
            SaveDataService.SaveSaveData(completed, storage);

            Assert.That(SaveDataService.ClearSaveData(storage), Is.True);
            AssertDefault(SaveDataService.LoadSaveData(storage));
        }

        private static void AssertProgress(SaveDataV1 data, LevelId levelId, Medal medal, double? cost)
        {
            Assert.That(data.Levels[levelId].HighestMedal, Is.EqualTo(medal));
            Assert.That(data.Levels[levelId].LowestCost, Is.EqualTo(cost));
        }

        private static void AssertDefault(SaveDataV1 data)
        {
            Assert.That(data.Version, Is.EqualTo(1));
            foreach (var levelId in LevelCatalog.LevelIds)
            {
                AssertProgress(data, levelId, Medal.None, null);
            }
        }

        private sealed class MemoryStorage : IKeyValueStorage
        {
            private readonly Dictionary<string, string> values = new Dictionary<string, string>();

            public string GetItem(string key)
            {
                return values.TryGetValue(key, out var value) ? value : null;
            }

            public void SetItem(string key, string value)
            {
                values[key] = value;
            }

            public void RemoveItem(string key)
            {
                values.Remove(key);
            }
        }
    }
}
