using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LevelEditorPlugin.BF1Core.Native;

public static class HotKey
{
    private static CancellationTokenSource _cancelToken = null;

    /// <summary>
    /// 热键字典集合
    /// </summary>
    private static readonly Dictionary<Keys, KeyInfo> HotKeyDirts = [];

    /// <summary>
    /// 按键弹起事件
    /// </summary>
    public static event Action<Keys> KeyUpEvent;
    /// <summary>
    /// 按键按下事件
    /// </summary>
    public static event Action<Keys> KeyDownEvent;

    /// <summary>
    /// 运行快捷键
    /// </summary>
    public static void Run()
    {
        _cancelToken = new CancellationTokenSource();

        Task.Run(UpdateHotKeyTask);
    }

    /// <summary>
    /// 停止快捷键
    /// </summary>
    public static void Stop()
    {
        _cancelToken?.Cancel();
    }

    /// <summary>
    /// 增加快捷按键
    /// </summary>
    public static void AddKey(Keys key)
    {
        if (_cancelToken != null)
            return;

        if (!HotKeyDirts.ContainsKey(key))
            HotKeyDirts.Add(key, new KeyInfo() { Key = key });
    }

    /// <summary>
    /// 按键按下
    /// </summary>
    private static void OnKeyDown(Keys key)
    {
        KeyDownEvent?.Invoke(key);
    }

    /// <summary>
    /// 按键弹起
    /// </summary>
    private static void OnKeyUp(Keys key)
    {
        KeyUpEvent?.Invoke(key);
    }

    /// <summary>
    /// 更新全局快捷键任务
    /// </summary>
    public static async Task UpdateHotKeyTask()
    {
        while (true)
        {
            if (_cancelToken.IsCancellationRequested)
                return;

            if (HotKeyDirts.Count == 0)
                goto SLEEP;

            // 枚举过程中是不允许动态修改集合的
            // 上面添加快捷键操作需要在运行此任务前执行
            foreach (var keyInfo in HotKeyDirts.Values)
            {
                if (Win32.IsKeyPressed(keyInfo.Key))
                {
                    if (!keyInfo.IsKeyDown)
                    {
                        keyInfo.IsKeyDown = true;
                        OnKeyDown(keyInfo.Key);
                    }
                }
                else
                {
                    if (keyInfo.IsKeyDown)
                    {
                        keyInfo.IsKeyDown = false;
                        OnKeyUp(keyInfo.Key);
                    }
                }
            }

        SLEEP:
            // 执行间隔
            await Task.Delay(20);
        }
    }
}
