#ifndef XRP_INPUTS_INCLUDED
#define XRP_INPUTS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// The VP matrix gets put in a per-frame buffer, while the M matrix gets put in a per-draw buffer.
// To be as efficient as possible, we'll also make use of constant buffers. 
// Unity puts the VP matrix in a UnityPerFrame buffer and the M matrix in a UnityPerDraw buffer.
// Because constant buffers don't benefit all platforms, Unity's shaders rely on macros to only use them when needed.
CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
	// Its Y component contains the number of lights affecting the object. 
	// Its X component contains an offset for when the second approach is used, so we can ignore that.
	float4 unity_LightIndicesOffsetAndCount;
	float4 unity_4LightIndices0, unity_4LightIndices1;
CBUFFER_END

#define UNITY_MATRIX_M unity_ObjectToWorld

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

#define MAX_VISIBLE_LIGHTS 16

CBUFFER_START(_LightBuffer)
	float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHTS];
CBUFFER_END

CBUFFER_START(_ShadowBuffer)
	float4x4 _WorldToShadowMatrices[MAX_VISIBLE_LIGHTS];
	float4x4 _WorldToShadowCascadeMatrices[5];
	float4 _ShadowData[MAX_VISIBLE_LIGHTS];
	float4 _ShadowMapSize;
	float4 _CascadedShadowMapSize;
	float4 _GlobalShadowData;
	float _CascadedShadowStrength;
	float4 _CascadeCullingSpheres[4];
CBUFFER_END

CBUFFER_START(UnityPerCamera)
	float3 _WorldSpaceCameraPos;
CBUFFER_END

// There is only a difference for OpenGL ES 2.0, because it doesn't support depth comparisons for shadow maps. 
// But we don't support OpenGL ES 2.0, so we could've used TEXTURE2D instead. 
// I used TEXTURE2D_SHADOW anyway to make it abundantly clear that we are dealing with shadow data.
TEXTURE2D_SHADOW(_ShadowMap);
// In old GLSL code, we use sampler2D to define both a texture and a sampler state together. 
// But they are two separate things, and both take up resources. 
// Sampler states exist separate from textures, which makes it possible to mix their use, typically reusing the same sampler state to sample from multiple textures.
// The comparison sampler that we're using will perform a depth comparison for us, before bilinear interpolation happens.
SAMPLER_CMP(sampler_ShadowMap);

TEXTURE2D_SHADOW(_CascadedShadowMap);
SAMPLER_CMP(sampler_CascadedShadowMap);

float DistanceToCameraSqr (float3 worldPos)
{
	float3 cameraToFragment = worldPos - _WorldSpaceCameraPos;
	return dot(cameraToFragment, cameraToFragment);
}

#endif