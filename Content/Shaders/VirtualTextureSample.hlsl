struct VertexShaderIntermediates
{
	float4 Pos : SV_POSITION;
	float3 Normal : NORMAL;
	float3 WorldPos : TEXCOORD0;
	float Keep : TEXCOORD1;
};

float KeepInputs(VertexShaderInput Input)
{
	return Input.Tangent.x + Input.Binormal.x
		+ Input.TexCoord0.x + Input.TexCoord1.x + Input.TexCoord2.x
		+ Input.Color0.x + Input.Color1.x
		+ (float)Input.TangentSpace;
}

float3 SkinnedPosition(VertexShaderInput Input)
{
#if SHADER_SKINNED==1
	uint indices[SHADER_BONES_PER_VERTEX];
	float weights[SHADER_BONES_PER_VERTEX];

	indices[0] = Input.BoneIndices.x;
	indices[1] = Input.BoneIndices.y;
	indices[2] = Input.BoneIndices.z;
	indices[3] = Input.BoneIndices.w;
	weights[0] = Input.BoneWeights.x;
	weights[1] = Input.BoneWeights.y;
	weights[2] = Input.BoneWeights.z;
	weights[3] = Input.BoneWeights.w;

	#if SHADER_BONES_PER_VERTEX > 4
	indices[4] = Input.BoneIndices2.x;
	indices[5] = Input.BoneIndices2.y;
	indices[6] = Input.BoneIndices2.z;
	indices[7] = Input.BoneIndices2.w;
	weights[4] = Input.BoneWeights2.x;
	weights[5] = Input.BoneWeights2.y;
	weights[6] = Input.BoneWeights2.z;
	weights[7] = Input.BoneWeights2.w;
	#endif

	float3 skinned = float3(0, 0, 0);
	for (int i = 0; i < SHADER_BONES_PER_VERTEX; i++)
		applyBoneMatrix(Input.Pos.xyz, indices[i], weights[i], skinned);
	return skinned;
#else
	return Input.Pos.xyz;
#endif
}

float3 SkinnedNormal(VertexShaderInput Input)
{
#if SHADER_SKINNED==1
	uint indices[SHADER_BONES_PER_VERTEX];
	float weights[SHADER_BONES_PER_VERTEX];

	indices[0] = Input.BoneIndices.x;
	indices[1] = Input.BoneIndices.y;
	indices[2] = Input.BoneIndices.z;
	indices[3] = Input.BoneIndices.w;
	weights[0] = Input.BoneWeights.x;
	weights[1] = Input.BoneWeights.y;
	weights[2] = Input.BoneWeights.z;
	weights[3] = Input.BoneWeights.w;

	#if SHADER_BONES_PER_VERTEX > 4
	indices[4] = Input.BoneIndices2.x;
	indices[5] = Input.BoneIndices2.y;
	indices[6] = Input.BoneIndices2.z;
	indices[7] = Input.BoneIndices2.w;
	weights[4] = Input.BoneWeights2.x;
	weights[5] = Input.BoneWeights2.y;
	weights[6] = Input.BoneWeights2.z;
	weights[7] = Input.BoneWeights2.w;
	#endif

	float3 skinned = float3(0, 0, 0);
	for (int i = 0; i < SHADER_BONES_PER_VERTEX; i++)
		applyBoneRotation(Input.Normal.xyz, indices[i], weights[i], skinned);
	return skinned;
#else
	return Input.Normal.xyz;
#endif
}

VertexShaderIntermediates GetVertexShaderIntermediates(VertexShaderInput Input)
{
	VertexShaderIntermediates Intermediates = (VertexShaderIntermediates)0;
	float3 skinned = SkinnedPosition(Input);
	float3 worldPos = mul(float4(skinned, 1.0f), worldMatrix).xyz;
	worldPos.x += KeepInputs(Input) * 1e-8;

	Intermediates.Pos = mul(float4(worldPos - cameraPos, 1.0f), crViewProjMatrix);
	Intermediates.Normal = mul((float3x3)worldMatrix, normalize(SkinnedNormal(Input)));
	Intermediates.WorldPos = worldPos;
	Intermediates.Keep = 0;
	return Intermediates;
}

GBuffer GetPixelShaderIntermediates(PixelShaderInput Input)
{
	GBuffer values = (GBuffer)0;
	float2 uv = float2(
		dot(float4(Input.WorldPos, 1.0f), WorldPosToVirtualTextureUVs0),
		dot(float4(Input.WorldPos, 1.0f), WorldPosToVirtualTextureUVs1));

	values.BaseColor = VirtualTextureAtlas.Sample(sampler1_s, uv).rgb;
	values.WorldNormals = normalize(Input.Normal.xyz);
	values.Reflectance = 0.0f;
	values.Smoothness = 0.0f;
	values.Metallic = 0.0f;
	values.MaterialAO = 1.0f;
	values.Radiosity = CalculateRadiosity(values.WorldNormals);
	return values;
}
