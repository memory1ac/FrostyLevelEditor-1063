using System;

namespace LevelEditorPlugin.Havok
{
    /// <summary>
    /// 解析失败时抛出。消息里始终带上"哪一步、哪个偏移、期望/实际"，方便定位。
    /// </summary>
    public sealed class HkxParseException : Exception
    {
        public HkxParseException(string message)
            : base(message)
        {
        }
    }
}
