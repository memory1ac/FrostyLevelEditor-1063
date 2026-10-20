using Frosty.Core.Screens;
using Frosty.Core.Viewport;
using LevelEditorPlugin.Entities;
using SharpDX;
using SharpDX.Direct3D11;
using System;

namespace LevelEditorPlugin.Render.Proxies;

public abstract class RenderProxy : MeshRenderBase
{
    public float CurrentDistanceToCamera { get; set; }
    public Matrix Transform { get; protected set; }
    public BoundingBox BoundingBox { get; protected set; }
    public Entity OwnerEntity { get; protected set; }

    protected bool isSelected;

    private BoundingSphere cachedBoundingSphere;
    private bool boundingSphereDirty = true;

    /// <summary>
    /// 每个 Proxy 缓存一次包围球，避免在 CollectMeshInstances 中每帧重复从包围盒推导。
    /// </summary>
    public BoundingSphere BoundingSphere
    {
        get
        {
            if (boundingSphereDirty)
            {
                cachedBoundingSphere = BoundingSphere.FromBox(BoundingBox);
                boundingSphereDirty = false;
            }
            return cachedBoundingSphere;
        }
    }


    public RenderProxy(ISpatialEntity owner)
    {
        OwnerEntity = owner as Entity;
        Transform = owner.GetTransform();
    }

    public override void Render(DeviceContext context, MeshRenderPath renderPath)
    {
        throw new NotImplementedException();
    }

    public virtual void Update(RenderCreateState state)
    {
        if (OwnerEntity.RequiresTransformUpdate)
        {
            Transform = (OwnerEntity as ISpatialEntity).GetTransform();
            RecalculateBoundingBox();

            OwnerEntity.RequiresTransformUpdate = false;
        }
    }

    public virtual void RecalculateBoundingBox()
    {
        // do nothing
        boundingSphereDirty = true;
    }

    public virtual bool HitTest(Ray hitTestRay, out Vector3 hitLocation)
    {
        hitLocation = Transform.TranslationVector;
        return true;
    }

    public virtual bool ShouldRender(float distToCamera, float screenSize)
    {
        return true;
    }

    public virtual MeshRenderInstance GetInstance(float distToCamera)
    {
        return new MeshRenderInstance() { RenderMesh = this, Transform = Transform };
    }

    public virtual void SetSelected(RenderCreateState state, bool newSelected)
    {
        isSelected = newSelected;
    }

    public abstract void Dispose();
}
