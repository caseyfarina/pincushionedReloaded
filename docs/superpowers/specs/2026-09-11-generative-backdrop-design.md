# Generative backdrop — design

**Date:** 2026-09-11
**Status:** Approved design, pending implementation plan

A VFX Graph system that fills the space behind the pincushioned dancers with an
algorithmically distributed array of meshes — towers, walls, architectural
elements. Four MF64 pads reshape it instantly during a performance. A companion
exploration scene searches the parameter space for looks worth keeping.

## Phasing

Built in two phases. Phase one ends with a playable backdrop; phase two adds the
search tool on top and changes nothing about phase one's behaviour.

**Phase 1 — the instrument.** `BackdropParameters`, `BackdropInstrument`,
`BackdropLibrary` + its scan button, `Backdrop.vfx`, `BackdropShading`,
`KeyboardBackdropDriver`, `MidiFighterBackdropDriver`. Deliverable: a backdrop
that distributes, animates, and responds to all four functions, playable from
the keyboard.

**Phase 2 — the search.** `BackdropRanges`, `BackdropPresetBook`,
`BackdropRandomizer` + its EditMode tests, `BackdropExplorerDriver`,
`BackdropExplore.unity`. Deliverable: randomise / mutate / history / save.

The dependency runs one way only: phase two consumes `BackdropParameters` and
`BackdropInstrument.Apply()`, both defined in phase one.

## Scope

**In:** a camera-anchored backdrop volume; even lattice distribution with
per-instance randomisation and animation; whole-field mesh swap; three shading
models; an emission flash; a preset/randomise/save exploration rig.

**Out:** per-instance mixed meshes (designed for, not built); audio reactivity
(the parameter surface accommodates it later); integration into the main scene's
pad map — pad assignment is being reworked wholesale and is not this design's
concern.

## Placement

A **separate backdrop layer**, independent of the 8-floor building and its
`FloorVisibilityController`. Always visible, behind whatever is on screen.

The distribution domain is **camera-anchored** and selectable:

| Domain | Even distribution | Split-screen safe |
|---|---|---|
| Plane | N x M grid, perpendicular to camera | **No** — sub-cameras at 90 degrees see it edge-on |
| Hemisphere | Fibonacci spherical cap around camera view | Yes |
| Cube | N x M x K lattice on the shell (`Solid Fill` toggle for the volume) | Yes |

The plane's split-screen weakness is accepted, not fixed: it is a main-camera
look. Cube and hemisphere survive a mosaic reroll.

Simulation is **local space** so instances hold position relative to the anchor
as the camera moves. Note this is the opposite of `skinnedMesh.vfx`, which must
simulate in world space so particles are left behind by the dancer.

## Approach: all VFX Graph

Chosen over a `Graphics.RenderMeshInstanced` path mirroring `MeshSurfaceScatter`.

For a *static* field the two are equivalent — both bottom out in the same
instanced draw, and the measured driver on this project is draw calls x cells,
not how the matrices were produced. The deciding factor is that this backdrop is
**continuously alive**: `Rotation Speed` and `Offset Wave` are per-frame,
per-instance values. VFX Graph computes them on GPU from `(instanceID, seed,
time)` for free; the C# path would re-fill the matrix array every frame.

Accepted costs of this choice:

- The `.vfx` asset is opaque binary — not diffable, not reviewable, not testable.
- Exposed properties are accessed by string name.
- Layout randomness uses VFX's GPU RNG, not `System.Random`. Reproducibility
  rests on VFX determinism for a given seed. **All other randomness in the system
  — the preset randomiser, shading and mesh cycling — uses seeded
  `System.Random` per project convention.**
- Three genuinely different shading models cannot be three `Material`
  assignments. They are branched inside one Shader Graph (see below).

## Components

