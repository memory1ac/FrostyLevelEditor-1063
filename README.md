# Frosty Level Editor Plugin (1.0.6.3)

A Battlefield 1 map / level editor plugin for **Frosty Editor 1.0.6.3**.

è¿™æ˜¯åŸºäº**å¯’éœœç¼–è¾‘å™¨ï¼ˆFrosty Editorï¼‰1.0.6.3**åˆ¶ä½œçš„ Battlefield 1 åœ°å›¾ç¼–è¾‘å™¨æ’ä»¶ã€‚

Author: [memory1ac](https://github.com/memory1ac)

License: [MIT](LICENSE)

---

## ä¸­æ–‡

### è¯´æ˜

- æœ¬æ’ä»¶é¢å‘ **Frosty Editor 1.0.6.3** ã€‚
- ç”¨äºåœ¨ç¼–è¾‘å™¨é‡Œæ‰“å¼€ `LevelData`ã€`SubWorldData`ã€`SpatialPrefabBlueprint`ã€`ObjectBlueprint` ç­‰èµ„æºï¼ŒæŸ¥çœ‹å’Œç¼–è¾‘å…³å¡ã€‚
- **éƒ¨åˆ†ä»£ç ç”± AI å®Œæˆã€‚**
- æœ¬ä»“åº“æŒ‰ MIT è®¸å¯å…¬å¼€ã€‚è‹¥ä½ æœ‰ä¿®æ”¹éœ€æ±‚ï¼Œè¯· **Fork** ä¸€ä»½è‡ªè¡Œä¿®æ”¹ï¼Œä¸å¿…ä¾èµ–æœ¬ä»“åº“æ¥å—æ‰€æœ‰æ”¹åŠ¨ã€‚

### å®‰è£…

1. æŠŠæœ¬ä»“åº“æ”¾åˆ° FrostyToolsuite **1.0.6.3** å·¥ç¨‹çš„ `Plugins/FrostyLevelEditor`ã€‚
2. ç”¨ Visual Studio æ‰“å¼€è§£å†³æ–¹æ¡ˆï¼Œé…ç½®é€‰ `Developer - Debug`ã€`x64`ï¼Œç¼–è¯‘æœ¬æ’ä»¶å·¥ç¨‹ï¼ˆä¸è¦æ•´è§£å†³æ–¹æ¡ˆä¹±ç¼–ï¼‰ã€‚
3. ç”Ÿæˆçš„ `LevelEditorPlugin.dll` å¤åˆ¶åˆ° Frosty Editor çš„ `Plugins` ç›®å½•ã€‚
4. è‹¥ç¼–è¾‘å™¨æ­£åœ¨è¿è¡Œï¼ŒDLL ä¼šè¢«å ç”¨ï¼Œå…ˆå…³æ‰å†å¤åˆ¶ã€‚

æœ¬æ’ä»¶å¼•ç”¨ `FrostyCore`ã€`FrostySdk`ã€`FrostyControls`ã€`FrostyHash`ã€`MeshSetPlugin`ï¼Œç›®æ ‡ä¸º .NET Framework 4.8ã€x64ã€‚

### ä½¿ç”¨

1. ç”¨ Frosty Editor **1.0.6.3** æ‰“å¼€ Battlefield 1ã€‚
2. åœ¨ Data Explorer ä¸­é€‰ä¸­å…³å¡æˆ–åˆ†åŒº EBXã€‚
3. å³é”®é€‰æ‹©ã€Œæ‰“å¼€å…³å¡ç¼–è¾‘å™¨ã€ï¼ˆOpen Level Editorï¼‰ã€‚

### è´¡çŒ®

æ¬¢è¿ Forkã€‚æœ¬ä»“åº“ä¸ä¿è¯ä¼šåˆå¹¶æ‰€æœ‰å¤–éƒ¨æ”¹åŠ¨ï¼›éœ€è¦è‡ªå·±çš„åŠŸèƒ½æ—¶ï¼Œè¯·ç»´æŠ¤ä½ è‡ªå·±çš„ Forkã€‚

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
3. Right-click and choose **æ‰“å¼€å…³å¡ç¼–è¾‘å™¨** (Open Level Editor).

### Contributing

Forks are welcome. This repository may not merge every external change. Maintain your own fork when you need custom behavior.

---

## ¸üĞÂÈÕÖ¾ / Changelog

### 0.2

ÖĞÎÄ£º

- ĞÂÔö**¶ÔÏóÃæ°å£¨Object Palette£©**£ºË÷Òı¹Ø¿¨ÄÚµÄ SubWorld¡¢WorldPart Óë¿É·ÅÖÃµÄ ObjectBlueprint£¬Ê÷ĞÎä¯ÀÀ²¢Ö§³ÖÍÏ×§·ÅÖÃ¡£
- ĞÂÔö**¹Ø¿¨·ÅÖÃ·şÎñ**£º×Ô¶¯½âÎöÄ¿±ê SubWorld / WorldPart£¬·ÅÖÃÊ±Éú³ÉÊµÊ±ÓÄÁéÔ¤ÀÀ£¬Ê°È¡µãÖ§³ÖµØĞÎ¡¢Íø¸ñÓë°üÎ§ºĞÉäÏß¼ì²â£¬²¢½ÓÈë³·ÏúÕ»¡£
- ĞÂÔö**ÆÁÄ»¿Õ¼äÑ¡ÇøÃè±ß**£ºÔ­°æ `SelectionOutline` µÄÓ²±àÂëÉî¶ÈãĞÖµÔÚ¹Ø¿¨Ïà»úÏÂ²»»á´¥·¢£¬¸ÄÓÃ²å¼ş×Ô´øµÄ `SelectionHighlight.hlsl` ºó´¦Àí£¨ÄÚÇ¶²¢×Ô¶¯ÊÍ·Å£©£¬Í¬Ê±²¹³äÁË´¿Ô­°æ 1.0.6.3 µÄÑ¡ÇøÉî¶È¶µµ×äÖÈ¾¡£
- ĞÂÔö**Havok Åö×²Ìåµş¼Ó²ã**£º´¿ .NET Havok ½âÎöÆ÷ + D3D11 äÖÈ¾£¬°´Ö¡Ô¤Ëã½âÎö£¬Ö§³Ö¹¤¾ßÀ¸¿ª¹ØÓëÖØĞÂ½âÎö£»hkClass ²¼¾Ö±í `HavokLayout.bf1.json` ÄÚÇ¶ÓÚ DLL£¬**²»ÔÙÒÀÀµÈÎºÎÍâ²¿ Havok ²å¼ş**¡£
- MeshView Ôö¼Ó **ViewMode** ÇĞ»»¡£
- ×ÊÔ´£¨shader / gizmo / ÌùÍ¼£©¼ÌĞøÄÚÇ¶½ø DLL£¬±à¼­Æ÷Æô¶¯Ê±×Ô¶¯ÊÍ·Å¡£

English:

- Added the **Object Palette**: indexes SubWorlds, WorldParts and placeable ObjectBlueprints in the level, browse them as a tree and drag them into the viewport.
- Added the **level placement service**: resolves the target SubWorld / WorldPart automatically, shows a live ghost preview, picks points via terrain / mesh / bounding-box raycasts and integrates with the undo stack.
- Added **screen-space selection outlining**: vanilla `SelectionOutline` uses hardcoded depth thresholds that never trigger with the level editor camera, so the plugin now ships its own `SelectionHighlight.hlsl` post-process (embedded and auto-extracted), plus a selection-depth fallback for unmodified 1.0.6.3.
- Added the **Havok collision overlay**: pure .NET Havok parser plus D3D11 rendering with frame-budgeted parsing, a toolbar toggle and a re-parse action. The `HavokLayout.bf1.json` hkClass layout table is embedded in the DLL, so **no external Havok plugin is required**.
- Added a **ViewMode** switcher to MeshView.
- All resources (shaders / gizmos / textures) stay embedded in the DLL and are extracted when the editor starts.

### 0.1

- First public release.