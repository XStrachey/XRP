#ifndef XRP_LIGHTING_INCLUDED
#define XRP_LIGHTING_INCLUDED

#include "Inputs.hlsl"
#include "Shadows.hlsl"

half3 DiffuseLight (int index, float3 positionWS, float3 normal, float shadowAttenuation)
{
	float3 lightColor = _VisibleLightColors[index].rgb;
	float4 lightAttenuation = _VisibleLightAttenuations[index];
	float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
	float3 spotDirection = _VisibleLightSpotDirections[index].xyz;

	// That works for point lights, but is nonsensical for directional lights. 
	// We can support both with the same calculation, by multiplying the world position with the W component of the light's direction or position vector.
	float3 lightVector = lightPositionOrDirection.xyz - positionWS * lightPositionOrDirection.w;
	float3 lightDirection = normalize(lightVector);

	float diffuse = saturate(dot(normal, lightDirection));

	float rangeFade = dot(lightVector, lightVector) * lightAttenuation.x;
	rangeFade = saturate(1.0 - rangeFade * rangeFade);
	rangeFade *= rangeFade;

	float spotFade = dot(spotDirection, lightDirection);
	spotFade = saturate(spotFade * lightAttenuation.z + lightAttenuation.w);
	spotFade *= spotFade;
	
	// Doesn't that increase the intensity very close to point lights?
	// Indeed, when distance is less than 1 a light's intensity goes up. 
	// When distance approaches its minimum the intensity becomes enormous.
	float distanceSqr = max(dot(lightVector, lightVector), 0.00001);
	diffuse *= shadowAttenuation * spotFade * rangeFade / distanceSqr;
	return diffuse * lightColor;
}

// add a separate MainLight function to take care of the main light. 
// It does the same as DiffuseLight, but limited to only the directional light at index zero, relying on CascadedShadowAttenuation for its shadows.
float3 MainLight (float3 normal, float3 worldPos)
{
	float shadowAttenuation = CascadedShadowAttenuation(worldPos);
	float3 lightColor = _VisibleLightColors[0].rgb;
	float3 lightDirection = _VisibleLightDirectionsOrPositions[0].xyz;
	float diffuse = saturate(dot(normal, lightDirection));
	diffuse *= shadowAttenuation;
	return diffuse * lightColor;
}

#endif