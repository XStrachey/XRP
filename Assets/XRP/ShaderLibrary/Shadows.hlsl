#ifndef XRP_SHADOWS_INCLUDED
#define XRP_SHADOWS_INCLUDED

#include "Inputs.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

// Let's add a convenient method to check whether a world position falls inside a given culling sphere, using its index as a parameter. 
// We'll do some math with the result later, so return it as a float.
float InsideCascadeCullingSphere (int index, float3 worldPos)
{
	float4 s = _CascadeCullingSpheres[index];
	return dot(worldPos - s.xyz, worldPos - s.xyz) < s.w;
}

// Let's make HardShadowAttenuation work with both maps by adding a boolean parameter to indicate whether we want to sample cascades, with false being the default. 
// Use that to decide which texture and sampler to use. We'll hard-code the cascade argument, so it won't result in shader branches.
float HardShadowAttenuation (float4 shadowPos, bool cascade = false)
{
	if (cascade)
	{
		return SAMPLE_TEXTURE2D_SHADOW(_CascadedShadowMap, sampler_CascadedShadowMap, shadowPos.xyz);
	}
	else
	{
		return SAMPLE_TEXTURE2D_SHADOW(_ShadowMap, sampler_ShadowMap, shadowPos.xyz);
	}
}

float SoftShadowAttenuation (float4 shadowPos, bool cascade = false)
{
	// Instead of a single sample, we'll have to accumulate nine samples to create the 5×5 tent filter. 
	// The SampleShadow_ComputeSamples_Tent_5x5 function gives us the weights and UV coordinates to use, 
	// by passing the shadow map size and the XY coordinates of the shadow position as arguments. 
	// The weights and UV are provided via two output parameters, a float array and a float2 array, both with nine elements.
	real tentWeights[9];
	real2 tentUVs[9];
	float4 size = cascade ? _CascadedShadowMapSize : _ShadowMapSize;
	SampleShadow_ComputeSamples_Tent_5x5(size, shadowPos.xy, tentWeights, tentUVs);
	float attenuation = 0;
	for (int i = 0; i < 9; ++i) 
	{
		attenuation += tentWeights[i] * HardShadowAttenuation(float4(tentUVs[i].xy, shadowPos.z, 0), cascade);
	}
	return attenuation;
}

float ShadowAttenuation (int index, float3 positionWS)
{
	// The first thing it needs to do is convert the world position to the shadow position.
	float4 shadowPos = mul(_WorldToShadowMatrices[index], float4(positionWS, 1.0));
	// The resulting position is defined with homogeneous coordinates, just like when we convert to clip space. 
	// But we need regular coordinates, so divide XYZ components by its W component.
	shadowPos.xyz /= shadowPos.w;

	// In ShadowAttenuation, clamp the XY coordinates of the shadow position after the perspective division. After that, apply the tile transformation.
	shadowPos.xy = saturate(shadowPos.xy);
	shadowPos.xy = shadowPos.xy * _GlobalShadowData.x + _ShadowData[index].zw;

	float attenuation;
	// The result is 1 when the position's Z value is less than what's stored in the shadow map, meaning that it is closer to the light than whatever's casting a shadow. 
	// Otherwise, it is behind a shadow caster and the result is zero. 
	// Because the sampler performs the comparison before bilinear interpolation, the edges of shadows will blend across shadow map texels.
	if (_ShadowData[index].y == 0)
	{
		attenuation = HardShadowAttenuation(shadowPos);
	}
	else
	{
		attenuation = SoftShadowAttenuation(shadowPos);
	}

	// Check whether we're beyond the shadow distance, and if so skip sampling shadows.
	if (_ShadowData[index].x <= 0 || DistanceToCameraSqr(positionWS) > _GlobalShadowData.y)
	{
		return 1.0;
	}

	// Add the shadow strength to the shadow buffer, then use it to interpolate between 1 and the sampled attenuation in ShadowAttenuation.
	return lerp(1, attenuation, _ShadowData[index].x);
}

float CascadedShadowAttenuation (float3 worldPos)
{
	// If there are no cascades, the attenuation is always 1.
	#if !defined(_CASCADED_SHADOWS_HARD) && !defined(_CASCADED_SHADOWS_SOFT)
		return 1.0;
	#endif

	if (DistanceToCameraSqr(worldPos) > _GlobalShadowData.y)
	{
		return 1.0;
	}

	// Invoke that function in CascadedShadowAttenuation for all four culling spheres. 
	// For each sphere, the result is 1 when the sphere encompasses the point and zero otherwise. 
	// These values serve as flags to indicate which spheres are valid. Put them in an ordered float4 variable before determining the cascade index.
	float4 cascadeFlags = float4(
		InsideCascadeCullingSphere(0, worldPos),
		InsideCascadeCullingSphere(1, worldPos),
		InsideCascadeCullingSphere(2, worldPos),
		InsideCascadeCullingSphere(3, worldPos)
	);
	// We want to use the first cascade that is valid, so we have to clear all the flags after the first one that's set. 
	// The first flag is always good, but the second should be cleared if the first one is set. 
	// And the third should be cleared when the second is set; likewise for the fourth. 
	// We can do that by subtracting the XYZ components from YZW and saturating the result.
	cascadeFlags.yzw = saturate(cascadeFlags.yzw - cascadeFlags.xyz);
	float cascadeIndex = 4 - dot(cascadeFlags, float4(4, 3, 2, 1));
	
	float4 shadowPos = mul(_WorldToShadowCascadeMatrices[cascadeIndex], float4(worldPos, 1.0));
	float attenuation;
	// Otherwise, compute the shadow position and retrieve either the hard or soft shadow attenuation and apply the shadow strength.
	#if defined(_CASCADED_SHADOWS_HARD)
		attenuation = HardShadowAttenuation(shadowPos, true);
	#else
		attenuation = SoftShadowAttenuation(shadowPos, true);
	#endif
	
	return lerp(1, attenuation, _CascadedShadowStrength);
}

#endif