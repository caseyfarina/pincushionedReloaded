# Mesh Surface Scatter System

## Overview

A GPU-instanced mesh scattering system for Unity 6.3 URP that distributes mesh variants across the surface of target meshes with a pin-cushion visual effect. Supports density-controlled clustering via three selectable modes: UV-mapped texture, world-space procedural noise, and point attractors. Designed for high instance counts (thousands) with minimal draw calls and zero GameObject overhead.

The system uses a two-phase architecture: **offline baking** of surface sample points (position + normal + UV), then **runtime scattering** that selects from the baked pool using density-weighted probability and renders via `Graphics.RenderMeshInstanced`.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        EDITOR TIME                              │
│                                                                 │
│  Source Meshes (FBX/OBJ/GLTF folder)                           │
│       │                                                         │
│       ▼                                                         │
│  BatchScatterProcessorWindow                                    │
│       │  - Area-weighted triangle sampling                      │
│       │  - Barycentric interpolation of position + normal + UV  │
│       │  - All data in mesh local space                         │
│       │                                                         │
│       ├──▶ SurfaceSampleData (.asset)  [per mesh]               │
│       │      positions: Vector3[]  (local space)                │
│       │      normals:   Vector3[]  (local space)                │
│       │      uvs:       Vector2[]  (from mesh UV0)              │
│       │      ~32 bytes per sample                               │
│       │                                                         │
│       └──▶ Prefab (.prefab)  [per mesh]                         │
│              MeshFilter + MeshRenderer (surface visuals)        │
│              MeshSurfaceScatter (wired to SurfaceSampleData)    │
│              Optional: default PinVariants pre-assigned         │
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│                        RUNTIME                                  │
│                                                                 │
│  MeshSurfaceScatter.Scatter()                                   │
│       │                                                         │
│       ├── Transform all samples to world space                  │
│       ├── ScatterDensity.EvaluateAll()                          │
│       │     ├── Uniform: all weights = 1 (Fisher-Yates path)   │
│       │     ├── Texture: UV lookup into grayscale density map   │
│       │     ├── Noise: fractal Perlin (pseudo-3D, world space) │
│       │     └── Attractor: distance falloff from transforms     │
│       │                                                         │
│       ├── Uniform mode:  shuffle + take first N                 │
│       │   Weighted mode: cumulative weight table + binary search│
│       │                  with HashSet dedup                     │
│       │                                                         │
│       ├── Per instance: TRS matrix build                        │
│       │     rotation = normal alignment + yaw + tilt            │
│       │     scale = lerp(baseRandom, densityModulated, blend)   │
│       │                                                         │
│       └── Batch into Matrix4x4[1023] arrays per variant         │
│                                                                 │
│  MeshSurfaceScatter.Render()  [every Update]                    │
│       └── Graphics.RenderMeshInstanced per variant per batch    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## File Manifest

| File | Unity Location | Purpose |
|---|---|---|
| `MeshSurfaceScatter.cs` | `Assets/` (any non-Editor folder) | Runtime MonoBehaviour — density-weighted scatter + GPU instanced rendering |
| `SurfaceSampleData.cs` | `Assets/` (any non-Editor folder) | ScriptableObject — baked surface sample pool (pos + normal + uv) |
| `ScatterDensity.cs` | `Assets/` (any non-Editor folder) | Serializable density evaluator — texture, noise, and attractor modes |
| `Editor/BatchScatterProcessorWindow.cs` | `Assets/Editor/` | EditorWindow — batch bakes samples + creates prefabs for a folder of meshes |
| `Editor/MeshSurfaceScatterEditor.cs` | `Assets/Editor/` | Custom inspector — runtime controls, quick count presets, batch processor link |

## Key Design Decisions

### Rendering: `Graphics.RenderMeshInstanced`
No Transform components, no per-object overhead, no GC pressure. One draw call per 1023 instances per mesh/material pair. At 5000 pins with 3 variants, roughly 15 draw calls total.

### Two-phase bake/scatter separation
Expensive surface sampling math (cumulative area table, binary search, barycentric interpolation) runs once in the editor. Runtime `Scatter()` indexes into flat arrays and builds TRS matrices. The density evaluation is the only non-trivial runtime cost, and `EvaluateAll()` is batch-optimized (texture mode reads pixels once into a flat array to avoid per-sample `GetPixelBilinear` overhead).

