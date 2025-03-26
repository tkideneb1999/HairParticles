#ifndef HAIR_GEO_GENERATION_INCLUDED
#define HAIR_GEO_GENERATION_INCLUDED

[maxvertexcount(6)]
void GeometryProgram(line GeometryIn IN[2], inout TriangleStream<FragmentIn> outputStream)
{
	float3 pos1 = IN[0].positionWS.xyz;
	float3 pos2 = IN[1].positionWS.xyz;

#ifdef HAIR_SHADOWPASS
	float3 bitangent1 = cross(IN[0].tangent, normalize(_HairInvLightDirection));
	float3 bitangent2 = cross(IN[1].tangent, normalize(_HairInvLightDirection));
#else
	float3 bitangent1 = cross(IN[0].tangent, normalize(_WorldSpaceCameraPos - pos1.xyz));
	float3 bitangent2 = cross(IN[1].tangent, normalize(_WorldSpaceCameraPos - pos2.xyz));
#endif

	float3 normal1 = cross(bitangent1, IN[0].tangent);
	float3 normal2 = cross(bitangent2, IN[1].tangent);

#ifdef HAIR_DEPTHPREPASS
	float offset0 = IN[0].hairData.z + 0.005f;
	float offset1 = IN[1].hairData.z + 0.005f;
#else
	float offset0 = IN[0].hairData.z;
	float offset1 = IN[1].hairData.z;
#endif

	FragmentIn p1;
	p1.positionWS = pos1.xyz + bitangent1 * offset0;
	p1.positionCS = TransformWorldToHClip(p1.positionWS);
	p1.uv = IN[0].uv;
	p1.normal = normal1;
	p1.tangent = IN[0].tangent;
	p1.shadowCoord = IN[0].shadowCoord;
	p1.hairData = IN[0].hairData.xyw;

	FragmentIn p2;
	p2.positionWS = pos2.xyz + bitangent2 * offset1;
	p2.positionCS = TransformWorldToHClip(p2.positionWS);
	p2.uv = IN[0].uv;
	p2.normal = normal2;
	p2.tangent = IN[1].tangent;
	p2.shadowCoord = IN[1].shadowCoord;
	p2.hairData = IN[1].hairData.xyw;

	FragmentIn p3;
	p3.positionWS = pos2.xyz - bitangent2 * offset1;
	p3.positionCS = TransformWorldToHClip(p3.positionWS);
	p3.uv = IN[0].uv;
	p3.normal = normal2;
	p3.tangent = IN[1].tangent;
	p3.shadowCoord = IN[1].shadowCoord;
	p3.hairData = IN[1].hairData.xyw;

	FragmentIn p4;
	p4.positionWS = pos1.xyz - bitangent1 * offset0;
	p4.positionCS = TransformWorldToHClip(p4.positionWS);
	p4.uv = IN[0].uv;
	p4.normal = normal1;
	p4.tangent = IN[0].tangent;
	p4.shadowCoord = IN[0].shadowCoord;
	p4.hairData = IN[0].hairData.xyw;

#ifndef HAIR_SHADOWPASS
	p1.positionSSS = mul(_HairSelfShadowVPMatrix, float4(p1.positionWS, 1.0f));
	p2.positionSSS = mul(_HairSelfShadowVPMatrix, float4(p2.positionWS, 1.0f));
	p3.positionSSS = mul(_HairSelfShadowVPMatrix, float4(p3.positionWS, 1.0f));
	p4.positionSSS = mul(_HairSelfShadowVPMatrix, float4(p4.positionWS, 1.0f));
#endif

	outputStream.Append(p1);
	outputStream.Append(p2);
	outputStream.Append(p3);
	outputStream.RestartStrip();
	outputStream.Append(p1);
	outputStream.Append(p3);
	outputStream.Append(p4);
}

#endif