#ifndef XRP_LIT_INCLUDED
#define XRP_LIT_INCLUDED

#include "Inputs.hlsl"
#include "Lighting.hlsl"
#include "Shadows.hlsl"

UNITY_INSTANCING_BUFFER_START(PerInstance)
	UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)

struct VertexInput
{
	float4 positionOS : POSITION;
	float3 normalOS : NORMAL;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput
{
	float4 positionCS : SV_POSITION;
	float3 normalWS : TEXCOORD1;
	float3 positionWS : TEXCOORD2;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

// The rule is to use float for positions and texture coordinate only and half for everything else, 
// provided that the results are acceptable.
VertexOutput LitPassVertex (VertexInput input)
{
	UNITY_SETUP_INSTANCE_ID(input);
	VertexOutput output;
	UNITY_TRANSFER_INSTANCE_ID(input, output);
    // The fourth component of the position is always 1. 
    // By making that explicit we make it possible for the compiler to optimize the computation.
	float4 positionWS = mul(UNITY_MATRIX_M, float4(input.positionOS.xyz, 1.0));
    float4 positionCS = mul(unity_MatrixVP, positionWS);
	output.positionWS = positionWS.xyz;
    output.positionCS = positionCS;

	output.normalWS = mul((float3x3)UNITY_MATRIX_M, input.normalOS);

	return output;
}

float4 LitPassFragment (VertexOutput input) : SV_TARGET
{
	UNITY_SETUP_INSTANCE_ID(input);

	half3 color = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;
	half3 normalWS = normalize(input.normalWS);

	half3 diffuseLight = 0;
	#if defined(_CASCADED_SHADOWS_HARD) || defined(_CASCADED_SHADOWS_SOFT)
		diffuseLight += MainLight(normalWS, input.positionWS);
	#endif
	[unroll]
	for (uint i = 0; i < MAX_VISIBLE_LIGHTS; ++i)
	{
		float shadowAttenuation = ShadowAttenuation(i, input.positionWS);
		diffuseLight += DiffuseLight(i, input.positionWS, normalWS, shadowAttenuation);
	}
	color = diffuseLight * color;

	return half4(color, 1);
}

#endif