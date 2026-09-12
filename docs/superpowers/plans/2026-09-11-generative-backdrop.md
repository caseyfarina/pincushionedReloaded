# Generative Backdrop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A VFX Graph mesh-array backdrop behind the pincushioned dancers that re-layouts, re-shades, re-meshes and flashes instantly from four MF64 pads, plus a preset-exploration rig for searching its parameter space.

**Architecture:** One `VisualEffect` renders a few hundred mesh instances on an even lattice over a camera-anchored plane / hemisphere / cube domain, with per-instance scale, offset, rotation, spin and sin-wave displacement computed on GPU from `(instanceID, seed, time)`. A single `BackdropInstrument` MonoBehaviour owns all state and is the only thing that writes to the graph; dumb keyboard and MIDI drivers feed it. Phase 2 layers a seeded randomiser, a preset book and a history ring on top of the same parameter struct.

**Tech Stack:** Unity 6 (6000.3.7f1), URP 17.3.0, VFX Graph 17.3.0, Shader Graph 17.3.0, Input System 1.18.0, Test Framework 1.6.0, `com.caseyfarina.midifighter64` v2.3.0. Direct3D 11 only.

**Spec:** `docs/superpowers/specs/2026-09-11-generative-backdrop-design.md`

## Global Constraints

- **Direct3D 11 only.** D3D12 causes unrecoverable GPU device loss on this project. Never add `-force-d3d12` or change `WindowsStandaloneSupport` in `ProjectSettings/ProjectSettings.asset`.
- **No bootstrapping.** Never `new GameObject(...).AddComponent<T>()` and never `[RuntimeInitializeOnLoadMethod]`. Anything with artistic parameters lives in the scene or on a prefab.
- **Seed anything random with `System.Random` and a serialized seed.** Never `UnityEngine.Random` — it is a global shared stream and destroys reproducibility.
- **Router events are static.** Every `+=` on a `MidiFighter*Router` event needs its matching `-=` in `OnDisable`.
- **Input System only.** `activeInputHandler = 1`; the legacy `UnityEngine.Input` class throws at runtime. Use `Keyboard.current`.
- **Namespace:** none (global), matching `Assets/mixamoDance/` and `Assets/proceduralPincushioning/`. Add `using MidiFighter64;` explicitly in any file touching MIDI.
- **1-based everywhere user-facing** (MF64 row, col); 0-based only in internal arrays.
- **Expected operating scale is hundreds of instances, not thousands.** `spawnCount` ranges are authored for hundreds.
- **Verify via the already-open editor**, never batch mode: `unity command recompile && unity command recompile_status`. `recompile` does NOT refresh the AssetDatabase — create new script files with `unity command write_text_file`, which writes *and* imports.
- **Optional CLI params need `--name <value>` flag form**, not `name=value`.
- **Measure in a build, never the editor** (5.2x inflation measured on this project).

## Task Ownership

Every task is tagged. **[AGENT]** tasks are fully executable by a coding agent. **[YOU]** tasks are node-graph authoring in the Unity GUI, which no agent can do — each ships a node-by-node recipe and is followed by an agent-runnable acceptance check.

| Task | Owner | Phase |
|---|---|---|
| 0 — D3D11 + VFX Graph smoke test | **[YOU]** + [AGENT] check | 1 |
| 1 — Core assembly, `BackdropParameters`, test harness | [AGENT] | 1 |
| 2 — `BackdropShading.shadergraph` | **[YOU]** | 1 |
| 3 — `Backdrop.vfx` | **[YOU]** | 1 |
| 4 — `BackdropInstrument` + graph validation | [AGENT] | 1 |
| 5 — `BackdropLibrary` + folder scan tool | [AGENT] | 1 |
| 6 — The four performance functions + `ShuffleBag` | [AGENT] | 1 |
| 7 — `KeyboardBackdropDriver` | [AGENT] | 1 |
| 8 — `MidiFighterBackdropDriver` | [AGENT] | 1 |
| 9 — `BackdropRanges` | [AGENT] | 2 |
| 10 — `BackdropRandomizer` + tests | [AGENT] | 2 |
| 11 — `BackdropPresetBook` | [AGENT] | 2 |
| 12 — `HistoryRing` + `BackdropExplorerDriver` | [AGENT] | 2 |
| 13 — `BackdropExplore.unity` + build measurement | **[YOU]** + [AGENT] check | 2 |

## File Structure

```
Assets/proceduralBackdrop/
  Core/                                   asmdef: Pincushioned.Backdrop.Core
    Pincushioned.Backdrop.Core.asmdef       No references. Pure, testable.
    BackdropParameters.cs                   The struct + two enums. The contract.
    ShuffleBag.cs                           Seeded non-repeating cycler.
    BackdropRanges.cs            (phase 2)  Min/max + lock per parameter.
    BackdropRandomizer.cs        (phase 2)  Pure static randomise/mutate.
    BackdropPresetBook.cs        (phase 2)  Named saved parameter sets.
    HistoryRing.cs               (phase 2)  Fixed-capacity undo ring.
  Tests/Editor/                           asmdef: Pincushioned.Backdrop.Tests
    Pincushioned.Backdrop.Tests.asmdef      Editor-only, references Core.
    BackdropParametersTests.cs
    ShuffleBagTests.cs
    BackdropRandomizerTests.cs   (phase 2)
    HistoryRingTests.cs          (phase 2)
  BackdropInstrument.cs                   Assembly-CSharp. Sole writer to the graph.
  BackdropLibrary.cs                      Assembly-CSharp. Mesh set SO.
  KeyboardBackdropDriver.cs               Assembly-CSharp.
  MidiFighterBackdropDriver.cs            Assembly-CSharp. The only MIDI-aware file.
  BackdropExplorerDriver.cs    (phase 2)  Assembly-CSharp.
  Editor/
    BackdropLibraryEditor.cs              Folder scan + triangle-count guard.
  Backdrop.vfx                            Hand-authored.
  BackdropShading.shadergraph             Hand-authored.
  BackdropShading.mat                     Material instance of the above.
  Meshes/                                 Drop FBXs here; scanned into the library.
  BackdropExplore.unity        (phase 2)  The exploration scene.
```

**Why `Core` is its own assembly:** asmdefs cannot reference `Assembly-CSharp` (predefined assemblies compile *after* asmdefs), so an EditMode test assembly cannot see code that lives loose under `Assets/`. Putting the pure, testable types in a small asmdef is the minimum change that makes them testable. `Assembly-CSharp` automatically references every asmdef, so the MonoBehaviours can still live loose alongside `mixamoDance` and `proceduralPincushioning` and see `BackdropParameters` fine.

---

# PHASE 1 — THE INSTRUMENT

Deliverable: a backdrop that distributes, animates, and responds to all four functions, playable from the keyboard with no MF64 attached.

---

### Task 0: D3D11 + VFX Graph smoke test — **[YOU]**, then [AGENT] check

**Why first:** VFX Graph has never rendered at scale in this project, and the project is hard-locked to D3D11 for native plugin reasons (Adobe Substance, KlakHap). D3D11 supports the compute shaders VFX Graph requires, so this should work — but discovering otherwise after building the whole system would waste the entire plan. This is a five-minute check that de-risks everything below.

**Files:**
- Create: `Assets/proceduralBackdrop/` (folder)
- Create: `Assets/proceduralBackdrop/BackdropSmoke.unity`

- [ ] **Step 1: [YOU] Confirm the editor is on D3D11**

In Unity: `Edit > Project Settings > Player > Other Settings > Rendering`. Confirm **Auto Graphics API for Windows is OFF** and the Graphics APIs list contains **Direct3D11 only**. Do not change it — just confirm.

- [ ] **Step 2: [YOU] Build a throwaway VFX**