### What is baked vs what is runtime
- **Baked (SurfaceSampleData):** Position, normal, and UV per sample in mesh local space. This is the triangle sampling math.
- **Runtime (Scatter()):** Density evaluation, weighted selection, local-to-world transform, variant assignment, rotation, scale (including density modulation), normal offset. These all depend on parameters the user adjusts at runtime.

### Density-weighted selection (non-uniform mode)
Replaces Fisher-Yates shuffle with a cumulative weight table and binary search. Each baked sample's weight comes from `ScatterDensity.Evaluate()`. Higher weight = higher selection probability. A `HashSet<int>` prevents duplicate selection. The `maxAttempts` cap (4x requested count) prevents infinite loops when most of the pool is zero-weight (heavy cutoff or small attractor radii). If the density distribution is very peaked, fewer unique samples may exist than requested — the system places as many as it can find.

### Uniform mode preserved
When `DensityMode.Uniform` is selected, the system bypasses density evaluation entirely and uses the original Fisher-Yates shuffle path. No weight arrays allocated, no cumulative table built. Zero overhead when density isn't needed.

### Scale modulation via `scaleByDensity`
The `scaleByDensity` parameter (0–1) blends between pure random scale and density-driven scale. At 0, scale is entirely random within `scaleRange`. At 1, scale maps directly to the density weight (high density = `scaleRange.y`, zero density = `scaleRange.x`). Intermediate values blend linearly. This means high-concentration areas can have both more pins and larger pins, reinforcing the visual clustering.

### Local-space baking with UV
Sample data stores everything in mesh local space. The runtime applies `localToWorldMatrix` so scattered instances follow surface transforms. UVs are baked via barycentric interpolation of vertex UVs and are required for texture density mode. The baker gracefully handles meshes without UVs (sets `hasUVs = false`, noise and attractor modes still work).

### Pseudo-3D noise from 2D Perlin
Unity's `Mathf.PerlinNoise` is 2D only. The noise evaluator combines three 2D samples across XZ, XY, and YZ planes with offset magic numbers to produce pseudo-3D variation. This works well for surfaces with varied orientations but may produce visible patterns on perfectly planar surfaces. For those cases, texture mode is preferable.

## Density Modes (ScatterDensity)

### Uniform
All samples weighted equally. Original Fisher-Yates behavior. No density evaluation cost.

### Texture
Samples a grayscale `Texture2D` using the baked UV coordinates. White (1.0) = full density, black (0.0) = no placement. Requires UVs on the source mesh (logged as warning in batch processor results if missing). The texture must have Read/Write enabled in import settings. Batch evaluation reads all pixels once via `GetPixels()` and does manual bilinear sampling to avoid per-sample API overhead.

### Noise
World-space fractal Perlin noise. Parameters: `noiseScale` (frequency — smaller value = larger blobs), `noiseOffset` (world-space translation — animate this to shift clusters), `noiseOctaves` (1–6, more = finer detail layered on top), `noisePersistence` (0–1, how much each octave contributes). Fully procedural, no textures or UVs required.

### Attractor
Proximity-based clustering around `Transform[]` references. Each attractor has a shared `attractorRadius` and `attractorFalloff` curve. Falloff of 1 = linear, 2 = quadratic (tighter clusters), 0.5 = square root (softer spread). The `attractorFloor` (0–1) controls minimum density outside all radii — set to 0 for hard cutoff or >0 for sparse background scatter. Attractor transforms can be moved at runtime; call `Scatter()` after repositioning. Max influence (not additive) is used when radii overlap, preventing blow-up at intersections.

### Global post-processing (all modes)
- **contrast** (0.1–5): Power curve applied after evaluation. >1 sharpens peaks (more extreme clustering), <1 flattens (more even with mild bias).
- **cutoff** (0–1): Weights below this threshold clamp to zero. Acts as a hard exclusion zone.
- **invert**: Flips the density map. Useful for "scatter everywhere except here" patterns without repainting textures.

## Artistic defaults (established 2026-09-04)

The pin look is defined by four settings that work together. All 19 `*_Pinned`
prefabs carry these explicitly:

| Setting | Value | Why |
|---|---|---|
| `Scale Mode` | `RelativeToModel` | Pin size as a fraction of the model's bounding-sphere radius, normalised per pin mesh. Scale-invariant across models. |
| `Relative Scale Range` | `0.03` – `0.20` | 3%–20% of model radius. |
| `Scale Bias` | `3` | Ease-out: most pins near the minimum, few reaching maximum. |
| `Max Tilt Angle` | `12` | Breaks perfect normal alignment so the field reads hand-placed, not mechanical. |

