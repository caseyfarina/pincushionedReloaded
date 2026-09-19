#ifndef NOISE_DISPLACEMENT_INCLUDED
#define NOISE_DISPLACEMENT_INCLUDED

// =============================================================================
// Procedural positional noise displacement (object- or world-space).
//
// Sole noise source is procedural 3D value noise -- no texture is sampled.
// Requires these in the CBUFFER before including:
//   float4 _DisplacementTiling        -> SPATIAL FREQUENCY (.xyz)
//   float4 _DisplacementScrollSpeed   -> SCROLL VELOCITY   (.xyz), units/sec
//   float  _DisplacementStrength       -> push distance
//   float  _DisplacementOffset         -> bias added to noise before scaling
//
// The _DisplacementMap texture/sampler are no longer used by this file. You can
// leave the TEXTURE2D(_DisplacementMap) declarations and the _DisplacementMap
// property in place (harmless, just unused) or strip them from all five passes
// for tidiness -- your choice, neither affects correctness.
//
// IMPORTANT: _DisplacementTiling.z / _DisplacementScrollSpeed.z default to 0.
// Set them in the material or the noise is constant along Z, e.g.
//   _DisplacementTiling      = (0.5, 0.5, 0.5, 0)
//   _DisplacementScrollSpeed = (0, 0, 0.2, 0)
// =============================================================================

// Domain the noise is sampled in, and the space the vertex is pushed in:
//   0 = object space : pattern locked to the mesh, transform-free (cheapest).
//   1 = world  space : pattern fixed in the scene, surface flows through it.
#define DISPLACE_DOMAIN_WORLD 0


// ---- Procedural 3D value noise (0..1) ---------------------------------------
float DispHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float DispValueNoise3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);   // smoothstep interpolant

    float n000 = DispHash31(i + float3(0, 0, 0));
    float n100 = DispHash31(i + float3(1, 0, 0));
    float n010 = DispHash31(i + float3(0, 1, 0));
    float n110 = DispHash31(i + float3(1, 1, 0));
    float n001 = DispHash31(i + float3(0, 0, 1));
    float n101 = DispHash31(i + float3(1, 0, 1));
    float n011 = DispHash31(i + float3(0, 1, 1));
    float n111 = DispHash31(i + float3(1, 1, 1));

    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);
    float nxy0 = lerp(nx00, nx10, u.y);
    float nxy1 = lerp(nx01, nx11, u.y);
    return lerp(nxy0, nxy1, u.z);          // 0..1
}


float3 ApplyNoiseDisplacement(float3 positionOS, float3 normalOS, float2 uv)
{
    #ifdef _DISPLACEMENT_ON
    #if DISPLACE_DOMAIN_WORLD
        // ---- World space: sample + push in world, round-trip back to OS ------
        float3 posWS = TransformObjectToWorld(positionOS);
        float3 nWS   = normalize(TransformObjectToWorldNormal(normalOS));

        float3 p = posWS * _DisplacementTiling.xyz
                 + _Time.y * _DisplacementScrollSpeed.xyz;

        float noise = DispValueNoise3D(p);
        float displacement = (noise + _DisplacementOffset) * _DisplacementStrength;

        posWS     += nWS * displacement;
        positionOS = TransformWorldToObject(posWS);
    #else
        // ---- Object space: no transforms, cheapest, pattern locked to mesh --
        float3 nOS = normalize(normalOS);

        float3 p = positionOS * _DisplacementTiling.xyz
                 + _Time.y * _DisplacementScrollSpeed.xyz;

        float noise = DispValueNoise3D(p);
        float displacement = (noise + _DisplacementOffset) * _DisplacementStrength;

        positionOS += nOS * displacement;
    #endif
    #endif

    return positionOS;
}

#endif // NOISE_DISPLACEMENT_INCLUDED
