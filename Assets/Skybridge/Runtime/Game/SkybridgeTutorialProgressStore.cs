using System.Collections.Generic;
using UnityEngine;

namespace Skybridge.Game
{
    public interface ISkybridgeTutorialProgressStore
    {
        bool IsCompleted(int lesson);
        bool WasSkipped(int lesson);
        void MarkCompleted(int lesson);
        void MarkSkipped(int lesson);
    }

    /// <summary>Only tutorial capability flags are persisted; this store never reads or writes game saves.</summary>
    public sealed class SkybridgeTutorialProgressStore : ISkybridgeTutorialProgressStore
    {
        public const string KeyPrefix = "skybridge.tutorial.v2.";

        public bool IsCompleted(int lesson) => PlayerPrefs.GetInt(Key(lesson, "complete"), 0) == 1;
        public bool WasSkipped(int lesson) => PlayerPrefs.GetInt(Key(lesson, "skipped"), 0) == 1;

        public void MarkCompleted(int lesson)
        {
            PlayerPrefs.SetInt(Key(lesson, "complete"), 1);
            PlayerPrefs.Save();
        }

        public void MarkSkipped(int lesson)
        {
            PlayerPrefs.SetInt(Key(lesson, "skipped"), 1);
            PlayerPrefs.Save();
        }

        private static string Key(int lesson, string flag) => KeyPrefix + lesson + "." + flag;
    }

    /// <summary>Inject this store in tests or transient previews to avoid writing the player's preferences.</summary>
    public sealed class SkybridgeMemoryTutorialProgressStore : ISkybridgeTutorialProgressStore
    {
        private readonly HashSet<int> completed = new HashSet<int>();
        private readonly HashSet<int> skipped = new HashSet<int>();

        public bool IsCompleted(int lesson) => completed.Contains(lesson);
        public bool WasSkipped(int lesson) => skipped.Contains(lesson);
        public void MarkCompleted(int lesson) => completed.Add(lesson);
        public void MarkSkipped(int lesson) => skipped.Add(lesson);
    }
}
