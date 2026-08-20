# Skybridge Unity

Unity 6000.5.4f1 / URP 移植版，独立重建了 `skybridge-threejs` 的五关玩法、表现和桌面输入。项目与已取代的 `SkybridgeWhitebox` 完全分离，不包含 PICO、OpenXR、AR Foundation、人物角色或独立新手教程。

## Scope

- 五关双岛造桥；第五关是 `10 x 10 x 10` 三维自由建造空间。
- 标准桥板、坡道桥板、桥墩、桁架四类组件；第五关允许任意层、转弯和坡向换层。
- 路径验证、车辆自动通行、确定性薄弱跨度断裂、失败恢复、成功评价、撤销/重做和本地存档。
- 程序化几何、材质、图标、粒子和音频；不依赖外部媒体资源。

## Controls

- 鼠标左键：选择工具、放置组件、选中已有组件；选中后再点击目标格可移动。
- 鼠标右键：单击取消当前工具或选中状态；拖动用于旋转镜头。
- `Q` / `E`：旋转放置方向。
- `PageUp` / `PageDown`：切换第五关建造层。
- `Delete` / `Backspace`：删除选中组件。
- `Space`：开始、停止当前测试；测试中可用“重新开始”按钮重跑。
- `Ctrl/Cmd+Z`、`Ctrl/Cmd+Shift+Z` 或 `Ctrl/Cmd+Y`：撤销、重做。
- 右侧工具栏：撤销、重做、旋转、删除、重置桥梁、复位镜头。
- 第五关底部高度/坡向控件：切换 `Y 00` 到 `Y 10` 并选择上坡或下坡。

## Run

直接打开：`Builds/macOS/Skybridge.app`。

默认窗口为 `1280 x 720`，支持窗口缩放；HUD 使用 `ScaleWithScreenSize`，以 `1280 x 720` 为参考分辨率，在高分辨率/Retina Game 视图中保持可读，同时仍按实际屏幕宽度在 `880 / 680 / 430` 断点切换两行或紧凑横向布局。

## Verification

在项目根目录执行以下命令（将 Unity 路径替换为本机安装位置）：

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/MacOS/Unity
$UNITY -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults editmode.xml
$UNITY -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults playmode.xml
$UNITY -batchmode -nographics -quit -projectPath "$PWD" -executeMethod Skybridge.Editor.SkybridgeProjectTools.ValidateConfiguration
$UNITY -batchmode -nographics -quit -projectPath "$PWD" -executeMethod Skybridge.Editor.SkybridgeProjectTools.BuildMacPlayer
```

本次交付验证结果：EditMode `42/42`、PlayMode `21/21`、配置校验通过、macOS Universal Player 构建通过；新增 PlayMode 断言锁定 HUD 参考分辨率缩放。PlayMode 额外覆盖空闲状态拾取优先已有桥板（不会被透明建造网格遮挡）。Player smoke 覆盖五关参考解和 Level 2 薄弱跨度恢复：成本分别为 `3 / 8 / 6 / 10 / 32`，五关均通过，薄弱跨度恢复通过。第五关任意转弯的回归会保留原路线、转角接头方向和两端朝向，不会回退为默认对角线路径。视觉回归锁定了五关岛屿变体（第五关终点为 Three.js 指定的变体 3），并对齐背景/雾色 `#DFE5E4`、FOV `38`、线性雾基线 `34–78`；镜头聚焦会按 Three.js 规则为第五关动态扩展雾范围。最新构建位于 `Builds/macOS/Skybridge.app`，同时包含 `arm64` 与 `x86_64`。

对应证据：`work/skybridge-editmode-ui-scale-2.xml`、`work/skybridge-playmode-ui-scale-2.xml`、`work/skybridge-validate-ui-scale.log`、`work/skybridge-build-ui-scale.log` 和 `work/skybridge-player-smoke-ui-scale.txt`。UI 放大后的 Unity 编辑器截图为 `work/ui-scale-final-clean.jpeg`；Player smoke 五关及恢复回归均通过。最终 Player 五关截图位于 `work/skybridge-player-level1-capture-final-20260818.png` 至 `work/skybridge-player-level5-capture-final-20260818.png`。

真实窗口回归记录位于 `work/skybridge-player-input-final-20260818.md`：已有桥板拾取、删除/撤销/重做、`Q/E`、`PageUp/PageDown`、滚轮缩放、`Space` 通车与成功结果层、设置窗口均已确认。Computer Use 连接器只能可靠执行左键拖动，因此中键平移和右键旋转保留为代码路径确认，未冒充直接 GUI 拖动证据；运行时实现仍使用 Unity Input System 的 `Mouse.middleButton` 与 `Mouse.rightButton`。

## Boundaries

这是 Unity 桌面可玩移植版，不是 PICO/VR/AR 适配工程。旧四关白盒项目保留在工作区但不再作为本项目的运行时来源。


本轮收尾：用户确认放大后的 UI 大小合适；已移除测试中的临时诊断输出，Unity 编辑器已加载 `Assets/Scenes/SkybridgeMain.unity`。
