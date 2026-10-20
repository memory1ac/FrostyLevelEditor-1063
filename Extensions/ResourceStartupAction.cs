using Frosty.Core;
using FrostySdk.Interfaces;
using System;

namespace LevelEditorPlugin.Extensions;

public class ResourceStartupAction : StartupAction
{
    public ResourceStartupAction()
    {
        ResourceExtractor.EnsureExtracted();
    }

    public override Action<ILogger> Action => (logger) =>
    {
        try
        {
            int written = ResourceExtractor.EnsureExtracted();
            if (written > 0)
                logger.Log($"Level Editor: 已释放 {written} 个内嵌资源");
        }
        catch (Exception ex)
        {
            logger.Log($"Level Editor: 释放内嵌资源失败: {ex.Message}");
        }
    };
}