| File | Role |
|---|---|
| `BackdropInstrument.cs` | Single owner of all state. `Apply(BackdropParameters)` is the only write path to the graph. **No MIDI knowledge.** |
| `BackdropParameters.cs` | `[Serializable]` struct — every parameter, one place. The unit of presets, randomisation and history. |
| `BackdropLibrary.cs` (SO) | Mesh set + per-mesh scale/orientation overrides. Populated by an editor folder-scan button. |
| `BackdropRanges.cs` (SO) | Min/max **plus a lock flag** per parameter. The box the randomiser samples inside. |
| `BackdropPresetBook.cs` (SO) | Named list of saved `BackdropParameters`. One asset, not one file per preset. |
| `BackdropRandomizer.cs` | Pure static, Unity-free: `(ranges, System.Random, base, mutationStrength) -> BackdropParameters`. |
| `KeyboardBackdropDriver.cs` | Keys -> instrument. Lets the system be built and judged with no hardware. |
| `MidiFighterBackdropDriver.cs` | Pads -> instrument, LED feedback. |
| `BackdropExplorerDriver.cs` | Exploration keyboard + history ring. |
| `Backdrop.vfx` | One particle system, one mesh output. |
| `BackdropShading.shadergraph` | VFX target; toon / fresnel-toon / lit branched on mode. |
| `BackdropExplore.unity` | The exploration scene. |

This is the instrument/driver split already proven twice here by
`PinDensityController` and `PoseInstrument`: the instrument owns the state, dumb
drivers feed it, the graph only renders. It is what allows the whole system to be
developed and verified without an MF64 attached.

### "A folder of meshes" must become an authored list

Runtime folder scanning is `AssetDatabase`, which is editor-only and vanishes in
a build — the same reason `Samples/Resources/` exists in this project. So
`BackdropLibrary` holds explicit mesh references, populated by an editor button
that scans a folder. The authoring gesture is still "drop FBXs in a folder, press
one button"; the ScriptableObject is what ships.

That scan button also **warns above a few thousand triangles per mesh**. Dropping
a 50k-triangle artifact scan into the folder and multiplying it by instances x
cells will read as "VFX Graph is slow" rather than as an authoring mistake.

## Distribution model

An **even lattice on the domain surface**, then per-instance modifiers layered on
top — Blender array modifier in shape, not random scatter.

Per instance, all derived from `(instanceID, seed)`:

| Modifier | Parameters |
|---|---|
| Random scale | min/max; uniform or per-axis |
| Random offset | jitter amount per axis |
| Random rotation | amount per axis |
| Rotation speed | per-instance randomised spin rate |
| Offset wave | sin displacement: axis, amplitude, frequency, per-instance phase spread |

The last two are the per-frame work that justified the VFX Graph choice.

## C# to graph interface

Deliberately small. Every exposed property appears in `BackdropParameters`, which
is what makes the preset system a straight serialisation of the graph's inputs.

| Exposed property | Type | Written when |
|---|---|---|
| `LayoutSeed` | uint | layout reroll |
| `DomainShape` | int | 0 plane / 1 hemisphere / 2 cube |
| `DomainSize` | Vector3 | parameters |
| `SolidFill` | bool | parameters (cube only) |
| `SpawnCount` | int | parameters; capacity is fixed at author time — set to the ceiling |
| `InstanceMesh` | Mesh | mesh pad |
| `ShadingMode` | int | shader pad |
| `FlashTime` | float | flash pad |
| Scale / offset / rotation / spin / wave | floats, Vector3s | parameters |

**`FlashTime` is a timestamp, not a level.** The pad writes `Time.time` once; the
graph computes `exp(-(t - FlashTime) * k)` per particle, offset by instance ID so
the flash ripples across the field rather than blinking flat. Zero per-frame C#,
and the stagger is free.

## The four performance functions

| Function | Method | Writes | Reinits |
|---|---|---|---|
| New layout | `RerollLayout()` | new `LayoutSeed` | **Yes** |
| Swap shading | `CycleShading()` | `ShadingMode` | No |
| Flash emission | `Flash()` | `FlashTime` | No |
| Next mesh | `CycleMesh()` | `InstanceMesh` | No |

