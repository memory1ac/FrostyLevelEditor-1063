using Frosty.Core;
using Frosty.Core.Viewport;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using MeshSetPlugin.Render;
using System;
using System.Collections.Generic;
using System.Threading;

namespace LevelEditorPlugin.Render;

/// <summary>
/// Stock Frosty 1.0.6.3 fallback shaders only bind parameters named Diffuse/BaseColor*
/// or exactly Normal. BF1 materials use TexBaseColor, *_CS, NAM, TexNormalmap, etc.
/// This remaps those onto Diffuse/Normal from the level-editor path so the
/// unmodified MeshSetPlugin / FrostyCore matcher can see them.
/// </summary>
public static class ShaderDbMeshTextures
{
    private static readonly HashSet<int> BoundMaterials = new HashSet<int>();

    public static void Apply(RenderCreateState state, MeshRenderable mesh)
    {
        if (ProfilesLibrary.DataVersion != (int)ProfileVersion.Battlefield1)
            return;
        if (state == null || mesh?.TexturedMesh == null || mesh.Materials == null || mesh.Materials.Count == 0)
            return;

        bool[] filled = new bool[mesh.Materials.Count];
        for (int i = 0; i < mesh.Materials.Count; i++)
        {
            if (BoundMaterials.Contains(RuntimeHelpersId(mesh.Materials[i])))
                continue;
            try
            {
                if (Fill(mesh.Materials[i]))
                    filled[i] = true;
            }
            catch (Exception ex) when (!(ex is ThreadAbortException))
            {
                BoundMaterials.Add(RuntimeHelpersId(mesh.Materials[i]));
                App.Logger.LogError("网格贴图绑定失败: {0}", ex.Message);
            }
        }

        if (!Any(filled))
            return;

        foreach (MeshRenderLod lod in mesh.TexturedMesh.LODs)
        {
            foreach (MeshRenderSection section in lod.Sections)
            {
                if (!section.IsFallback || section.Permutation == null)
                    continue;
                int id = section.MeshSection.MaterialId;
                if ((uint)id >= (uint)filled.Length || !filled[id])
                    continue;
                section.Permutation.AssignFallbackParameters(state, section, mesh.Materials[id]);
            }
        }
    }

    private static bool Fill(MeshMaterial material)
    {
        if (material == null)
            return false;
        int id = RuntimeHelpersId(material);
        if (material.Shader.External.FileGuid == Guid.Empty)
        {
            BoundMaterials.Add(id);
            return false;
        }

        EbxAssetEntry shader = App.AssetManager.GetEbxEntry(material.Shader.External.FileGuid);
        if (shader == null)
        {
            BoundMaterials.Add(id);
            return false;
        }

        TerrainAlbedoResolver.ResolveShader(shader.Name);
        if (TerrainAlbedoResolver.IsVirtualTextureShader(shader.Name))
        {
            BoundMaterials.Add(id);
            return false;
        }

        bool hadStockColor = HasStockColor(material);
        bool hadStockNormal = HasStockNormal(material);
        EbxAssetEntry color = PickColor(material);
        EbxAssetEntry normal = PickNormal(material);

        if (color == null || normal == null)
        {
            TerrainAlbedoResolver.TryGetMeshTextures(shader.Name, out string colorPath, out string normalPath);
            if (color == null && !string.IsNullOrEmpty(colorPath))
                color = App.AssetManager.GetEbxEntry(colorPath);
            if (normal == null && !string.IsNullOrEmpty(normalPath))
                normal = App.AssetManager.GetEbxEntry(normalPath);
        }

        bool changed = false;
        if (color != null && !hadStockColor)
        {
            material.SetTextureParameter("Diffuse", color);
            changed = true;
        }
        if (normal != null && !hadStockNormal)
        {
            material.SetTextureParameter("Normal", normal);
            changed = true;
        }

        BoundMaterials.Add(id);
        if (!changed)
            return false;

        App.Logger.Log("网格贴图: {0} <- {1}", shader.Filename, color?.Filename ?? normal?.Filename);
        return true;
    }

