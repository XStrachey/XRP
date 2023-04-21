#ifndef XRP_UNLIT_INCLUDED
#define XRP_UNLIT_INCLUDED

#include "Inputs.hlsl"

UNITY_INSTANCING_BUFFER_START(PerInstance)
	UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)

struct VertexInput
{
	float4 positionOS : POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput
{
	float4 positionCS : SV_POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

// The rule is to use float for positions and texture coordinate only and half for everything else, 
// provided that the results are acceptable.
VertexOutput UnlitPassVertex (VertexInput input)
{
	UNITY_SETUP_INSTANCE_ID(input);
	VertexOutput output;
	UNITY_TRANSFER_INSTANCE_ID(input, output);
    // The fourth component of the position is always 1. 
    // By making that explicit we make it possible for the compiler to optimize the computation.
	float4 positionWS = mul(UNITY_MATRIX_M, float4(input.positionOS.xyz, 1.0));
    float4 positionCS = mul(unity_MatrixVP, positionWS);
    output.positionCS = positionCS;
	return output;
}

float4 UnlitPassFragment (VertexOutput input) : SV_TARGET
{
	UNITY_SETUP_INSTANCE_ID(input);
	return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color);
}

#endif