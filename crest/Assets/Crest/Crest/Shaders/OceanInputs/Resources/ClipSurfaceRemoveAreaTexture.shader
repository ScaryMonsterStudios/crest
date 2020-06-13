﻿// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// Renders the R channel from the provided texture to the clip surface texture.

Shader "Crest/Inputs/Clip Surface/Remove Area Texture"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
	}

	SubShader
	{
		Pass
		{
			Blend One One
			ZWrite Off
			ColorMask R

			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#include "UnityCG.cginc"

			sampler2D _MainTex;

			CBUFFER_START(CrestPerOceanInput)
			float4 _MainTex_ST;
			float3 _DisplacementAtInputPosition;
			CBUFFER_END

			struct Attributes
			{
				float3 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			Varyings Vert(Attributes input)
			{
				Varyings o;

				float3 positionWS = mul(unity_ObjectToWorld, float4(input.positionOS, 1.0)).xyz;
				// Correct for displacement
				positionWS.xz -= _DisplacementAtInputPosition.xz;
				o.positionCS = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));

				o.uv = TRANSFORM_TEX(input.uv, _MainTex);

				return o;
			}

			float4 Frag(Varyings input) : SV_Target
			{
				return tex2D(_MainTex, input.uv);
			}

			ENDCG
		}
	}
}
