Shader "Custom/URP_NoiseDisplacement_Toon"
{
    Properties
    {
        [Header(Surface)]
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BaseMap ("Albedo", 2D) = "white" {}

        [Header(Cel Shading)]
        _ShadowColor ("Shadow Color", Color) = (0.4, 0.4, 0.5, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.5
        _ShadowSmoothness ("Shadow Edge Smoothness", Range(0.001, 0.15)) = 0.02
        _Bands ("Light Bands", Range(1, 8)) = 2
        _SpecularSize ("Specular Size", Range(0.9, 1.0)) = 0.97
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.5
        [HDR] _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)

        [Header(Outline)]
        [Toggle(_OUTLINE_ON)] _EnableOutline ("Enable Outline", Float) = 1
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 10)) = 2.0

        [Header(Shading Mode)]
        // 0 = Toon, 1 = Fresnel Toon, 2 = Lit. Matches BackdropShadingMode.
        _ShadingMode ("Shading Mode", Float) = 0
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3
        [HDR] _FresnelColor ("Fresnel Color", Color) = (0.2, 0.8, 1, 1)

        [Header(Ambient Occlusion)]
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1.0

        [Header(Normal)]
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 2)) = 1.0

        [Header(Emission)]
        [Toggle(_EMISSION)] _EnableEmission ("Enable Emission", Float) = 0
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionMap ("Emission Map", 2D) = "white" {}

        [Header(Displacement)]
        [Toggle(_DISPLACEMENT_ON)] _EnableDisplacement ("Enable Displacement", Float) = 0
        _DisplacementMap ("Noise Displacement Map", 2D) = "gray" {}
        _DisplacementStrength ("Displacement Strength", Range(-2, 2)) = 0.5
        _DisplacementOffset ("Displacement Offset", Range(-1, 1)) = 0.0
        _DisplacementTiling ("Displacement Tiling", Vector) = (1, 1, 0, 0)

        [Header(Displacement Animation)]
        _DisplacementScrollSpeed ("Scroll Speed (XY)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // =============================================
        // OUTLINE PASS (inverted hull)
        // Renders back faces extruded along normals.
        // Runs before the forward pass so it sits behind.
        // =============================================
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing

            #pragma multi_compile_local _ _DISPLACEMENT_ON
            #pragma shader_feature_local _ _OUTLINE_ON

            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half _ShadowSmoothness;
                half _Bands;
                half _SpecularSize;
                half _SpecularStrength;
                half4 _SpecularColor;
                half4 _OutlineColor;
                float _OutlineWidth;
                half _BumpScale;
                half4 _EmissionColor;
                float4 _DisplacementMap_ST;
                float _DisplacementStrength;
                float _DisplacementOffset;
                float4 _DisplacementTiling;
                float4 _DisplacementScrollSpeed;
                half _ShadingMode;
                half _FresnelPower;
                half4 _FresnelColor;
                half _OcclusionStrength;
            CBUFFER_END

            // Per-instance flash, written as a float array by BackdropInstrument.
            // Defaults to 0, so anything not driving it - the dancer - is
            // unaffected.
            UNITY_INSTANCING_BUFFER_START(BackdropProps)
                UNITY_DEFINE_INSTANCED_PROP(float, _Flash)
            UNITY_INSTANCING_BUFFER_END(BackdropProps)

            TEXTURE2D(_DisplacementMap); SAMPLER(sampler_DisplacementMap);

            #include "NoiseDisplacement.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                #ifdef _OUTLINE_ON
                    // Apply displacement first so outline follows deformed mesh
                    float3 posOS = ApplyNoiseDisplacement(input.positionOS.xyz, input.normalOS, input.uv);

                    // Transform to clip space
                    output.positionCS = TransformObjectToHClip(posOS);

                    // Screen-space outline: extrude in clip space for consistent pixel width
                    // Transform normal to clip space, then push along it
                    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                    float3 normalCS = mul((float3x3)GetWorldToHClipMatrix(), normalWS);

                    // Scale by W for perspective-correct screen-space width
                    // Divide by screen resolution to get pixel-scale units
                    float2 screenScale = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
                    output.positionCS.xy += normalize(normalCS.xy) * _OutlineWidth * output.positionCS.w * screenScale;
                #else
                    // Outline disabled — collapse to degenerate triangle
                    output.positionCS = float4(0, 0, 0, 1);
                #endif

                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                #ifdef _OUTLINE_ON
                    return _OutlineColor;
                #else
                    discard;
                    return 0;
                #endif
            }

            ENDHLSL
        }

        // =============================================
        // FORWARD LIT PASS (toon/cel shading)
        // =============================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing

            // URP keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            // Custom keywords
            #pragma multi_compile_local _ _DISPLACEMENT_ON
            #pragma shader_feature_local _EMISSION
            #pragma multi_compile _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3

            #pragma vertex ToonVert
            #pragma fragment ToonFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half _ShadowSmoothness;
                half _Bands;
                half _SpecularSize;
                half _SpecularStrength;
                half4 _SpecularColor;
                half4 _OutlineColor;
                float _OutlineWidth;
                half _BumpScale;
                half4 _EmissionColor;
                float4 _DisplacementMap_ST;
                float _DisplacementStrength;
                float _DisplacementOffset;
                float4 _DisplacementTiling;
                float4 _DisplacementScrollSpeed;
                half _ShadingMode;
                half _FresnelPower;
                half4 _FresnelColor;
                half _OcclusionStrength;
            CBUFFER_END

            // Per-instance flash, written as a float array by BackdropInstrument.
            // Defaults to 0, so anything not driving it - the dancer - is
            // unaffected.
            UNITY_INSTANCING_BUFFER_START(BackdropProps)
                UNITY_DEFINE_INSTANCED_PROP(float, _Flash)
            UNITY_INSTANCING_BUFFER_END(BackdropProps)

            TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_EmissionMap);         SAMPLER(sampler_EmissionMap);
            TEXTURE2D(_DisplacementMap);     SAMPLER(sampler_DisplacementMap);

            #include "NoiseDisplacement.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
                float2 lightmapUV   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS       : SV_POSITION;
                float2 uv               : TEXCOORD0;
                float3 positionWS       : TEXCOORD1;
                float3 normalWS         : TEXCOORD2;
                float4 tangentWS        : TEXCOORD3;
                float  fogFactor        : TEXCOORD4;
                float4 shadowCoord      : TEXCOORD5;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 6);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ----- Cel shading helpers -----

            // Quantize a 0-1 lighting value into discrete bands
            half CelBand(half NdotL, half bands)
            {
                // Shift NdotL from [-1,1] to [0,1] for banding
                half halfLambert = NdotL * 0.5 + 0.5;
                return floor(halfLambert * bands) / bands;
            }

            // Smooth step threshold for primary shadow
            half CelShadow(half NdotL, half threshold, half smoothness)
            {
                return smoothstep(threshold - smoothness, threshold + smoothness, NdotL * 0.5 + 0.5);
            }

            // Toon specular highlight
            half CelSpecular(half3 normalWS, half3 viewDirWS, half3 lightDirWS, half size, half strength)
            {
                half3 halfVec = normalize(lightDirWS + viewDirWS);
                half NdotH = saturate(dot(normalWS, halfVec));
                // Hard-edge specular
                return step(size, NdotH) * strength;
            }

            // Calculate cel-shaded contribution for a single light
            half3 ToonLightContribution(half3 lightColor, half3 lightDir, half lightAtten,
                                         half3 normalWS, half3 viewDirWS, half3 albedo)
            {
                half NdotL = dot(normalWS, lightDir);

                // Combine banding with shadow threshold
                half shadow = CelShadow(NdotL, _ShadowThreshold, _ShadowSmoothness);
                half band = CelBand(NdotL, _Bands);

                // Blend: use shadow for the primary lit/shadow split, band for subtle stepping
                half lightIntensity = shadow * band;

                // Lit mode: bypass the cel split for a smooth lambert ramp. Done
                // by lerping the same term rather than branching to a separate
                // PBR path, so shadow colour, specular and the light loop stay
                // shared and the three modes cannot drift apart.
                half smoothLit = saturate(NdotL * 0.5 + 0.5);
                lightIntensity = lerp(lightIntensity, smoothLit, step(1.5, _ShadingMode));

                // Lerp between shadow color and lit albedo
                half3 diffuse = lerp(_ShadowColor.rgb * albedo, albedo, lightIntensity);
                diffuse *= lightColor * lightAtten;

                // Specular — only if in light
                half spec = CelSpecular(normalWS, viewDirWS, lightDir, _SpecularSize, _SpecularStrength);
                half3 specular = spec * _SpecularColor.rgb * lightColor * lightAtten * shadow;

                return diffuse + specular;
            }

            // ----- Vertex -----
            Varyings ToonVert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posOS = ApplyNoiseDisplacement(input.positionOS.xyz, input.normalOS, input.uv);

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                output.shadowCoord = GetShadowCoord(posInputs);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);

                return output;
            }

            // ----- Fragment -----
            half4 ToonFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Sample surface
                half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 albedo = albedoSample.rgb;

                // Normal mapping
                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);

                // Apply DBuffer decals to albedo and normalTS before building normalWS
                #if defined(_DBUFFER_MRT1) || defined(_DBUFFER_MRT2) || defined(_DBUFFER_MRT3)
                {
                    SurfaceData decalSurface = (SurfaceData)0;
                    decalSurface.albedo    = albedo;
                    decalSurface.normalTS  = normalTS;
                    decalSurface.smoothness = 0.5;
                    InputData decalInput = (InputData)0;
                    decalInput.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    ApplyDecalToSurfaceData(input.positionCS, decalSurface, decalInput);
                    albedo   = decalSurface.albedo;
                    normalTS = decalSurface.normalTS;
                }
                #endif

                float sgn = input.tangentWS.w;
                float3 bitangent = sgn * cross(input.normalWS, input.tangentWS.xyz);
                half3x3 TBN = half3x3(input.tangentWS.xyz, bitangent, input.normalWS);
                half3 normalWS = normalize(mul(normalTS, TBN));

                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // ---- Main light ----
                Light mainLight = GetMainLight(input.shadowCoord);
                half3 color = ToonLightContribution(
                    mainLight.color, mainLight.direction, mainLight.distanceAttenuation * mainLight.shadowAttenuation,
                    normalWS, viewDirWS, albedo);

                // ---- Additional lights ----
                #ifdef _ADDITIONAL_LIGHTS
                    uint additionalLightCount = GetAdditionalLightsCount();

                    // Forward+ uses a different iteration pattern
                    #ifdef _FORWARD_PLUS
                        // FORWARD_PLUS: light indices from cluster
                        InputData inputData = (InputData)0;
                        inputData.positionWS = input.positionWS;
                        inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                        LIGHT_LOOP_BEGIN(additionalLightCount)
                            Light addLight = GetAdditionalLight(lightIndex, input.positionWS, input.shadowCoord);
                            color += ToonLightContribution(
                                addLight.color, addLight.direction,
                                addLight.distanceAttenuation * addLight.shadowAttenuation,
                                normalWS, viewDirWS, albedo);
                        LIGHT_LOOP_END
                    #else
                        for (uint i = 0; i < additionalLightCount; i++)
                        {
                            Light addLight = GetAdditionalLight(i, input.positionWS, input.shadowCoord);
                            color += ToonLightContribution(
                                addLight.color, addLight.direction,
                                addLight.distanceAttenuation * addLight.shadowAttenuation,
                                normalWS, viewDirWS, albedo);
                        }
                    #endif
                #endif

                // ---- Ambient / GI ----
                half3 bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);

                // Screen-space AO, from whichever renderer feature is supplying
                // it. Applied to ambient only: folding it into direct light
                // would smear a soft gradient across the cel bands and undo the
                // reason for a toon shader in the first place.
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 aoUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(aoUV);
                    bakedGI *= lerp(1.0h, aoFactor.indirectAmbientOcclusion, _OcclusionStrength);
                #endif

                // Apply GI with a subtle cel treatment — avoid fully flat ambient
                color += bakedGI * albedo;

                // ---- Fresnel rim (mode 1) ----
                // On a field of small objects the rim is most of what separates
                // one instance from the one behind it; the outline pass alone
                // does not, because it only draws the silhouette.
                if (_ShadingMode > 0.5 && _ShadingMode < 1.5)
                {
                    half rim = pow(saturate(1.0 - saturate(dot(normalWS, viewDirWS))), _FresnelPower);
                    color += _FresnelColor.rgb * rim;
                }

                // ---- Emission ----
                #ifdef _EMISSION
                    color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb
                           * _EmissionColor.rgb;
                #endif

                // ---- Backdrop flash ----
                // Per-instance, and additive on top of every mode so a pad press
                // reads the same whichever shading is live. Zero for anything
                // that does not drive it.
                color += _EmissionColor.rgb * UNITY_ACCESS_INSTANCED_PROP(BackdropProps, _Flash);

                // ---- Fog ----
                color = MixFog(color, input.fogFactor);

                return half4(color, albedoSample.a);
            }

            ENDHLSL
        }

        // =============================================
        // SHADOW CASTER PASS
        // =============================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _DISPLACEMENT_ON

            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half _ShadowSmoothness;
                half _Bands;
                half _SpecularSize;
                half _SpecularStrength;
                half4 _SpecularColor;
                half4 _OutlineColor;
                float _OutlineWidth;
                half _BumpScale;
                half4 _EmissionColor;
                float4 _DisplacementMap_ST;
                float _DisplacementStrength;
                float _DisplacementOffset;
                float4 _DisplacementTiling;
                float4 _DisplacementScrollSpeed;
                half _ShadingMode;
                half _FresnelPower;
                half4 _FresnelColor;
                half _OcclusionStrength;
            CBUFFER_END

            // Per-instance flash, written as a float array by BackdropInstrument.
            // Defaults to 0, so anything not driving it - the dancer - is
            // unaffected.
            UNITY_INSTANCING_BUFFER_START(BackdropProps)
                UNITY_DEFINE_INSTANCED_PROP(float, _Flash)
            UNITY_INSTANCING_BUFFER_END(BackdropProps)

            TEXTURE2D(_DisplacementMap); SAMPLER(sampler_DisplacementMap);

            #include "NoiseDisplacement.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posOS = ApplyNoiseDisplacement(input.positionOS.xyz, input.normalOS, input.uv);

                float3 posWS = TransformObjectToWorld(posOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }

        // =============================================
        // DEPTH ONLY PASS
        // =============================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _DISPLACEMENT_ON

            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half _ShadowSmoothness;
                half _Bands;
                half _SpecularSize;
                half _SpecularStrength;
                half4 _SpecularColor;
                half4 _OutlineColor;
                float _OutlineWidth;
                half _BumpScale;
                half4 _EmissionColor;
                float4 _DisplacementMap_ST;
                float _DisplacementStrength;
                float _DisplacementOffset;
                float4 _DisplacementTiling;
                float4 _DisplacementScrollSpeed;
                half _ShadingMode;
                half _FresnelPower;
                half4 _FresnelColor;
                half _OcclusionStrength;
            CBUFFER_END

            // Per-instance flash, written as a float array by BackdropInstrument.
            // Defaults to 0, so anything not driving it - the dancer - is
            // unaffected.
            UNITY_INSTANCING_BUFFER_START(BackdropProps)
                UNITY_DEFINE_INSTANCED_PROP(float, _Flash)
            UNITY_INSTANCING_BUFFER_END(BackdropProps)

            TEXTURE2D(_DisplacementMap); SAMPLER(sampler_DisplacementMap);

            #include "NoiseDisplacement.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posOS = ApplyNoiseDisplacement(input.positionOS.xyz, input.normalOS, input.uv);
                output.positionCS = TransformObjectToHClip(posOS);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }

        // =============================================
        // DEPTH NORMALS PASS
        // =============================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _DISPLACEMENT_ON

            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half _ShadowSmoothness;
                half _Bands;
                half _SpecularSize;
                half _SpecularStrength;
                half4 _SpecularColor;
                half4 _OutlineColor;
                float _OutlineWidth;
                half _BumpScale;
                half4 _EmissionColor;
                float4 _DisplacementMap_ST;
                float _DisplacementStrength;
                float _DisplacementOffset;
                float4 _DisplacementTiling;
                float4 _DisplacementScrollSpeed;
                half _ShadingMode;
                half _FresnelPower;
                half4 _FresnelColor;
                half _OcclusionStrength;
            CBUFFER_END

            // Per-instance flash, written as a float array by BackdropInstrument.
            // Defaults to 0, so anything not driving it - the dancer - is
            // unaffected.
            UNITY_INSTANCING_BUFFER_START(BackdropProps)
                UNITY_DEFINE_INSTANCED_PROP(float, _Flash)
            UNITY_INSTANCING_BUFFER_END(BackdropProps)

            TEXTURE2D(_DisplacementMap); SAMPLER(sampler_DisplacementMap);

            #include "NoiseDisplacement.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 normalWS     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posOS = ApplyNoiseDisplacement(input.positionOS.xyz, input.normalOS, input.uv);
                output.positionCS = TransformObjectToHClip(posOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                return float4(PackNormalOctRectEncode(normalize(input.normalWS)), 0, 0);
            }

            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
