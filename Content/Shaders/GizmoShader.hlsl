
struct VertexShaderIntermediates
{
	float4 Pos : SV_POSITION;
	float3 Normal : NORMAL;
	float2 TexCoord0 : TEXCOORD;
	float3 WorldPos : WORLDPOS;
	float3 LocalPos : LOCALPOS;
};

float4 TransformPosition2(VertexShaderInput Input, float3 Position)
{
	float3 worldPosition = mul(float4(Position, 1.0f), worldMatrix).xyz;
	return mul(float4(worldPosition, 1.0f), viewProjMatrix);
}

float3 TransformTBN2(VertexShaderInput Input, float3 Value)
{
	return mul(normalize(Value), (float3x3)worldMatrix);
}

// Spin a Z-axis torus so it faces the camera.
float3 RotateFromZ(float3 p, float3 toAxis)
{
	float3 to = normalize(toAxis);
	float3 from = float3(0.0, 0.0, 1.0);
	float d = dot(from, to);
	if (d > 0.999)
		return p;

	float3 c = cross(from, to);
	float clen = length(c);
	if (clen < 1e-4)
		return float3(-p.x, p.y, -p.z);

	float3 k = c / clen;
	float ang = atan2(clen, d);
	float ca = cos(ang);
	float sa = sin(ang);
	return p * ca + cross(k, p) * sa + k * dot(k, p) * (1.0 - ca);
}

VertexShaderIntermediates GetVertexShaderIntermediates(VertexShaderInput Input)
{
	VertexShaderIntermediates Intermediates = (VertexShaderIntermediates)0;

	float3 localPos = Input.Pos.xyz;
	float3 localNrm = Input.Normal.xyz;
	if (AxisColor.a > 2.5)
	{
		localPos = RotateFromZ(localPos, RingAxis.xyz);
		localNrm = RotateFromZ(localNrm, RingAxis.xyz);
	}

	Intermediates.Pos = TransformPosition2(Input, localPos);
	Intermediates.Normal = TransformTBN2(Input, localNrm);
	Intermediates.TexCoord0 = Input.TexCoord0;
	Intermediates.WorldPos = mul(float4(localPos, 1.0f), worldMatrix).xyz - cameraPos;
	Intermediates.LocalPos = localPos;

	return Intermediates;
}

#define RENDER_PATH
float4 GetPixelShaderIntermediates(PixelShaderInput Input)
{
	float4 color = Texture.Sample(sampler0_s, Input.TexCoord0);
	float selectMask = SelectMask.Sample(sampler0_s, Input.TexCoord0).r;
	int selectIndex = (int)SelectIndex.r;

	// AxisColor.a: 0 textured arrows, 1 axis ring, 2 highlighted ring, 3 view ring
	if (AxisColor.a > 0.5)
	{
		color = float4(AxisColor.rgb, 1.0);
		if (AxisColor.a < 1.5)
		{
			float3 axis = RingAxis.xyz;
			float3 cam = LocalCamera.xyz;
			float camLen = length(cam);
			float3 camPlane = cam - axis * dot(cam, axis);
			float planeLen = length(camPlane);
			float3 radial = Input.LocalPos - axis * dot(Input.LocalPos, axis);
			float radialLen = length(radial);
			if (camLen > 1e-4 && planeLen > camLen * 0.2 && radialLen > 1e-4)
			{
				float facing = dot(radial / radialLen, camPlane / planeLen);
				float shade = saturate(facing * 1.4 + 0.55);
				color.rgb *= lerp(0.4, 1.0, shade);
			}
		}
	}
	else
	{
		if (selectIndex == 1 && selectMask > 0.5f) color = float4(1, 1, 0, 1);
		else if (selectIndex == 2 && selectMask > 0.3f && selectMask < 0.5f) color = float4(1, 1, 0, 1);
		else if (selectIndex == 0 && selectMask > 0.0f && selectMask < 0.3f) color = float4(1, 1, 0, 1);
	}

	float v = clamp(dot(normalize(Input.Normal), normalize(-Input.WorldPos)), 0, 1);
	float b = lerp(0.5, 1, v);
	float spec = pow(v, 20) * 0.5f;

	return color * (spec + b);
}