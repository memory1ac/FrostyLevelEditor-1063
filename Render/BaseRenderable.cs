using Frosty.Core.Viewport;
using SharpDX.Direct3D11;
using System;

namespace LevelEditorPlugin.Render;

public class BaseRenderable : MeshRenderBase
{
    public override void Render(DeviceContext context, MeshRenderPath renderPath)
    {
        throw new NotImplementedException();
    }
}
