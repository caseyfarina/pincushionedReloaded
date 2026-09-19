// Backdrop lattice distribution and animation, as two Custom HLSL blocks.
//
// Why HLSL and not nodes: the three domains plus per-instance variation is
// roughly forty operator nodes of branch tree and trigonometry, which is slow to
// author, impossible to diff, and easy to mis-wire silently. As text it is
// reviewable, version-controlled, and the whole distribution reads in one place.
//
// Everything is a pure function of (particleId, LayoutSeed), so nothing has to be
// stored between frames except the two values Initialize stashes for Update:
//   targetPosition  = the lattice point this instance belongs to
//   angularVelocity = spin axis * this instance's spin rate
// Both are stock attributes being reused, which keeps custom attributes - and the
// extra graph plumbing they need - out of the picture entirely.

// Integer hash. Deterministic across machines and driver versions, which
// UnityEngine.Random-style float noise is not.
uint BD_Hash(uint x)
{
    x ^= x >> 16; x *= 0x7feb352du;
    x ^= x >> 15; x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float BD_Rand01(uint id, uint seed, uint stream)
{
    return float(BD_Hash(id * 0x9e3779b9u + seed * 0x85ebca6bu + stream * 0xc2b2ae35u)) / 4294967295.0;
}

float3 BD_Rand3(uint id, uint seed, uint stream)
{
    return float3(BD_Rand01(id, seed, stream),
                  BD_Rand01(id, seed, stream + 101u),
                  BD_Rand01(id, seed, stream + 202u));
}

// The lattice point for one instance. Even by construction rather than by
// rejection sampling, so density does not pile up at a pole or an edge.
float3 BD_LatticePosition(uint id, int count, int shape, float3 size, bool solidFill)
{
    count = max(count, 1);
    float t = float(id) / float(count);

    if (shape <= 0)
    {
        // Plane: an N x M grid, N chosen so cells stay near-square on the
        // requested aspect rather than stretching with it.
        float aspect = max(size.x, 0.001) / max(size.y, 0.001);
        int n = max(1, (int)ceil(sqrt(float(count) * aspect)));
        int m = max(1, (int)ceil(float(count) / float(n)));
        int col = (int)(id % (uint)n);
        int row = (int)(id / (uint)n);
        float u = (n > 1) ? (float(col) / float(n - 1) - 0.5) : 0.0;
        float v = (m > 1) ? (float(row) / float(m - 1) - 0.5) : 0.0;
        return float3(u * size.x, v * size.y, 0.0);
    }

    if (shape == 1)
    {
        // Hemisphere: a Fibonacci spherical cap. The golden angle keeps
        // successive points maximally separated, so there is no pole cluster.
        float y = 1.0 - t;
        float r = sqrt(saturate(1.0 - y * y));
        float theta = float(id) * 2.399963229728653;
        return float3(cos(theta) * r, y, sin(theta) * r) * size * 0.5;
    }

    // Cube: an N x N x N lattice. Hollow by default - the interior of a solid
    // cube is invisible from outside and would waste most of the instance budget.
    int n3 = max(2, (int)ceil(pow(max(float(count), 1.0), 1.0 / 3.0)));
    uint un = (uint)n3;
    int ix = (int)(id % un);
    int iy = (int)((id / un) % un);
    int iz = (int)((id / (un * un)) % un);

    float3 u3 = float3(float(ix), float(iy), float(iz)) / float(n3 - 1) - 0.5;

    if (!solidFill)
    {
        // Push the dominant axis out to the shell. Snapping rather than culling
        // keeps every requested instance visible instead of hiding the interior
        // ones and quietly spending the budget on nothing.
        float3 a = abs(u3);
        if (a.x >= a.y && a.x >= a.z)      u3.x = sign(u3.x + 1e-6) * 0.5;
        else if (a.y >= a.x && a.y >= a.z) u3.y = sign(u3.y + 1e-6) * 0.5;
        else                               u3.z = sign(u3.z + 1e-6) * 0.5;
    }

    return u3 * size;
}

// Place one instance: lattice point, jitter, scale, rotation, and the two values
// Update needs stashed into stock attributes.
void BackdropPlace(inout VFXAttributes attributes,
                   in uint LayoutSeed,
                   in int SpawnCount,
                   in int DomainShape,
                   in float3 DomainSize,
                   in bool SolidFill,
                   in float2 ScaleRange,
                   in float3 ScaleAxisBias,
                   in float3 OffsetJitter,
                   in float3 RotationJitter,
                   in float2 SpinRateRange,
                   in float3 SpinAxis)
{
    uint id = (uint)attributes.particleId;

    float3 base = BD_LatticePosition(id, SpawnCount, DomainShape, DomainSize, SolidFill);
    base += (BD_Rand3(id, LayoutSeed, 11u) * 2.0 - 1.0) * OffsetJitter;

    attributes.position = base;
    attributes.targetPosition = base;   // Update reads this as the rest pose.

    float s = lerp(ScaleRange.x, ScaleRange.y, BD_Rand01(id, LayoutSeed, 31u));
    attributes.scaleX = s * ScaleAxisBias.x;
    attributes.scaleY = s * ScaleAxisBias.y;
    attributes.scaleZ = s * ScaleAxisBias.z;

    float3 rot = (BD_Rand3(id, LayoutSeed, 53u) * 2.0 - 1.0) * RotationJitter;
    attributes.angleX = rot.x;
    attributes.angleY = rot.y;
    attributes.angleZ = rot.z;

    float rate = lerp(SpinRateRange.x, SpinRateRange.y, BD_Rand01(id, LayoutSeed, 71u));
    attributes.angularVelocity = normalize(SpinAxis + 1e-6) * rate;
}

// Per-frame: wave displacement, spin, and the emission flash.
void BackdropAnimate(inout VFXAttributes attributes,
                     in int SpawnCount,
                     in float3 WaveAxis,
                     in float WaveAmplitude,
                     in float WaveFrequency,
                     in float WavePhaseSpread,
                     in float FlashTime,
                     in float FlashDecay,
                     in float FlashRipple,
                     in float4 EmissionColor,
                     in float Time,
                     in float DeltaTime)
{
    uint id = (uint)attributes.particleId;
    float idNorm = float(id) / float(max(SpawnCount, 1));

    // Absolute offset from the stored rest pose, never an accumulating add -
    // accumulating would walk every instance off its lattice point for good.
    float phase = idNorm * WavePhaseSpread * 6.28318530718;
    float w = sin(Time * WaveFrequency * 6.28318530718 + phase) * WaveAmplitude;
    attributes.position = attributes.targetPosition + normalize(WaveAxis + 1e-6) * w;

    attributes.angleX += attributes.angularVelocity.x * DeltaTime;
    attributes.angleY += attributes.angularVelocity.y * DeltaTime;
    attributes.angleZ += attributes.angularVelocity.z * DeltaTime;

    // FlashTime is a timestamp, not a level: the decay is computed here rather
    // than driven per-frame from C#. The idNorm term staggers the onset so the
    // flash sweeps across the field instead of blinking flat.
    float since = Time - FlashTime - idNorm * FlashRipple;
    float flash = (since < 0.0) ? 0.0 : exp(-since * max(FlashDecay, 0.01));
    attributes.color = EmissionColor.rgb * flash;
}
