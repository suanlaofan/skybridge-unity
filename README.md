# 空桥运输 / Skybridge Unity

Unity 6000.5.4f1 / URP 的五关造桥游戏，PICO XR 版本 **1.1.0**。

[下载 APK](https://github.com/suanlaofan/skybridge-unity/releases/download/v1.1.0-pico/SkybridgePico.apk) · [发布页](https://github.com/suanlaofan/skybridge-unity/releases/tag/v1.1.0-pico)

## 本版修改

- 沿用原界面，HUD 与桥梁共用世界空间 WorldRoot；头部移动时 UI 保持在场景中。
- 修正交替旋转造成的累计侧倾，默认桥梁水平、建筑竖直，支持自由旋转、平移和缩放。
- 在原界面新增渐进教程提示；放置完成后直接点原“测试”，没有额外确认步骤。
- 修复模拟器佩戴状态误判、鼠标映射确认、虚拟手柄射线位置偏移和近眼射线色带；真机保留原有佩戴检查和控制器位置射线。
- APK 使用游戏原有的黄色运输车跨桥图标，包含默认、传统、圆形与自适应 Android 图标。

## 操作

- **PICO 模拟器**：底部选择“右手柄模式”，鼠标左键按下并松开确认；模拟器视角工具可移动、旋转视点。参见 [PICO 官方操作说明](https://developer.picoxr.com/document/spatial-toolkit/pico-emulator-ui/)。
- **Unity / 电脑**：左键选材、放置和测试；右键拖动旋转，中键拖动平移，滚轮缩放。原右侧准星按钮复位。
- **PICO 真机**：右手射线加右扳机确认；左摇杆旋转，左握键加左摇杆平移，右摇杆上下缩放；右主按钮帮助、右副按钮取消。

## 构建

开发依赖及首次打开工程的步骤见 [Packages/README.md](Packages/README.md)。PICO SDK 0.13.1 为本地安装依赖，不包含在仓库源码中。

打开 `Assets/Scenes/SkybridgeMain.unity`，使用 **Skybridge > PICO > Build Android APK**。构建会自动配置版本和游戏图标，生成 `Builds/PICO/SkybridgePico.apk`。

Android：`com.skybridge.pico`，版本 1.1.0 / versionCode 3，ARM64 / IL2CPP / Vulkan，min SDK 29、target SDK 35。

## 验证

- EditMode **73/73**、PlayMode **40/40**，共 **113** 项通过，无失败或跳过。
- 最终运行时与测试源码哈希保存在 `Validation/tested-source-hashes.json`；发布构建通过。
- 用户已确认世界 UI、场景朝向和模拟器可用，并要求发布。模拟器安装、启动、左键进入教程和放置组件由本任务实测；不等同于真机验收或完整通车验收。
- Android 构建 **0 错误、9 条警告**，APK 签名检查通过。PICO 音频库 16KB 对齐等 SDK 提示见 `Validation/build-warnings.json`。
- 真实头显的追踪、舒适度、音频兼容性和性能尚未验收。当前是 XR 游戏，不含 MR 透视、平面锚定或手势玩法。

APK 大小：**66,299,084 字节**。SHA-256：

```text
8f1f5d5953c958cf7e88d4c1e9784ccc91388200b4b62c19ade4e852a851aa69
```

游戏图标源文件见 `Assets/Skybridge/Art/Icons/SkybridgeIcon.png`。中文字体使用 Noto Sans CJK SC，许可随包保存在 `Assets/Resources/Fonts/OFL.txt`。
