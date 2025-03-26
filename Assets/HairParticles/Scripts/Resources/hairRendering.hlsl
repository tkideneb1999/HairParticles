#ifndef CUSTOM_HAIR_RENDERING_INCLUDED
#define CUSTOM_HAIR_RENDERING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Kajiya Kay Shading Model
// KAJIYA, JAMES T and KAY, TIMOTHY L. “Rendering Fur with Three Dimensional Textures”

half4 CalcKajiyaKayDiffuse(
	half4 diffuseCol, 
	float3 lightDirWS, 
	float3 tangentWS)
{
	// Diffuse
	float TdotL = 1 - pow(dot(tangentWS, lightDirWS), 2);
	float sinTL = TdotL / sqrt(TdotL);
	return diffuseCol * sinTL;
}

half CalcSingleKKSpec(float3 tangentWS, float3 viewDirWS, float3 lightDirWS, float specStrength)
{
	float TdotL = 1 - pow(dot(tangentWS, lightDirWS), 2);
	float sinTL = TdotL / sqrt(TdotL);

	float TdotV = 1 - pow(dot(tangentWS, viewDirWS), 2);
	float sinTV = TdotV / sqrt(TdotV);
	return pow(max(TdotL * TdotV + sinTL * sinTV, 0), specStrength);
}

half4 CalcKajiyaKaySpec(half4 specCol1, half4 specCol2,
	half specShift1, half specShift2, half specStrength,
	float3 tangentWS, float3 normalWS, float3 viewDirWS, float3 lightDirWS,
	float strandRandomness)
{
	float3 t1 = ShiftTangent(tangentWS, normalWS, strandRandomness + specShift1);
	float3 t2 = ShiftTangent(tangentWS, normalWS, strandRandomness + specShift2);

	half4 spec = specCol1 * CalcSingleKKSpec(t1, viewDirWS, lightDirWS, specStrength);
	spec += specCol2 * CalcSingleKKSpec(t2, viewDirWS, lightDirWS, specStrength);

	return saturate(spec);
}

float3 Bezier3(float3 p1, float3 t1, float3 p2, float3 t2, float t)
{
	float3 p1c = p1 + t1;
	float3 p2c = p2 + t2;

	return (pow(1 - t, 3) * p1) + (3 * pow(1 - t, 2) * t * p1c) + (3 * (1 - t) * pow(t, 2) * p2c) + (pow(t, 3) * p2);
}

#endif