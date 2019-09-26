﻿// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

Shader "Crest/Underwater/Post Process"
{
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#pragma shader_feature _SUBSURFACESCATTERING_ON
			#pragma shader_feature _SUBSURFACESHALLOWCOLOUR_ON
			#pragma shader_feature _TRANSPARENCY_ON
			#pragma shader_feature _CAUSTICS_ON
			#pragma shader_feature _SHADOWS_ON
			#pragma shader_feature _COMPILESHADERWITHDEBUGINFO_ON
			#pragma shader_feature _MENISCUS_ON

			#pragma multi_compile __ _FULL_SCREEN_EFFECT
			#pragma multi_compile __ _DEBUG_VIEW_OCEAN_MASK

			#if _COMPILESHADERWITHDEBUGINFO_ON
			#pragma enable_d3d11_debug_symbols
			#endif

			#include "UnityCG.cginc"
			#include "Lighting.cginc"

			#include "../OceanConstants.hlsl"
			#include "../OceanLODData.hlsl"

			float _CrestTime;
			half3 _AmbientLighting;

			#include "../OceanEmission.hlsl"

			float _OceanHeight;
			float4x4 _InvViewProjection;
			float4x4 _InvViewProjectionRight;

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			Varyings Vert (Attributes input)
			{
				Varyings output;
				output.positionCS = UnityObjectToClipPos(input.positionOS);
				output.uv = input.uv;
				return output;
			}

			sampler2D _MainTex;
			sampler2D _MaskTex;
			sampler2D _MaskDepthTex;

			// In-built Unity textures
			sampler2D _CameraDepthTexture;
			sampler2D _Normals;

			half3 ApplyUnderwaterEffect(half3 sceneColour, const float sceneZ01, const half3 view, bool isOceanSurface)
			{
				const float sceneZ = LinearEyeDepth(sceneZ01);
				const float3 lightDir = _WorldSpaceLightPos0.xyz;

				float3 surfaceAboveCamPosWorld = 0.0;
				half3 scatterCol = 0.0;
				{
					half sss = 0.0;
					const float3 uv_slice = WorldToUV(_WorldSpaceCameraPos.xz);
					SampleDisplacements(_LD_TexArray_AnimatedWaves, uv_slice, 1.0, surfaceAboveCamPosWorld, sss);
					surfaceAboveCamPosWorld.y += _OceanCenterPosWorld.y;

					// depth and shadow are computed in ScatterColour when underwater==true, using the LOD1 texture.
					const float depth = 0.0;
					const half shadow = 1.0;

					scatterCol = ScatterColour(surfaceAboveCamPosWorld, depth, _WorldSpaceCameraPos, lightDir, view, shadow, true, true, sss);
				}

#if _CAUSTICS_ON
				if (sceneZ01 != 0.0 && !isOceanSurface)
				{
					ApplyCaustics(view, lightDir, sceneZ, _Normals, true, sceneColour);
				}
#endif // _CAUSTICS_ON

				return lerp(sceneColour, scatterCol, 1.0 - exp(-_DepthFogDensity.xyz * sceneZ));
			}

			fixed4 Frag (Varyings input) : SV_Target
			{
				float3 viewWS;
				float farPlanePixelHeight;
				{
					// We calculate these values in the pixel shader as
					// calculating them in the vertex shader results in
					// precision errors.
					const float2 pixelCS = input.uv * 2 - float2(1.0, 1.0);
#if UNITY_SINGLE_PASS_STEREO || UNITY_STEREO_INSTANCING_ENABLED || UNITY_STEREO_MULTIVIEW_ENABLED
					const float4x4 InvViewProjection = unity_StereoEyeIndex == 0 ? _InvViewProjection : _InvViewProjectionRight;
#else
					const float4x4 InvViewProjection = _InvViewProjection;
#endif
					const float4 pixelWS_H = mul(InvViewProjection, float4(pixelCS, 1.0, 1.0));
					const float3 pixelWS = pixelWS_H.xyz / pixelWS_H.w;
					viewWS = _WorldSpaceCameraPos - pixelWS;
					farPlanePixelHeight = pixelWS.y;
				}

				#if !_FULL_SCREEN_EFFECT
				const bool isBelowHorizon = (farPlanePixelHeight <= _OceanHeight);
				#else
				const bool isBelowHorizon = true;
				#endif

				const float2 uvScreenSpace = UnityStereoTransformScreenSpaceTex(input.uv);
				half3 sceneColour = tex2D(_MainTex, uvScreenSpace).rgb;

				float sceneZ01 = tex2D(_CameraDepthTexture, uvScreenSpace).x;

				float mask = tex2D(_MaskTex, uvScreenSpace).x;
				const float oceanDepth01 = tex2D(_MaskDepthTex, uvScreenSpace);
				bool isOceanSurface = mask != UNDERWATER_MASK_NO_MASK && (sceneZ01 < oceanDepth01);
				bool isUnderwater = mask == UNDERWATER_MASK_WATER_SURFACE_BELOW || (isBelowHorizon && mask != UNDERWATER_MASK_WATER_SURFACE_ABOVE);
				sceneZ01 = isOceanSurface ? oceanDepth01 : sceneZ01;

				float wt = 1.0;

#if _MENISCUS_ON
				float wt1 = 0.8, wt2 = 0.6, wt3 = 0.8;

				// Detect water to no water transitions which happen if mask values on below pixels are less than this mask
				//if (mask <= 1.0)
				{
					float4 dy = float4(0.0, -1.0, -2.0, -3.0) / _ScreenParams.y;
					wt *= (tex2D(_MaskTex, uvScreenSpace + dy.xy).x > mask) ? wt1 : 1.0;
					wt *= (tex2D(_MaskTex, uvScreenSpace + dy.xz).x > mask) ? wt2 : 1.0;
					wt *= (tex2D(_MaskTex, uvScreenSpace + dy.xw).x > mask) ? wt3 : 1.0;
				}
#endif // _MENISCUS_ON

#if _DEBUG_VIEW_OCEAN_MASK
				if(!isOceanSurface)
				{
					return float4(sceneColour * float3(isUnderwater * 0.5, (1.0 - isUnderwater) * 0.5, 1.0), 1.0);
				}
				else
				{
					return float4(sceneColour * float3(mask == UNDERWATER_MASK_WATER_SURFACE_ABOVE, mask == UNDERWATER_MASK_WATER_SURFACE_BELOW, 0.0), 1.0);
				}
#else
				if(isUnderwater)
				{
					const half3 view = normalize(viewWS);
					sceneColour = ApplyUnderwaterEffect(sceneColour, sceneZ01, view, isOceanSurface);
				}

				return half4(wt * sceneColour, 1.0);
#endif // _DEBUG_VIEW_OCEAN_MASK
			}
			ENDCG
		}
	}
}