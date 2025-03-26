#ifndef HAIR_INPUTS_INCLUDED
#define HAIR_INPUTS_INCLUDED

struct HullIn {
	float4 positionWS       : POSITION;
	float3 tangent          : TANGENT;
	float3 normal           : NORMAL;
	float2 uv               : TEXCOORD0;
	float4 hairData         : TEXCOORD1;
};

struct TessellationFactors {
	float edge[2]           : SV_TessFactor;
};

struct GeometryIn {
	float4 positionWS   : POSITION;
	float3 tangent      : TANGENT;
	float2 uv           : TEXCOORD0;
	float4 shadowCoord  : TEXCOORD1;
	float4 hairData     : TEXCOORD2; // factor, length, radius, per strand randomness
};

struct FragmentIn {
	float4 positionCS 	: SV_POSITION;
	float3 positionWS   : TEXCOORD1;
#ifndef HAIR_SHADOWPASS
	float4 positionSSS  : TEXCOORD4;
#endif
	float2 uv		    : TEXCOORD0;
	float3 normal       : NORMAL;
	float3 tangent      : TANGENT;
	float4 shadowCoord  : TEXCOORD2;
	float3 hairData     : TEXCOORD3; // factor, length, strand ID
};

#endif