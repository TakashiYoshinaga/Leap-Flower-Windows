﻿Shader "Hypercube/Unlit"
{
    Properties
    {
		_Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
		[Toggle(ENABLE_SOFTSLICING)] _softSlicingToggle ("Soft Sliced", Float) = 1
        [MaterialEnum(Off,0,Front,1,Back,2)] _Cull ("Cull", Int) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque"  "Queue" = "Geometry"  }
        Cull [_Cull]
        ZWrite On
 
        Pass
        {    
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
         
            #include "UnityCG.cginc"        

			#pragma shader_feature ENABLE_SOFTSLICING
			#pragma multi_compile __ SOFT_SLICING 

 
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
				
            };
 
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
				float4 projPos : TEXCOORD1; //Screen position of pos
            };
 
            sampler2D _MainTex;
            float4 _MainTex_ST;
			fixed4 _Color;		

			float _softPercent;
			half4 _blackPoint;
         
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = mul(UNITY_MATRIX_MVP, v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				//o.projPos = ComputeScreenPos(o.vertex);
				o.projPos = o.vertex;
                return o;
            }
         
            fixed4 frag (v2f i) : SV_Target
            {

	#if defined(SOFT_SLICING) && defined(ENABLE_SOFTSLICING)
				//float d = i.projPos.z;
				float d = i.projPos.z;

				if (UNITY_NEAR_CLIP_VALUE == -1) //OGL will use this.
				{
					d = (d*.5) + .5;  //map  -1 to 1   into  0 to 1
				}


				//return d; //uncomment this to show the raw depth


				//note: if _softPercent == 0  that is the same as hard slice.

				float mask = 1;	
									
				if (d < _softPercent)
					mask *= d / _softPercent; //this is the darkening of the slice near 0 (near)
				else if (d > 1 - _softPercent)
					mask *= 1 - ((d - (1-_softPercent))/_softPercent); //this is the darkening of the slice near 1 (far)

				//return mask;
				return ((tex2D(_MainTex, i.uv) * _Color) + _blackPoint) * mask;  //multiply mask after everything because _blackPoint must be included in there or we will get 'hardness' from non-black blackpoints		
#endif
                return (tex2D(_MainTex, i.uv) * _Color) + _blackPoint;


            }
            ENDCG
        }

				// ------------------------------------------------------------------
		//  Shadow rendering pass
		Pass {
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }
			
			ZWrite On ZTest LEqual

			CGPROGRAM
			#pragma target 3.0
			// TEMPORARY: GLES2.0 temporarily disabled to prevent errors spam on devices without textureCubeLodEXT
			#pragma exclude_renderers gles
			
			// -------------------------------------

			#pragma shader_feature _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
			#pragma multi_compile_shadowcaster

			#pragma vertex vertShadowCaster
			#pragma fragment fragShadowCaster

			#include "UnityStandardShadow.cginc"

			ENDCG
		}
    }
	Fallback "Diffuse"
}
