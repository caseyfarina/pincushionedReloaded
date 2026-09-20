# Cable instrument — design

**Date:** 2026-09-20
**Status:** Approved design, pending implementation plan

Cables — XLR, quarter-inch, USB — are shot from a source toward a configurable
target. Each flies out trailing its own length behind it, lands, and stays,
hanging with slack and never quite going still. A nest accumulates over a set.

The cable is a **flat camera-facing ribbon** whose normal map makes it read as a
round cylinder, tipped with a connector mesh chosen from a folder.

## The central decision: closed form, not simulation

**A cable is a pure function of `(t, seed, time)`. Nothing integrates frame to
frame, and there is no solver.**

This repeats the reversal already recorded in
`Assets/proceduralBackdrop/CLAUDE.md`, for the same reasons. The project is
expressionistic; it needs the *appearance* of gravity and life, not accurate
rope physics. A closed-form evaluator is cheaper, cannot go unstable, cannot
explode at high cable counts, is reproducible from its seed (satisfying the
project's "seed anything random" rule), and is unit-testable the way
`BackdropLattice` is.

### Why not Shuriken

The original idea was a Shuriken particle system with the Trail module as the
cable. It was evaluated and rejected. Three blockers, each independently fatal:

1. **A trail is frozen history.** It cannot sag, sway, or react once drawn.
   Cables must keep moving after arrival — an explicit requirement.
2. **Trail length is a function of particle lifetime and speed**, not of cable
   length. Cable length could only be tuned indirectly, and would drift whenever
   target distance changed.
3. **`ParticleSystemRenderer` caps at four meshes.** Connectors come from a
   folder, which is more than four.

Shuriken also has no attractor, so the steering script would have been written
regardless. It would have saved only ribbon mesh generation — roughly sixty
lines — at the cost of surrendering control of the UV layout the normal map
depends on.

A Verlet rope was also considered, and rejected as more machinery than the look
requires.

## Scope

**In:** shot-then-settle cables driven by a single evaluator; camera-facing
normal-mapped ribbon; folder-scanned connector heads oriented to travel; a
single target Transform with a scatter radius; a keyboard explorer scene.

**Out:** cable-to-cable and cable-to-world collision (would require returning to
a solver); MIDI bindings; main-scene integration. The parameter surface
accommodates MIDI later, but the pad budget is contested and is not this
design's concern.

## The evaluator

```
Position(t) = lerp(anchorA, anchorB, t)        // the chord
            + sagDir * slack * 4t(1-t)          // parabolic droop
            + Noise(t, seed, time) * sin(PI*t)  // life, windowed to the ends
```

- **`sagDir`** is world down, `(0,-1,0)`. Sag is not measured perpendicular to
  the chord: a cable droops toward the floor regardless of how its two ends are
  oriented, and a perpendicular sag on a near-vertical cable would swing it
  sideways instead of down.
- **`4t(1-t)`** is zero at both anchors and `slack` at the midpoint. A true
  catenary differs by a fraction of a percent at realistic droop; this costs one
  multiply instead of a `cosh`.
- **`sin(PI*t)` is load-bearing.** It forces noise to zero at both ends, welding
  the cable to its source and its plug. Without it the ends jitter and the
  illusion collapses immediately. This is the single most important invariant in
  the system, and is asserted directly by test.

### Flight and settle are the same function

Nothing converts between two representations, because there is only one.

```
flight:   anchorB = lerp(source, landingPoint, Ease(age, deceleration))
          slack, noise = whippy, easing toward rest
settled:  anchorB = pinned at landingPoint
          slack, noise = authored resting values
```

A cable has "arrived" when `anchorB` stops moving. There is no handoff, and
therefore no pop at the moment of impact.

`landingPoint` is sampled once per shot, seeded, on a sphere of
`targetScatterRadius` around the target Transform — so a nest converges on one
point without every cable ending identically.

### Motion after arrival — two separate terms

A settled cable must never freeze. Two distinct behaviours, deliberately not
merged:

- **Impact shiver:** `exp(-shiverDecay * age) * sin(shiverFreq * age)` scaling
  the noise amplitude. Decays to nothing shortly after landing.
- **Idle sway:** the noise field's offset advances continuously at
  `driftSpeed`, exactly as `PinDensityController` advances `_driftOffset`. Its
  amplitude **never reaches zero**, so a settled nest keeps breathing for as
  long as it exists.

Both remain closed-form; neither stores state.

## Render path

### Billboarding happens in the vertex shader, not in C#

**This is mandatory, not a preference.** The project renders up to 32
split-screen cameras. A ribbon whose camera-facing is resolved on the CPU is
correct for exactly one camera and collapses to an invisible sliver in every
other cell of the mosaic.

The mesh therefore stores only camera-independent data — node position, tangent,
a `+/-1` side sign, UVs, and per-cable colour. The vertex stage resolves facing:

```hlsl
float3 toCam = normalize(_WorldSpaceCameraPos - nodePos);
float3 side  = normalize(cross(tangent, toCam));
positionWS   = nodePos + side * (halfWidth * sideSign);
```

The same stage emits the tangent frame the normal map needs — tangent along the
cable, bitangent across it. No `RecalculateTangents`, and no chance of a
mismatch between the generated frame and the billboard orientation.

One mesh build per frame serves every camera correctly.

### Two draws total

- **Ribbons:** every live cable in one dynamic mesh, rebuilt per frame with
  `Mesh.SetVertices` / `SetIndices`. Bounds are computed directly rather than
  recalculated. Buffers are pre-sized to `cableCap`, so nothing allocates at
  steady state. At 200 cables x 24 nodes this is 9,600 vertices.
- **Heads:** `Graphics.RenderMeshInstanced`, one batch per distinct connector
  mesh — the same call the scatter system and the backdrop already use.

Cables are not individually culled or individually shaded, so per-cable draw
calls would buy nothing.

**Per-cable colour rides the vertex stream.** One mesh and one draw means colour
cannot be a material property. This falls out of the single-mesh decision and
costs nothing.

### The tube illusion

The normal map is a strip whose tangent-space normals bow from `-X` at one edge
to `+X` at the other. With a rolled highlight, a flat ribbon reads as a cylinder
from any angle — far cheaper than tube geometry at the intended cable counts.
Jacket detail (braid, ribbing, strain relief) tiles along `U`.

**`U` scales with the cable's world length, not with `t`.** Otherwise a short
patch cable and a long run get identical repeat counts, and the mismatch in
texel density is the most obvious tell that the cable is a stretched ribbon.

## Head orientation

The plug orients along its **direction of travel**, which includes the
`pathNoise` wander, so it banks into curves.

At arrival, travel direction goes to zero, and normalising it is a textbook NaN.
Three rules:

- while flying: aim along velocity
- as speed decays: blend toward the **cable's end tangent**, which is what makes
  the plug read as seated in a socket rather than frozen mid-flight
- guard the normalise with a length epsilon, falling back to the last valid
  direction

## Parameters — `CableParameters`

| Parameter | Effect |
|---|---|
| `cableSpeed` | World units per second. Flight duration is `distance / cableSpeed`, so a distant target takes proportionally longer rather than every shot landing in the same time |
| `deceleration` | Easing of the head along its path. Sole job; not damping. |
| `slack` | The sag term. 0 = taut chord, high = deep droop |
| `cableNoise` | Amplitude of the windowed noise along the cable's length |
| `noiseScale` / `driftSpeed` | Feature size, and how fast the idle sway breathes |
| `pathNoise` | Wander in the head's flight path |
| `thickness` | Ribbon half-width |
| `headScale` | Connector mesh scale |
| `colors[6]` | Palette, six primaries by default; each cable seeds its own pick |
| `targetScatterRadius` | Landing spread around the target Transform |
| `nodesPerCable` / `cableCap` | Resolution, and the live-cable ring buffer size |
| `shiverDecay` / `shiverFreq` | Impact shiver shape |

**`pathNoise` and `cableNoise` stay separate.** The first curves a cable's route
to its target; the second makes the cable restless once it is there. Merging
them forfeits both a straight-flying cable that hangs loose and a wild flight
that settles taut.

Retirement is a ring buffer: at `cableCap` the oldest cable retires by easing
`thickness` to zero, which is free.

## Files

```
Assets/proceduralCables/
  Core/                          <- asmdef. Pure, Unity-object-free, tested.
    CableParameters.cs           - every value, one struct
    CableCurve.cs                - the evaluator. THE tested surface.
    CableShot.cs                 - one cable: seed, anchors, age, colour, mesh index
    CableRibbonBuilder.cs        - node positions -> vertex/index arrays
  CableInstrument.cs             - sole owner of state, sole issuer of draws
  CableLibrary.cs                - folder-scanned connectors + triangle guard
  KeyboardCableDriver.cs         - X fires, R rerolls seed, C cycles connector
  CableRibbon.shader + .mat
  Meshes/Connectors/             - XLR, quarter-inch, USB
  Tests/
  cableExplorer.unity
```

`Core/` is its own asmdef because **asmdefs cannot reference `Assembly-CSharp`**,
so an EditMode test assembly cannot see code loose under `Assets/`. The
MonoBehaviours stay loose and still see `Core` — `Assembly-CSharp`
auto-references every asmdef.

Architecture follows `PinDensityController`, `PoseInstrument` and
`BackdropInstrument`: **the instrument owns all state, dumb drivers feed it.**
That is what allows the whole system to be built and judged before any hardware
is attached.

## Explorer scene

`cableExplorer.unity` — a source object, one target Transform, a global Volume,
and the rig.

`KeyboardCableDriver`: **`X` fires a cable.** `R` rerolls the seed, `C` cycles
the connector mesh.

## Testing

EditMode tests on `CableCurve` and `CableRibbonBuilder`, both pure:

- **The ends stay pinned.** `Position(0) == anchorA` and `Position(1) == anchorB`
  for any seed, time, and noise amplitude. The load-bearing assertion — a
  regression here detaches every cable from its plug.
- Sag reaches `slack` at the midpoint, and is monotonic either side.
- Same seed and time give the same curve; different seeds differ.
- `U` is proportional to world length across cable lengths.
- Head orientation is finite at the arrival instant — the NaN guard, asserted
  directly.
- Ribbon vertex and index counts, and triangle winding, for an N-node cable.

MonoBehaviours are compile-verified only, as with the backdrop.

## Risks

- **Nothing runs against MIDI hardware.** Consistent with the rest of the
  project; the explorer scene is keyboard-only by design.
- **The normal-map strip must be authored.** The tube illusion depends entirely
  on it, and there is no placeholder that demonstrates the effect. Until it
  exists, cables look like flat ribbons and the look cannot be judged.
- **Connector meshes must be sourced or modelled.** `CableLibrary` degrades to
  an empty set without them; the ribbon still renders headless.
