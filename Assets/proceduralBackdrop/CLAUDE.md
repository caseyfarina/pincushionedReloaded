# Generative backdrop (`Assets/proceduralBackdrop/`)

An instanced field of meshes behind the performers — towers, walls, scattered
architecture — sized to the camera frame and played live from the MF64 and the
MIDI Mix. Built 2026-09-11 to 2026-09-20.

`backdropExplorer.unity` is the working scene. Press Play, then `Space`.

## State

Renders, tested, and driven by MIDI. **90 EditMode tests pass.** Verified in
Play mode at 2.67:1; the whole pipeline is CLI-driven with no GUI step.

**Never run against real MIDI hardware for the backdrop specifically** — the 33
bindings and soft takeover are unexercised. The router wiring is confirmed
working, but which controls feel right is untested.

**No build measurement yet.** Editor draw calls and triangle counts are
trustworthy; the editor's frame times are not (this project measures 5.2x
inflation). See *Performance* for what is and is not known.

## Why instanced meshes and not VFX Graph

The design picked VFX Graph, and that was wrong. Worth recording, because the
reasoning looks sound right up until you write the motion code.

Everything the backdrop does is a **closed-form function of
`(instanceId, seed, time)`** — nothing integrates frame to frame. That only
became obvious while writing the displacement as HLSL, where avoiding
accumulated state took deliberate effort.

GPU-simulated persistent particle state is the one thing VFX Graph does that
nothing else does. With it unused, what remained was an opaque binary asset that
cannot be diffed, cannot be unit-tested, and — checked directly — has **zero
support across the Unity CLI's 142 commands**. The CLI has rich authoring
coverage for the comparable node assets (`add_animator_layer`,
`add_animator_state`, `add_timeline_track`, `set_animation_curve`) and nothing
at all for VFX Graph. It is reachable only by reflection into `internal` types.

`Graphics.RenderMeshInstanced` gives the same single instanced draw, and puts
the lattice math in C# where it is asserted rather than eyeballed.

**The switch cost one file.** `BackdropParameters`, `ShuffleBag`,
`BackdropRandomizer`, `HistoryRing`, `BackdropRanges`, `BackdropPresetBook` and
all four drivers were untouched — they never knew what the renderer was. That is
the instrument/driver split paying for itself.

`backDrop.vfx` survives as an empty default graph. Delete it whenever.

## Architecture

Same split as `PinDensityController` and `PoseInstrument`: **the instrument owns
all state, dumb drivers feed it.** That is what let the whole system be built
and judged before any hardware was attached.

| File | Role |
|---|---|
| `Core/BackdropParameters.cs` | Every value, one struct. Inspector surface, preset unit, randomiser output, history entry. |
| `Core/BackdropLattice.cs` | All the math. Pure, static, Unity-object-free — **the tested surface.** |
| `Core/ShuffleBag.cs` | Seeded non-repeating cycler. |
| `Core/BackdropRanges.cs` | The box the randomiser samples inside, plus per-parameter locks. |
| `Core/BackdropRandomizer.cs` | `Randomize` finds a region, `Mutate` refines inside it. |
| `Core/BackdropPresetBook.cs` | Named saved parameter sets. |
| `Core/HistoryRing.cs` | 20-deep undo. |
| `BackdropInstrument.cs` | Sole owner of state, sole issuer of the draw. |
| `BackdropLibrary.cs` | Mesh set + folder scan with a triangle guard. |
| `KeyboardBackdropDriver.cs` | Keys 1-4, D. |
| `MidiFighterBackdropDriver.cs` | MF64 pads. Unbound by default. |
| `MidiMixBackdropDriver.cs` | 33 MIDI Mix controls. |
| `BackdropExplorerDriver.cs` | Space / M / arrows / S / F1-9. |

`Core/` is its own asmdef because **asmdefs cannot reference `Assembly-CSharp`**,
so an EditMode test assembly cannot see code loose under `Assets/`. The
MonoBehaviours stay loose and still see `Core` — `Assembly-CSharp` auto-references
every asmdef.

## Camera fit

The field sits in front of the camera facing back, with X and Y taken from the
frame. Aspect comes from `Camera.aspect`, so any ratio works.

**Measured at the far face, not the near one.** A perspective frustum widens
with distance; fitting at the near face leaves the back of the field inside the
frame with visibly empty corners.

Depth (`domainSize.z`) stays authored — the frame cannot imply it.