**With an ease-out bias the MINIMUM dominates the look, not the maximum.** Median
pin size is roughly `min + (max-min)/2^bias` — at bias 3 over 0.03–0.20 the
typical pin is about 5% of radius. Tune the min and the bias together; reaching
for the max does little because few pins get there by design.

**Scale Mode replaced hand-tuned absolute values, and the drift it fixed was
severe.** The previous per-model absolute values spanned roughly 4,930x
(0.0016 to 7.73) across models whose radii varied only 1.5x (0.517–0.773) — so
the spread was never explained by model size, it was accumulated inconsistency.
Expressed as a fraction of radius the old values ran from 0.1% to 395%. Two
models (Allosaurus, Stegosaurus) sat at 0.1% and were effectively invisible.

`Absolute` mode is retained for anything deliberately hand-tuned, but new work
should use `RelativeToModel`.

**Tune in `Assets/Scenes/PinDebug.unity`**, not the main scene. It lays every
pinned mesh in a grid under one set of controls, which is the only reliable way
to judge a value that has to work across all of them. It also carries no MIDI
rig, so recompiling there cannot crash the editor.

## Inspector Parameters (MeshSurfaceScatter)

### Precomputed Surface Data
- **Sample Data** — `SurfaceSampleData` ScriptableObject. One per target mesh.
- **Surface Transform** — Transform for local-to-world. Falls back to `this.transform`.

### Pin Meshes
- **Pin Variants** — Array of `PinVariant` (Mesh, Material, weight). Weight is relative probability.

### Scatter Settings
- **Scatter Count** (0–10000) — Instance target. Clamped to pool size.
- **Seed** — Deterministic random seed.

### Pin Transform
- **Scale Range** (Vector2) — Min/max uniform scale per instance.
- **Normal Offset** (float) — Displacement along surface normal.
- **Random Yaw Rotation** (bool) — Random spin around normal axis.
- **Max Tilt Angle** (0–45°) — Random deviation from normal.

### Density
- **Mode** — Uniform / Texture / Noise / Attractor.
- **Scale By Density** (0–1) — Blend factor. 0 = random scale only. 1 = scale driven by density weight.
- Mode-specific parameters (see Density Modes section above).

### Rendering
- **Cast Shadows** — ShadowCastingMode.
- **Receive Shadows** — Boolean.
- **Rendering Layer Mask** — URP layer mask.

## Public API (MeshSurfaceScatter)

```csharp
void SetCount(int count)               // Set count + rescatter
void RandomizeAndScatter()             // New seed + rescatter
void SetSeed(int newSeed)              // Set seed + rescatter
void SetSampleData(SurfaceSampleData)  // Swap surface + rescatter
void ForceRescatter()                  // Rescatter with current params
ScatterDensity Density { get; }        // Access density config; call ForceRescatter() after changes
```

## Batch Processor (Tools > Scatter > Batch Process Folder)

### Inputs
- **Source Folder** — Project folder of mesh assets (FBX, OBJ, GLTF, GLB, Blend). Handles multi-mesh FBX files.
- **Include Subfolders** — Recursive search.
- **Pool Size** — Global sample count per mesh (default 20000). ~32 bytes/sample.
- **Bake Seed** — Deterministic bake RNG seed.
- **Default Scatter Settings** — Written to each prefab's MeshSurfaceScatter.
- **Surface Material** — Optional, assigned to prefab MeshRenderer.
- **Default Pin Variants** — Optional foldout. Wired into every prefab if configured.

### Outputs
- `Assets/ScatterData/{MeshName}_SampleData.asset` — Baked pool with position + normal + UV.
- `Assets/ScatterPrefabs/{MeshName}_Scatter.prefab` — Fully wired prefab.

Results report includes UV availability per mesh. Meshes without UVs can still use Noise and Attractor density modes.

### Two things the batch processor does NOT do

**It does not create a surface material.** `CreatePrefab` assigns
`Surface Material` only if you set it in the window; otherwise the prefab's
`MeshRenderer` has a null material and the model renders untextured grey or
magenta. Every artifact needs a URP/Lit material built from its
`_BaseColor`/`_Normal`/`_MetallicSmoothness`/`_Occlusion` maps and assigned by
hand. This is the single most common reason a freshly baked artifact "looks
broken".

