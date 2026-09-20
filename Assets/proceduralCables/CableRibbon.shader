Shader "Pincushioned/CableRibbon"
{
    Properties
    {
        _HalfWidth   ("Half Width", Float) = 0.05
        _BraidTiling ("Braid Tiling", Float) = 8.0
        _BraidDepth  ("Braid Depth", Range(0,1)) = 0.35
        _Smoothness  ("Smoothness", Range(0,1)) = 0.45
        _Metallic    ("Metallic", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // A cable is thin enough that backface culling buys nothing, and
        // leaving it off means the ribbon's winding can never flip it invisible.
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _HalfWidth;
                float _BraidTiling;
                float _BraidDepth;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 tangentOS  : NORMAL;   // repurposed: along-cable direction
                float2 uv         : TEXCOORD0; // x = world length * tiling, y = 0/1 side
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 tangentWS  : TEXCOORD1;
                float3 sideWS     : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float4 color      : COLOR;
                float  fogCoord   : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 nodeWS    = TransformObjectToWorld(IN.positionOS.xyz);
                float3 tangentWS = normalize(TransformObjectToWorldDir(IN.tangentOS));

                // Billboard here, not on the CPU. One mesh build must serve
                // every split-screen camera correctly.
                float3 toCam = _WorldSpaceCameraPos - nodeWS;
                float  lenSq = dot(toCam, toCam);
                toCam = lenSq > 1e-12 ? toCam * rsqrt(lenSq) : float3(0, 0, 1);

                float3 side  = cross(tangentWS, toCam);
                float  sideSq = dot(side, side);
                // Degenerate when the cable points straight at the camera. Any
                // perpendicular will do there, since the ribbon is edge-on.
                side = sideSq > 1e-8 ? side * rsqrt(sideSq)
                                     : normalize(cross(tangentWS, float3(0, 1, 0)) + float3(1e-4, 0, 0));

                float sideSign = IN.uv.y * 2.0 - 1.0;
                float3 posWS = nodeWS + side * (_HalfWidth * sideSign);

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.tangentWS  = tangentWS;
                OUT.sideWS     = side;
                OUT.uv         = IN.uv;
                OUT.color      = IN.color;
                OUT.fogCoord   = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // The cylinder normal, derived rather than sampled. x is the
                // sideways bow across the ribbon, and sqrt(1-x*x) is the bulge
                // toward the viewer. Smooth at any width, with none of the
                // sRGB / flipGreenChannel import traps a painted strip carries.
                float x = saturate(IN.uv.y) * 2.0 - 1.0;
                float bulge = sqrt(saturate(1.0 - x * x));

                // Procedural braid: a diagonal ridge running along the jacket.
                // Perturbs the bow rather than adding a separate normal, so it
                // reads as moulded into the cable instead of printed on it.
                float braid = sin((IN.uv.x * _BraidTiling + IN.uv.y * 3.0) * 6.2831853);
                float bow = clamp(x + braid * _BraidDepth * 0.25, -1.0, 1.0);

                float3 viewWS = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 faceWS = normalize(cross(IN.sideWS, IN.tangentWS));
                float3 normalWS = normalize(IN.sideWS * bow + faceWS * bulge);
                if (dot(normalWS, viewWS) < 0.0) normalWS = -normalWS; // Cull Off: light the side we see

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogCoord;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.bakedGI = SampleSH(normalWS);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = IN.color.rgb;
                surfaceData.alpha = 1.0;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0;
                surfaceData.normalTS = float3(0, 0, 1);

                half4 col = UniversalFragmentPBR(inputData, surfaceData);
                col.rgb = MixFog(col.rgb, IN.fogCoord);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _HalfWidth;
                float _BraidTiling;
                float _BraidDepth;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            float3 _LightDirection;

            struct SAttributes { float4 positionOS : POSITION; float3 tangentOS : NORMAL; float2 uv : TEXCOORD0; };

            float4 shadowVert(SAttributes IN) : SV_POSITION
            {
                // The shadow pass must widen the ribbon the same way the lit
                // pass does, or cables cast hairline shadows that do not match
                // what is on screen. The shadow camera is the camera here.
                float3 nodeWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 tangentWS = normalize(TransformObjectToWorldDir(IN.tangentOS));

                float3 toCam = _WorldSpaceCameraPos - nodeWS;
                float lenSq = dot(toCam, toCam);
                toCam = lenSq > 1e-12 ? toCam * rsqrt(lenSq) : float3(0, 0, 1);

                float3 side = cross(tangentWS, toCam);
                float sideSq = dot(side, side);
                side = sideSq > 1e-8 ? side * rsqrt(sideSq) : float3(1, 0, 0);

                float3 posWS = nodeWS + side * (_HalfWidth * (IN.uv.y * 2.0 - 1.0));
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, side, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 shadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
