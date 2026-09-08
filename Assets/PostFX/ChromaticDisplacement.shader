// Red Giant style chromatic displacement for URP 17 / Unity 6.
//
// The image displaces itself. Its luminance is shaped by a levels stage,
// blurred heavily, and read as a HEIGHT FIELD; pixels then slide along the
// gradient of that field while the sample offset is swept across a spectrum.
//
// Using the gradient (rather than reading R/G as X/Y like AE's Displacement
// Map) is what makes it read as refraction. It also means displacement peaks at
// the EDGES of bright regions, not their centres -- a flat bright area has no
// slope and barely moves.
Shader "Hidden/PostFX/ChromaticDisplacement"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        // _BlitTexture and _BlitTexture_TexelSize come from Blit.hlsl -- do NOT
        // redeclare them, that is a shader compile error.

        // levels
        float _InBlack, _InWhite, _Gamma, _OutBlack, _OutWhite;
        // blur: low-res texel size. CONSTANT across every blur pass, which matters
        // because blit passes are recorded now and executed later against the
        // shared material -- a per-pass uniform would leak between passes.
        float2 _BlurTexel;
        // displace
        float  _Strength, _Spread, _HeightGamma, _Intensity;
        int    _Samples;
        float  _TintAmount;
        float4 _TintA, _TintB;
        float2 _AspectFix;
        float  _ViewHeight;

        float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

        float3 Tap(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
        }

        float R(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).r;
        }

        // Substance-style levels.
        float Levels(float x)
        {
            float t = saturate((x - _InBlack) / max(1e-5, _InWhite - _InBlack));
            t = pow(max(t, 1e-6), 1.0 / max(0.01, _Gamma));
            return _OutBlack + t * (_OutWhite - _OutBlack);
        }

        // Cheap approximation of the visible spectrum, t in 0..1.
        float3 Spectrum(float t)
        {
            return saturate(float3(1.5 - abs(4.0 * t - 3.0),
                                   1.5 - abs(4.0 * t - 2.0),
                                   1.5 - abs(4.0 * t - 1.0)));
        }

        // 9-tap Gaussian along an arbitrary direction, one texel per step.
        // Wider spacing would show nine discrete ghosts instead of a blur, so
        // radius is achieved by running this repeatedly rather than stretching it.
        float Gauss9(float2 uv, float2 dir)
        {
            float w0 = 0.2270270270, w1 = 0.1945945946, w2 = 0.1216216216;
            float w3 = 0.0540540541, w4 = 0.0162162162;
            float acc = R(uv) * w0;
            acc += (R(uv + dir * 1.0) + R(uv - dir * 1.0)) * w1;
            acc += (R(uv + dir * 2.0) + R(uv - dir * 2.0)) * w2;
            acc += (R(uv + dir * 3.0) + R(uv - dir * 3.0)) * w3;
            acc += (R(uv + dir * 4.0) + R(uv - dir * 4.0)) * w4;
            return acc;
        }
        ENDHLSL

        // 0 — Downsample to 1/N res, convert to luminance, apply levels.
        //     Levels go BEFORE the blur so contrast shaping produces smooth ramps;
        //     doing it after would re-sharpen what the blur just softened.
        Pass
        {
            Name "DownsampleLevels"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target
            {
                float2 o = _BlitTexture_TexelSize.xy;
                float l = Luma(Tap(i.texcoord + float2(-o.x, -o.y)))
                        + Luma(Tap(i.texcoord + float2( o.x, -o.y)))
                        + Luma(Tap(i.texcoord + float2(-o.x,  o.y)))
                        + Luma(Tap(i.texcoord + float2( o.x,  o.y)));
                return float4(Levels(l * 0.25), 0, 0, 1);
            }
            ENDHLSL
        }

        // 1 — Horizontal blur. Direction is baked into the pass, not a uniform.
        Pass
        {
            Name "BlurH"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target
            {
                return float4(Gauss9(i.texcoord, float2(_BlurTexel.x, 0)), 0, 0, 1);
            }
            ENDHLSL
        }

        // 2 — Vertical blur.
        Pass
        {
            Name "BlurV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target
            {
                return float4(Gauss9(i.texcoord, float2(0, _BlurTexel.y)), 0, 0, 1);
            }
            ENDHLSL
        }

        // 3 — Displace + spectral separation.
        Pass
        {
            Name "Displace"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURE2D_X(_HeightTex);
            SAMPLER(sampler_HeightTex);
            float4 _HeightTex_TexelSize;

            float H(float2 uv)
            {
                float h = SAMPLE_TEXTURE2D_X_LOD(_HeightTex, sampler_HeightTex, uv, 0).r;
                return pow(max(h, 1e-6), _HeightGamma);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                if (_ViewHeight > 0.5)
                    return float4(H(i.texcoord).xxx, 1);

                float3 original = Tap(i.texcoord);

                // Central-difference gradient of the height field. Sampling a
                // couple of texels out gives a smoother, less noisy slope.
                float2 o = _HeightTex_TexelSize.xy * 2.0;
                float2 grad = float2(H(i.texcoord + float2(o.x, 0)) - H(i.texcoord - float2(o.x, 0)),
                                     H(i.texcoord + float2(0, o.y)) - H(i.texcoord - float2(0, o.y)));

                float2 d = grad * _Strength * _AspectFix;

                // Sweep the sample offset across the spectrum and accumulate.
                float3 sum = 0, wsum = 0;
                int n = max(3, _Samples);
                [loop] for (int k = 0; k < n; k++)
                {
                    float t = (float)k / (n - 1);
                    float3 w = Spectrum(t);
                    if (_TintAmount > 0)
                        w = lerp(w, lerp(_TintA.rgb, _TintB.rgb, t), _TintAmount);

                    float scale = lerp(1.0 - _Spread, 1.0 + _Spread, t);
                    sum  += Tap(i.texcoord + d * scale) * w;
                    wsum += w;
                }
                float3 col = sum / max(wsum, 1e-4);

                return float4(lerp(original, col, saturate(_Intensity)), 1);
            }
            ENDHLSL
        }

        // 4 — Copy back to the camera colour target.
        Pass
        {
            Name "CopyBack"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target { return float4(Tap(i.texcoord), 1); }
            ENDHLSL
        }
    }
    Fallback Off
}