**Its output is `_Scatter`, not `_Pinned`.** The scenes consume
`Assets/pinnedMeshes/{name}_Pinned.prefab`. Producing one means copying the
`_Scatter` asset, raising `scatterCount` to 2000, and assigning the 6 pin
variants — `Editor/BatchPinVariantAssigner.cs` automates that last part. The
artistic fields need no changes: `MeshSurfaceScatter`'s **defaults already are**
the house look (`RelativeToModel`, 0.03-0.20, bias 3, tilt 12), so a prefab
created by this tool inherits it automatically.

Baking is fast — 25 meshes at a 20,000-sample pool took **2.8 s** total, well
inside the Unity CLI's 5 s main-thread budget. The bake is not the slow part of
an import; the FBX/texture AssetDatabase import is.

### Driving the batch processor headlessly

`BakeSamples` and `CreatePrefab` are private instance methods on the
`EditorWindow`. To run a batch from the CLI without opening the window, create
the window with `ScriptableObject.CreateInstance` and invoke them by reflection
(`BindingFlags.NonPublic | BindingFlags.Instance`). That reuses the real
sampling math instead of reimplementing it — important, because the bake must
stay identical across all artifacts for pin scale to be comparable.

## Artifact Importer (Tools > Scatter > Import Processed Artifacts)

`Editor/ArtifactImportWindow.cs`. Takes a scan2unity output folder and produces
finished, pinnable artifacts — copy into `Assets/Artifacts/`, fix import
settings, build the URP material, bake, and emit `{stem}_Pinned.prefab` with pin
variants and the house look applied. Every step is a separate toggle, so it also
works as a repair tool (re-run with only "Build materials" to rebuild them all).

Two implementation notes worth keeping:

**Configure the component on the SAVED prefab asset, not on a temporary scene
GameObject.** Building a GameObject, setting `sampleData` via `SerializedObject`,
then `SaveAsPrefabAsset` looks correct and mostly works — but the object
reference to a freshly baked `SurfaceSampleData` gets dropped, because the
`SaveAndReimport()` calls made while fixing import settings disturb the
AssetDatabase enough to discard the pending value. The result is a `_Pinned`
prefab whose `sampleData` is null while every value-type field looks right.
Save first, then edit the persisted asset.

**`SurfaceSampleBaker` updates an existing asset in place** (`CopySerialized`)
rather than delete-and-recreate, so the asset's GUID survives and every existing
reference to it keeps working.

## SurfaceSampleBaker

`SurfaceSampleBaker.Bake(mesh, poolSize, seed, assetPath, overwrite)` holds the
area-weighted sampling math. **Both** `BatchScatterProcessorWindow` and
`ArtifactImportWindow` call it, and that is deliberate: pin scale is judged by
comparing artifacts side by side, so two code paths that sampled differently
would make models silently incomparable.

`overwrite: false` reproduces the batch window's historical behaviour of calling
`AssetDatabase.GenerateUniqueAssetPath` — which is how the `_SampleData 1`
duplicates in this project appeared. The importer passes `true`.

**It lives outside `Editor/`** (in `Assets/proceduralPincushioning/`, guarded by
`#if UNITY_EDITOR`) because `BatchScatterProcessorWindow` is itself outside
`Editor/` and therefore compiles into `Assembly-CSharp`, which cannot reference
`Assembly-CSharp-Editor`. Moving the baker into `Editor/` breaks the build.

## Selection is stable under count changes

Both selection paths build their randomness from a **fresh `System.Random(seed)`**
and consume draws in a fixed order, so the first N picks are identical whether you
ask for N or N+500. With the density field held still, **changing the count adds
or removes pins from a stable ordering rather than reshuffling** — existing pins
do not move.

This is what makes the two performance controls independent: the MIDI knob can
sweep density without disturbing a composition the audio wave produced.

Verified by capture on the Giant Moa with a frozen `noiseOffset`: 400 pins to 900
and back to 400 returns a **pixel-identical frame** (max diff 0), and at 900,
**92.5%** of the 400-frame's pin pixels are still pins — the remainder is the new
pins occluding old ones, not old ones moving.

## All `*_Pinned` prefabs are DensityMode.Noise (2026-09-07)

Switched from Uniform so the audio wave has something to act on: `noiseOffset` is
never read in Uniform mode. Settings are `noiseScale 5`, `octaves 2`,
`contrast 1.4`.

**This changes the look, not just the behaviour** — noise-weighted selection makes
pins **cluster** rather than spread evenly. If the clustering reads wrong, lower
`contrast` toward 1.0 to flatten it rather than reverting to Uniform, which would
disable the wave entirely. See the audio agitation section in the root CLAUDE.md.

## PinDebug gotchas

