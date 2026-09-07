using System;
using System.Collections.Generic;
using Skybridge.Game;
using Skybridge.Presentation;
using UnityEngine;

namespace Skybridge.UI
{
    /// <summary>Compatibility presenter for the original SkybridgeHud; creates no replacement UI.</summary>
    public sealed class SkybridgeXrWorkbenchView : MonoBehaviour
    {
        private SkybridgeGameController game;
        private SkybridgeHud hud;
        private bool confirmation, adjustment, tracking = true;
        public bool HasModal => confirmation || hud != null && (hud.IsSettingsOpen || hud.IsResultOpen);
        public bool IsAdjustmentOpen => adjustment;
        public bool BlocksWorldInput => HasModal;
        public bool ShortcutsEnabled => false;
        public IReadOnlyList<Canvas> Canvases => new[] { hud.HudCanvas };
        public void Initialize(SkybridgeGameController controller, Camera camera)
        {
            game = controller; hud = game.Hud;
            hud.TutorialHelpRequested += ShowHelp;
        }
        public void Place(Transform workspace, Vector3 position, Quaternion rotation)
        {
            if (game.XrUsesWorldSpace) hud.ConfigureWorldSpaceHud(game.XrCamera, workspace, position, rotation);
        }
        public void Refresh() { }
        public void ShowWelcome() => hud.ShowTutorialNote("空桥运输 · 操作提示",
            "选材料 → 点击连接点 → 点“测试”让小车通车。" + ViewHint,
            "开始基础教程", () => game.XrDispatch("tutorial-start"), "直接搭建", hud.HideTutorialNote);
        private string ViewHint => game.XrUsesSimulatorPointer
            ? "\n模拟器底部选“右手柄模式”，左键点击；底部视角工具可移动和旋转视角。"
            : Application.platform == RuntimePlatform.Android
                ? "\n左摇杆旋转；握住左握键 + 左摇杆平移；右摇杆缩放。右扳机确认。"
                : "\n右键自由旋转，中键平移，滚轮缩放；右侧准星按钮重置视角。";
        public void ShowHelp()
        {
            var lesson = game.CurrentLevel.Number == 1 ? 0 : game.CurrentLevel.Number;
            hud.ShowTutorialNote("教程与视角操作", "再次点击选中的材料可取消选材，再点击已有部件进行编辑。" + ViewHint,
                "练习本关操作", () => game.XrDispatch("tutorial-replay", lesson),
                "基础教程", () => game.XrDispatch("tutorial-start"), "关闭提示", hud.HideTutorialNote);
        }
        public void ShowSettings() => hud.OpenSettings();
        public void ShowLevelSelect() => hud.ShowTutorialNote("选择关卡", "使用原界面顶部的关卡标签切换关卡。", "关闭提示", hud.HideTutorialNote);
        public void ShowLevelIntro() => hud.ShowTutorialNote("第 " + game.CurrentLevel.Number + " 关 · " + game.CurrentLevel.Name,
            game.CurrentLevel.Objective, "开始搭建", hud.HideTutorialNote,
            "本关教程", () => game.XrDispatch("tutorial-replay", game.CurrentLevel.Number == 1 ? 0 : game.CurrentLevel.Number));
        public void ShowAdjustment()
        {
            adjustment = true;
            hud.ShowTutorialNote("转换视角", ViewHint.Trim(), "完成调整", () => game.XrDispatch("continue"));
        }
        public void ShowTracking(bool available)
        {
            tracking = available; hud.SetXrTrackingAvailable(available);
            if (!available) hud.ShowStatus("手柄追踪中断。恢复后先松开扳机。", SkybridgePalette.Warning, 0);
            else hud.ClearStatus();
        }
        public void SetTutorial(string title, string body, string progress, bool active, int assistance = 0)
        {
            if (!active) return;
            if (confirmation || adjustment || hud.IsSettingsOpen) return;
            if (game.XrTutorial.IsComplete)
                hud.ShowTutorialNote(title, "教程已完成。返回后会恢复原关卡方案与撤销记录。", "返回游戏", () => game.XrDispatch("tutorial-exit"));
            else
                hud.ShowTutorialNote(title + "  " + progress, body,
                    "查看提示", () => game.XrDispatch("tutorial-hint"), "重试本步", () => game.XrDispatch("tutorial-retry"),
                    "退出教程", () => game.XrDispatch("tutorial-exit"));
        }
        public void ShowResult(string title, string body, bool success)
        {
            if (game.XrIsTutorialActive)
            {
                hud.HideResult();
                if (success) SetTutorial(title, body, "", true);
                else hud.ShowTutorialNote(title, body, "继续练习", hud.HideTutorialNote);
            }
            else if (!success) hud.ShowStatus(body, SkybridgePalette.Warning, 5);
        }
        public void SetPointerHint(string text, bool valid)
        {
            // Preserve the original transient status/tooltip system.
        }
        public void ShowConfirm(string title, string body, Action confirmed)
        {
            confirmation = true;
            hud.ShowTutorialNote(title, body, "确认", () => { confirmation = false; hud.HideTutorialNote(); confirmed?.Invoke(); },
                "取消", () => { confirmation = false; hud.HideTutorialNote(); });
        }
        public void CloseModal()
        {
            confirmation = false; adjustment = false;
            hud.CloseSettings(); hud.HideResult(); hud.HideTutorialNote();
        }
        private void OnDestroy()
        {
            if (hud != null) hud.TutorialHelpRequested -= ShowHelp;
        }
    }
}
