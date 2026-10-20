using SharpDX;
using System;

namespace LevelEditorPlugin.BF1Core.Utils;

/// <summary>
/// 游戏按 sample/65535 * worldSizeY 还原高度。视口预览已经用这套公式，写回必须一致。
/// </summary>
public static class TerrainHeightCodec
{
    public static float DecodeLocalY(ushort sample, float worldSizeY)
    {
        if (worldSizeY <= 0.0f)
            return 0.0f;
        return worldSizeY * (sample / 65535.0f);
    }

    public static ushort EncodeLocalY(float localY, float worldSizeY)
    {
        if (worldSizeY <= 0.0f)
            return 0;

        float normalized = localY / worldSizeY;
        if (normalized < 0.0f)
            normalized = 0.0f;
        else if (normalized > 1.0f)
            normalized = 1.0f;
        return (ushort)(normalized * 65535.0f);
    }

    /// <summary>
    /// 让世界坐标高度落到局部 Y：worldY = (M * (localX, localY, localZ)).Y。
    /// </summary>
    public static float LocalYForWorldHeight(float worldY, float localX, float localZ, Matrix world)
    {
        float scaleY = world.M22;
        if (Math.Abs(scaleY) < 1e-8f)
            scaleY = 1.0f;
        return (worldY - world.M21 * localX - world.M23 * localZ - world.M42) / scaleY;
    }

    public static Vector3 WorldToLocal(Vector3 worldPosition, Matrix world)
    {
        return Vector3.TransformCoordinate(worldPosition, Matrix.Invert(world));
    }

    public static Vector3 LocalToWorld(Vector3 localPosition, Matrix world)
    {
        return Vector3.TransformCoordinate(localPosition, world);
    }

    public static float BrushWeight(float distance, float radius, float feather)
    {
        if (radius <= 0.0f || distance >= radius)
            return 0.0f;

        if (feather < 0.0f)
            feather = 0.0f;
        else if (feather > 1.0f)
            feather = 1.0f;

        float inner = radius * (1.0f - feather);
        if (distance <= inner)
            return 1.0f;

        float span = radius - inner;
        if (span <= 1e-6f)
            return 1.0f;

        float t = (distance - inner) / span;
        float u = 1.0f - t * t;
        return u * u;
    }

    public static void WriteU16(byte[] buffer, int offset, ushort value)
    {
        if (offset < 0 || offset + 1 >= buffer.Length)
            return;
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    public static ushort ReadU16(byte[] buffer, int offset)
    {
        if (offset < 0 || offset + 1 >= buffer.Length)
            return 0;
        return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
    }

    public static void WriteFloat(byte[] buffer, int offset, float value)
    {
        if (offset < 0 || offset + 4 > buffer.Length)
            return;
        byte[] bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        buffer[offset] = bytes[0];
        buffer[offset + 1] = bytes[1];
        buffer[offset + 2] = bytes[2];
        buffer[offset + 3] = bytes[3];
    }
}