**`PinDebugRig.RebuildGrid()` early-returns when `prefabs` is empty.** It does
*not* self-populate from `Assets/pinnedMeshes`, despite what its tooltip says —
that logic is `PinDebugSceneBuilder.LoadPinnedPrefabs()`, behind the inspector
button. Clearing the list and calling `RebuildGrid()` yields an empty scene.

**`PinDebugRig.BuildProblemReport()`** is the fastest way to validate a batch:
it names every model with no pin variants, a missing pin mesh or material, or no
sample data.

**To see pins from the CLI, enter Play mode.** This is the only reliable way.
`unity command editor_play`, wait a few seconds, then `unity command screenshot`.
A single frame is enough -- `Scatter()` runs on the first `Update()` and
`Render()` follows. Edit-mode captures show the models bare even with the
editor window focused, so an empty-looking grid is a capture artefact, not a
broken scatter.

**Two CLI status traps found 2026-09-07.** `runtime_status.IsPlaying` is
unreliable -- it reported `true` with `FrameCount` climbing well after play mode
had exited, because the player loop ticks in edit mode too. Use
`editor_status.playMode` instead, which correctly reads `stopped`. And
`editor_stop` / setting `EditorApplication.isPlaying = false` cannot take effect
while the editor is unfocused, because the change needs a frame to process --
foreground the Unity window (PowerShell `SetForegroundWindow`) and frames start
advancing.

**Pins do not render while the editor is unfocused.** Rendering happens through
`Graphics.RenderMeshInstanced` inside `[ExecuteAlways] Update()`, and an
unfocused Unity editor does not tick. CLI-captured screenshots of `PinDebug`
therefore show bare models with no pins — a capture artifact, not a fault. An
off-screen `camera.Render()` from an `eval` misses them for the same reason:
instanced submissions are per-frame.

## Requirements and Constraints

- **Unity 6.3+ with URP.**
- **Read/Write on target meshes** — Required at bake time only (for vertex/normal/UV/triangle access). Not needed at runtime.
- **Read/Write on density textures** — Required if using Texture density mode (`GetPixels()` needs CPU read access).
- **GPU Instancing on pin materials** — Material inspector checkbox. URP Lit/Simple Lit default to on.
- **worldBounds** — Set to 1000-unit cube centered on surface transform. Compute from mesh AABB if surface is very large or far from origin.
- **Max scatter count** = pool size ceiling. Increase pool at bake time if needed.
- **No physics/collision** — Instances are visual only. No colliders.
- **No per-instance interaction** — No raycast picking.
- **Weighted selection dedup** — At high scatter counts with very peaked density (e.g., small attractor radius), available unique samples may be fewer than requested. The `maxAttempts = count * 4` safety valve prevents hangs.
- **Density textures must be grayscale-interpretable.** Color textures work (`.grayscale` property) but only luminance is used.

## Extending the System

### Adding new density modes
1. Add enum value to `ScatterDensity.DensityMode`.
2. Add mode-specific parameters as serialized fields in `ScatterDensity`.
3. Implement `EvaluateMyMode(Vector3 worldPos, Vector2 uv)` private method.
4. Add case to the `switch` blocks in both `Evaluate()` and `EvaluateAll()`.
5. The rest of the pipeline (weighted selection, scale modulation, batching, rendering) is mode-agnostic.

### Adding new scatter parameters
1. Add `[SerializeField]` field to `MeshSurfaceScatter` with attributes.
2. Use it in `BuildInstance()` (per-instance loop).
3. If batch-processor configurable, add field to `BatchScatterProcessorWindow` and set via `SerializedObject.FindProperty()` in `CreatePrefab()`.

### Combining density modes
Currently one mode at a time. To combine (e.g., texture * noise), add a `DensityMode.Combined` that multiplies outputs from two sub-evaluators. The `ScatterDensity` class would need a secondary mode + blend operation field.

### Animating density at runtime
Access `scatter.Density` property, modify parameters (e.g., `Density.noiseOffset += Time.deltaTime * drift`), then call `scatter.ForceRescatter()`. For attractor mode, move the attractor transforms and rescatter. Budget: rescattering 2000–5000 instances is sub-millisecond on modern hardware for the selection + TRS build; the density evaluation pass is the variable cost.

### Raising the instance ceiling
The `[Range(0, 10000)]` attribute and batch processor slider are conservative. On a 3090, vertex count per pin mesh is the bottleneck, not instance count. For low-poly pins, increase freely. Profile with Frame Debugger to verify draw call count stays reasonable.
