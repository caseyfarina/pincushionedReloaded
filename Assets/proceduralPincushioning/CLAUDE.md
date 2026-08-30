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