Fit is applied to a **copy** in `Update`, never written back through `Apply`.
Folding it into stored state would overwrite the authored `domainSize` and bump
`Revision` every frame, which the explorer would read as an endless stream of
external edits.

## Domains

All three share one convention: **XY is the screen plane, Z is depth away from
the camera.**

| Domain | Distribution |
|---|---|
| Plane | N x M grid, N chosen so cells stay near-square on the requested aspect |
| Hemisphere | Fibonacci spherical cap, **pole along +Z** so the dome faces the viewer |
| Cube | Per-axis lattice, hollow shell unless `solidFill` |

The hemisphere pole was +Y originally, which presented the dome edge-on and
wasted most of the width. The golden angle is what keeps points from clustering
at the pole — the same reason `SplitScreenCameraRig` samples a cap this way.

### Cube resolution is per axis — do not revert to a shared N

`AxisCounts` derives resolution per axis from that axis's length. A shared N is
only correct for an actual cube, and **a camera-fitted domain never is**: at
163 x 61 x 22 a shared N spaced instances 6.8 apart across and 1.4 through. Per
axis the same field resolves to 24 x 9 x 4 — 6.8 / 6.8 / 5.5.

Rounding can leave the product short of the count, which makes the index wrap
and stack instances invisibly. `AxisCounts` grows the longest axis until there
is room; a test covers it.

## Occupancy — what stops it reading as a grid

A fully populated lattice reads as a grid however the instances are shaped.
`occupancy` is the fraction of slots that carry one.

**It filters a fixed lattice rather than rebuilding a smaller one**, so lowering
it removes instances without moving the survivors — the same property the pin
scatter depends on, and what makes it playable rather than another reshuffle. A
test asserts every thinned position still exists in the full field.

`occupancyNoiseScale` clusters the gaps through a noise field. **Evenly
scattered holes read as damage; clustered holes read as voids.** The threshold
is nudged per-slot so its edge is ragged rather than a clean contour, which
would read as a machined cut.

## Size accents — the Fibonacci hierarchy

A field drawn from one continuous scale range has **no hierarchy**: every
instance reads as the same object at a different distance and the eye has
nothing to measure against.

`accentFraction` promotes a minority to a discrete size class at `accentRatio`
(default **1.618**), so each class stands to the next as consecutive Fibonacci
terms do. `accentSteps` allows climbing more than one step, with higher steps
progressively rarer — many mid-rise, a few towers, one spire.

Accents draw from **their own hash stream**, so they stay put while the base
scale range is dialled. `LocalBounds` accounts for them: they are the largest
objects in the field and therefore exactly the ones that would pop at a glancing
angle if culling ignored them.

## Motion — sine or noise

A mode, not a blend.

- **Sine** — one shared sine, phase-spread across the field. Reads as a wave passing through. Runs along `waveAxis`.
- **Noise** — a 3D field sampled at each instance's own position, so neighbours move together and the field drifts as a body. Displaces in all three axes; `waveAxis` is unused.

**Both are absolute offsets from the lattice point, never accumulating adds.**
An accumulating add looks fine for seconds and then walks the whole field away
permanently. A test runs 100 wave cycles and asserts the rest pose is unchanged.

## Randomisation — why uniform sampling failed

Uniform sampling across ~20 parameters **regresses to the mean**: the odds of
several landing near an extreme are vanishingly small, so every roll is
mid-range on everything. That reads as one look with different noise, and the
interesting corners of the space are unreachable no matter how long you press
Space.

| Control | Default | Effect |
|---|---:|---|
| `extremeChance` | 0.40 | odds a parameter snaps to an end of its range |
| `maxVsMin` | 0.50 | which end — below 0.5 favours sparse/small/slow |
| `holdChance` | 0.15 | odds a parameter keeps its current value |

`holdChance` applies to the discrete parameters too. Domain and shading are the
loudest changes on screen; flipping them every roll drowns out whatever the
continuous parameters did.

Measured over 40 rolls: scale bias Y spans its full 0.5-8.0 range and spawn
count holds about a fifth of the time. Under uniform it clustered mid-range and
never held.

### Proportion and orientation are single decisions

Three independent per-axis draws almost never produce a decisive field: you get
1.3 / 2.1 / 0.8, which reads as mush rather than as towers or slabs. Both are now
one decision for the whole field.

**Proportion** rolls uniform against stretched, and if stretched picks one axis
and one magnitude. Y is weighted heaviest — standing forms read as architecture
where a stretched X or Z reads as debris — but all three stay reachable.
Measured over 200 rolls: 66 uniform, 71 towers, 40 slabs, 23 fins.

