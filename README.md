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
