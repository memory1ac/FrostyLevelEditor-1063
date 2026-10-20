cbuffer Bake : register(b0)
{
	float4x4 TerrainWorld;
	float4 AtlasOrigin;
	float4 TileSize;
	float4 Has0;
	float4 Has1;
	float4 Has2;
	float4 Has3;
	float4 Has4;
	float4 Has5;
	float4 Offsets[24];
};

Texture2D LayerGroup0 : register(t0);
Texture2D LayerGroup1 : register(t1);
Texture2D LayerGroup2 : register(t2);
Texture2D LayerGroup3 : register(t3);
Texture2D LayerGroup4 : register(t4);
Texture2D LayerGroup5 : register(t5);
Texture2D LayerColor0 : register(t6);
Texture2D LayerColor1 : register(t7);
Texture2D LayerColor2 : register(t8);
Texture2D LayerColor3 : register(t9);
Texture2D LayerColor4 : register(t10);
Texture2D LayerColor5 : register(t11);
Texture2D LayerColor6 : register(t12);
Texture2D LayerColor7 : register(t13);
Texture2D LayerColor8 : register(t14);
Texture2D LayerColor9 : register(t15);
Texture2D LayerColor10 : register(t16);
Texture2D LayerColor11 : register(t17);
Texture2D LayerColor12 : register(t18);
Texture2D LayerColor13 : register(t19);
Texture2D LayerColor14 : register(t20);
Texture2D LayerColor15 : register(t21);
Texture2D LayerColor16 : register(t22);
Texture2D LayerColor17 : register(t23);
Texture2D LayerColor18 : register(t24);
Texture2D LayerColor19 : register(t25);
Texture2D LayerColor20 : register(t26);
Texture2D LayerColor21 : register(t27);
Texture2D LayerColor22 : register(t28);
Texture2D LayerColor23 : register(t29);

SamplerState sampClamp : register(s0);
SamplerState sampWrap : register(s1);

struct VSIn
{
	float3 Pos : POSITION;
	float3 Normal : NORMAL;
	float2 TexCoord : TEXCOORD;
	float3 Color : COLOR;
};

struct VSOut
{
	float4 Pos : SV_POSITION;
	float2 MaskUv : TEXCOORD0;
	float2 TileUv : TEXCOORD1;
};

VSOut VSMain(VSIn Input)
{
	VSOut output = (VSOut)0;
	float3 worldPos = mul(float4(Input.Pos, 1.0f), TerrainWorld).xyz;
	float2 uv = (worldPos.xz - AtlasOrigin.xy) / max(AtlasOrigin.zw, float2(0.001, 0.001));
	output.Pos = float4(uv.x * 2.0f - 1.0f, -(uv.y * 2.0f - 1.0f), 0.5f, 1.0f);
	output.MaskUv = Input.TexCoord;
	output.TileUv = worldPos.xz / max(TileSize.xy, float2(0.001, 0.001));
	return output;
}

#define PAINT(layer, has, group, channel, tex) \
	{ \
		float2 maskUv = input.MaskUv * Offsets[layer].zw + Offsets[layer].xy; \
		float m = group.Sample(sampClamp, maskUv).channel * has; \
		accum += tex.Sample(sampWrap, input.TileUv).rgb * m; \
		wsum += m; \
	}

float4 PSMain(VSOut input) : SV_Target
{
	float3 accum = float3(0, 0, 0);
	float wsum = 0;
	PAINT(0, Has0.x, LayerGroup0, r, LayerColor0)
	PAINT(1, Has0.y, LayerGroup0, g, LayerColor1)
	PAINT(2, Has0.z, LayerGroup0, b, LayerColor2)
	PAINT(3, Has0.w, LayerGroup0, a, LayerColor3)
	PAINT(4, Has1.x, LayerGroup1, r, LayerColor4)
	PAINT(5, Has1.y, LayerGroup1, g, LayerColor5)
	PAINT(6, Has1.z, LayerGroup1, b, LayerColor6)
	PAINT(7, Has1.w, LayerGroup1, a, LayerColor7)
	PAINT(8, Has2.x, LayerGroup2, r, LayerColor8)
	PAINT(9, Has2.y, LayerGroup2, g, LayerColor9)
	PAINT(10, Has2.z, LayerGroup2, b, LayerColor10)
	PAINT(11, Has2.w, LayerGroup2, a, LayerColor11)
	PAINT(12, Has3.x, LayerGroup3, r, LayerColor12)
	PAINT(13, Has3.y, LayerGroup3, g, LayerColor13)
	PAINT(14, Has3.z, LayerGroup3, b, LayerColor14)
	PAINT(15, Has3.w, LayerGroup3, a, LayerColor15)
	PAINT(16, Has4.x, LayerGroup4, r, LayerColor16)
	PAINT(17, Has4.y, LayerGroup4, g, LayerColor17)
	PAINT(18, Has4.z, LayerGroup4, b, LayerColor18)
	PAINT(19, Has4.w, LayerGroup4, a, LayerColor19)
	PAINT(20, Has5.x, LayerGroup5, r, LayerColor20)
	PAINT(21, Has5.y, LayerGroup5, g, LayerColor21)
	PAINT(22, Has5.z, LayerGroup5, b, LayerColor22)
	PAINT(23, Has5.w, LayerGroup5, a, LayerColor23)
	float3 color = wsum > 0.001 ? accum / wsum : float3(0.45, 0.45, 0.45);
	return float4(color, 1);
}