**Orientation** rolls aligned against tumbled. Aligned means oriented *to the
shape* — radial on the arch, outward on the dome, along the face normal on the
cube and corridor — not merely zeroed; the flat and box-like domains have no
orientation of their own and read as axis-aligned. Tumbled jitters all three
axes by the same amount. The in-between, one axis jittered and the others not,
is no longer reachable by chance.

`Mutate` scales the existing proportion rather than re-picking the axis, since
changing which axis dominates is a jump out of the region.

### Two invariants with tests behind them

**Every parameter consumes exactly three RNG draws before any branch.** The
count must not depend on the outcome, or locking one parameter silently shifts
every parameter drawn after it — so ticking a lock would change things you never
touched.

**A held value is clamped into range, not passed through.** Otherwise a value
pushed out of range by a MIDI knob survives every future roll, and `Randomize`
stops honouring the ranges asset that is also the performance rail.

`Mutate` never snaps to extremes. Refining a look should not jump out of the
region — that is what `Randomize` is for.

## Shading

Uses **`Assets/mixamoDance/newDUG/URP_NoiseDisplacement_Toon.shader`**, the same
shader as the dancer. An earlier hand-rolled `BackdropShading.shader` was deleted:
it was a worse reimplementation missing the outline pass, displacement, shadow
casting and depth-normals. The missing outlines gave it away.

Three additive changes so it serves both:

- **`#pragma multi_compile_instancing` on all five passes.** The instance-ID macros were already present, but without the pragma Unity compiles no instanced variants and `RenderMeshInstanced` has nothing to bind to. This is the trap: the shader *looked* instancing-ready.
- **Per-instance `_Flash`** in an instancing buffer.
- **`_ShadingMode`** — 0 toon, 1 toon + fresnel rim, 2 lit. Lit **lerps the existing cel term to a smooth ramp** rather than branching to a separate PBR path, so shadow colour, specular and the light loop stay shared and the modes cannot drift apart.

Every default preserves prior behaviour, so `newDUG.mat` on the dancer is
unaffected.

### Ambient occlusion

The shader never declared `_SCREEN_SPACE_OCCLUSION`, so URP compiled no SSAO
variant and **the AO texture was never sampled** — every frame of AO computed
and discarded, for the dancer as well as the backdrop. It *did* write
DepthNormals, so it was contributing occlusion while receiving none.

Now declared, and applied to the **ambient term only**. Folding AO into direct
light smears a soft gradient across the cel bands and undoes the point of a toon
shader. `_OcclusionStrength` dials it back.

**Both AO systems are active on `PC_Renderer`** — HTrace at intensity 3 and URP's
own at 0.4. They will now stack. Worth picking one; not changed here because it
affects the whole project.

## MIDI Mix — one concept per channel strip

The hardware is eight vertical strips, three knobs above a fader. The eye reads
a column, not a row. An earlier layout grouped by category across knob *rows* and
put the axis vectors — the least performable values on the panel — on the faders.

| Ch | Fader | Knob 1 | Knob 2 | Knob 3 | Mute |
|---|---|---|---|---|---|
| 1 | Spawn Count | Fit Margin X | Fit Margin Y | Depth | Plane |
| 2 | Scale | Bias X | **Bias Y** | Accent % | Hemisphere |
| 3 | Offset Amount | Offset X | Offset Y | Gap Cluster | Cube |
| 4 | Rotation Amount | Rot X | Rot Y | Rot Z | Toon |
| 5 | Spin Rate | Axis X | Axis Y | Axis Z | Fresnel |
| 6 | Wave Amplitude | Axis X | Axis Y | Axis Z | Lit |
| 7 | Wave Frequency | Phase Spread | Noise Scale | Scale Spread | Solid fill |
| 8 | Flash Intensity | Hue | Saturation | Value | **Flash** |

**Master fader: Occupancy.** **Rec-arm 1: sine / noise motion** — a row that was
otherwise unbound, and a mode switch suits a button where a knob reads as dead
through half its travel.

Bias Y (ch2 knob 2) is the tower dial. Channels run coarse to fine left to
right. Channel 8 pairs its fader with its mute: ride the flash, fire it below.

Flash decay and ripple are **not** on the surface — inspector and randomiser
only. The flash gestures that matter are intensity and trigger.

### Soft takeover is load-bearing