**Only reroll reinits.** Positions are computed in Initialize, so a seed change
only affects newly spawned particles — `Reinit()` is the instant re-layout, and
it resets drift phase and any in-flight flash, which is correct for a reroll.
Everything else leaves the composition intact, so a look can be found once and
then played against: reroll into an arrangement, then work shader, mesh and flash
over it.

**Cycling is shuffled, not random.** With three shading models, uniform random
repeats roughly a third of the time, and a third of presses doing nothing visible
reads as a dead button. Shading and mesh advance through a shuffled order from a
seeded `System.Random`: every press changes something, without feeling
sequential.

**Domain shape is an inspector enum**, not one of the four pads — it is a staging
decision more than a performance gesture. Trivially bindable later.

## Preset exploration

The premise: **a randomiser needs authored ranges, or nearly all its output is
garbage.** Hence two data objects — `BackdropPresetBook` holds points in the
space, `BackdropRanges` defines the box they are sampled from. Tightening ranges
as the search proceeds is how it converges.

| Key | Action |
|---|---|
| Space | Full randomise within ranges |
| M | **Mutate** current at ~10% |
| Left / Right | Walk a 20-deep history ring |
| S | Save current into the preset book, named |
| 1-9 | Recall a saved preset |

Three properties that carry most of the value:

- **Locks make the search converge.** Once a scale range reads well, lock it and
  randomise only what is still open. Without locks, settled ground is
  re-explored on every press.
- **Mutate versus randomise is the difference between hours and minutes.** Pure
  random finds regions; it never refines. If scope has to shrink, this is the
  last feature to cut.
- **History recovers the good one you lost.** A 20-deep ring buffer costs
  nothing and rescues the press-before-last that read well.

The scene also shows a live parameter readout, so a look on screen can be
inspected before saving.

## Performance

**Expected operating scale is hundreds of instances, not thousands.** At ~300
instances across 8 split-screen cells that is ~2,400 instance-renders, which
against this project's measured 9.17 ms build baseline is noise. This is not a
performance-constrained design.

The guards stay anyway, because the randomiser can run away and because each
costs one field:

- **Shadow casting off** on the backdrop output. Shadow casters drove the entire
  dancer sweep curve.
- **Explicit VFX bounds** covering the domain, or the field is permanently
  unculled — the same failure mode as `MeshSurfaceScatter`'s 1000-unit cube.
- **Triangle-count warning** in the library scan button.
- **`SpawnCount` and `DomainSize` ranges are the ceiling.** Set them for
  hundreds; raising them is a deliberate act followed by a measurement.

The governing shape, from the dancer sweep: cost is linear in `instances x
cells`, driven by draw calls and shadow casters — not by animation or by how
matrices are produced.

## Verification

- `unity command recompile && unity command recompile_status` for CS errors.
- **EditMode tests on `BackdropRandomizer`** — pure, seeded, Unity-free. Same
  seed gives identical parameters; locked parameters are untouched; mutation
  stays inside range; output never exceeds the count ceiling. This is the only
  component with a real automated test surface; everything downstream is a GPU
  shader judged by eye.
- **`BackdropExplore.unity` is the visual verification**, and most of why it is
  worth building.
- **Measure in a build, never the editor** — established at 5.2x inflation on
  this project. Three configs: 1 camera, 8 cells, 8 cells at the range ceiling.

Known trap: an unfocused editor does not tick, so CLI screenshots will show the
backdrop unrendered. Same capture artifact as the pins. Judge with the editor
focused.

## Risks

1. ~~**VFX Graph has never rendered here, and the project is hard-locked to
   D3D11**~~ — **CLOSED 2026-09-11.** VFX Graph was exercised against the
   Learning Templates samples in this project and works. D3D11 supplies the
   compute shaders it needs. No smoke test required.
2. **The Shader Graph needs its VFX target enabled** at author time, and a lit
   VFX output under URP has its own quirks. Stand up a one-cube version of the
   shader before building anything around it.
