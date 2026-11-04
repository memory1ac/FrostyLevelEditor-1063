// -----------------------------------------------------------------------------
// Frosty Level Editor - selection highlight (screen space)
//
// The built-in SelectionOutline pixel shader compares world-space depths of the
// selection mask with a hard-coded 10000 unit threshold. The level editor camera
// has a 1000 unit far plane, so that threshold can never be reached and the
// outline silently disappears. This pass only looks at whether the selection
// mask has geometry at a pixel, which is independent of camera distance and map
// coordinates, and adds a soft interior tint so a selected object is obvious in
// Default Lit mode.
//
// Inputs:
//   t0  tonemapped scene color (finalColorTexture)
//   t1  selection mask depth (selectionDepthTexture, selected objects only)
//   t2  full scene depth (Viewport.DepthBufferSRV, used for occlusion)
// Output:
//   scene color + highlight, written into selectionOutlineTexture, which
//   PostProcessEditorComposite already treats as the base image.
// -----------------------------------------------------------------------------

cbuffer HighlightConstants : register(b0)
{
    float4 OutlineColor;    // rgb = outline color, a = outline strength
    float4 FillColor;       // rgb = interior tint, a = tint strength
    float4 Params;          // x = outline radius (pixels), y = depth tolerance, z = glow strength, w = fill toggle
    float4 ScreenParams;    // xy = 1 / viewport size, zw = viewport size
};

Texture2D<float4> g_sceneColor : register(t0);
Texture2D<float> g_selectionDepth : register(t1);
Texture2D<float> g_sceneDepth : register(t2);

SamplerState sampler0_s : register(s0);

// Only SV_Position is read from the fullscreen quad, the uv is rebuilt from the
// pixel position so this pass does not depend on how FullscreenQuad.hlsl lays
// out its interpolators.
struct PS_IN
{
    float4 Position : SV_Position;
};

// Is this pixel a selected surface that is not hidden by the rest of the scene?
// Depth is non-linear 0..1 (smaller is closer). The selected object is also part
// of the scene depth buffer, so visible pixels satisfy maskDepth <= sceneDepth.
float IsSelectedVisible(float2 texCoord, float tolerance)
{
    float maskDepth = g_selectionDepth.SampleLevel(sampler0_s, texCoord, 0);
    if (maskDepth >= 1.0)
        return 0.0;

    float sceneDepth = g_sceneDepth.SampleLevel(sampler0_s, texCoord, 0);
    return (maskDepth <= sceneDepth + tolerance) ? 1.0 : 0.0;
}

float4 PS_Main(PS_IN In) : SV_Target
{
    // Pixel centre -> [0, 1] texture uv.
    float2 uv = In.Position.xy * ScreenParams.xy;

    float3 scene = g_sceneColor.SampleLevel(sampler0_s, uv, 0).rgb;

    float tolerance = max(Params.y, 1e-6);
    float2 texel = ScreenParams.xy * max(Params.x, 1.0);

    static const float2 directions[8] =
    {
        float2( 1.0,  0.0), float2(-1.0,  0.0),
        float2( 0.0,  1.0), float2( 0.0, -1.0),
        float2( 0.70710678,  0.70710678), float2(-0.70710678,  0.70710678),
        float2( 0.70710678, -0.70710678), float2(-0.70710678, -0.70710678)
    };

    float center = IsSelectedVisible(uv, tolerance);

    float nearSelected = 0.0;   // any selected pixel within one texel
    float nearEmpty = 0.0;      // any unselected pixel within one texel
    float farSelected = 0.0;    // any selected pixel within two texels (glow)

    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float s1 = IsSelectedVisible(uv + directions[i] * texel, tolerance);
        nearSelected = max(nearSelected, s1);
        nearEmpty = max(nearEmpty, 1.0 - s1);
    }

    // Most of the screen is far away from the selection, skip the second ring.
    if (center + nearSelected <= 0.0)
        return float4(scene, 1.0);

    [unroll]
    for (int j = 0; j < 8; j++)
        farSelected = max(farSelected, IsSelectedVisible(uv + directions[j] * texel * 2.0, tolerance));

    // One pixel inside plus one pixel outside form the solid line, the second
    // ring outside becomes the soft glow.
    float innerEdge = center * nearEmpty;
    float outerEdge = (1.0 - center) * nearSelected;
    float glow = (1.0 - center) * farSelected;

    float3 color = scene;

    // Darken the outer ring so the outline also reads on bright backgrounds.
    color = lerp(color, color * 0.25, outerEdge * 0.9);

    // Solid line.
    color = lerp(color, OutlineColor.rgb, innerEdge * OutlineColor.a);

    // Soft glow.
    color += OutlineColor.rgb * (glow * Params.z);

    // Keep a faint tint on the whole object so it stays readable while moving.
    color = lerp(color, FillColor.rgb, center * FillColor.a * Params.w);

    return float4(color, 1.0);
}
