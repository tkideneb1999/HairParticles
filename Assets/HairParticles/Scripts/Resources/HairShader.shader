Shader "Custom/Hair" {
	Properties
	{
		_DiffuseColor("Diffuse Color", Color) = (0.9, 0.9, 0.9, 1)
		_OmbreColor("Ombre Color", Color) = (0.9, 0.9, 0.9, 1)
		_OmbreStart("Ombre Start", float) = 0.5
		_OmbreFadeLength("Ombre Fade Length", float) = 0.25
		_SpecColor1("Primary Specular Color", Color) = (0, 0.66, 0.73, 1)
		_SpecShift1("Primary Specular Shift", float) = 0.0
		_SpecColor2("Secondary Specular Color", color) = (0.1, 0.1, 0.1, 1)
		_SpecShift2("Secondary Specular Shift", float) = 0.1
		_SpecPower("Specular Power", float) = 1
		_AOStrength("AO Strength", Range(0, 1)) = 0.5
		_StrandRandomRange("Strand Randomness Range", Range(0, 1)) = 0.1
		_MinRadius("Minimum Hair Radius", float) = 0.005
		_MaxRadius("Maximum Hair Radius", float) = 0.01
		_InterpolationDistance("InterpolationDistance", float) = 0.2
		_HairAmount("Hair Amount", float) = 10
		_HairResolution("Hair Resolution", float) = 2
		_Clumping("Clumping Amount", Range(0, 1)) = 0.5
		_ClumpShape("Clump Shape", Range(0.01, 4)) = 2
		_HairOpacityLayerDistance("Shadows Layer Distance", Range(-0.5, 0.5)) = 0.5
		_HairOpacityLayerOffset("Shadow Layer Offset", Range(0.0, 1.0)) = 0.0
	}

	SubShader{
		Tags {
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Opaque"
			"Queue" = "Geometry"
		}

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "hairRendering.hlsl"

		struct HairParticle 
		{
			int ID;
			int parentID;
			int curveID;
			float3 position;
			float3 tangent;
			float3 normal;
			float2 uv;
			float length;
			float factor;
		};

		
		float _HairShadowResolution;
		float4x4 _HairSelfShadowVPMatrix;
		float4 _HairShadowProjectionParams;

		CBUFFER_START(UnityPerMaterial)
		StructuredBuffer<HairParticle> _Hair;
		float4x4 hair_ObjectToWorld;

		half4 _DiffuseColor;
		half4 _OmbreColor;
		half _OmbreStart;
		half _OmbreFadeLength;
		half4 _SpecColor1;
		half _SpecShift1;
		half4 _SpecColor2;
		half _SpecShift2;
		float _SpecPower;
		half _StrandRandomRange;
		half _AOStrength;

		float _MinRadius;
		float _MaxRadius;

		float _InterpolationDistance;
		int _HairAmount;
		int _HairResolution;
		float _Clumping;
		float _ClumpShape;

		float _HairOpacityLayerDistance;
		float _HairOpacityLayerOffset;

		CBUFFER_END
		ENDHLSL

		// Main Pass
		Pass
		{
			Name "ForwardLit"

			HLSLPROGRAM
			#pragma require geometry
			#pragma vertex VertexProgram
			#pragma hull HullProgram
			#pragma domain DomainProgram
			#pragma fragment FragmentProgram
			#pragma geometry GeometryProgram
			#pragma target 4.6

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

			TEXTURE2D(_HairShadowMap);
			SAMPLER(sampler_HairShadowMap);

			TEXTURE2D(_HairShadowDepthMap);
			SAMPLER(sampler_HairShadowDepthMap);

			#include "hairInputs.hlsl"
			#include "hairTessellation.hlsl"
			#include "hairGeoGeneration.hlsl"		

			// Fragment Shader
			half4 FragmentProgram(FragmentIn IN) : SV_Target
			{
				float3 projCoords = IN.positionSSS.xyz / IN.positionSSS.w;
				float2 depthSampleCoords = projCoords.xy * 0.5f + 0.5f;
				//return half4(depthSampleCoords - 0.5f, 0, 1);
				float lightDepth = 1 - SAMPLE_TEXTURE2D(_HairShadowDepthMap, sampler_HairShadowDepthMap, depthSampleCoords).r;
				//return lightDepth;
				float layerDistance = _HairOpacityLayerDistance;// *_HairShadowProjectionParams.z - _HairShadowProjectionParams.x;
				int index = min((int((projCoords.z - lightDepth) / layerDistance) + 1), 3);// +1;
				// return projCoords.z - lightDepth;
				float opacityArray[] = { 0, 0, 0, 0 };
				opacityArray[index] = 1.0f;
				float4 opacity = float4(
					opacityArray[1],
					opacityArray[2],
					opacityArray[3],
					1);
				// return opacity;

				float3 tangent = normalize(IN.tangent);
				float3 normal = normalize(IN.normal);
				float3 viewDirWS = GetWorldSpaceViewDir(IN.positionWS);
				Light mainLight = GetMainLight();
				half4 mainLightColor = half4(mainLight.color, 1);

				half ombreGradient = saturate((IN.hairData.x * IN.hairData.y - (_OmbreStart - 0.5 * _OmbreFadeLength)) / _OmbreFadeLength);
				half4 baseColor = lerp(_DiffuseColor, _OmbreColor, ombreGradient);
				half4 diffuse = CalcKajiyaKayDiffuse(baseColor, mainLight.direction, tangent);
				half4 spec = CalcKajiyaKaySpec(_SpecColor1, _SpecColor2,
					_SpecShift1, _SpecShift2, _SpecPower,
					tangent, normal, viewDirWS, mainLight.direction,
					0
					);
				half4 finalColor = saturate(diffuse + spec) * mainLightColor;
				half shadowSample = lerp(1 - _AOStrength, 1, saturate(MainLightRealtimeShadow(IN.shadowCoord)));
				// finalColor *= shadowSample;
				finalColor.a = 1;
				return finalColor;
			}
			ENDHLSL
		}
		// ShadowPass
		Pass
		{
			Name "ShadowCaster"
			Tags { "Lightmode"="ShadowCaster"}

			HLSLPROGRAM

			#pragma require geometry
			#pragma vertex VertexProgram
			#pragma geometry GeometryProgram
			#pragma fragment FragmentProgram

			struct GeometryIn {
				float4 positionWS   : POSITION;
				float3 tangent      : TANGENT;
				half2 uv           : TEXCOORD0;
			};
			
			struct FragmentIn {
				float4 positionCS   : SV_POSITION;
			};


			GeometryIn VertexProgram(uint vertexID : SV_VertexID) {
				GeometryIn OUT;

				HairParticle h = _Hair[vertexID];
				OUT.positionWS = float4(h.position, 1);
				float clumping = lerp(1, pow(saturate(1 - h.factor), _ClumpShape), _Clumping);
				OUT.uv = float2(_InterpolationDistance * clumping, 0.5);
				OUT.tangent = h.tangent;
				return OUT;
			}

			//Geometry
			[maxvertexcount(6)]
			void GeometryProgram(line GeometryIn IN[2], inout TriangleStream<FragmentIn> outputStream)
			{
				float3 pos1 = IN[0].positionWS.xyz;
				float3 pos2 = IN[1].positionWS.xyz;

				Light mainLight = GetMainLight();

				float3 bitangent1 = cross(IN[0].tangent, normalize(mainLight.direction));
				float3 bitangent2 = cross(IN[1].tangent, normalize(mainLight.direction));

				float3 normal1 = cross(bitangent1, IN[0].tangent);
				float3 normal2 = cross(bitangent2, IN[1].tangent);

				FragmentIn p1;
				p1.positionCS = TransformWorldToHClip(pos1.xyz + bitangent1 * IN[0].uv.x);

				FragmentIn p2;
				p2.positionCS = TransformWorldToHClip(pos2.xyz + bitangent2 * IN[1].uv.x);

				FragmentIn p3;
				p3.positionCS = TransformWorldToHClip(pos2.xyz - bitangent2 * IN[1].uv.x);

				FragmentIn p4;
				p4.positionCS = TransformWorldToHClip(pos1.xyz - bitangent1 * IN[0].uv.x);

				outputStream.Append(p1);
				outputStream.Append(p2);
				outputStream.Append(p3);
				outputStream.RestartStrip();
				outputStream.Append(p1);
				outputStream.Append(p3);
				outputStream.Append(p4);
			}

			half4 FragmentProgram(FragmentIn IN) : SV_Target
			{
				return half4(1,1,1,1);
			}

				ENDHLSL
		}
		// Depth Prepass
		Pass
		{
			Name "ShadowDepthPrepass"

			HLSLPROGRAM

			#pragma require geometry
			#pragma vertex VertexProgram
			#pragma hull HullProgram
			#pragma domain DomainProgram
			#pragma fragment FragmentProgram
			#pragma geometry GeometryProgram
			#pragma target 4.6

			float3 _HairInvLightDirection;

			#define HAIR_SHADOWPASS
			#define HAIR_DEPTHPREPASS

			#include "hairInputs.hlsl"
			#include "hairTessellation.hlsl"
			#include "hairGeoGeneration.hlsl"

			half FragmentProgram(FragmentIn IN) : SV_Target
			{
				//float2 xy = int2((_HairShadowResolution - IN.positionCS.xy - 256) / 64) * 64 / _HairShadowResolution;
				//return xy.x * xy.y;
				return 1 - IN.positionCS.z;
			}

			ENDHLSL
		}

		// Hair Self Shadow Pass
		Pass
		{
			Name "SelfShadowPass"
			Blend One One
			ZWrite Off
			ZTest Always

			HLSLPROGRAM

			#pragma require geometry
			#pragma vertex VertexProgram
			#pragma hull HullProgram
			#pragma domain DomainProgram
			#pragma fragment FragmentProgram
			#pragma geometry GeometryProgram
			#pragma target 4.6

			float3 _HairInvLightDirection;

			TEXTURE2D(_HairShadowDepthMap);
			SAMPLER(sampler_HairShadowDepthMap);

			#define HAIR_SHADOWPASS

			#include "hairInputs.hlsl"
			#include "hairTessellation.hlsl"
			#include "hairGeoGeneration.hlsl"

			half4 FragmentProgram(FragmentIn IN) : SV_Target
			{
				// Light Space Coordinates for Depth Texture
				float lightDepth = SAMPLE_TEXTURE2D(_HairShadowDepthMap, sampler_HairShadowDepthMap, IN.positionCS.xy / _HairShadowResolution).r;
				// convert positionCS.z to index
			float layerDistance = _HairOpacityLayerDistance;// *_HairShadowProjectionParams.z - _HairShadowProjectionParams.x;
				int index = min(int((IN.positionCS.z - lightDepth) / layerDistance) + 1, 3);
				float opacityArray[] = {0, 0, 0, 0};
				opacityArray[index] = 0.01f;
				float4 opacity = float4(
					opacityArray[0],
					opacityArray[1],
					opacityArray[2],
					opacityArray[3]);
				// if IN.positionCS.z > lightDepth && < lightDepth + _HairOpacityLayerDistance
				// --> return float4(0, 0.01f, 0, 0)
				// if IN.positionCS.z > lightDepth + _HairOpacityLayerDistance && < lightDepth + _HairOpacityLayerDistance * 2
				// --> return float4(0, 0, 0.01f, 0)
				// if IN.positionCS.z > lightDepth + _HairOpacityLayerDistance * 2 && < lightDepth + _HairOpacityLayerDistance * 3
				// --> return float4(0, 0, 0.01f, 0)
				return opacity;
			}

			ENDHLSL
		}
	}
}