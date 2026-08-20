using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Skybridge.Core
{
    public static class SaveDataService
    {
        public const string SaveDataKey = "skybridge-threejs.save.v1";

        private static readonly Regex LowestCostNumberPattern = new Regex(
            "(\\\"lowestCost\\\"\\s*:\\s*)(-?(?:[0-9]+(?:\\.[0-9]+)?|\\.[0-9]+)(?:[eE][+-]?[0-9]+)?)",
            RegexOptions.CultureInvariant);

        private static readonly Regex LowestCostNullPattern = new Regex(
            "(\\\"lowestCost\\\"\\s*:\\s*)null",
            RegexOptions.CultureInvariant);

        public static SaveDataV1 CreateDefaultSaveData()
        {
            var levels = new Dictionary<LevelId, LevelProgress>();
            foreach (var levelId in LevelCatalog.LevelIds)
            {
                levels[levelId] = EmptyProgress();
            }

            return new SaveDataV1(levels);
        }

        public static SaveDataV1 ParseSaveData(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return CreateDefaultSaveData();
            }

            try
            {
                var normalized = NormalizeLevelKeys(serialized);
                if (normalized.IndexOf("\"hasLowestCost\"", StringComparison.Ordinal) < 0)
                {
                    normalized = LowestCostNumberPattern.Replace(normalized, "$1$2,\"hasLowestCost\":true");
                    normalized = LowestCostNullPattern.Replace(normalized, "$1null,\"hasLowestCost\":false");
                }

                var dto = JsonUtility.FromJson<SaveDataDto>(normalized);
                if (dto == null || dto.version != 1 || dto.levels == null)
                {
                    return CreateDefaultSaveData();
                }

                return new SaveDataV1(new Dictionary<LevelId, LevelProgress>
                {
                    [LevelId.Level1] = SanitizeProgress(dto.levels.level1),
                    [LevelId.Level2] = SanitizeProgress(dto.levels.level2),
                    [LevelId.Level3] = SanitizeProgress(dto.levels.level3),
                    [LevelId.Level4] = SanitizeProgress(dto.levels.level4),
                    [LevelId.Level5] = SanitizeProgress(dto.levels.level5),
                });
            }
            catch
            {
                return CreateDefaultSaveData();
            }
        }

        public static string SerializeSaveData(SaveDataV1 data)
        {
            var sanitized = SanitizeData(data);
            return "{\"version\":1,\"levels\":{" +
                   $"\"level-1\":{SerializeProgress(sanitized.Levels[LevelId.Level1])}," +
                   $"\"level-2\":{SerializeProgress(sanitized.Levels[LevelId.Level2])}," +
                   $"\"level-3\":{SerializeProgress(sanitized.Levels[LevelId.Level3])}," +
                   $"\"level-4\":{SerializeProgress(sanitized.Levels[LevelId.Level4])}," +
                   $"\"level-5\":{SerializeProgress(sanitized.Levels[LevelId.Level5])}" +
                   "}}";
        }

        public static SaveDataV1 LoadSaveData()
        {
            return LoadSaveData(new PlayerPrefsStorage());
        }

        public static SaveDataV1 LoadSaveData(IKeyValueStorage storage)
        {
            if (storage == null)
            {
                return CreateDefaultSaveData();
            }

            try
            {
                var serialized = storage.GetItem(SaveDataKey);
                return string.IsNullOrEmpty(serialized) ? CreateDefaultSaveData() : ParseSaveData(serialized);
            }
            catch
            {
                return CreateDefaultSaveData();
            }
        }

        public static bool SaveSaveData(SaveDataV1 data)
        {
            return SaveSaveData(data, new PlayerPrefsStorage());
        }

        public static bool SaveSaveData(SaveDataV1 data, IKeyValueStorage storage)
        {
            if (storage == null)
            {
                return false;
            }

            try
            {
                storage.SetItem(SaveDataKey, SerializeSaveData(data));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool ClearSaveData()
        {
            return ClearSaveData(new PlayerPrefsStorage());
        }

        public static bool ClearSaveData(IKeyValueStorage storage)
        {
            if (storage == null)
            {
                return false;
            }

            try
            {
                storage.RemoveItem(SaveDataKey);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static SaveDataV1 RecordLevelCompletion(
            SaveDataV1 data,
            LevelId levelId,
            Medal medal,
            double cost)
        {
            if (double.IsNaN(cost) || double.IsInfinity(cost) || cost < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cost), "Completion cost must be a finite non-negative number.");
            }

            if (medal == Medal.None)
            {
                throw new ArgumentException("A completion medal cannot be None.", nameof(medal));
            }

            var sanitized = SanitizeData(data);
            var levels = new Dictionary<LevelId, LevelProgress>();
            foreach (var id in LevelCatalog.LevelIds)
            {
                levels[id] = sanitized.Levels[id].Clone();
            }

            var current = levels[levelId];
            levels[levelId] = new LevelProgress(
                BestMedal(current.HighestMedal, medal),
                current.LowestCost.HasValue ? Math.Min(current.LowestCost.Value, cost) : cost);
            return new SaveDataV1(levels);
        }

        public static bool IsLevelUnlocked(SaveDataV1 data, LevelId levelId)
        {
            var sanitized = SanitizeData(data);
            var index = -1;
            for (var candidateIndex = 0; candidateIndex < LevelCatalog.LevelIds.Count; candidateIndex++)
            {
                if (LevelCatalog.LevelIds[candidateIndex] == levelId)
                {
                    index = candidateIndex;
                    break;
                }
            }

            if (index <= 0)
            {
                return true;
            }

            var previousLevelId = LevelCatalog.LevelIds[index - 1];
            return sanitized.Levels[previousLevelId].HighestMedal != Medal.None;
        }

        private static SaveDataV1 SanitizeData(SaveDataV1 data)
        {
            if (data == null || data.Version != 1 || data.Levels == null)
            {
                return CreateDefaultSaveData();
            }

            var levels = new Dictionary<LevelId, LevelProgress>();
            foreach (var levelId in LevelCatalog.LevelIds)
            {
                levels[levelId] = data.Levels.TryGetValue(levelId, out var progress)
                    ? SanitizeProgress(progress)
                    : EmptyProgress();
            }

            return new SaveDataV1(levels);
        }

        private static LevelProgress SanitizeProgress(LevelProgress progress)
        {
            if (progress == null || !Enum.IsDefined(typeof(Medal), progress.HighestMedal))
            {
                return EmptyProgress();
            }

            var cost = progress.LowestCost;
            var validCost = cost.HasValue && !double.IsNaN(cost.Value) && !double.IsInfinity(cost.Value) && cost.Value >= 0
                ? cost
                : null;
            return new LevelProgress(
                progress.HighestMedal,
                progress.HighestMedal == Medal.None ? null : validCost);
        }

        private static LevelProgress SanitizeProgress(ProgressDto dto)
        {
            if (dto == null || !TryParseMedal(dto.highestMedal, out var medal))
            {
                return EmptyProgress();
            }

            double? cost = dto.hasLowestCost &&
                           !double.IsNaN(dto.lowestCost) &&
                           !double.IsInfinity(dto.lowestCost) &&
                           dto.lowestCost >= 0
                ? dto.lowestCost
                : (double?)null;
            return new LevelProgress(medal, medal == Medal.None ? null : cost);
        }

        private static string SerializeProgress(LevelProgress progress)
        {
            var cost = progress.LowestCost.HasValue
                ? progress.LowestCost.Value.ToString("R", CultureInfo.InvariantCulture)
                : "null";
            return $"{{\"highestMedal\":\"{MedalToken(progress.HighestMedal)}\",\"lowestCost\":{cost}}}";
        }

        private static bool TryParseMedal(string value, out Medal medal)
        {
            switch (value)
            {
                case "none":
                    medal = Medal.None;
                    return true;
                case "bronze":
                    medal = Medal.Bronze;
                    return true;
                case "silver":
                    medal = Medal.Silver;
                    return true;
                case "gold":
                    medal = Medal.Gold;
                    return true;
                default:
                    medal = Medal.None;
                    return false;
            }
        }

        private static string MedalToken(Medal medal)
        {
            return medal.ToString().ToLower(CultureInfo.InvariantCulture);
        }

        private static Medal BestMedal(Medal left, Medal right)
        {
            return right > left ? right : left;
        }

        private static LevelProgress EmptyProgress()
        {
            return new LevelProgress(Medal.None, null);
        }

        private static string NormalizeLevelKeys(string serialized)
        {
            var normalized = serialized;
            for (var index = 1; index <= 5; index++)
            {
                normalized = normalized.Replace($"\"level-{index}\"", $"\"level{index}\"");
            }

            return normalized;
        }

        [Serializable]
        private sealed class SaveDataDto
        {
            public int version;
            public LevelsDto levels;
        }

        [Serializable]
        private sealed class LevelsDto
        {
            public ProgressDto level1;
            public ProgressDto level2;
            public ProgressDto level3;
            public ProgressDto level4;
            public ProgressDto level5;
        }

        [Serializable]
        private sealed class ProgressDto
        {
            public string highestMedal;
            public double lowestCost;
            public bool hasLowestCost;
        }

        private sealed class PlayerPrefsStorage : IKeyValueStorage
        {
            public string GetItem(string key)
            {
                return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            }

            public void SetItem(string key, string value)
            {
                PlayerPrefs.SetString(key, value);
                PlayerPrefs.Save();
            }

            public void RemoveItem(string key)
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }
    }
}