- `File > New Scene > Basic (URP)`, save as `Assets/proceduralBackdrop/BackdropSmoke.unity`.
- `Assets/proceduralBackdrop/` right-click > `Create > Visual Effects > Visual Effect Graph`, name it `Smoke.vfx`.
- Open it. Delete the default **Output Particle Quad** context and replace it with **Output Particle Mesh** (right-click canvas > `Create Node` > search "Output Particle Mesh", then drag from the Update context's flow output into it).
- On the mesh output, set **Mesh** to Unity's built-in `Cube`.
- On the **Initialize Particle** context set **Capacity** to `1000`.
- On **Spawn**, replace `Constant Spawn Rate` with a **Single Burst** of `1000`.
- In Initialize, add a **Set Position (Sphere)** block, radius `20`.
- Drag `Smoke.vfx` into the scene. Save the scene.

- [ ] **Step 3: [YOU] Enter Play mode and confirm**

Press Play. You should see 1000 cubes in a sphere. Note whether the frame is stable and the console is clean.

- [ ] **Step 4: [AGENT] Verify from the CLI**

```bash
unity command recompile_status
unity command get_console_logs
```

Expected: `failed: false`, and no console entries containing `VFX`, `compute`, `D3D`, or `Kernel`. Any error mentioning compute shader support or graphics API is a **stop condition** — report it and halt the plan rather than continuing.

- [ ] **Step 5: [AGENT] Commit the folder, delete the throwaway**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop
git commit -m "Add backdrop folder and D3D11 VFX Graph smoke test"
```

Leave `Smoke.vfx` and `BackdropSmoke.unity` in place for now — Task 3 uses the scene as a workbench and Task 13 replaces it.

---

### Task 1: Core assembly, `BackdropParameters`, test harness — **[AGENT]**

**Files:**
- Create: `Assets/proceduralBackdrop/Core/Pincushioned.Backdrop.Core.asmdef`
- Create: `Assets/proceduralBackdrop/Core/BackdropParameters.cs`
- Create: `Assets/proceduralBackdrop/Tests/Editor/Pincushioned.Backdrop.Tests.asmdef`
- Test: `Assets/proceduralBackdrop/Tests/Editor/BackdropParametersTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `BackdropDomain` enum (`Plane=0, Hemisphere=1, Cube=2`); `BackdropShadingMode` enum (`Toon=0, FresnelToon=1, Lit=2`); `struct BackdropParameters` with the fields listed below and `static BackdropParameters Default { get; }`, `BackdropParameters Clamped()`, `const int MaxSpawnCount = 4000`. **Every task below depends on these exact names.**

**Critical:** use `unity command write_text_file` to create these files, not the raw filesystem — Auto Refresh is off on this project, so files written directly to disk are never imported, never compile, and never get a `.meta`.

- [ ] **Step 1: Write the assembly definitions**

`Assets/proceduralBackdrop/Core/Pincushioned.Backdrop.Core.asmdef`:

```json
{
    "name": "Pincushioned.Backdrop.Core",
    "rootNamespace": "",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`Assets/proceduralBackdrop/Tests/Editor/Pincushioned.Backdrop.Tests.asmdef`:

```json
{
    "name": "Pincushioned.Backdrop.Tests",
    "rootNamespace": "",
    "references": [
        "Pincushioned.Backdrop.Core",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Write the failing test**

`Assets/proceduralBackdrop/Tests/Editor/BackdropParametersTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class BackdropParametersTests
{
    [Test]
    public void Default_ProducesAVisibleField()
    {
        var p = BackdropParameters.Default;

        Assert.Greater(p.spawnCount, 0, "a default backdrop with no instances renders nothing");
        Assert.LessOrEqual(p.spawnCount, BackdropParameters.MaxSpawnCount);
        Assert.Greater(p.domainSize.x, 0f);
        Assert.Greater(p.domainSize.y, 0f);
        Assert.Greater(p.domainSize.z, 0f);
        Assert.Greater(p.scaleRange.y, 0f, "max scale of 0 makes every instance invisible");
        Assert.LessOrEqual(p.scaleRange.x, p.scaleRange.y);
    }

    [Test]
    public void Clamped_PullsSpawnCountUnderTheCeiling()
    {
        var p = BackdropParameters.Default;
        p.spawnCount = BackdropParameters.MaxSpawnCount + 5000;

        Assert.AreEqual(BackdropParameters.MaxSpawnCount, p.Clamped().spawnCount);
    }

    [Test]
    public void Clamped_OrdersAnInvertedScaleRange()
    {
        var p = BackdropParameters.Default;
        p.scaleRange = new Vector2(3f, 1f);

        var c = p.Clamped();

        Assert.AreEqual(1f, c.scaleRange.x, 1e-5f);
        Assert.AreEqual(3f, c.scaleRange.y, 1e-5f);
    }

    [Test]
    public void Clamped_OrdersAnInvertedSpinRange()
    {
        var p = BackdropParameters.Default;
        p.spinRateRange = new Vector2(90f, -90f);

        var c = p.Clamped();

        Assert.AreEqual(-90f, c.spinRateRange.x, 1e-5f);
        Assert.AreEqual(90f, c.spinRateRange.y, 1e-5f);
    }

    [Test]
    public void Clamped_RejectsNegativeSpawnCount()
    {
        var p = BackdropParameters.Default;
        p.spawnCount = -10;

        Assert.AreEqual(0, p.Clamped().spawnCount);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
unity command recompile
unity command recompile_status
```

Expected: `failed: true` with errors naming `BackdropParameters` as an unknown type. That is the failure we want — the type does not exist yet.

- [ ] **Step 4: Write the minimal implementation**

`Assets/proceduralBackdrop/Core/BackdropParameters.cs`:

```csharp
using UnityEngine;

/// <summary>Which shape the instances are distributed evenly across.</summary>
public enum BackdropDomain { Plane = 0, Hemisphere = 1, Cube = 2 }

/// <summary>
/// Which shading model BackdropShading.shadergraph branches to. Three
/// genuinely different models rather than three parameter sets, which is why
/// they are branched inside one graph rather than being three Materials:
/// a VFX mesh output binds its material at author time and cannot be
/// reassigned at runtime.
/// </summary>
public enum BackdropShadingMode { Toon = 0, FresnelToon = 1, Lit = 2 }

/// <summary>
/// Every value the backdrop has. One struct, because it is simultaneously the
/// inspector surface, the unit of preset save/recall, the thing the randomiser
/// produces, and the entry in the undo history. Anything that is not in here
/// cannot be saved as a preset or randomised, so new parameters belong here
/// rather than as loose fields on the instrument.
///
/// Mirrors the exposed properties of Backdrop.vfx one-for-one. If you add a
/// field, add the matching exposed property in the graph and push it in
/// BackdropInstrument.Apply.
/// </summary>
[System.Serializable]
public struct BackdropParameters
{
    /// <summary>
    /// Hard ceiling on instances. Expected operating scale is hundreds; this
    /// exists so a runaway randomiser cannot hand the graph tens of thousands.
    /// Raising it is a deliberate act followed by a build measurement.
    /// </summary>
    public const int MaxSpawnCount = 4000;

    [Header("Layout")]
    public uint layoutSeed;
    [Min(0)] public int spawnCount;
    public BackdropDomain domain;
    public Vector3 domainSize;
    [Tooltip("Cube domain only: fill the volume rather than the shell.")]
    public bool solidFill;

    [Header("Per-instance variation")]
    [Tooltip("Uniform scale multiplier, min to max.")]
    public Vector2 scaleRange;
    [Tooltip("Per-axis multiplier applied after the uniform scale. (1,1,1) = untouched.")]
    public Vector3 scaleAxisBias;
    [Tooltip("Random displacement off the lattice point, metres per axis.")]
    public Vector3 offsetJitter;
    [Tooltip("Random rotation, degrees per axis.")]
    public Vector3 rotationJitter;

    [Header("Animation")]
    [Tooltip("Degrees per second, min to max. Negative reverses.")]
    public Vector2 spinRateRange;
    public Vector3 spinAxis;
    public Vector3 waveAxis;
    public float waveAmplitude;
    public float waveFrequency;
    [Tooltip("0 = every instance waves in unison, 1 = phases spread over a full cycle.")]
    [Range(0f, 1f)] public float wavePhaseSpread;

    [Header("Look")]
    public BackdropShadingMode shading;
    public Color emissionColor;
    [Tooltip("Flash decay rate. Higher = shorter flash.")]
    [Min(0.01f)] public float flashDecay;
    [Min(0f)] public float flashIntensity;
    [Tooltip("Seconds of delay per unit of normalised instance ID, so the flash ripples rather than blinking flat.")]
    [Min(0f)] public float flashRipple;

    public static BackdropParameters Default => new BackdropParameters
    {
        layoutSeed      = 1u,
        spawnCount      = 300,
        domain          = BackdropDomain.Cube,
        domainSize      = new Vector3(60f, 30f, 60f),
        solidFill       = false,

        scaleRange      = new Vector2(0.6f, 1.8f),
        scaleAxisBias   = new Vector3(1f, 3f, 1f),
        offsetJitter    = new Vector3(0.5f, 0.5f, 0.5f),
        rotationJitter  = new Vector3(0f, 180f, 0f),

        spinRateRange   = new Vector2(-8f, 8f),
        spinAxis        = Vector3.up,
        waveAxis        = Vector3.up,
        waveAmplitude   = 0.75f,
        waveFrequency   = 0.15f,
        wavePhaseSpread = 1f,

        shading         = BackdropShadingMode.Toon,
        emissionColor   = Color.white,
        flashDecay      = 4f,
        flashIntensity  = 8f,
        flashRipple     = 0.3f,
    };

    /// <summary>
    /// Returns a copy with every value forced into a range the graph can render.
    /// Called on every write path, so neither the inspector, the randomiser, nor
    /// a hand-edited preset asset can push the graph somewhere it cannot recover
    /// from. Inverted min/max pairs are ordered rather than rejected, because an
    /// inverted range is a plausible authoring slip with an obvious intent.
    /// </summary>
    public BackdropParameters Clamped()
    {
        var c = this;

        c.spawnCount = Mathf.Clamp(c.spawnCount, 0, MaxSpawnCount);

        c.domainSize = new Vector3(
            Mathf.Max(0.01f, c.domainSize.x),
            Mathf.Max(0.01f, c.domainSize.y),
            Mathf.Max(0.01f, c.domainSize.z));

        c.scaleRange    = Ordered(c.scaleRange, 0f);
        c.spinRateRange = Ordered(c.spinRateRange, float.NegativeInfinity);

        c.offsetJitter   = Abs(c.offsetJitter);
        c.rotationJitter = Abs(c.rotationJitter);

        c.spinAxis = c.spinAxis.sqrMagnitude < 1e-6f ? Vector3.up : c.spinAxis.normalized;
        c.waveAxis = c.waveAxis.sqrMagnitude < 1e-6f ? Vector3.up : c.waveAxis.normalized;

        c.waveFrequency   = Mathf.Max(0f, c.waveFrequency);
        c.wavePhaseSpread = Mathf.Clamp01(c.wavePhaseSpread);

        c.flashDecay     = Mathf.Max(0.01f, c.flashDecay);
        c.flashIntensity = Mathf.Max(0f, c.flashIntensity);
        c.flashRipple    = Mathf.Max(0f, c.flashRipple);

        return c;
    }

    private static Vector2 Ordered(Vector2 v, float floor)
    {
        float a = Mathf.Max(floor, v.x);
        float b = Mathf.Max(floor, v.y);
        return a <= b ? new Vector2(a, b) : new Vector2(b, a);
    }

    private static Vector3 Abs(Vector3 v)
        => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
unity command recompile
unity command recompile_status
unity command run_tests --mode EditMode --filter BackdropParametersTests
unity command test_status
```

Expected: `failed: false` from recompile, and 5 passing tests. If `run_tests` does not accept `--mode`, read the real schema with `unity --json list` and use whatever parameter names `data.tools[].parameters` declares.

- [ ] **Step 6: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/Core Assets/proceduralBackdrop/Tests
git commit -m "Add BackdropParameters and the backdrop core test assembly"
```

---

### Task 2: `BackdropShading.shadergraph` — **[YOU]**

**Files:**
- Create: `Assets/proceduralBackdrop/BackdropShading.shadergraph`
- Create: `Assets/proceduralBackdrop/BackdropShading.mat`

**Interfaces:**
- Consumes: nothing.
- Produces: a material usable by a VFX **Output Particle Mesh** context, reading three per-particle custom attributes named exactly `shadingMode` (float), `flashPhase` (float), and the standard `color`. Task 3 writes those attributes; the names must match exactly.

**Why one graph and not three materials:** a VFX mesh output binds its material at author time. There is no runtime `renderer.material = x`. Three genuinely different shading models therefore have to live inside one shader, branched on a value. Because that value is uniform across the whole field in normal use, the GPU branch is coherent and costs essentially nothing — and as a bonus, per-instance mixed shading comes free later by writing a varying `shadingMode` instead of a constant one.

- [ ] **Step 1: Create the graph with the VFX target**

- `Assets/proceduralBackdrop/` right-click > `Create > Shader Graph > URP > Lit Shader Graph`. Name it `BackdropShading`.
- Open it. In **Graph Settings** (the Graph Inspector, top-right — press the "Graph Inspector" button if hidden), under **Targets**, click **+** and add **Visual Effect**. Keep **Universal** in the list as well.
- Still in Graph Settings, set **Universal > Surface Type** to `Opaque` and **Universal > Fragment Normal Space** to `Tangent`.

**This step is the one that silently fails.** If the Visual Effect target is missing, the material will not appear in the VFX mesh output's material slot in Task 3, and the symptom is "Unity won't let me pick my shader" rather than any error.

- [ ] **Step 2: Expose the properties**

In the Blackboard (top-left), add these **exactly**, matching name and type. The **Reference** field of each must be set to the string in the Reference column — Shader Graph's auto-generated references contain GUIDs and will not match what Task 4 writes.

| Name | Type | Reference | Default |
|---|---|---|---|
| Base Color | Color (HDR off) | `_BaseColor` | light warm grey, `(0.75, 0.73, 0.70, 1)` |
| Toon Steps | Float (Slider 2–8) | `_ToonSteps` | `3` |
| Fresnel Power | Float (Slider 0.5–8) | `_FresnelPower` | `3` |
| Fresnel Color | Color (HDR on) | `_FresnelColor` | cyan, intensity 1 |
| Emission Color | Color (HDR on) | `_EmissionColor` | white, intensity 1 |

- [ ] **Step 3: Wire the three custom attribute reads**

Add three **Custom Vertex Streams / VFX attribute** reads. In a Shader Graph with the Visual Effect target, per-particle attributes arrive as **`Custom Interpolator`**-free named properties: add a **Float** property to the Blackboard named `shadingMode` with Reference `shadingMode`, and another named `flashPhase` with Reference `flashPhase`. Tick **Exposed** off for both — VFX writes them per-particle, they are not material-level values.

Then in Task 3 you will tick "shadingMode" and "flashPhase" in the mesh output's **Material Property Bindings**, which is what connects the particle attribute to the shader property. If that panel does not list them, the Reference strings do not match.

- [ ] **Step 4: Build the three shading branches**

Build these three sub-networks and feed them into a **Branch** chain:

**Toon** — `Dot Product(Normal Vector (World), Main Light Direction)` → `Remap(-1,1 → 0,1)` → `Multiply(Toon Steps)` → `Ceil` → `Divide(Toon Steps)` → `Multiply(Base Color)`.

Unity has no built-in "Main Light Direction" node in Shader Graph. Use a **Custom Function** node, File mode off (String), with:
- Name: `MainLight`
- Output: `Direction` (Vector 3)
- Body: `Direction = normalize(float3(0.4, 0.9, 0.25));`

A baked light direction is deliberate: a backdrop that re-lights as the camera orbits reads as a bug in a mosaic where eight cells see it from eight angles. Tune the vector by eye.

**Fresnel Toon** — the Toon result, plus `Fresnel Effect(Power = Fresnel Power)` → `Multiply(Fresnel Color)` → `Add`.

**Lit** — `Base Color` straight into Base Color, with Metallic `0`, Smoothness `0.35`, letting URP light it normally.

Chain them: `Branch(Predicate = shadingMode > 1.5, True = Lit, False = Branch(Predicate = shadingMode > 0.5, True = FresnelToon, False = Toon))`.

Use `Comparison(Greater)` nodes to produce the boolean predicates.

- [ ] **Step 5: Wire the flash**

`flashPhase` is a 0→1 value the graph in Task 3 computes as `exp(-(time - flashTime) * flashDecay)`, already staggered per instance. Here it is just a multiplier:

`Multiply(Emission Color, flashPhase)` → **Emission** on the Fragment block.

For Toon and Fresnel Toon, also feed the branch result into **Base Color**; for Lit, feed `Base Color` directly. Emission is shared across all three modes.

- [ ] **Step 6: Save and make a material**

- `Save Asset` in the Shader Graph toolbar.
- In the Project window, right-click `BackdropShading.shadergraph` > `Create > Material`. Name the result `BackdropShading.mat`.

- [ ] **Step 7: Verify on a plain mesh before touching VFX**

Drag `BackdropShading.mat` onto a plain cube in `BackdropSmoke.unity`. In the material inspector, drag **Toon Steps** and confirm you see banding change. This proves the shader works before VFX adds a second thing that can be broken.

- [ ] **Step 8: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/BackdropShading.shadergraph Assets/proceduralBackdrop/BackdropShading.shadergraph.meta Assets/proceduralBackdrop/BackdropShading.mat Assets/proceduralBackdrop/BackdropShading.mat.meta
git commit -m "Add backdrop shader graph with toon, fresnel toon and lit branches"
```

---

### Task 3: `Backdrop.vfx` — **[YOU]**

**Files:**
- Create: `Assets/proceduralBackdrop/Backdrop.vfx`
- Modify: `Assets/proceduralBackdrop/BackdropSmoke.unity` (add the VisualEffect object)

**Interfaces:**
- Consumes: `BackdropShading.mat` from Task 2.
- Produces: a `.vfx` asset exposing **exactly** the property names in the table below. Task 4's `BackdropInstrument` writes these by name and validates their presence at startup, so a typo here surfaces as a named error rather than a silent no-op.

**The exposed property contract — these strings are the API:**

| Property name | Type | Notes |
|---|---|---|
| `LayoutSeed` | uint | |
| `SpawnCount` | int | |
| `DomainShape` | int | 0 plane, 1 hemisphere, 2 cube |
| `DomainSize` | Vector3 | |
| `SolidFill` | bool | |
| `InstanceMesh` | Mesh | |
| `ShadingMode` | int | |
| `FlashTime` | float | absolute `Time.time` of the last flash |
| `FlashDecay` | float | |
| `FlashRipple` | float | |
| `ScaleRange` | Vector2 | |
| `ScaleAxisBias` | Vector3 | |
| `OffsetJitter` | Vector3 | |
| `RotationJitter` | Vector3 | |
| `SpinRateRange` | Vector2 | |
| `SpinAxis` | Vector3 | |
| `WaveAxis` | Vector3 | |
| `WaveAmplitude` | float | |
| `WaveFrequency` | float | |
| `WavePhaseSpread` | float | |
| `EmissionColor` | Color | |

- [ ] **Step 1: Create the graph and set the system up**

- `Assets/proceduralBackdrop/` right-click > `Create > Visual Effects > Visual Effect Graph`, name it `Backdrop`.
- Open it. Delete the default **Output Particle Quad**; create an **Output Particle Mesh** and connect Update's flow into it.
- **Initialize Particle > Capacity: `4000`.** This must equal `BackdropParameters.MaxSpawnCount`. Capacity is fixed at author time and cannot be raised at runtime, which is why the C# ceiling exists.
- Select the **system** (click the title bar of the particle system) and in the Inspector set **Simulation Space: Local**. Local is deliberate — the backdrop is anchored to a transform that follows the camera, and instances must hold position relative to that anchor. This is the opposite of `skinnedMesh.vfx`, which uses World so particles are left behind by the dancer.

- [ ] **Step 2: Declare the exposed properties**

In the Blackboard, add every row of the contract table above, each with **Exposed** ticked. Names are case-sensitive and must match exactly.

For `DomainShape` and `ShadingMode`, use type **int** (not Enum) — `VisualEffect.SetInt` is what the C# writes, and an Enum property does not accept it.

- [ ] **Step 3: Spawn**

Replace `Constant Spawn Rate` with **Single Burst**. Bind its **Count** to the `SpawnCount` property.

Add a **Set Spawn Time** / leave loop settings at defaults; the field is static between reroll presses and `Reinit()` from C# is what re-fires the burst.

- [ ] **Step 4: Even lattice distribution, branched on domain**

In **Initialize Particle**, add a **Set Position** block driven by a custom expression. Build these three position sub-networks and select between them with two nested **Branch** operators on `DomainShape`.

Everything below derives from `particleId` (the **Get Attribute: particleId** operator) and `SpawnCount`, so the distribution is even by construction rather than random. Let `t = particleId / SpawnCount` (a **Divide**, both cast to float).

**Plane (0)** — an N × M grid. Let `N = ceil(sqrt(SpawnCount * (DomainSize.x / DomainSize.y)))`:
- `col = fmod(particleId, N)`, `row = floor(particleId / N)`
- `x = (col / (N-1) - 0.5) * DomainSize.x`
- `y = (row / (M-1) - 0.5) * DomainSize.y`, where `M = ceil(SpawnCount / N)`
- `z = 0`

**Hemisphere (1)** — a Fibonacci spherical cap. Even by construction; do **not** use random spherical coordinates, which cluster at the pole (the same reason `SplitScreenCameraRig` samples a cap rather than rejection-sampling):
- `y = 1 - t` (walks the cap from pole to rim)
- `r = sqrt(1 - y*y)`
- `theta = particleId * 2.399963` (the golden angle, radians)
- `position = (cos(theta) * r, y, sin(theta) * r) * DomainSize * 0.5`

**Cube (2)** — an N × M × K lattice, `N = ceil(pow(SpawnCount, 1/3))`:
- `ix = fmod(particleId, N)`, `iy = fmod(floor(particleId / N), N)`, `iz = floor(particleId / (N*N))`
- normalised `u = (ix/(N-1), iy/(N-1), iz/(N-1)) - 0.5`
- if `SolidFill` is false, project onto the shell: keep only instances where any of `ix, iy, iz` is `0` or `N-1`. Implement by **snapping** rather than culling — take the axis with the largest `|u|` component and push it to `±0.5`. Culling would leave the interior instances alive but invisible and waste the count; snapping puts every requested instance on the shell.
- `position = u * DomainSize`

Then `Branch(DomainShape > 1.5, Cube, Branch(DomainShape > 0.5, Hemisphere, Plane))`.

**This is the largest single piece of node work in the plan.** Build and verify one domain at a time — get Plane rendering a clean grid before starting Hemisphere.

- [ ] **Step 5: Per-instance variation**

Still in Initialize, using **Random Number** operators seeded per-particle. Set the graph's random to be seed-driven: on the system, set **Seed Mode: Custom** and bind **Seed** to `LayoutSeed`. That is what makes `RerollLayout()` reproducible.

- **Offset**: `Add` to position — `(rand3 * 2 - 1) * OffsetJitter`.
- **Scale**: `Set Scale` = `lerp(ScaleRange.x, ScaleRange.y, rand) * ScaleAxisBias`.
- **Rotation**: `Set Angle` = `(rand3 * 2 - 1) * RotationJitter`.
- **Spin rate**: store `lerp(SpinRateRange.x, SpinRateRange.y, rand)` into a **custom attribute** named `spinRate` (float), via **Set Custom Attribute**.
- **Wave phase**: store `t * WavePhaseSpread * 6.28318` into a custom attribute named `wavePhase` (float).
- **Normalised id**: store `t` into a custom attribute named `idNorm` (float). Used by the flash ripple.
- **Shading mode**: `Set Custom Attribute shadingMode = ShadingMode` (int → float cast). Writing it per-particle rather than as a material constant is what makes per-instance mixed shading a later data change rather than a rework.

- [ ] **Step 6: Per-frame animation in Update**

In **Update Particle**:

- **Spin**: `Set Angle` += `spinRate * deltaTime * SpinAxis`. Use **Add Angle** if available, otherwise read `angle`, add, and write back.
- **Wave**: `Set Position` += `sin(totalTime * WaveFrequency * 6.28318 + wavePhase) * WaveAmplitude * WaveAxis`.

  **Careful:** this must be an absolute offset from the lattice point, not an accumulating add, or instances drift away permanently. Store the Initialize position into a custom attribute `basePosition` (Vector3) and compute `position = basePosition + waveOffset` each frame rather than `position += waveOffset`.

- **Flash**: `Set Custom Attribute flashPhase = saturate(exp(-max(0, totalTime - FlashTime - idNorm * FlashRipple) * FlashDecay))`.

  The `idNorm * FlashRipple` term is what makes the flash sweep across the field instead of blinking flat, and the `max(0, ...)` keeps instances dark until their turn arrives.

- **Alive**: add a **Set Alive** / do not add any lifetime block. Particles must be immortal — in Initialize set **Lifetime** to a very large constant (e.g. `1e9`) or remove the lifetime block entirely so the default infinite applies.

- [ ] **Step 7: The mesh output**

On **Output Particle Mesh**:
- **Mesh**: bind to the `InstanceMesh` property.
- **Material**: `BackdropShading.mat` from Task 2. If it does not appear in the picker, the Visual Effect target is missing from the Shader Graph — go back to Task 2 Step 1.
- **Material Property Bindings**: tick `shadingMode` and `flashPhase` so the custom attributes reach the shader.
- **Cast Shadows: Off.** Shadow casters drove the entire dancer-sweep cost curve on this project; a backdrop has no business in the shadow atlas.
- Bind **Emission Color** on the material to the `EmissionColor` property.

- [ ] **Step 8: Explicit bounds**

Select the **Initialize** context. Set **Bounds Mode: Manual** and bind **Bounds** to a box centred at origin with size = `DomainSize * 1.5`. The 1.5 pads for jitter, wave and scale.

Do not leave this on Recorded or Automatic. VFX's defaults can leave the field permanently unculled — the same failure mode as `MeshSurfaceScatter`'s hardcoded 1000-unit cube, which is what cost this project 3.6x before it was found.

- [ ] **Step 9: Place it in the smoke scene and eyeball it**

- Drag `Backdrop.vfx` into `BackdropSmoke.unity`.
- Set `InstanceMesh` on the VisualEffect component to the built-in `Cube`.
- Enter Play mode. You should see ~300 cubes on a cube shell, gently spinning and waving.
- Flip `DomainShape` between 0, 1, 2 in the inspector and confirm all three distributions.

- [ ] **Step 10: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/Backdrop.vfx Assets/proceduralBackdrop/Backdrop.vfx.meta Assets/proceduralBackdrop/BackdropSmoke.unity
git commit -m "Add Backdrop.vfx with plane, hemisphere and cube lattice domains"
```

---

### Task 4: `BackdropInstrument` + graph validation — **[AGENT]**

**Files:**
- Create: `Assets/proceduralBackdrop/BackdropInstrument.cs`

**Interfaces:**
- Consumes: `BackdropParameters` (Task 1), `Backdrop.vfx`'s exposed property names (Task 3).
- Produces: `class BackdropInstrument : MonoBehaviour` with `BackdropParameters Current { get; }`, `void Apply(BackdropParameters p)`, `void ApplyAndRelayout(BackdropParameters p)`, `List<string> ValidateGraph()`. Tasks 6–8 and 12 call these.

- [ ] **Step 1: Write the implementation**

Create via `unity command write_text_file`. `Assets/proceduralBackdrop/BackdropInstrument.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// The single owner of backdrop state and the only thing that writes to
/// Backdrop.vfx. Input-agnostic: it knows nothing about MIDI or the keyboard,
/// which is what lets the whole system be built and judged with no MF64
/// attached. Same split as PinDensityController and PoseInstrument.
///
/// Every write goes through Apply(), so pads, the inspector, presets and the
/// randomiser can never disagree about what the graph is showing.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(VisualEffect))]
public class BackdropInstrument : MonoBehaviour
{
    [SerializeField] private VisualEffect vfx;

    [Tooltip("The live parameter set. Edit here, or drive it from a driver or preset.")]
    [SerializeField] private BackdropParameters parameters = BackdropParameters.Default;

    [Tooltip("Mesh set the mesh-swap function cycles through.")]
    [SerializeField] private BackdropLibrary library;

    [Tooltip("Index into the library. -1 leaves whatever mesh the graph was authored with.")]
    [SerializeField] private int meshIndex = 0;

    public BackdropParameters Current => parameters;
    public BackdropLibrary Library => library;
    public int MeshIndex => meshIndex;

    // Exposed-property name IDs. Cached because Apply runs on every pad press
    // and string lookups in VisualEffect are not free.
    private static readonly int IdLayoutSeed      = Shader.PropertyToID("LayoutSeed");
    private static readonly int IdSpawnCount      = Shader.PropertyToID("SpawnCount");
    private static readonly int IdDomainShape     = Shader.PropertyToID("DomainShape");
    private static readonly int IdDomainSize      = Shader.PropertyToID("DomainSize");
    private static readonly int IdSolidFill       = Shader.PropertyToID("SolidFill");
    private static readonly int IdInstanceMesh    = Shader.PropertyToID("InstanceMesh");
    private static readonly int IdShadingMode     = Shader.PropertyToID("ShadingMode");
    private static readonly int IdFlashTime       = Shader.PropertyToID("FlashTime");
    private static readonly int IdFlashDecay      = Shader.PropertyToID("FlashDecay");
    private static readonly int IdFlashRipple     = Shader.PropertyToID("FlashRipple");
    private static readonly int IdScaleRange      = Shader.PropertyToID("ScaleRange");
    private static readonly int IdScaleAxisBias   = Shader.PropertyToID("ScaleAxisBias");
    private static readonly int IdOffsetJitter    = Shader.PropertyToID("OffsetJitter");
    private static readonly int IdRotationJitter  = Shader.PropertyToID("RotationJitter");
    private static readonly int IdSpinRateRange   = Shader.PropertyToID("SpinRateRange");
    private static readonly int IdSpinAxis        = Shader.PropertyToID("SpinAxis");
    private static readonly int IdWaveAxis        = Shader.PropertyToID("WaveAxis");
    private static readonly int IdWaveAmplitude   = Shader.PropertyToID("WaveAmplitude");
    private static readonly int IdWaveFrequency   = Shader.PropertyToID("WaveFrequency");
    private static readonly int IdWavePhaseSpread = Shader.PropertyToID("WavePhaseSpread");
    private static readonly int IdEmissionColor   = Shader.PropertyToID("EmissionColor");

    private void Reset()  => vfx = GetComponent<VisualEffect>();
    private void Awake()  { if (vfx == null) vfx = GetComponent<VisualEffect>(); }

    private void OnEnable()
    {
        if (vfx == null) vfx = GetComponent<VisualEffect>();

        var missing = ValidateGraph();
        if (missing.Count > 0)
            Debug.LogError(
                $"[BackdropInstrument] Backdrop.vfx is missing {missing.Count} exposed " +
                $"properties this component writes: {string.Join(", ", missing)}. " +
                "Names are case-sensitive; see the exposed property contract in the plan.",
                this);

        ApplyAndRelayout(parameters);
    }

    // Inspector edits go through the same single write path as everything else.
    private void OnValidate()
    {
        if (!isActiveAndEnabled || vfx == null) return;
        Apply(parameters);
    }

    /// <summary>
    /// Pushes every parameter to the graph. Does NOT reinitialise, so the
    /// existing arrangement, spin phase and wave phase all survive. This is
    /// what lets shading, mesh and flash be played against a layout that was
    /// found once and is being held.
    /// </summary>
    public void Apply(BackdropParameters p)
    {
        parameters = p.Clamped();
        if (vfx == null) return;

        var c = parameters;

        vfx.SetUInt(IdLayoutSeed, c.layoutSeed);
        vfx.SetInt(IdSpawnCount, c.spawnCount);
        vfx.SetInt(IdDomainShape, (int)c.domain);
        vfx.SetVector3(IdDomainSize, c.domainSize);
        vfx.SetBool(IdSolidFill, c.solidFill);

        vfx.SetInt(IdShadingMode, (int)c.shading);
        vfx.SetFloat(IdFlashDecay, c.flashDecay);
        vfx.SetFloat(IdFlashRipple, c.flashRipple);
        vfx.SetVector4(IdEmissionColor, c.emissionColor * c.flashIntensity);

        vfx.SetVector2(IdScaleRange, c.scaleRange);
        vfx.SetVector3(IdScaleAxisBias, c.scaleAxisBias);
        vfx.SetVector3(IdOffsetJitter, c.offsetJitter);
        vfx.SetVector3(IdRotationJitter, c.rotationJitter);

        vfx.SetVector2(IdSpinRateRange, c.spinRateRange);
        vfx.SetVector3(IdSpinAxis, c.spinAxis);
        vfx.SetVector3(IdWaveAxis, c.waveAxis);
        vfx.SetFloat(IdWaveAmplitude, c.waveAmplitude);
        vfx.SetFloat(IdWaveFrequency, c.waveFrequency);
        vfx.SetFloat(IdWavePhaseSpread, c.wavePhaseSpread);

        ApplyMesh();
    }

    /// <summary>
    /// Apply, then re-fire the spawn burst so new positions take effect.
    /// Positions are computed in Initialize, so a seed or domain change only
    /// affects newly spawned particles — Reinit is the re-layout. It resets
    /// spin phase, wave phase and any in-flight flash, which is correct for a
    /// deliberate reroll and wrong for everything else.
    /// </summary>
    public void ApplyAndRelayout(BackdropParameters p)
    {
        Apply(p);
        if (vfx != null) vfx.Reinit();
    }

    /// <summary>Sets the mesh from the library without disturbing the layout.</summary>
    public void SetMeshIndex(int index)
    {
        meshIndex = index;
        ApplyMesh();
    }

    private void ApplyMesh()
    {
        if (vfx == null || library == null) return;
        var mesh = library.Get(meshIndex);
        if (mesh != null) vfx.SetMesh(IdInstanceMesh, mesh);
    }

    /// <summary>
    /// Names of exposed properties this component writes that the graph does
    /// not declare. Empty means the C# and the hand-authored graph agree.
    /// This exists because Backdrop.vfx is built by hand in the GUI and a typo
    /// in a property name is otherwise a silent no-op — the value is dropped
    /// and the backdrop simply ignores that parameter forever.
    /// </summary>
    public List<string> ValidateGraph()
    {
        var missing = new List<string>();
        if (vfx == null || vfx.visualEffectAsset == null) return missing;

        void Req(bool has, string name) { if (!has) missing.Add(name); }

        Req(vfx.HasUInt(IdLayoutSeed),        "LayoutSeed");
        Req(vfx.HasInt(IdSpawnCount),         "SpawnCount");
        Req(vfx.HasInt(IdDomainShape),        "DomainShape");
        Req(vfx.HasVector3(IdDomainSize),     "DomainSize");
        Req(vfx.HasBool(IdSolidFill),         "SolidFill");
        Req(vfx.HasMesh(IdInstanceMesh),      "InstanceMesh");
        Req(vfx.HasInt(IdShadingMode),        "ShadingMode");
        Req(vfx.HasFloat(IdFlashTime),        "FlashTime");
        Req(vfx.HasFloat(IdFlashDecay),       "FlashDecay");
        Req(vfx.HasFloat(IdFlashRipple),      "FlashRipple");
        Req(vfx.HasVector2(IdScaleRange),     "ScaleRange");
        Req(vfx.HasVector3(IdScaleAxisBias),  "ScaleAxisBias");
        Req(vfx.HasVector3(IdOffsetJitter),   "OffsetJitter");
        Req(vfx.HasVector3(IdRotationJitter), "RotationJitter");
        Req(vfx.HasVector2(IdSpinRateRange),  "SpinRateRange");
        Req(vfx.HasVector3(IdSpinAxis),       "SpinAxis");
        Req(vfx.HasVector3(IdWaveAxis),       "WaveAxis");
        Req(vfx.HasFloat(IdWaveAmplitude),    "WaveAmplitude");
        Req(vfx.HasFloat(IdWaveFrequency),    "WaveFrequency");
        Req(vfx.HasFloat(IdWavePhaseSpread),  "WavePhaseSpread");
        Req(vfx.HasVector4(IdEmissionColor),  "EmissionColor");

        return missing;
    }
}
```

- [ ] **Step 2: Compile**

```bash
unity command recompile
unity command recompile_status
```

Expected: `failed: true` — `BackdropLibrary` does not exist yet (Task 5), so `library.Get(meshIndex)` will not resolve. That is expected and is fixed in the next task. If any *other* error appears, fix it now.

- [ ] **Step 3: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/BackdropInstrument.cs Assets/proceduralBackdrop/BackdropInstrument.cs.meta
git commit -m "Add BackdropInstrument as the sole writer to Backdrop.vfx"
```

---

### Task 5: `BackdropLibrary` + folder scan tool — **[AGENT]**

**Files:**
- Create: `Assets/proceduralBackdrop/BackdropLibrary.cs`
- Create: `Assets/proceduralBackdrop/Editor/BackdropLibraryEditor.cs`
- Create: `Assets/proceduralBackdrop/Meshes/` (folder, via the scan tool's first run)

**Interfaces:**
- Consumes: nothing.
- Produces: `class BackdropLibrary : ScriptableObject` with `int Count`, `Mesh Get(int index)`, `List<Mesh> meshes`, `string scanFolder`. Task 4 and Task 6 call `Get`.

**Why an authored list and not a runtime folder scan:** `AssetDatabase` is editor-only and vanishes in a build. The same reason `Assets/midiSupport/Samples/Resources/` exists on this project. The authoring gesture stays "drop FBXs in a folder, press one button" — the ScriptableObject is just what ships.

- [ ] **Step 1: Write the ScriptableObject**

`Assets/proceduralBackdrop/BackdropLibrary.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The mesh set the backdrop cycles through. A ScriptableObject rather than a
/// runtime folder scan because AssetDatabase is editor-only and does not exist
/// in a build — the same reason Samples/Resources exists in this project.
/// Populate it with the Scan Folder button in the inspector.
/// </summary>
[CreateAssetMenu(menuName = "Performance/Backdrop Library", fileName = "BackdropLibrary")]
public class BackdropLibrary : ScriptableObject
{
    [Tooltip("Project-relative folder the Scan Folder button reads, e.g. Assets/proceduralBackdrop/Meshes")]
    public string scanFolder = "Assets/proceduralBackdrop/Meshes";

    [Tooltip("Warn on scan when a mesh exceeds this triangle count. A backdrop instance is multiplied by instances x split-screen cells.")]
    public int triangleWarnThreshold = 3000;

    public List<Mesh> meshes = new List<Mesh>();

    public int Count => meshes.Count;

    /// <summary>Index-safe, null-safe fetch. Out-of-range returns null rather than throwing, because index comes from a pad press.</summary>
    public Mesh Get(int index)
    {
        if (index < 0 || index >= meshes.Count) return null;
        return meshes[index];
    }
}
```

- [ ] **Step 2: Write the editor scan tool**

`Assets/proceduralBackdrop/Editor/BackdropLibraryEditor.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scan Folder button for BackdropLibrary, plus the triangle-count guard.
///
/// The guard is not cosmetic. This project's artifact scans are 50k+ triangles
/// and dropping one into the backdrop folder multiplies it by instances x
/// split-screen cells. The resulting stall reads as "VFX Graph is slow" rather
/// than as an authoring mistake, so the tool names it at import time.
/// </summary>
[CustomEditor(typeof(BackdropLibrary))]
public class BackdropLibraryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var lib = (BackdropLibrary)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Scan Folder", GUILayout.Height(28)))
            Scan(lib);

        if (lib.meshes.Count > 0)
            EditorGUILayout.HelpBox(
                $"{lib.meshes.Count} meshes, {TotalTriangles(lib):n0} triangles total.",
                MessageType.None);
    }

    private static void Scan(BackdropLibrary lib)
    {
        if (!AssetDatabase.IsValidFolder(lib.scanFolder))
        {
            Debug.LogError($"[BackdropLibrary] Not a folder: {lib.scanFolder}", lib);
            return;
        }

        var found = new List<Mesh>();
        var heavy = new List<string>();

        var guids = AssetDatabase.FindAssets("t:Mesh", new[] { lib.scanFolder });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(obj is Mesh m)) continue;
                if (found.Contains(m)) continue;

                found.Add(m);

                int tris = m.triangles.Length / 3;
                if (tris > lib.triangleWarnThreshold)
                    heavy.Add($"{m.name} ({tris:n0} tris)");
            }
        }

        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        Undo.RecordObject(lib, "Scan Backdrop Meshes");
        lib.meshes = found;
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();

        Debug.Log($"[BackdropLibrary] Found {found.Count} meshes in {lib.scanFolder}.", lib);

        if (heavy.Count > 0)
            Debug.LogWarning(
                $"[BackdropLibrary] {heavy.Count} mesh(es) exceed {lib.triangleWarnThreshold:n0} " +
                $"triangles and will be multiplied by instances x split-screen cells: " +
                $"{string.Join(", ", heavy)}. Decimate them or drop them from the folder.",
                lib);
    }

    private static long TotalTriangles(BackdropLibrary lib)
    {
        long total = 0;
        foreach (var m in lib.meshes)
            if (m != null) total += m.triangles.Length / 3;
        return total;
    }
}
```

- [ ] **Step 3: Compile and confirm Task 4 now resolves**

```bash
unity command recompile
unity command recompile_status
```

Expected: `failed: false`. The `library.Get(meshIndex)` error from Task 4 is gone.

- [ ] **Step 4: Create the asset and a starter mesh folder**

```bash
unity command eval_file --file - <<'CS'
using UnityEditor;
using UnityEngine;
public static class Tmp {
    public static void Run() {
        if (!AssetDatabase.IsValidFolder("Assets/proceduralBackdrop/Meshes"))
            AssetDatabase.CreateFolder("Assets/proceduralBackdrop", "Meshes");
        var lib = ScriptableObject.CreateInstance<BackdropLibrary>();
        AssetDatabase.CreateAsset(lib, "Assets/proceduralBackdrop/BackdropLibrary.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("[plan] BackdropLibrary.asset created");
    }
}
CS
unity command get_console_logs
```

If `eval_file` does not accept stdin, write the snippet to a temp `.cs` file under the scratchpad and pass its path. Check the real schema with `unity --json list`.

- [ ] **Step 5: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/BackdropLibrary.cs Assets/proceduralBackdrop/BackdropLibrary.cs.meta Assets/proceduralBackdrop/Editor Assets/proceduralBackdrop/BackdropLibrary.asset Assets/proceduralBackdrop/BackdropLibrary.asset.meta Assets/proceduralBackdrop/Meshes.meta
git commit -m "Add BackdropLibrary with folder scan and triangle-count guard"
```

---

### Task 6: The four performance functions + `ShuffleBag` — **[AGENT]**

**Files:**
- Create: `Assets/proceduralBackdrop/Core/ShuffleBag.cs`
- Test: `Assets/proceduralBackdrop/Tests/Editor/ShuffleBagTests.cs`
- Modify: `Assets/proceduralBackdrop/BackdropInstrument.cs`

**Interfaces:**
- Consumes: `BackdropInstrument.Apply` / `ApplyAndRelayout` (Task 4), `BackdropLibrary.Count` (Task 5).
- Produces: `class ShuffleBag` with `ShuffleBag(int count, int seed)`, `int Next()`, `int Count { get; }`, `void Resize(int count)`. And on `BackdropInstrument`: `void RerollLayout()`, `void CycleShading()`, `void CycleMesh()`, `void Flash()`. Tasks 7 and 8 call the four functions.

**Why a shuffle bag and not `Random.Range`:** with three shading modes, uniform random repeats about a third of the time, so a third of presses do nothing visible and the pad reads as dead. A shuffle bag guarantees every press changes something while still not feeling sequential.

- [ ] **Step 1: Write the failing test**

`Assets/proceduralBackdrop/Tests/Editor/ShuffleBagTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class ShuffleBagTests
{
    [Test]
    public void Next_NeverRepeatsTheSameValueTwiceInARow()
    {
        var bag = new ShuffleBag(3, seed: 12345);

        int previous = bag.Next();
        for (int i = 0; i < 500; i++)
        {
            int next = bag.Next();
            Assert.AreNotEqual(previous, next,
                $"repeat at draw {i}: a repeated value is a pad press that does nothing visible");
            previous = next;
        }
    }

    [Test]
    public void Next_VisitsEveryValueWithinTwoPasses()
    {
        var bag = new ShuffleBag(5, seed: 7);
        var seen = new HashSet<int>();

        for (int i = 0; i < 10; i++) seen.Add(bag.Next());

        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, seen);
    }

    [Test]
    public void Next_IsReproducibleForAGivenSeed()
    {
        var a = new ShuffleBag(6, seed: 99);
        var b = new ShuffleBag(6, seed: 99);

        for (int i = 0; i < 50; i++)
            Assert.AreEqual(a.Next(), b.Next(), $"divergence at draw {i}");
    }

    [Test]
    public void Next_ReturnsZeroForASingleEntry()
    {
        var bag = new ShuffleBag(1, seed: 1);
        Assert.AreEqual(0, bag.Next());
        Assert.AreEqual(0, bag.Next());
    }

    [Test]
    public void Next_ReturnsMinusOneWhenEmpty()
    {
        var bag = new ShuffleBag(0, seed: 1);
        Assert.AreEqual(-1, bag.Next());
    }

    [Test]
    public void Resize_KeepsDrawingValidIndices()
    {
        var bag = new ShuffleBag(3, seed: 4);
        bag.Next();
        bag.Resize(8);

        for (int i = 0; i < 40; i++)
        {
            int v = bag.Next();
            Assert.GreaterOrEqual(v, 0);
            Assert.Less(v, 8);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
unity command recompile && unity command recompile_status
```

Expected: `failed: true`, unknown type `ShuffleBag`.

- [ ] **Step 3: Write the implementation**

`Assets/proceduralBackdrop/Core/ShuffleBag.cs`:

```csharp
using System;
using System.Collections.Generic;

/// <summary>
/// Draws indices in a shuffled order that visits every value once per pass and
/// never repeats across a pass boundary.
///
/// This exists because uniform random is the wrong feel for a performance pad.
/// With three shading modes, Random.Range repeats about a third of the time,
/// and a press that produces no visible change reads as a dead button. A
/// shuffle bag guarantees motion on every press without feeling sequential.
///
/// System.Random with an explicit seed, per this project's convention:
/// UnityEngine.Random is a global shared stream anything else can perturb, so
/// compositions built on it are not reproducible.
/// </summary>
public class ShuffleBag
{
    private readonly List<int> order = new List<int>();
    private System.Random rng;
    private int count;
    private int cursor;
    private int lastDrawn = -1;

    public int Count => count;

    public ShuffleBag(int count, int seed)
    {
        rng = new System.Random(seed);
        Resize(count);
    }

    /// <summary>Changes the value range, e.g. when the mesh library is rescanned.</summary>
    public void Resize(int newCount)
    {
        count = Math.Max(0, newCount);
        Reshuffle();
    }

    public int Next()
    {
        if (count == 0) return -1;
        if (count == 1) return 0;

        if (cursor >= order.Count) Reshuffle();

        int value = order[cursor++];
        lastDrawn = value;
        return value;
    }

    private void Reshuffle()
    {
        order.Clear();
        for (int i = 0; i < count; i++) order.Add(i);

        // Fisher-Yates.
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // Guard the pass boundary: without this, a pass ending in 2 followed by
        // a pass starting with 2 produces exactly the dead press this class
        // exists to prevent.
        if (count > 1 && order.Count > 1 && order[0] == lastDrawn)
            (order[0], order[order.Count - 1]) = (order[order.Count - 1], order[0]);

        cursor = 0;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

```bash
unity command recompile && unity command recompile_status
unity command run_tests --mode EditMode --filter ShuffleBagTests
unity command test_status
```

Expected: 6 passing.

- [ ] **Step 5: Add the four functions to `BackdropInstrument`**

Insert into `Assets/proceduralBackdrop/BackdropInstrument.cs`, after `SetMeshIndex`:

```csharp
    [Header("Performance functions")]
    [Tooltip("Seed for the shading and mesh shuffle bags. Same seed replays the same sequence of presses.")]
    [SerializeField] private int cycleSeed = 20260911;

    private ShuffleBag shadingBag;
    private ShuffleBag meshBag;
    private System.Random layoutRng;

    private void EnsureCyclers()
    {
        if (layoutRng == null)  layoutRng  = new System.Random(cycleSeed);
        if (shadingBag == null) shadingBag = new ShuffleBag(3, cycleSeed);

        int libCount = library != null ? library.Count : 0;
        if (meshBag == null)          meshBag = new ShuffleBag(libCount, cycleSeed + 1);
        else if (meshBag.Count != libCount) meshBag.Resize(libCount);
    }

    /// <summary>
    /// New arrangement. The only function that reinitialises: positions are
    /// computed in Initialize, so a seed change only reaches newly spawned
    /// particles. Resets spin phase, wave phase and any in-flight flash, which
    /// is correct for a deliberate reroll.
    /// </summary>
    public void RerollLayout()
    {
        EnsureCyclers();
        var p = parameters;
        p.layoutSeed = unchecked((uint)layoutRng.Next(1, int.MaxValue));
        ApplyAndRelayout(p);
    }

    /// <summary>Next shading model. No reinit — the composition is held.</summary>
    public void CycleShading()
    {
        EnsureCyclers();
        int next = shadingBag.Next();
        if (next < 0) return;

        var p = parameters;
        p.shading = (BackdropShadingMode)next;
        Apply(p);
    }

    /// <summary>Next mesh from the library. No reinit — same positions, different object.</summary>
    public void CycleMesh()
    {
        EnsureCyclers();
        int next = meshBag.Next();
        if (next < 0) return;
        SetMeshIndex(next);
    }

    /// <summary>
    /// Writes the flash timestamp. One property write and nothing else: the
    /// graph computes exp(-(t - FlashTime) * FlashDecay) per instance, offset
    /// by normalised instance ID so the flash ripples across the field. There
    /// is deliberately no coroutine and no per-frame C# here.
    /// </summary>
    public void Flash()
    {
        if (vfx == null) return;
        vfx.SetFloat(IdFlashTime, Time.time);
    }
```

- [ ] **Step 6: Compile**

```bash
unity command recompile && unity command recompile_status
```

Expected: `failed: false`.

- [ ] **Step 7: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/Core/ShuffleBag.cs Assets/proceduralBackdrop/Core/ShuffleBag.cs.meta Assets/proceduralBackdrop/Tests/Editor/ShuffleBagTests.cs Assets/proceduralBackdrop/Tests/Editor/ShuffleBagTests.cs.meta Assets/proceduralBackdrop/BackdropInstrument.cs
git commit -m "Add the four backdrop performance functions and a seeded shuffle bag"
```

---

### Task 7: `KeyboardBackdropDriver` — **[AGENT]**

**Files:**
- Create: `Assets/proceduralBackdrop/KeyboardBackdropDriver.cs`

**Interfaces:**
- Consumes: `BackdropInstrument.RerollLayout / CycleShading / CycleMesh / Flash` (Task 6), `BackdropInstrument.Current` (Task 4).
- Produces: nothing consumed downstream.

**Why this exists:** it is how the backdrop gets built and judged tonight with no MF64 attached — the same role `KeyboardPoseDriver` plays for the pose instrument. Nothing in this project has been run against the MIDI hardware.

- [ ] **Step 1: Write the implementation**

`Assets/proceduralBackdrop/KeyboardBackdropDriver.cs`:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keys 1-4 -> the four backdrop performance functions, plus D to walk the
/// domain shape. How the backdrop is played without the MF64 attached.
///
/// This project is Input System package only (activeInputHandler = 1), so the
/// legacy UnityEngine.Input class throws at runtime. Everything goes through
/// Keyboard.current.
///
/// Keys:
///   1  new layout      2  swap shading
///   3  flash           4  next mesh
///   D  next domain (plane / hemisphere / cube)
/// </summary>
[RequireComponent(typeof(BackdropInstrument))]
public class KeyboardBackdropDriver : MonoBehaviour
{
    [SerializeField] private BackdropInstrument instrument;

    [Tooltip("Draw a small on-screen legend and live state readout.")]
    [SerializeField] private bool showOverlay = true;

    private void Reset() => instrument = GetComponent<BackdropInstrument>();
    private void Awake() { if (instrument == null) instrument = GetComponent<BackdropInstrument>(); }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || instrument == null) return;

        if (kb.digit1Key.wasPressedThisFrame) instrument.RerollLayout();
        if (kb.digit2Key.wasPressedThisFrame) instrument.CycleShading();
        if (kb.digit3Key.wasPressedThisFrame) instrument.Flash();
        if (kb.digit4Key.wasPressedThisFrame) instrument.CycleMesh();

        if (kb.dKey.wasPressedThisFrame) NextDomain();
    }

    private void NextDomain()
    {
        var p = instrument.Current;
        p.domain = (BackdropDomain)(((int)p.domain + 1) % 3);
        instrument.ApplyAndRelayout(p);
    }

    private void OnGUI()
    {
        if (!showOverlay || instrument == null) return;

        var p = instrument.Current;
        int meshes = instrument.Library != null ? instrument.Library.Count : 0;

        GUI.Box(new Rect(10, 150, 320, 112), GUIContent.none);
        GUILayout.BeginArea(new Rect(20, 158, 300, 100));
        GUILayout.Label($"Backdrop: {p.spawnCount} on {p.domain}   seed {p.layoutSeed}");
        GUILayout.Label($"Shading: {p.shading}   Mesh {instrument.MeshIndex + 1}/{meshes}");
        GUILayout.Label("1 layout   2 shading   3 flash");
        GUILayout.Label("4 mesh     D domain");
        GUILayout.EndArea();
    }
}
```

The overlay is drawn at y=150 so it does not collide with `KeyboardPoseDriver`'s box at y=10 when both drivers end up in the same scene.

- [ ] **Step 2: Compile**

```bash
unity command recompile && unity command recompile_status
```

Expected: `failed: false`.

- [ ] **Step 3: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/KeyboardBackdropDriver.cs Assets/proceduralBackdrop/KeyboardBackdropDriver.cs.meta
git commit -m "Add keyboard driver so the backdrop is playable without an MF64"
```

---

### Task 8: `MidiFighterBackdropDriver` — **[AGENT]**

**Files:**
- Create: `Assets/proceduralBackdrop/MidiFighterBackdropDriver.cs`

**Interfaces:**
- Consumes: `BackdropInstrument`'s four functions, `MidiFighterButtonRouter.OnButtonPress/OnButtonRelease`, `GridButton`, `MidiFighterOutput`, `MidiFighterLEDColor`, `MidiFighter64InputMap.ToNote` (from the `com.caseyfarina.midifighter64` package).
- Produces: nothing consumed downstream.

**Structure is copied deliberately from `Assets/mixamoDance/MidiFighterPoseDriver.cs`** — same serialized-pad-binding shape, same static-event discipline, same optional LED output. Read that file before writing this one.

- [ ] **Step 1: Write the implementation**

`Assets/proceduralBackdrop/MidiFighterBackdropDriver.cs`:

```csharp
using UnityEngine;
using MidiFighter64;

/// <summary>
/// Midi Fighter 64 pads -> BackdropInstrument. The only component in this
/// system that knows MIDI exists; the instrument itself is input-agnostic.
///
/// Pad bindings are serialized data rather than a hardcoded note function, so
/// the claimed pads are visible in the Inspector and auditable against
/// MidiFighterInteriorSpawner, which claims every pad it is not explicitly
/// told to skip.
///
/// All four pads default to row 0 / col 0, which means unbound. The main
/// scene's pad map is being reworked; assign these when that lands.
/// </summary>
public class MidiFighterBackdropDriver : MonoBehaviour
{
    [SerializeField] private BackdropInstrument instrument;

    [Header("Performance pads (row 0 / col 0 = unbound)")]
    [SerializeField] private Vector2Int newLayoutPad   = Vector2Int.zero;
    [SerializeField] private Vector2Int swapShadingPad = Vector2Int.zero;
    [SerializeField] private Vector2Int flashPad       = Vector2Int.zero;
    [SerializeField] private Vector2Int nextMeshPad    = Vector2Int.zero;

    [Header("LED feedback")]
    [Tooltip("Optional. Leave empty to skip LED output entirely.")]
    [SerializeField] private MidiFighterOutput output;
    [SerializeField] private MidiFighterLEDColor idleColor    = MidiFighterLEDColor.DarkBlue;
    [SerializeField] private MidiFighterLEDColor pressedColor = MidiFighterLEDColor.BrightPink;

    private void Reset() => instrument = GetComponent<BackdropInstrument>();
    private void Awake() { if (instrument == null) instrument = GetComponent<BackdropInstrument>(); }

    // MidiFighterButtonRouter events are static - the -= is mandatory, not optional.
    private void OnEnable()
    {
        MidiFighterButtonRouter.OnButtonPress   += HandlePress;
        MidiFighterButtonRouter.OnButtonRelease += HandleRelease;
        PaintIdleLeds();
    }

    private void OnDisable()
    {
        MidiFighterButtonRouter.OnButtonPress   -= HandlePress;
        MidiFighterButtonRouter.OnButtonRelease -= HandleRelease;
    }

    private void HandlePress(GridButton b, float velocity)
    {
        if (instrument == null) return;

        if (Matches(b, newLayoutPad))   { instrument.RerollLayout();  SetLed(b.row, b.col, pressedColor); return; }
        if (Matches(b, swapShadingPad)) { instrument.CycleShading();  SetLed(b.row, b.col, pressedColor); return; }
        if (Matches(b, flashPad))       { instrument.Flash();         SetLed(b.row, b.col, pressedColor); return; }
        if (Matches(b, nextMeshPad))    { instrument.CycleMesh();     SetLed(b.row, b.col, pressedColor); return; }
    }

    private void HandleRelease(GridButton b)
    {
        if (Matches(b, newLayoutPad) || Matches(b, swapShadingPad) ||
            Matches(b, flashPad)     || Matches(b, nextMeshPad))
            SetLed(b.row, b.col, idleColor);
    }

    private static bool Matches(GridButton b, Vector2Int pad)
        => pad.x >= 1 && pad.y >= 1 && b.row == pad.x && b.col == pad.y;

    private void PaintIdleLeds()
    {
        if (output == null) return;
        PaintOne(newLayoutPad);
        PaintOne(swapShadingPad);
        PaintOne(flashPad);
        PaintOne(nextMeshPad);
    }

    private void PaintOne(Vector2Int pad)
    {
        if (pad.x < 1 || pad.y < 1) return;
        SetLed(pad.x, pad.y, idleColor);
    }

    private void SetLed(int row, int col, MidiFighterLEDColor color)
    {
        if (output == null) return;
        output.SetLED(MidiFighter64InputMap.ToNote(row, col), color);
    }
}
```

- [ ] **Step 2: Compile**

```bash
unity command recompile && unity command recompile_status
unity command get_console_logs
```

Expected: `failed: false`. If `MidiFighter64` types do not resolve, check `Packages/com.caseyfarina.midifighter64/CLAUDE.md` for the current API — that package is the authority and this project must not edit it.

- [ ] **Step 3: [YOU] Wire the scene and play it**

- Open `BackdropSmoke.unity`.
- On the `Backdrop` VisualEffect GameObject, add `BackdropInstrument`, `KeyboardBackdropDriver`, `MidiFighterBackdropDriver`.
- Assign `BackdropLibrary.asset` to the instrument's **Library** field.
- Drop 3–5 low-poly FBXs into `Assets/proceduralBackdrop/Meshes/`, select the library asset, press **Scan Folder**.
- Enter Play mode. Press `1`, `2`, `3`, `4`, `D` and confirm each does what the overlay says.

- [ ] **Step 4: [AGENT] Confirm the graph contract holds**

```bash
unity command get_console_logs
```

Expected: **no** `[BackdropInstrument] Backdrop.vfx is missing` error. If that error appears, it names exactly which exposed properties in `Backdrop.vfx` are misspelled or missing — fix them in the graph (Task 3, Step 2) rather than changing the C#.

- [ ] **Step 5: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop
git commit -m "Add MF64 driver for the backdrop and wire the smoke scene"
```

**PHASE 1 COMPLETE.** The backdrop distributes across three domains, animates continuously, and responds to all four functions from the keyboard.

---

# PHASE 2 — THE SEARCH

Deliverable: randomise, mutate, history and save. Depends on Phase 1 only through `BackdropParameters` and `BackdropInstrument.Apply()`.

---

### Task 9: `BackdropRanges` — **[AGENT]**

**Files:**
- Create: `Assets/proceduralBackdrop/Core/BackdropRanges.cs`

**Interfaces:**
- Consumes: `BackdropParameters` (Task 1).
- Produces: `class BackdropRanges : ScriptableObject` with a `FloatRange`/`Vector3Range` field per randomisable parameter, each carrying a `locked` bool, plus `static BackdropRanges CreateDefault()`. Task 10 reads every field.

**Why ranges exist:** a randomiser with no authored bounds produces garbage on nearly every press. Ranges are the box it samples inside, and tightening them as the search proceeds is how it converges. The locks are what stop settled ground being re-explored on every press.

- [ ] **Step 1: Write the implementation**

`Assets/proceduralBackdrop/Core/BackdropRanges.cs`:

```csharp
using UnityEngine;

/// <summary>
/// One randomisable scalar: the interval it is sampled from, plus a lock.
/// Locked parameters are passed through from the base value untouched, which
/// is what lets a search converge — settle the scale range, lock it, and
/// randomise only what is still open.
/// </summary>
[System.Serializable]
public struct RandomRange
{
    public bool locked;
    public float min;
    public float max;

    public RandomRange(float min, float max, bool locked = false)
    {
        this.min = min; this.max = max; this.locked = locked;
    }

    /// <summary>Ordered so an inverted min/max authored by hand still samples correctly.</summary>
    public float Sample(System.Random rng)
    {
        float a = Mathf.Min(min, max);
        float b = Mathf.Max(min, max);
        return a + (float)rng.NextDouble() * (b - a);
    }

    public float Span => Mathf.Abs(max - min);
}

/// <summary>
/// The box BackdropRandomizer samples inside. Separate from BackdropPresetBook
/// on purpose: the book holds points in the space, this defines the space.
///
/// spawnCount and domainSize ranges are also the performance rail — the
/// expected operating scale is hundreds of instances, and an unbounded
/// randomiser would happily hand the graph tens of thousands across eight
/// split-screen cells.
/// </summary>
[CreateAssetMenu(menuName = "Performance/Backdrop Ranges", fileName = "BackdropRanges")]
public class BackdropRanges : ScriptableObject
{
    [Header("Layout")]
    public bool randomiseDomain = true;
    public RandomRange spawnCount = new RandomRange(120, 600);
    public RandomRange domainSizeX = new RandomRange(30f, 90f);
    public RandomRange domainSizeY = new RandomRange(15f, 60f);
    public RandomRange domainSizeZ = new RandomRange(30f, 90f);
    public bool randomiseSolidFill = true;

    [Header("Per-instance variation")]
    public RandomRange scaleMin = new RandomRange(0.2f, 1.0f);
    public RandomRange scaleMax = new RandomRange(1.0f, 3.5f);
    public RandomRange scaleBiasX = new RandomRange(0.5f, 2f);
    public RandomRange scaleBiasY = new RandomRange(0.5f, 8f);
    public RandomRange scaleBiasZ = new RandomRange(0.5f, 2f);
    public RandomRange offsetJitter = new RandomRange(0f, 3f);
    public RandomRange rotationJitter = new RandomRange(0f, 180f);

    [Header("Animation")]
    public RandomRange spinRate = new RandomRange(0f, 45f);
    public RandomRange waveAmplitude = new RandomRange(0f, 3f);
    public RandomRange waveFrequency = new RandomRange(0.02f, 0.6f);
    public RandomRange wavePhaseSpread = new RandomRange(0f, 1f);

    [Header("Look")]
    public bool randomiseShading = true;
    public RandomRange flashDecay = new RandomRange(1.5f, 10f);
    public RandomRange flashIntensity = new RandomRange(2f, 20f);
    public RandomRange flashRipple = new RandomRange(0f, 0.8f);
    public bool randomiseEmissionHue = true;
    public RandomRange emissionSaturation = new RandomRange(0f, 0.8f);

    public static BackdropRanges CreateDefault() => CreateInstance<BackdropRanges>();
}
```

- [ ] **Step 2: Compile**

```bash
unity command recompile && unity command recompile_status
```

Expected: `failed: false`.

- [ ] **Step 3: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/Core/BackdropRanges.cs Assets/proceduralBackdrop/Core/BackdropRanges.cs.meta
git commit -m "Add BackdropRanges as the sampling box and performance rail"
```

---

### Task 10: `BackdropRandomizer` + tests — **[AGENT]**

**Files:**
- Create: `Assets/proceduralBackdrop/Core/BackdropRandomizer.cs`
- Test: `Assets/proceduralBackdrop/Tests/Editor/BackdropRandomizerTests.cs`

**Interfaces:**
- Consumes: `BackdropParameters` (Task 1), `BackdropRanges` / `RandomRange` (Task 9).
- Produces: `static class BackdropRandomizer` with `static BackdropParameters Randomize(BackdropRanges r, BackdropParameters basis, System.Random rng)` and `static BackdropParameters Mutate(BackdropRanges r, BackdropParameters basis, float strength, System.Random rng)`. Task 12 calls both.

**This is the only component in the whole system with a real automated test surface** — it is pure, seeded and free of Unity runtime dependencies. Everything downstream is a GPU shader judged by eye. Test it properly.

- [ ] **Step 1: Write the failing tests**

`Assets/proceduralBackdrop/Tests/Editor/BackdropRandomizerTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class BackdropRandomizerTests
{
    private static BackdropRanges Ranges() => BackdropRanges.CreateDefault();

    [Test]
    public void Randomize_IsReproducibleForAGivenSeed()
    {
        var r = Ranges();
        var basis = BackdropParameters.Default;

        var a = BackdropRandomizer.Randomize(r, basis, new System.Random(4242));
        var b = BackdropRandomizer.Randomize(r, basis, new System.Random(4242));

        Assert.AreEqual(a.spawnCount, b.spawnCount);
        Assert.AreEqual(a.domain, b.domain);
        Assert.AreEqual(a.domainSize, b.domainSize);
        Assert.AreEqual(a.scaleRange, b.scaleRange);
        Assert.AreEqual(a.waveFrequency, b.waveFrequency, 1e-6f);
        Assert.AreEqual(a.shading, b.shading);
    }

    [Test]
    public void Randomize_DiffersAcrossSeeds()
    {
        var r = Ranges();
        var basis = BackdropParameters.Default;

        var a = BackdropRandomizer.Randomize(r, basis, new System.Random(1));
        var b = BackdropRandomizer.Randomize(r, basis, new System.Random(2));

        Assert.AreNotEqual(a.waveFrequency, b.waveFrequency);
    }

    [Test]
    public void Randomize_RespectsSpawnCountRange()
    {
        var r = Ranges();
        r.spawnCount = new RandomRange(50, 200);

        for (int seed = 0; seed < 200; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));
            Assert.GreaterOrEqual(p.spawnCount, 50, $"seed {seed}");
            Assert.LessOrEqual(p.spawnCount, 200, $"seed {seed}");
        }
    }

    [Test]
    public void Randomize_NeverExceedsTheHardCeiling()
    {
        var r = Ranges();
        // An operator could plausibly type this into the inspector.
        r.spawnCount = new RandomRange(50000, 90000);

        for (int seed = 0; seed < 50; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));
            Assert.LessOrEqual(p.spawnCount, BackdropParameters.MaxSpawnCount, $"seed {seed}");
        }
    }

    [Test]
    public void Randomize_LeavesLockedParametersAtTheBasisValue()
    {
        var r = Ranges();
        r.spawnCount = new RandomRange(50, 200, locked: true);
        r.waveAmplitude = new RandomRange(0f, 5f, locked: true);

        var basis = BackdropParameters.Default;
        basis.spawnCount = 777;
        basis.waveAmplitude = 1.25f;

        for (int seed = 0; seed < 50; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, basis, new System.Random(seed));
            Assert.AreEqual(777, p.spawnCount, $"seed {seed}");
            Assert.AreEqual(1.25f, p.waveAmplitude, 1e-6f, $"seed {seed}");
        }
    }

    [Test]
    public void Randomize_AlwaysProducesAnOrderedScaleRange()
    {
        var r = Ranges();
        // Deliberately overlapping min/max ranges: the sampler can draw min > max.
        r.scaleMin = new RandomRange(0.5f, 3f);
        r.scaleMax = new RandomRange(0.5f, 3f);

        for (int seed = 0; seed < 200; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));
            Assert.LessOrEqual(p.scaleRange.x, p.scaleRange.y, $"seed {seed}");
        }
    }

    [Test]
    public void Randomize_NeverProducesAnInvisibleField()
    {
        var r = Ranges();

        for (int seed = 0; seed < 200; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));
            Assert.Greater(p.spawnCount, 0, $"seed {seed}: zero instances renders nothing");
            Assert.Greater(p.scaleRange.y, 0f, $"seed {seed}: zero max scale renders nothing");
            Assert.Greater(p.domainSize.x, 0f, $"seed {seed}");
        }
    }

    [Test]
    public void Mutate_AtZeroStrengthReturnsTheBasisUnchanged()
    {
        var r = Ranges();
        var basis = BackdropParameters.Default;

        var m = BackdropRandomizer.Mutate(r, basis, 0f, new System.Random(9));

        Assert.AreEqual(basis.spawnCount, m.spawnCount);
        Assert.AreEqual(basis.waveAmplitude, m.waveAmplitude, 1e-5f);
        Assert.AreEqual(basis.waveFrequency, m.waveFrequency, 1e-5f);
        Assert.AreEqual(basis.domain, m.domain);
    }

    [Test]
    public void Mutate_MovesLessThanAFullRandomize()
    {
        var r = Ranges();
        var basis = BackdropParameters.Default;
        basis.waveAmplitude = 1.5f;

        float mutated = 0f, randomised = 0f;
        for (int seed = 0; seed < 100; seed++)
        {
            mutated += Mathf.Abs(
                BackdropRandomizer.Mutate(r, basis, 0.1f, new System.Random(seed)).waveAmplitude
                - basis.waveAmplitude);
            randomised += Mathf.Abs(
                BackdropRandomizer.Randomize(r, basis, new System.Random(seed)).waveAmplitude
                - basis.waveAmplitude);
        }

        Assert.Less(mutated, randomised,
            "mutation is meant to refine inside a region, not resample the space");
    }

    [Test]
    public void Mutate_StaysInsideTheRange()
    {
        var r = Ranges();
        r.waveAmplitude = new RandomRange(0f, 3f);

        var basis = BackdropParameters.Default;
        basis.waveAmplitude = 2.95f;   // right against the ceiling

        for (int seed = 0; seed < 200; seed++)
        {
            var m = BackdropRandomizer.Mutate(r, basis, 0.5f, new System.Random(seed));
            Assert.GreaterOrEqual(m.waveAmplitude, 0f, $"seed {seed}");
            Assert.LessOrEqual(m.waveAmplitude, 3f, $"seed {seed}");
        }
    }

    [Test]
    public void Mutate_RespectsLocks()
    {
        var r = Ranges();
        r.spawnCount = new RandomRange(50, 600, locked: true);

        var basis = BackdropParameters.Default;
        basis.spawnCount = 321;

        for (int seed = 0; seed < 50; seed++)
            Assert.AreEqual(321,
                BackdropRandomizer.Mutate(r, basis, 0.9f, new System.Random(seed)).spawnCount,
                $"seed {seed}");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
unity command recompile && unity command recompile_status
```

Expected: `failed: true`, unknown type `BackdropRandomizer`.

- [ ] **Step 3: Write the implementation**

`Assets/proceduralBackdrop/Core/BackdropRandomizer.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Turns a BackdropRanges box into concrete BackdropParameters. Pure, seeded
/// and free of Unity runtime dependencies, which makes it the one part of the
/// backdrop system with a real automated test surface — everything downstream
/// is a GPU shader judged by eye.
///
/// Randomize resamples the whole space; Mutate perturbs an existing point by a
/// fraction of each range's span. Both honour locks. The pair is the point of
/// the exploration rig: Randomize finds a region, Mutate refines inside it.
/// </summary>
public static class BackdropRandomizer
{
    /// <summary>Full resample. Locked parameters come through from basis untouched.</summary>
    public static BackdropParameters Randomize(
        BackdropRanges r, BackdropParameters basis, System.Random rng)
    {
        return Sample(r, basis, rng, strength: 1f, mutate: false).Clamped();
    }

    /// <summary>
    /// Perturbs basis by `strength` of each range's span. strength 0 returns
    /// basis unchanged; strength 1 moves up to a full span in either direction
    /// but is still anchored on basis, so it is not the same as Randomize.
    /// </summary>
    public static BackdropParameters Mutate(
        BackdropRanges r, BackdropParameters basis, float strength, System.Random rng)
    {
        return Sample(r, basis, rng, Mathf.Max(0f, strength), mutate: true).Clamped();
    }

    private static BackdropParameters Sample(
        BackdropRanges r, BackdropParameters basis, System.Random rng, float strength, bool mutate)
    {
        var p = basis;
        if (r == null) return p;

        // Draw order is fixed and never conditional on a lock, so locking one
        // parameter does not shift the values every later parameter receives.
        // Without this, ticking a lock would silently change the whole result.

        float spawn        = Draw(r.spawnCount,        basis.spawnCount,        rng, strength, mutate);
        float sizeX        = Draw(r.domainSizeX,       basis.domainSize.x,      rng, strength, mutate);
        float sizeY        = Draw(r.domainSizeY,       basis.domainSize.y,      rng, strength, mutate);
        float sizeZ        = Draw(r.domainSizeZ,       basis.domainSize.z,      rng, strength, mutate);
        float sMin         = Draw(r.scaleMin,          basis.scaleRange.x,      rng, strength, mutate);
        float sMax         = Draw(r.scaleMax,          basis.scaleRange.y,      rng, strength, mutate);
        float biasX        = Draw(r.scaleBiasX,        basis.scaleAxisBias.x,   rng, strength, mutate);
        float biasY        = Draw(r.scaleBiasY,        basis.scaleAxisBias.y,   rng, strength, mutate);
        float biasZ        = Draw(r.scaleBiasZ,        basis.scaleAxisBias.z,   rng, strength, mutate);
        float jitter       = Draw(r.offsetJitter,      basis.offsetJitter.x,    rng, strength, mutate);
        float rotJitter    = Draw(r.rotationJitter,    basis.rotationJitter.y,  rng, strength, mutate);
        float spin         = Draw(r.spinRate,          Mathf.Abs(basis.spinRateRange.y), rng, strength, mutate);
        float waveAmp      = Draw(r.waveAmplitude,     basis.waveAmplitude,     rng, strength, mutate);
        float waveFreq     = Draw(r.waveFrequency,     basis.waveFrequency,     rng, strength, mutate);
        float wavePhase    = Draw(r.wavePhaseSpread,   basis.wavePhaseSpread,   rng, strength, mutate);
        float fDecay       = Draw(r.flashDecay,        basis.flashDecay,        rng, strength, mutate);
        float fIntensity   = Draw(r.flashIntensity,    basis.flashIntensity,    rng, strength, mutate);
        float fRipple      = Draw(r.flashRipple,       basis.flashRipple,       rng, strength, mutate);
        float emissionSat  = Draw(r.emissionSaturation, 0.5f,                   rng, strength, mutate);

        // Discrete draws come last, again always consuming the same number of
        // rng values whether or not they are enabled.
        int   domainRoll   = rng.Next(3);
        int   shadingRoll  = rng.Next(3);
        bool  solidRoll    = rng.Next(2) == 0;
        float hueRoll      = (float)rng.NextDouble();

        p.spawnCount     = Mathf.RoundToInt(spawn);
        p.domainSize     = new Vector3(sizeX, sizeY, sizeZ);
        p.scaleRange     = new Vector2(Mathf.Min(sMin, sMax), Mathf.Max(sMin, sMax));
        p.scaleAxisBias  = new Vector3(biasX, biasY, biasZ);
        p.offsetJitter   = new Vector3(jitter, jitter, jitter);
        p.rotationJitter = new Vector3(0f, rotJitter, 0f);
        p.spinRateRange  = new Vector2(-spin, spin);
        p.waveAmplitude  = waveAmp;
        p.waveFrequency  = waveFreq;
        p.wavePhaseSpread = wavePhase;
        p.flashDecay     = fDecay;
        p.flashIntensity = fIntensity;
        p.flashRipple    = fRipple;

        // Discrete parameters are not mutated at low strength — flipping the
        // domain shape is a jump out of the region, not a refinement of it.
        bool allowDiscrete = !mutate || strength >= 0.5f;

        if (r.randomiseDomain && allowDiscrete)
            p.domain = (BackdropDomain)domainRoll;

        if (r.randomiseShading && allowDiscrete)
            p.shading = (BackdropShadingMode)shadingRoll;

        if (r.randomiseSolidFill && allowDiscrete)
            p.solidFill = solidRoll;

        if (r.randomiseEmissionHue && allowDiscrete)
            p.emissionColor = Color.HSVToRGB(hueRoll, Mathf.Clamp01(emissionSat), 1f);

        return p;
    }

    /// <summary>
    /// One scalar. A locked range returns the basis value but still consumes an
    /// rng draw, so locking a parameter changes only that parameter rather than
    /// reshuffling everything sampled after it.
    /// </summary>
    private static float Draw(
        RandomRange range, float basis, System.Random rng, float strength, bool mutate)
    {
        float roll = (float)rng.NextDouble();

        if (range.locked) return basis;

        float lo = Mathf.Min(range.min, range.max);
        float hi = Mathf.Max(range.min, range.max);

        if (!mutate) return Mathf.Lerp(lo, hi, roll);

        float delta = (roll * 2f - 1f) * strength * range.Span;
        return Mathf.Clamp(basis + delta, lo, hi);
    }
}
```

- [ ] **Step 4: Run to verify they pass**

```bash
unity command recompile && unity command recompile_status
unity command run_tests --mode EditMode --filter BackdropRandomizerTests
unity command test_status
```

Expected: 11 passing.

- [ ] **Step 5: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/Core/BackdropRandomizer.cs Assets/proceduralBackdrop/Core/BackdropRandomizer.cs.meta Assets/proceduralBackdrop/Tests/Editor/BackdropRandomizerTests.cs Assets/proceduralBackdrop/Tests/Editor/BackdropRandomizerTests.cs.meta
git commit -m "Add seeded backdrop randomiser with locks and mutation"
```

---

### Task 11: `BackdropPresetBook` — **[AGENT]**

**Files:**
- Create: `Assets/proceduralBackdrop/Core/BackdropPresetBook.cs`

**Interfaces:**
- Consumes: `BackdropParameters` (Task 1).
- Produces: `class BackdropPresetBook : ScriptableObject` with `List<BackdropPreset> presets`, `int Count`, `bool TryGet(int index, out BackdropParameters p)`, `void Save(string name, BackdropParameters p)`. Task 12 calls `TryGet` and `Save`.

- [ ] **Step 1: Write the implementation**

`Assets/proceduralBackdrop/Core/BackdropPresetBook.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct BackdropPreset
{
    public string name;
    public BackdropParameters parameters;
}

/// <summary>
/// Named points in the backdrop parameter space. One asset holding a list
/// rather than one asset per preset, so the whole set can be browsed and
/// reordered in a single inspector during a search session.
///
/// Where BackdropRanges defines the space, this holds the results worth
/// keeping from searching it.
/// </summary>
[CreateAssetMenu(menuName = "Performance/Backdrop Preset Book", fileName = "BackdropPresetBook")]
public class BackdropPresetBook : ScriptableObject
{
    public List<BackdropPreset> presets = new List<BackdropPreset>();

    public int Count => presets.Count;

    public bool TryGet(int index, out BackdropParameters p)
    {
        if (index < 0 || index >= presets.Count) { p = default; return false; }
        p = presets[index].parameters;
        return true;
    }

    /// <summary>
    /// Appends, or overwrites when the name already exists. Overwriting by name
    /// is deliberate: during a search you re-save the same slot repeatedly as a
    /// look is refined, and silently accumulating twelve presets called "tower"
    /// is worse than replacing one.
    /// </summary>
    public void Save(string name, BackdropParameters p)
    {
        for (int i = 0; i < presets.Count; i++)
        {
            if (presets[i].name != name) continue;
            presets[i] = new BackdropPreset { name = name, parameters = p };
            return;
        }

        presets.Add(new BackdropPreset { name = name, parameters = p });
    }
}
```

- [ ] **Step 2: Compile**

```bash
unity command recompile && unity command recompile_status
```

Expected: `failed: false`.

- [ ] **Step 3: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/Core/BackdropPresetBook.cs Assets/proceduralBackdrop/Core/BackdropPresetBook.cs.meta
git commit -m "Add BackdropPresetBook for saved parameter sets"
```

---

### Task 12: `HistoryRing` + `BackdropExplorerDriver` — **[AGENT]**

**Files:**
- Create: `Assets/proceduralBackdrop/Core/HistoryRing.cs`
- Test: `Assets/proceduralBackdrop/Tests/Editor/HistoryRingTests.cs`
- Create: `Assets/proceduralBackdrop/BackdropExplorerDriver.cs`

**Interfaces:**
- Consumes: `BackdropParameters`, `BackdropRanges`, `BackdropRandomizer`, `BackdropPresetBook`, `BackdropInstrument.ApplyAndRelayout`.
- Produces: `class HistoryRing<T>` with `HistoryRing(int capacity)`, `void Push(T item)`, `bool TryBack(out T item)`, `bool TryForward(out T item)`, `int Count`.

- [ ] **Step 1: Write the failing test**

`Assets/proceduralBackdrop/Tests/Editor/HistoryRingTests.cs`:

```csharp
using NUnit.Framework;

public class HistoryRingTests
{
    [Test]
    public void TryBack_WalksToThePreviousEntry()
    {
        var ring = new HistoryRing<int>(20);
        ring.Push(1); ring.Push(2); ring.Push(3);

        Assert.IsTrue(ring.TryBack(out int a));
        Assert.AreEqual(2, a);
        Assert.IsTrue(ring.TryBack(out int b));
        Assert.AreEqual(1, b);
    }

    [Test]
    public void TryBack_StopsAtTheOldestEntry()
    {
        var ring = new HistoryRing<int>(20);
        ring.Push(1); ring.Push(2);

        Assert.IsTrue(ring.TryBack(out _));
        Assert.IsFalse(ring.TryBack(out _), "walking past the oldest entry must not wrap");
    }

    [Test]
    public void TryForward_ReturnsToTheNewerEntry()
    {
        var ring = new HistoryRing<int>(20);
        ring.Push(1); ring.Push(2); ring.Push(3);

        ring.TryBack(out _);
        ring.TryBack(out _);

        Assert.IsTrue(ring.TryForward(out int f));
        Assert.AreEqual(2, f);
    }

    [Test]
    public void TryForward_StopsAtTheNewestEntry()
    {
        var ring = new HistoryRing<int>(20);
        ring.Push(1); ring.Push(2);

        Assert.IsFalse(ring.TryForward(out _));
    }

    [Test]
    public void Push_AfterWalkingBackTruncatesTheRedoBranch()
    {
        var ring = new HistoryRing<int>(20);
        ring.Push(1); ring.Push(2); ring.Push(3);

        ring.TryBack(out _);       // now at 2
        ring.Push(99);             // new branch from 2

        Assert.IsFalse(ring.TryForward(out _), "3 was on the abandoned branch");
        Assert.IsTrue(ring.TryBack(out int b));
        Assert.AreEqual(2, b);
    }

    [Test]
    public void Push_DropsTheOldestWhenAtCapacity()
    {
        var ring = new HistoryRing<int>(3);
        ring.Push(1); ring.Push(2); ring.Push(3); ring.Push(4);

        Assert.AreEqual(3, ring.Count);

        Assert.IsTrue(ring.TryBack(out int a));
        Assert.AreEqual(3, a);
        Assert.IsTrue(ring.TryBack(out int b));
        Assert.AreEqual(2, b);
        Assert.IsFalse(ring.TryBack(out _), "1 was evicted");
    }

    [Test]
    public void TryBack_OnAnEmptyRingReportsFailure()
    {
        var ring = new HistoryRing<int>(5);
        Assert.IsFalse(ring.TryBack(out _));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
unity command recompile && unity command recompile_status
```

Expected: `failed: true`, unknown type `HistoryRing`.

- [ ] **Step 3: Write `HistoryRing`**

`Assets/proceduralBackdrop/Core/HistoryRing.cs`:

```csharp
using System.Collections.Generic;

/// <summary>
/// Fixed-capacity undo history with a cursor.
///
/// This is the cheapest high-value feature in the exploration rig: during a
/// randomise search the look you wanted is routinely the one two presses ago,
/// and without a history it is gone for good. Twenty entries costs nothing.
///
/// Pushing after walking back truncates the forward branch, which is standard
/// undo semantics — the alternative (keeping an orphaned redo stack that a new
/// press can no longer reach coherently) is more confusing than losing it.
/// </summary>
public class HistoryRing<T>
{
    private readonly List<T> items = new List<T>();
    private readonly int capacity;
    private int cursor = -1;

    public int Count => items.Count;

    public HistoryRing(int capacity)
    {
        this.capacity = System.Math.Max(1, capacity);
    }

    public void Push(T item)
    {
        // Truncate the forward branch.
        if (cursor >= 0 && cursor < items.Count - 1)
            items.RemoveRange(cursor + 1, items.Count - cursor - 1);

        items.Add(item);

        if (items.Count > capacity)
            items.RemoveAt(0);

        cursor = items.Count - 1;
    }

    public bool TryBack(out T item)
    {
        if (cursor <= 0) { item = default; return false; }
        cursor--;
        item = items[cursor];
        return true;
    }

    public bool TryForward(out T item)
    {
        if (cursor < 0 || cursor >= items.Count - 1) { item = default; return false; }
        cursor++;
        item = items[cursor];
        return true;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

```bash
unity command recompile && unity command recompile_status
unity command run_tests --mode EditMode --filter HistoryRingTests
unity command test_status
```

Expected: 7 passing.

- [ ] **Step 5: Write the explorer driver**

`Assets/proceduralBackdrop/BackdropExplorerDriver.cs`:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The parameter-space search rig. Space finds a region, M refines inside it,
/// the arrow keys recover the one you lost, S keeps it.
///
/// Lives in the exploration scene, not the performance rig — during a set the
/// backdrop is played with the four pads, not searched.
///
/// Keys:
///   Space        full randomise within BackdropRanges
///   M            mutate current by Mutation Strength
///   Left/Right   walk the history ring
///   S            save current into the preset book under Save Name
///   1-9          recall preset 1-9
/// </summary>
[RequireComponent(typeof(BackdropInstrument))]
public class BackdropExplorerDriver : MonoBehaviour
{
    [SerializeField] private BackdropInstrument instrument;

    [Tooltip("The box the randomiser samples inside. Tighten it as the search converges.")]
    [SerializeField] private BackdropRanges ranges;

    [Tooltip("Where S writes. Presets survive Play mode because it is an asset, not scene state.")]
    [SerializeField] private BackdropPresetBook presetBook;

    [Tooltip("Seed for the search itself. Same seed replays the same sequence of randomisations.")]
    [SerializeField] private int searchSeed = 1;

    [Range(0f, 1f)]
    [Tooltip("How far M moves. 0.1 refines; above 0.5 discrete parameters start flipping too.")]
    [SerializeField] private float mutationStrength = 0.1f;

    [SerializeField] private int historyCapacity = 20;
    [SerializeField] private string saveName = "untitled";
    [SerializeField] private bool showOverlay = true;

    private System.Random rng;
    private HistoryRing<BackdropParameters> history;
    private string lastAction = "-";

    private void Reset() => instrument = GetComponent<BackdropInstrument>();

    private void Awake()
    {
        if (instrument == null) instrument = GetComponent<BackdropInstrument>();
        rng = new System.Random(searchSeed);
        history = new HistoryRing<BackdropParameters>(historyCapacity);
        history.Push(instrument.Current);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || instrument == null) return;

        if (kb.spaceKey.wasPressedThisFrame) DoRandomize();
        if (kb.mKey.wasPressedThisFrame)     DoMutate();
        if (kb.leftArrowKey.wasPressedThisFrame)  StepHistory(back: true);
        if (kb.rightArrowKey.wasPressedThisFrame) StepHistory(back: false);
        if (kb.sKey.wasPressedThisFrame)     DoSave();

        for (int i = 0; i < 9; i++)
            if (kb[Key.Digit1 + i].wasPressedThisFrame) DoRecall(i);
    }

    private void DoRandomize()
    {
        if (ranges == null) { lastAction = "no ranges asset"; return; }
        var p = BackdropRandomizer.Randomize(ranges, instrument.Current, rng);
        Commit(p, "randomize");
    }

    private void DoMutate()
    {
        if (ranges == null) { lastAction = "no ranges asset"; return; }
        var p = BackdropRandomizer.Mutate(ranges, instrument.Current, mutationStrength, rng);
        Commit(p, $"mutate {mutationStrength:0.00}");
    }

    private void StepHistory(bool back)
    {
        bool ok = back ? history.TryBack(out var p) : history.TryForward(out p);
        if (!ok) { lastAction = back ? "at oldest" : "at newest"; return; }

        // Applied without pushing, or walking history would itself write history.
        instrument.ApplyAndRelayout(p);
        lastAction = back ? "back" : "forward";
    }

    private void DoSave()
    {
        if (presetBook == null) { lastAction = "no preset book"; return; }

        presetBook.Save(saveName, instrument.Current);
        lastAction = $"saved '{saveName}'";

#if UNITY_EDITOR
        // Without this the preset is lost when Play mode exits, which is exactly
        // when it matters — the whole point is to keep what the search found.
        UnityEditor.EditorUtility.SetDirty(presetBook);
        UnityEditor.AssetDatabase.SaveAssets();
#endif
    }

    private void DoRecall(int index)
    {
        if (presetBook == null || !presetBook.TryGet(index, out var p))
        {
            lastAction = $"no preset {index + 1}";
            return;
        }
        Commit(p, $"recall {index + 1}");
    }

    private void Commit(BackdropParameters p, string action)
    {
        instrument.ApplyAndRelayout(p);
        history.Push(instrument.Current);   // Current is the clamped version.
        lastAction = action;
    }

    private void OnGUI()
    {
        if (!showOverlay || instrument == null) return;

        var p = instrument.Current;

        GUI.Box(new Rect(10, 270, 340, 176), GUIContent.none);
        GUILayout.BeginArea(new Rect(20, 278, 320, 164));
        GUILayout.Label($"Last: {lastAction}    History: {history.Count}");
        GUILayout.Label($"{p.spawnCount} on {p.domain}{(p.solidFill ? " solid" : "")}   {p.shading}");
        GUILayout.Label($"size {p.domainSize.x:0}x{p.domainSize.y:0}x{p.domainSize.z:0}   scale {p.scaleRange.x:0.00}-{p.scaleRange.y:0.00}");
        GUILayout.Label($"bias {p.scaleAxisBias.x:0.0},{p.scaleAxisBias.y:0.0},{p.scaleAxisBias.z:0.0}   spin +/-{p.spinRateRange.y:0}");
        GUILayout.Label($"wave amp {p.waveAmplitude:0.00} freq {p.waveFrequency:0.00} spread {p.wavePhaseSpread:0.00}");
        GUILayout.Label("Space randomize   M mutate   <- -> history");
        GUILayout.Label($"S save as '{saveName}'   1-9 recall");
        GUILayout.EndArea();
    }
}
```

- [ ] **Step 6: Compile**

```bash
unity command recompile && unity command recompile_status
```

Expected: `failed: false`.

- [ ] **Step 7: Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop/Core/HistoryRing.cs Assets/proceduralBackdrop/Core/HistoryRing.cs.meta Assets/proceduralBackdrop/Tests/Editor/HistoryRingTests.cs Assets/proceduralBackdrop/Tests/Editor/HistoryRingTests.cs.meta Assets/proceduralBackdrop/BackdropExplorerDriver.cs Assets/proceduralBackdrop/BackdropExplorerDriver.cs.meta
git commit -m "Add history ring and the backdrop parameter-space explorer"
```

---

### Task 13: `BackdropExplore.unity` + build measurement — **[YOU]**, then [AGENT] check

**Files:**
- Create: `Assets/proceduralBackdrop/BackdropExplore.unity`
- Create: `Assets/proceduralBackdrop/BackdropRanges.asset`
- Create: `Assets/proceduralBackdrop/BackdropPresetBook.asset`
- Delete: `Assets/proceduralBackdrop/BackdropSmoke.unity`, `Assets/proceduralBackdrop/Smoke.vfx`

- [ ] **Step 1: [AGENT] Create the two assets**

```bash
unity command eval_file --file - <<'CS'
using UnityEditor;
using UnityEngine;
public static class Tmp {
    public static void Run() {
        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BackdropRanges>(),
            "Assets/proceduralBackdrop/BackdropRanges.asset");
        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BackdropPresetBook>(),
            "Assets/proceduralBackdrop/BackdropPresetBook.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("[plan] ranges + preset book created");
    }
}
CS
unity command get_console_logs
```

- [ ] **Step 2: [YOU] Build the scene**

- `File > Save As` on `BackdropSmoke.unity` → `Assets/proceduralBackdrop/BackdropExplore.unity`.
- On the `Backdrop` GameObject, add `BackdropExplorerDriver`.
- Assign `BackdropRanges.asset` and `BackdropPresetBook.asset` to its fields.
- Remove `MidiFighterBackdropDriver` from this scene — it is a search rig, not a performance rig, and a stray static subscription in an exploration scene is noise.
- Add a directional light angled to match the `MainLight` custom function vector from Task 2 Step 4, so the toon banding reads the way it did in authoring.
- Save.

- [ ] **Step 3: [YOU] Search**

Enter Play mode. Press Space twenty times. Press M to refine anything promising. Walk back with ←. Save three or four looks with S, renaming `Save Name` between each.

If most randomisations are unusable, that is the ranges asset telling you it is too wide — tighten `BackdropRanges.asset` and go again. That tightening loop is the tool working as designed, not a bug.

- [ ] **Step 4: [YOU] Measure in a build**

Editor frame times on this project are meaningless — the same scene measures 5.2x slower in the editor than in a build. Make a development build of `BackdropExplore.unity` with vsync off and note CPU/GPU ms at your chosen preset. Expected: well under 1 ms at a few hundred instances with one camera.

If you want the split-screen number, add the `Split Screen Rig` prefab to the scene and measure again at 8 cells.

- [ ] **Step 5: [AGENT] Remove the smoke scaffolding**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git rm Assets/proceduralBackdrop/BackdropSmoke.unity Assets/proceduralBackdrop/BackdropSmoke.unity.meta Assets/proceduralBackdrop/Smoke.vfx Assets/proceduralBackdrop/Smoke.vfx.meta
unity command recompile && unity command recompile_status
unity command run_tests --mode EditMode
unity command test_status
```

Expected: `failed: false` and all EditMode tests green (5 + 6 + 11 + 7 = 29).

- [ ] **Step 6: [AGENT] Commit**

```bash
cd "F:/Unity Projects 2026/pincushioned_Reloaded"
git add Assets/proceduralBackdrop
git commit -m "Add backdrop exploration scene, ranges and preset book"
```

- [ ] **Step 7: [AGENT] Document it**

Add a `## Generative backdrop (Assets/proceduralBackdrop/)` section to the project `CLAUDE.md`, following the shape of the existing `## Pose instrument` section. It must record: the instrument/driver split; the exposed-property contract as the C#↔graph API; that only `RerollLayout` reinits; that shadow casting is off and bounds are manual, with the reasons; the shuffle-bag-not-random rationale; the mesh triangle guard; and the measured build numbers from Step 4. Commit separately.

---

## Self-Review

**Spec coverage.** Every section of the spec maps to a task: placement and domains → Tasks 3, 4; the all-VFX approach and its accepted costs → Tasks 2, 3, plus `ValidateGraph` in Task 4 mitigating the string-name cost; components table → Tasks 1, 4, 5, 7, 8, 9, 10, 11, 12; authored mesh list and the triangle guard → Task 5; distribution model → Task 3 Steps 4–6; C#↔graph interface → Task 3 Step 2 and Task 4; the four functions and shuffled cycling → Task 6; preset exploration with locks, mutate and history → Tasks 9, 10, 12; performance guards → Task 3 Steps 7–8, Task 1's `MaxSpawnCount`, Task 9's ranges; verification → the test tasks and Task 13 Steps 4–5; risk 1 (D3D11/VFX) → Task 0; risk 2 (Shader Graph VFX target) → Task 2 Steps 1 and 7.

**Known coverage gap, stated rather than hidden:** the spec's Phase 1 deliverable list does not include a `Backdrop` prefab, and this plan does not create one — the instrument lives on a scene object in `BackdropExplore.unity`. Moving the backdrop into the main scene is explicitly out of scope per the spec's Scope section, and prefabbing it is the first task of that future work.

**Type consistency.** `BackdropShadingMode` (not `BackdropShading`, which is the shader asset's name) is used consistently in Tasks 1, 4, 6, 10. `ApplyAndRelayout` is the name in Tasks 4, 6, 7, 12. `BackdropLibrary.Get(int)` is called in Task 4 before Task 5 defines it — this is deliberate and called out in Task 4 Step 2's expected-failure note. `MaxSpawnCount = 4000` matches the VFX Capacity in Task 3 Step 1. `RandomRange` is defined in Task 9 and consumed in Task 10's tests and implementation.