    // 1.0.6.3 AssignFallbackParameters for BF1: StartsWith BaseColor/Diffuse, Equals Normal.
    private static bool HasStockColor(MeshMaterial material)
    {
        foreach (dynamic param in material.TextureParameters)
        {
            string name = param.ParameterName as string;
            if (string.IsNullOrEmpty(name))
                continue;
            if (name.StartsWith("BaseColor", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Diffuse", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool HasStockNormal(MeshMaterial material)
    {
        foreach (dynamic param in material.TextureParameters)
        {
            string name = param.ParameterName as string;
            if (!string.IsNullOrEmpty(name) && name.Equals("Normal", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static EbxAssetEntry PickColor(MeshMaterial material)
    {
        EbxAssetEntry primary = null;
        EbxAssetEntry secondary = null;
        EbxAssetEntry byFile = null;
        foreach (dynamic param in material.TextureParameters)
        {
            EbxAssetEntry entry = TextureEntry(param);
            if (entry == null)
                continue;
            string name = param.ParameterName as string ?? "";
            if (IsColorName(name, out bool tiling))
            {
                if (tiling)
                    secondary ??= entry;
                else
                    primary ??= entry;
            }
            else if (byFile == null && IsColorFile(entry.Filename))
                byFile = entry;
        }
        return primary ?? secondary ?? byFile;
    }

    private static EbxAssetEntry PickNormal(MeshMaterial material)
    {
        EbxAssetEntry primary = null;
        EbxAssetEntry secondary = null;
        EbxAssetEntry byFile = null;
        foreach (dynamic param in material.TextureParameters)
        {
            EbxAssetEntry entry = TextureEntry(param);
            if (entry == null)
                continue;
            string name = param.ParameterName as string ?? "";
            if (IsNormalName(name, out bool tiling))
            {
                if (tiling)
                    secondary ??= entry;
                else
                    primary ??= entry;
            }
            else if (byFile == null && IsNormalFile(entry.Filename))
                byFile = entry;
        }
        return primary ?? secondary ?? byFile;
    }

    private static bool IsColorName(string paramName, out bool secondary)
    {
        string key = paramName.TrimStart('_');
        secondary = key.IndexOf("Tiling", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("Detail", StringComparison.OrdinalIgnoreCase) >= 0;
        return key.IndexOf("BaseColor", StringComparison.OrdinalIgnoreCase) >= 0
            || key.StartsWith("Diffuse", StringComparison.OrdinalIgnoreCase)
            || key.IndexOf("CS", StringComparison.OrdinalIgnoreCase) >= 0
            || key.EndsWith("_CA", StringComparison.OrdinalIgnoreCase)
            || key.IndexOf("ColorTexture", StringComparison.OrdinalIgnoreCase) >= 0
            || key.Equals("Color", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNormalName(string paramName, out bool secondary)
    {
        string key = paramName.TrimStart('_');
        secondary = key.IndexOf("Tiling", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("Detail", StringComparison.OrdinalIgnoreCase) >= 0;
        return key.Equals("Normal", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("NAM", StringComparison.OrdinalIgnoreCase)
            || key.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0
            || key.EndsWith("_NS", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("_NM", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("_NA", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsColorFile(string filename)
    {
        return filename.EndsWith("_CS", StringComparison.OrdinalIgnoreCase)
            || filename.EndsWith("_CA", StringComparison.OrdinalIgnoreCase)
            || filename.EndsWith("_D", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNormalFile(string filename)
    {
        if (filename.EndsWith("_NS", StringComparison.OrdinalIgnoreCase))
            return false;
        return filename.EndsWith("_N", StringComparison.OrdinalIgnoreCase)
            || filename.EndsWith("_NM", StringComparison.OrdinalIgnoreCase)
            || filename.EndsWith("_NA", StringComparison.OrdinalIgnoreCase)
            || filename.EndsWith("_NRH", StringComparison.OrdinalIgnoreCase)
            || filename.EndsWith("_NAH", StringComparison.OrdinalIgnoreCase);
    }

    private static EbxAssetEntry TextureEntry(dynamic textureParam)
    {
        try
        {
            dynamic value = textureParam.Value;
            if (value.Type != PointerRefType.External)
                return null;
            return App.AssetManager.GetEbxEntry((Guid)value.External.FileGuid);
        }
        catch
        {
            return null;
        }
    }

    private static int RuntimeHelpersId(MeshMaterial material)
    {
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(material);
    }

    private static bool Any(bool[] values)
    {
        foreach (bool value in values)
        {
            if (value)
                return true;
        }
        return false;
    }
}
