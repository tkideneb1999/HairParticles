#ifndef HAIR_TESSELLATION_SHADER_INCLUDED
#define HAIR_TESSELLATION_SHADER_INCLUDED

#include "hashes.hlsl"

// -----------------------------------------------------------
// Vertex Shader
// -----------------------------------------------------------
// loads necessary Data from Structured Buffer
HullIn VertexProgram(uint vertexID : SV_VertexID) {
	HullIn OUT;

	HairParticle h = _Hair[vertexID];

	// Position is already in Worldspace -> Happens in Skinning Compute Shader
	OUT.positionWS = float4(h.position, 1); //float4(mul(hair_ObjectToWorld, float4(h.position, 1)).xyz, 1);
	OUT.hairData = float4(h.factor, h.length, lerp(_MaxRadius, _MinRadius, h.factor), h.curveID);
	OUT.uv = h.uv;
	OUT.tangent = h.tangent;
	OUT.normal = h.normal;
	return OUT;
}

// -----------------------------------------------------------
// Patch Constant Function
// -----------------------------------------------------------
// sets up Hair Amount, Hair Resolution is not yet implemented
TessellationFactors PatchConstantProgram(InputPatch<HullIn, 2> patch)
{
	TessellationFactors f;
	f.edge[0] = _HairAmount;
	f.edge[1] = _HairResolution;
	return f;
}

// -----------------------------------------------------------
// Hull Shader
// -----------------------------------------------------------
[domain("isoline")]
[outputcontrolpoints(2)]
[outputtopology("line")]
[partitioning("integer")]
[patchconstantfunc("PatchConstantProgram")]
HullIn HullProgram(InputPatch<HullIn, 2> patch, uint id: SV_OutputControlPointID)
{
	return patch[id];
}

// -----------------------------------------------------------
// Domain Shader
// -----------------------------------------------------------
// Scatters Hair in interpolation radius
[domain("isoline")]
GeometryIn DomainProgram(TessellationFactors f, OutputPatch<HullIn, 2> patch, float2 baryCoord : SV_DomainLocation)
{
	GeometryIn OUT;
	float tangentLength = (patch[1].hairData.x - patch[0].hairData.y) * patch[0].hairData.y * 0.5;
	OUT.positionWS = lerp(patch[0].positionWS, patch[1].positionWS, baryCoord.x);
	OUT.tangent = lerp(patch[0].tangent, patch[1].tangent, baryCoord.x);
	OUT.uv = lerp(patch[0].uv, patch[1].uv, baryCoord.x);
	OUT.hairData = lerp(patch[0].hairData, patch[1].hairData, baryCoord.x);
	float3 inNormal = normalize(lerp(patch[0].normal, patch[1].normal, baryCoord.x));

	float2 startRandom = hash21(baryCoord.y * _HairAmount);
	float2 endRandom = hash21(baryCoord.y * _HairAmount + _HairAmount);
	float2 randomLerp = lerp(startRandom, endRandom, OUT.hairData.x);
	float2 offsetAmount = _InterpolationDistance * (2 * startRandom - 1);
	float3 spanVector = normalize(float3(OUT.uv, OUT.hairData.w));
	float3 normal = cross(OUT.tangent, inNormal);
	float3 bitangent = cross(OUT.tangent, normal);
	float clumping = lerp(1, pow(saturate(1 - OUT.hairData.x), _ClumpShape), _Clumping);
	float3 offset = (bitangent * offsetAmount.x + normal * offsetAmount.y) * clumping;

	// Tangent Offset for Clumping
	float tangentOffsetAmount = _ClumpShape * pow(saturate(1 - OUT.hairData.x), _ClumpShape - 1) * (1 - _Clumping);
	//OUT.tangent = normalize(OUT.tangent + normalize(offset) * clumping);

	OUT.positionWS.xyz += offset;
	OUT.shadowCoord = TransformWorldToShadowCoord(OUT.positionWS.xyz);

	return OUT;
}

#endif
