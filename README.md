# Frosty Level Editor Plugin (1.0.6.3)

A Battlefield 1 map / level editor plugin for **Frosty Editor 1.0.6.3**.

这是基于**寒霜编辑器（Frosty Editor）1.0.6.3**制作的 Battlefield 1 地图编辑器插件。

Author: [memory1ac](https://github.com/memory1ac)

License: [MIT](LICENSE)

---

## 中文

### 说明

- 本插件面向 **Frosty Editor 1.0.6.3** 。
- 用于在编辑器里打开 `LevelData`、`SubWorldData`、`SpatialPrefabBlueprint`、`ObjectBlueprint` 等资源，查看和编辑关卡。
- **部分代码由 AI 完成。**
- 本仓库按 MIT 许可公开。若你有修改需求，请 **Fork** 一份自行修改，不必依赖本仓库接受所有改动。

### 安装

1. 把本仓库放到 FrostyToolsuite **1.0.6.3** 工程的 `Plugins/FrostyLevelEditor`。
2. 用 Visual Studio 打开解决方案，配置选 `Developer - Debug`、`x64`，编译本插件工程（不要整解决方案乱编）。
3. 生成的 `LevelEditorPlugin.dll` 复制到 Frosty Editor 的 `Plugins` 目录。
4. 若编辑器正在运行，DLL 会被占用，先关掉再复制。

本插件引用 `FrostyCore`、`FrostySdk`、`FrostyControls`、`FrostyHash`、`MeshSetPlugin`，目标为 .NET Framework 4.8、x64。

### 使用

1. 用 Frosty Editor **1.0.6.3** 打开 Battlefield 1。
2. 在 Data Explorer 中选中关卡或分区 EBX。
3. 右键选择「打开关卡编辑器」（Open Level Editor）。

### 贡献

欢迎 Fork。本仓库不保证会合并所有外部改动；需要自己的功能时，请维护你自己的 Fork。

---

## English

### About

- This plugin is built for **Frosty Editor 1.0.6.3**.
- It opens Battlefield 1 `LevelData`, `SubWorldData`, `SpatialPrefabBlueprint`, `ObjectBlueprint`, and related assets in a level editor view.
- **Some of this code was written with AI assistance.**
- The project is released under the MIT License. If you need changes, **fork this repository and modify your own copy**.

### Install

1. Place this repository at `Plugins/FrostyLevelEditor` inside a FrostyToolsuite **1.0.6.3** tree.
2. Open the solution in Visual Studio. Use `Developer - Debug` and `x64`, then build this plugin project.
3. Copy `LevelEditorPlugin.dll` into the Frosty Editor `Plugins` folder.
4. Close Frosty first if that DLL is already loaded.

The project references `FrostyCore`, `FrostySdk`, `FrostyControls`, `FrostyHash`, and `MeshSetPlugin`. It targets .NET Framework 4.8, x64.

### Use

1. Open Battlefield 1 in Frosty Editor **1.0.6.3**.
2. Select a level or subworld EBX in the Data Explorer.
3. Right-click and choose **打开关卡编辑器** (Open Level Editor).

### Contributing

Forks are welcome. This repository may not merge every external change. Maintain your own fork when you need custom behavior.

---

## 更新日志 / Changelog

### 0.2

中文：

- 新增**对象面板（Object Palette）**：索引关卡内的 SubWorld、WorldPart 与可放置的 ObjectBlueprint，树形浏览并支持拖拽放置。
- 新增**关卡放置服务**：自动解析目标 SubWorld / WorldPart，放置时生成实时幽灵预览，拾取点支持地形、网格与包围盒射线检测，并接入撤销栈。
- 新增**屏幕空间选区描边**：原版 `SelectionOutline` 的硬编码深度阈值在关卡相机下不会触发，改用插件自带的 `SelectionHighlight.hlsl` 后处理（内嵌并自动释放），同时补充了纯原版 1.0.6.3 的选区深度兜底渲染。
- 新增**Havok 碰撞体叠加层**：纯 .NET Havok 解析器 + D3D11 渲染，按帧预算解析，支持工具栏开关与重新解析；hkClass 布局表 `HavokLayout.bf1.json` 内嵌于 DLL，**不再依赖任何外部 Havok 插件**。
- 新增**地形笔刷投影圈**：地形编辑模式下，鼠标落点处会在地表投影一个跟随光标的圆环，直观显示这一笔会改到哪片地形。圆环每个顶点都从地形高度场采样，因此会贴合起伏而不是浮成平面圆盘；**外圈为笔刷半径，内圈为衰减起点**，抬升显示琥珀色、压低显示青色。线宽按半径比例计算（约为半径的 1.5%），近远观感一致，拉远时也不会膨胀成实心色块。投影圈的着色器直接编译在 DLL 内，不产生任何外部 shader 文件。
- 地形笔刷的**羽化滑块**步长由 0.05 改为 0.01，并开启刻度吸附，便于把衰减调到更细的档位。
- MeshView 增加 **ViewMode** 切换。
- 资源（shader / gizmo / 贴图）继续内嵌进 DLL，编辑器启动时自动释放。

English:

- Added the **Object Palette**: indexes SubWorlds, WorldParts and placeable ObjectBlueprints in the level, browse them as a tree and drag them into the viewport.
- Added the **level placement service**: resolves the target SubWorld / WorldPart automatically, shows a live ghost preview, picks points via terrain / mesh / bounding-box raycasts and integrates with the undo stack.
- Added **screen-space selection outlining**: vanilla `SelectionOutline` uses hardcoded depth thresholds that never trigger with the level editor camera, so the plugin now ships its own `SelectionHighlight.hlsl` post-process (embedded and auto-extracted), plus a selection-depth fallback for unmodified 1.0.6.3.
- Added the **Havok collision overlay**: pure .NET Havok parser plus D3D11 rendering with frame-budgeted parsing, a toolbar toggle and a re-parse action. The `HavokLayout.bf1.json` hkClass layout table is embedded in the DLL, so **no external Havok plugin is required**.
- Added the **terrain brush projected ring**: in terrain edit mode a ring is projected onto the terrain under the cursor and follows it, showing exactly which area a stroke will affect. Every ring vertex samples the heightfield, so the ring hugs the actual surface instead of floating as a flat disc. The **outer ring is the brush radius and the inner ring marks the falloff start**; amber for raise, cyan for lower. Thickness scales with the radius (about 1.5% of it), so it reads the same near and far and never bloats into a solid blob when you zoom out. The ring shader is compiled straight into the DLL, so no external shader file is needed.
- The terrain **feather slider** step changed from 0.05 to 0.01 with tick snapping enabled, for finer falloff control.
- Added a **ViewMode** switcher to MeshView.
- All resources (shaders / gizmos / textures) stay embedded in the DLL and are extracted when the editor starts.

### 0.1

- First public release.
