// Backdrop instance shading: toon, fresnel toon, and lit, branched on one
// property. Written by hand rather than as a Shader Graph so it is text - it
// diffs, it reviews, and it can be authored without opening the editor, which is
// the same reason the lattice math is C# instead of a node tree.
//
// Three shading models in one shader rather than three materials because the
// whole field draws in a single instanced call; swapping materials would mean
// three draws and a per-instance branch on which to use. The branch is uniform
// across the field, so it is coherent and effectively free.
Shader "Pincushioned/Backdrop Shading"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.75, 0.73, 0.70, 1)
        [HDR] _EmissionColor("Emission Color", Color) = (1, 1, 1, 1)

        // 0 = Toon, 1 = Fresnel Toon, 2 = Lit. Matches BackdropShadingMode.
        _ShadingMode("Shading Mode", Float) = 0

        _ToonSteps("Toon Steps", Range(2, 8)) = 3
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3
        [HDR] _FresnelColor("Fresnel Color", Color) = (0.2, 0.8, 1, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.35
        _Metallic("Metallic", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float4 _FresnelColor;
                float  _ShadingMode;
                float  _ToonSteps;
                float  _FresnelPower;
                float  _Smoothness;
                float  _Metallic;
            CBUFFER_END

            // Per-instance flash, written as a float array by the instrument.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Flash)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light main = GetMainLight();
                float ndotl = dot(N, main.direction);

                float3 albedo = _BaseColor.rgb;
                float3 col;

                if (_ShadingMode < 0.5)
                {
                    // Toon: quantise the lambert term into flat bands.
                    float steps = max(_ToonSteps, 2.0);
                    float band = ceil(saturate(ndotl * 0.5 + 0.5) * steps) / steps;
                    col = albedo * main.color * band;
                }
                else if (_ShadingMode < 1.5)
                {
                    // Fresnel toon: the same banding, plus a rim that reads the
                    // silhouette. On a field of small objects the rim is most of
                    // what separates one instance from the one behind it.
                    float steps = max(_ToonSteps, 2.0);
                    float band = ceil(saturate(ndotl * 0.5 + 0.5) * steps) / steps;
                    float rim = pow(saturate(1.0 - saturate(dot(N, V))), _FresnelPower);
                    col = albedo * main.color * band + _FresnelColor.rgb * rim;
                }
                else
                {
                    // Lit: hand the surface to URP and let it light normally.
                    InputData inputData = (InputData)0;
                    inputData.positionWS = IN.positionWS;
                    inputData.normalWS = N;
                    inputData.viewDirectionWS = V;
                    inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                    inputData.bakedGI = SampleSH(N);

                    SurfaceData surf = (SurfaceData)0;
                    surf.albedo = albedo;
                    surf.metallic = _Metallic;
                    surf.smoothness = _Smoothness;
                    surf.occlusion = 1.0;
                    surf.alpha = 1.0;

                    col = UniversalFragmentPBR(inputData, surf).rgb;
                }

                // Flash rides on top of every mode, so a pad press reads the same
                // whichever shading model is live.
                float flash = UNITY_ACCESS_INSTANCED_PROP(Props, _Flash);
                col += _EmissionColor.rgb * flash;

                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Depth-only, so the backdrop participates in depth prepass and any
        // effect that samples depth - the chromatic displacement pass included.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
