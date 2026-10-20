using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace LevelEditorPlugin.Resources;

internal unsafe class PtrUtils
{
    public static string GetNullTerminatedString(byte* ptr)
    {
        StringBuilder sb = new StringBuilder();
        while (*ptr != 0x00)
        {
            sb.Append((char)*ptr);
            ptr++;
        }

        return sb.ToString();
    }
}

[StructLayout(LayoutKind.Explicit, Pack = 4)]
public unsafe struct TerrainLayerCombinationDrawPassData
{
    public string ShaderName => PtrUtils.GetNullTerminatedString(StringPtr);

    [FieldOffset(0)]
    public byte* StringPtr;
    [FieldOffset(8)]
    public int RegularIndexCount;
    [FieldOffset(12)]
    public byte* RegularIndices;
    [FieldOffset(20)]
    public int MaskedIndexCount;
    [FieldOffset(24)]
    public byte* MaskedIndices;
    [FieldOffset(32)]
    public uint Unknown;
}

[StructLayout(LayoutKind.Explicit)]
public unsafe struct TerrainLayerCombinationDrawData
{
    [FieldOffset(0)]
    public int IndexCount;
    [FieldOffset(4)]
    public byte* Indices;
    [FieldOffset(12)]
    public int MaskedDrawCount;
    [FieldOffset(16)]
    public TerrainLayerCombinationDrawPassData* MaskedDrawPass;
    [FieldOffset(24)]
    public int TwoDDrawCount;
    [FieldOffset(28)]
    public TerrainLayerCombinationDrawPassData* TwoDDrawPass;
    [FieldOffset(36)]
    public int DisplacementDrawCount;
    [FieldOffset(40)]
    public TerrainLayerCombinationDrawPassData* DisplacementDrawPass;
    [FieldOffset(48)]
    public int ThreeDDrawCount;
    [FieldOffset(52)]
    public TerrainLayerCombinationDrawPassData* ThreeDDrawPass;
    [FieldOffset(60)]
    public int Unknown2;
}

public sealed class TerrainColorShader
{
    public string Name;
    public int[] Layers;
}

public unsafe class TerrainLayerCombinations : Resource
{
    public List<TerrainColorShader> ColorShaders { get; } = [];

    private unsafe struct MetaData
    {
        public int RelocationTableOffset => *(int*)&data[0];
        public int RelocationTableSize => *(int*)&data[4];

        private byte* data;

        internal MetaData(byte[] inData)
        {
            fixed (byte* ptr = &inData[0])
            {
                data = ptr;
            }
        }
    }

    private byte* stringData => (byte*)&dataPtr[0];
    private TerrainLayerCombinationDrawData* drawDatas => *(TerrainLayerCombinationDrawData**)&dataPtr[0x0c];
    private int drawDataCount => *(int*)&dataPtr[0x08];

    private byte[] data;
    private byte* dataPtr;

    public override void Read(NativeReader reader, AssetManager am, ResAssetEntry entry, ModifiedResource modifiedData)
    {
        // Shader paths are inline strings. Do not apply the relocation table: that write is not
        // used for the atlas and would mutate bytes the heightfield load does not own.
        data = reader.ReadToEnd();
        CollectColorShaders();

        //for (int i = 0; i < drawDataCount; i++)
        //{
        //    var drawData = drawDatas[i];
        //    System.Diagnostics.Debug.WriteLine($"{drawData.MaskedDrawCount},{drawData.TwoDDrawCount},{drawData.DisplacementDrawCount},{drawData.ThreeDDrawCount},{drawData.Unknown2}");
        //    System.Diagnostics.Debug.Write("  I: ");
        //    for (int j = 0; j < drawData.IndexCount; j++)
        //    {
        //        System.Diagnostics.Debug.Write($"{drawData.Indices[j]} ");
        //    }
        //    System.Diagnostics.Debug.Write("\n");

        //    for (int j = 0; j < drawData.MaskedDrawCount; j++)
        //    {
        //        System.Diagnostics.Debug.WriteLine($"  M: {drawData.MaskedDrawPass[j].Unknown.ToString("x8")} {drawData.MaskedDrawPass[j].ShaderName}");
        //    }
        //    for (int j = 0; j < drawData.TwoDDrawCount; j++)
        //    {
        //        System.Diagnostics.Debug.WriteLine($"  2: {drawData.TwoDDrawPass[j].Unknown.ToString("x8")} {drawData.TwoDDrawPass[j].ShaderName}");
        //    }
        //    for (int j = 0; j < drawData.DisplacementDrawCount; j++)
        //    {
        //        System.Diagnostics.Debug.WriteLine($"  D: {drawData.DisplacementDrawPass[j].Unknown.ToString("x8")} {drawData.DisplacementDrawPass[j].ShaderName}");
        //    }
        //    for (int j = 0; j < drawData.ThreeDDrawCount; j++)
        //    {
        //        System.Diagnostics.Debug.WriteLine($"  3: {drawData.ThreeDDrawPass[j].Unknown.ToString("x8")} {drawData.ThreeDDrawPass[j].ShaderName}");
        //    }
        //}
    }

    private void CollectColorShaders()
    {
        ColorShaders.Clear();
        if (data == null || data.Length < 8)
            return;

        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < data.Length)
        {
            int marker = IndexOfAscii(data, "__2d__", i);
            if (marker < 0)
                break;

            int start = marker;
            while (start > 0 && IsPathChar(data[start - 1]))
                start--;

            int end = marker;
            while (end < data.Length && data[end] != 0 && end - start < 512)
                end++;

            if (end > start)
            {
                string name = Encoding.ASCII.GetString(data, start, end - start);
                if (seen.Add(name))
                {
                    int[] layers = ParseLayerIndices(name);
                    if (layers.Length > 0)
                        ColorShaders.Add(new TerrainColorShader { Name = name, Layers = layers });
                }
            }

            i = Math.Max(end, marker + 6);
        }
    }

    private static bool IsPathChar(byte value)
    {
        return (value >= (byte)'A' && value <= (byte)'Z')
            || (value >= (byte)'a' && value <= (byte)'z')
            || (value >= (byte)'0' && value <= (byte)'9')
            || value == (byte)'/'
            || value == (byte)'_'
            || value == (byte)'.'
            || value == (byte)'-';
    }

    private static int IndexOfAscii(byte[] buffer, string text, int from)
    {
        int limit = buffer.Length - text.Length;
        for (int i = Math.Max(0, from); i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < text.Length; j++)
            {
                if (buffer[i + j] != (byte)text[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }

    private static int[] ParseLayerIndices(string shaderName)
    {
        if (shaderName.IndexOf("__2d__", StringComparison.OrdinalIgnoreCase) < 0)
            return [];

        MatchCollection matches = Regex.Matches(shaderName, @"(\d+)MV");
        int[] layers = new int[matches.Count];
        for (int i = 0; i < matches.Count; i++)
            layers[i] = int.Parse(matches[i].Groups[1].Value);
        return layers;
    }

    internal void Fixup(byte[] relocationTable)
    {
        fixed (byte** addrOfDataPtr = &dataPtr)
        {
            long startAddress = *(long*)addrOfDataPtr;
            for (int i = 0; i < relocationTable.Length / 4; i++)
            {
                uint value = BitConverter.ToUInt32(relocationTable, i * 4);
                byte* locationPtr = dataPtr + value;

                *(long*)locationPtr += startAddress;
            }
        }
    }
}
