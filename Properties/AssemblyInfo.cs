using Frosty.Core.Attributes;
using FrostySdk.Managers;
using LevelEditorPlugin.BF1Core.Extensions;
using LevelEditorPlugin.Definitions;
using LevelEditorPlugin.Extensions;
using System.Runtime.InteropServices;
using System.Windows;

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //where theme specific resource dictionaries are located
                                     //(used if a resource is not found in the page, 
                                     // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly //where the generic resource dictionary is located
                                              //(used if a resource is not found in the page, 
                                              // app, or any theme specific resource dictionaries)
)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("4b612468-9b6a-4304-88a5-055c3575eb3d")]

[assembly: PluginDisplayName("Level Editor")]
[assembly: PluginAuthor("CrazyZhang And MemoryAc")]
[assembly: PluginVersion("2.0.0.0")]

[assembly: RegisterMenuExtension(typeof(DumpLayoutsToLogExtension))]
[assembly: RegisterMenuExtension(typeof(BlueprintAnalysisExtension))]

[assembly: RegisterMenuExtension(typeof(BF1CoreMenuExtension))]
[assembly: RegisterDataExplorerContextMenu(typeof(OpenLevelEditorContextMenuItem))]
[assembly: RegisterStartupAction(typeof(ResourceStartupAction))]

[assembly: RegisterAssetDefinition("LevelData", typeof(LevelDataAssetDefinition))]
[assembly: RegisterAssetDefinition("SubWorldData", typeof(SubWorldDataAssetDefinition))]
[assembly: RegisterAssetDefinition("DetachedSubWorldData", typeof(DetachedSubWorldDataAssetDefinition))]
[assembly: RegisterAssetDefinition("ObjectBlueprint", typeof(ObjectBlueprintAssetDefinition))]
[assembly: RegisterAssetDefinition("LogicPrefabBlueprint", typeof(LogicPrefabAssetDefinition))]
[assembly: RegisterAssetDefinition("SpatialPrefabBlueprint", typeof(SpatialPrefabAssetDefinition))]

// 不注册 CustomHandler：CommitToRes 已将完整修改后的二进制写入 Data，
// 使用 ResResource 直接导出即可，无需通过 ModifiedResource 序列化。
//[assembly: RegisterCustomHandler(CustomHandlerType.Res, typeof(LevelEditorPlugin.Resources.HavokPhysicsDataActionHandler), ResType = ResourceType.HavokPhysicsData)]

// various shaders used by the level editor
[assembly: RegisterUserShader("TerrainShader", "TerrainShader")]
[assembly: RegisterUserShader("VirtualTextureSample", "VirtualTextureSample")]
[assembly: RegisterUserShader("GizmoShader", "GizmoShader")]
[assembly: RegisterUserShader("SpriteShader", "SpriteShader")]
[assembly: RegisterUserShader("LevelShader", "LevelShader")]
[assembly: RegisterUserShader("CustomCubeShader", "CustomCubeShader")]