The MIDI Mix sends **absolute CC**, so after a randomise the physical controls
match nothing. Without takeover, the first touch snaps that parameter to
wherever the knob is sitting and silently destroys part of the roll. A control
does nothing until swept through the live value.

Any change from another source — Space, M, a preset, a pad — resets every
control's caught state, because they are all stale at once.

### Three derived controls

The faders need a single magnitude to ride, but the values are `Vector3`s.

- `OffsetAmount` / `RotationAmount` **scale the vector the knobs shaped** rather than overwriting it, so proportions survive a fader move. A zero vector has no proportions to preserve and goes uniform, or the fader would be dead until a knob was touched.
- `ScaleSpread` replaces the raw scale minimum, which is meaningless without knowing the maximum.

### Fit controls, not domain sizes

Channel 1 knobs 1 and 2 drive `fitMargin`, **not** `domainSize.x/y`. With fit on
those are derived from the frame every frame, so a knob bound to them would be a
control that visibly does nothing.

## Keyboard

| Key | Action | | Key | Action |
|---|---|---|---|---|
| `1` | Reroll layout | | `Space` | Randomize |
| `2` | Cycle shading | | `M` | Mutate |
| `3` | Flash | | `←` `→` | History |
| `4` | Next mesh | | `S` | Save preset |
| `D` | Next domain | | `F1`-`F9` | Recall preset |

Preset recall is **F1-F9, not the digits** — the digits belong to the
performance driver, and both components sit on the same GameObject. Pressing `1`
would otherwise recall a preset and reroll the layout on top of it.

## Performance

Measured in the editor, so **draw calls and triangles are trustworthy; frame
times are not.**

| | draws | batches | triangles |
|---|---:|---:|---:|
| Outline on | 23 | 3 | 58,024 |
| Outline off | 22 | 2 | 39,424 |

The **outline is an inverted-hull pass** — a second full draw with `Cull Front`.
+47% triangles for one extra draw call, and it is what makes the forms legible.
One instanced batch, so the CPU cost is negligible.

**Unused shader features cost nothing.** Displacement is behind
`#ifdef _DISPLACEMENT_ON`; with the keyword off the compiled variant contains no
noise, no texture sample, no instructions. Same for the DBuffer decal block,
lightmaps and Forward+. Only build-time variant count is affected.

**Split-screen is the case to watch.** This project's measured curve is linear in
`instances x cells`, and the outline pass doubles the geometry half of it. At 8
cells it is paid eight times. A screen-space edge detect would scale far better;
not worth doing until the mosaic case is real.

## Traps

- **`recompile_status` lies here.** `write_text_file` imports as it writes, so the compile already happened and `recompile` is a no-op reporting `up_to_date / failed: false` while real errors sit in the console. Gate on `get_console_logs --severity error`. Eight tasks hit this.
- **`run_tests --mode editor`**, not `EditMode`. The live schema is `all | editor | playmode`.
- **`eval_file --file <path>`**, and the file must be **bare statements** — no `using`, no class or method wrapper. It is spliced into a method body.
- **A dirty scene wedges the CLI.** `run_tests` raises Unity's modal "Scene(s) Have Been Modified" prompt, and dismissing it jams the main-thread pump: `unity status` still says ready while every command times out. Recovery needs a human — focus Unity, Ctrl+R, resolve the scene.
- **Two editors may be running.** This project is port 7801; `eventGameToolKit` holds 7800. `unity` auto-detects from the working directory.
- **A driver with no router does nothing, silently.** `MidiMixRouter`'s events are static, so a driver subscribes and compiles happily whether or not anything raises them. The scene needs the package's `MIDI Controller` prefab. Allow-list `Fighter` / `MIDI Mix`, blocked list empty.

## Where to pick up

1. **Hardware pass.** 33 MIDI Mix bindings and soft takeover, unexercised.
2. **Build measurement.** No real frame time exists for the backdrop.
3. **Two AO systems are stacked.** Pick one.
4. **MF64 pads are unbound** — `MidiFighterBackdropDriver` defaults to row 0 / col 0 throughout.
5. **Into the main scene.** Reserve any pads used, and decide whether the backdrop is `Always Active` against `FloorVisibilityController`.
6. **The randomiser cannot produce anisotropic jitter, non-Y rotation, or asymmetric spin** — it writes each from one scalar. A hand-authored preset can hold those values and a re-randomise discards them.
7. **Emission hue mutates from a hardcoded 0.5 saturation**, because `BackdropParameters` stores RGB with no HSV field. Refining a saturated colour drifts it toward mid-saturation.
