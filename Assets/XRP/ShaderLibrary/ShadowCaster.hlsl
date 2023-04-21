#ifndef XRP_SHADOWCASTER_INCLUDED
#define XRP_SHADOWCASTER_INCLUDED

#include "Inputs.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

CBUFFER_START(_ShadowCasterBuffer)
	float _ShadowBias;
CBUFFER_END

struct VertexInput
{
	float4 positionOS : POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput
{
	float4 positionCS : SV_POSITION;
};

VertexOutput ShadowCasterPassVertex (VertexInput input)
{
	UNITY_SETUP_INSTANCE_ID(input);
	VertexOutput output;	
	float4 positionWS = mul(UNITY_MATRIX_M, float4(input.positionOS.xyz, 1.0));
	output.positionCS = mul(unity_MatrixVP, positionWS);
	// This is sufficient to render shadows, but it is possible for shadow casters to intersect the near place, which can cause holes to appear in shadows. 
	// To prevent this, we have to clamp the vertices to the near place in the vertex program. 
	// This is done by taking the maximum of the Z coordinate and the W coordinate of the clip-space position.
	// It's actually the reverse for all but OpenGL APIs, with the value being 1 at the near plane. And for OpenGL the near plane value is −1.
	#if UNITY_REVERSED_Z
		// We'll support the simplest way to mitigate acne, which is by adding a small depth offset when rendering to the shadow map.
		output.positionCS.z -= _ShadowBias;
		output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
	#else
		// We'll support the simplest way to mitigate acne, which is by adding a small depth offset when rendering to the shadow map.
		output.positionCS.z += _ShadowBias;
		output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
	#endif
	return output;
}

// We only care about depth information.
// The output of the fragment program is simply zero.
float4 ShadowCasterPassFragment (VertexOutput input) : SV_TARGET
{
	return 0;
}

#endif