using System;
using System.Runtime.InteropServices;

namespace LevelEditorPlugin.BF1Core.Native;

public static class Win32
{
    [DllImport("user32.dll")]
    private static extern int GetAsyncKeyState(Keys key);

    private const int KEY_PRESSED = 0x8000;

    /// <summary>
    /// 判断按键是否按下
    /// </summary>
    public static bool IsKeyPressed(Keys key)
    {
        return Convert.ToBoolean(GetAsyncKeyState(key) & KEY_PRESSED);
    }
}

public class KeyInfo
{
    public Keys Key { get; set; }
    public bool IsKeyDown { get; set; }
}
