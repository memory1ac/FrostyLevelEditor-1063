# Frosty Level Editor Plugin (1.0.6.3)

A Battlefield 1 map / level editor plugin for **Frosty Editor 1.0.6.3**.

杩欐槸鍩轰簬**瀵掗湝缂栬緫鍣紙Frosty Editor锛?.0.6.3**鍒朵綔鐨?Battlefield 1 鍦板浘缂栬緫鍣ㄦ彃浠躲€?

Author: [memory1ac](https://github.com/memory1ac)

License: [MIT](LICENSE)

---

## 涓枃

### 璇存槑

- 鏈彃浠堕潰鍚?**Frosty Editor 1.0.6.3** 銆?
- 鐢ㄤ簬鍦ㄧ紪杈戝櫒閲屾墦寮€ `LevelData`銆乣SubWorldData`銆乣SpatialPrefabBlueprint`銆乣ObjectBlueprint` 绛夎祫婧愶紝鏌ョ湅鍜岀紪杈戝叧鍗°€?
- **閮ㄥ垎浠ｇ爜鐢?AI 瀹屾垚銆?*
- 鏈粨搴撴寜 MIT 璁稿彲鍏紑銆傝嫢浣犳湁淇敼闇€姹傦紝璇?**Fork** 涓€浠借嚜琛屼慨鏀癸紝涓嶅繀渚濊禆鏈粨搴撴帴鍙楁墍鏈夋敼鍔ㄣ€?

### 瀹夎

1. 鎶婃湰浠撳簱鏀惧埌 FrostyToolsuite **1.0.6.3** 宸ョ▼鐨?`Plugins/FrostyLevelEditor`銆?
2. 鐢?Visual Studio 鎵撳紑瑙ｅ喅鏂规锛岄厤缃€?`Developer - Debug`銆乣x64`锛岀紪璇戞湰鎻掍欢宸ョ▼锛堜笉瑕佹暣瑙ｅ喅鏂规涔辩紪锛夈€?
3. 鐢熸垚鐨?`LevelEditorPlugin.dll` 澶嶅埗鍒?Frosty Editor 鐨?`Plugins` 鐩綍銆?
4. 鑻ョ紪杈戝櫒姝ｅ湪杩愯锛孌LL 浼氳鍗犵敤锛屽厛鍏虫帀鍐嶅鍒躲€?

鏈彃浠跺紩鐢?`FrostyCore`銆乣FrostySdk`銆乣FrostyControls`銆乣FrostyHash`銆乣MeshSetPlugin`锛岀洰鏍囦负 .NET Framework 4.8銆亁64銆?

### 浣跨敤

1. 鐢?Frosty Editor **1.0.6.3** 鎵撳紑 Battlefield 1銆?
2. 鍦?Data Explorer 涓€変腑鍏冲崱鎴栧垎鍖?EBX銆?
3. 鍙抽敭閫夋嫨銆屾墦寮€鍏冲崱缂栬緫鍣ㄣ€嶏紙Open Level Editor锛夈€?

### 璐＄尞

娆㈣繋 Fork銆傛湰浠撳簱涓嶄繚璇佷細鍚堝苟鎵€鏈夊閮ㄦ敼鍔紱闇€瑕佽嚜宸辩殑鍔熻兘鏃讹紝璇风淮鎶や綘鑷繁鐨?Fork銆?

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
3. Right-click and choose **鎵撳紑鍏冲崱缂栬緫鍣?* (Open Level Editor).

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
- 新增**地形笔刷投影圈**：地形编辑模式下，鼠标落点处会在地表投影一个跟随光标的圆环。圆环每个顶点都从地形高度场采样，因此会贴合起伏而不是浮成平面圆盘；外圈为笔刷半径，内圈为衰减起点，抬升显示琥珀色、压低显示青色。线宽按半径比例计算，从近到远观感一致。着色器直接编译在 DLL 内（纯 ASCII，不依赖任何外部 shader 文件）。
- 地形笔刷的**羽化滑块**步长由 0.05 改为 0.01，并开启刻度吸附。
- MeshView 增加 **ViewMode** 切换。
- 资源（shader / gizmo / 贴图）继续内嵌进 DLL，编辑器启动时自动释放。

English:

- Added the **Object Palette**: indexes SubWorlds, WorldParts and placeable ObjectBlueprints in the level, browse them as a tree and drag them into the viewport.
- Added the **level placement service**: resolves the target SubWorld / WorldPart automatically, shows a live ghost preview, picks points via terrain / mesh / bounding-box raycasts and integrates with the undo stack.
- Added **screen-space selection outlining**: vanilla `SelectionOutline` uses hardcoded depth thresholds that never trigger with the level editor camera, so the plugin now ships its own `SelectionHighlight.hlsl` post-process (embedded and auto-extracted), plus a selection-depth fallback for unmodified 1.0.6.3.
- Added the **Havok collision overlay**: pure .NET Havok parser plus D3D11 rendering with frame-budgeted parsing, a toolbar toggle and a re-parse action. The `HavokLayout.bf1.json` hkClass layout table is embedded in the DLL, so **no external Havok plugin is required**.
- Added the **terrain brush projected ring**: in terrain edit mode a ring is projected onto the terrain under the cursor and follows it. Every ring vertex samples the heightfield, so the ring hugs the actual surface instead of floating as a flat disc. The outer ring is the brush radius, the inner ring marks the falloff start; amber for raise, cyan for lower. Ring thickness scales with the brush radius so it reads the same at any range. Its shader is compiled straight into the DLL (pure ASCII, no external shader file).
- The terrain **feather slider** step changed from 0.05 to 0.01 with tick snapping enabled.
- Added a **ViewMode** switcher to MeshView.
- All resources (shaders / gizmos / textures) stay embedded in the DLL and are extracted when the editor starts.

### 0.1

- First public release.