# Cable Instrument Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cables that shoot from a source to a target, land, and stay — hanging with slack and never going quite still — rendered as camera-facing ribbons that read as round cable.

**Architecture:** A cable is a pure closed-form function of `(t, seed, time)`. No solver, no integration, no per-frame state. Flight and settle are the same evaluator with animated anchors. All live cables render as one dynamic mesh billboarded in the vertex shader; connector heads go through one instanced draw per mesh.

**Tech Stack:** Unity 6 (6000.3.7f1), URP, C#, HLSL. NUnit EditMode tests. No new packages.

**Spec:** `docs/superpowers/specs/2026-09-20-cable-instrument-design.md`

## Global Constraints

- **Unity conventions here use no namespace.** `Pincushioned.Backdrop.Core.asmdef` has `"rootNamespace": ""` and every type is global. Match that — do not introduce a namespace.
- **`Core/` must not reference any Unity object type** beyond `UnityEngine` structs (`Vector3`, `Color`, `Mathf`). No `MonoBehaviour`, no `Mesh`, no `AssetDatabase`. This is what keeps it testable.
- **Seed everything with `System.Random` or the integer hash — never `UnityEngine.Random`.** Project rule: `UnityEngine.Random` is a global shared stream that other systems perturb, so compositions stop being reproducible.
- **No bootstrapping.** No `new GameObject(...).AddComponent<T>()`, no `[RuntimeInitializeOnLoadMethod]`. Anything with artistic parameters belongs on a prefab or in the scene.
- **Auto Refresh is off in this project.** The `Write` tool puts a file on disk but Unity never imports it — no `.meta`, no compile. After creating or deleting any file, run `AssetDatabase.Refresh()` (see *Verification* below) or nothing you wrote exists as far as the editor is concerned.
- **Never use Unity batch mode.** The editor holds the project lock; batch mode aborts. Drive the already-open editor.
- **The editor must be focused to tick.** If a command times out while `unity status` reports ready, the Unity window has lost focus. Click into it.

## Verification

Run from the project root after every implementation step:

```bash
unity command recompile
unity command recompile_status      # -> { failed, errors[] }; CS errors surface here
unity command run_tests --mode EditMode
unity command test_status           # poll to completion
```

To import files created with `Write`:

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
```

Optional params need `--name value` flag form. `key=value` is rejected.

---

## File Structure

```
Assets/proceduralCables/
  Core/
    Pincushioned.Cables.Core.asmdef   no references, autoReferenced true
    CableParameters.cs                every value, one struct + Default
    CableCurve.cs                     hash, noise, the evaluator. THE tested surface.
    CableShot.cs                      one cable's state + landing point + head aim
    CableRibbonBuilder.cs             node positions -> vertex/index arrays
  Tests/Editor/
    Pincushioned.Cables.Tests.asmdef  refs Core + TestRunner, Editor-only
    CableCurveTests.cs
    CableShotTests.cs
    CableRibbonBuilderTests.cs
  CableInstrument.cs                  sole owner of state, sole issuer of draws
  CableLibrary.cs                     serialized connector meshes + editor scan
  KeyboardCableDriver.cs              X fires, R rerolls, C cycles
  CableRibbon.shader
  CableRibbon.mat
  Meshes/Connectors/                  XLR, quarter-inch, USB (may start empty)
  cableExplorer.unity
```

`Core/` is a separate asmdef because **asmdefs cannot reference `Assembly-CSharp`**, so an EditMode test assembly cannot see code loose under `Assets/`. The MonoBehaviours stay loose and still see `Core` — `Assembly-CSharp` auto-references every asmdef.

---

### Task 1: Core scaffold and CableParameters

**Files:**
- Create: `Assets/proceduralCables/Core/Pincushioned.Cables.Core.asmdef`
- Create: `Assets/proceduralCables/Core/CableParameters.cs`
- Create: `Assets/proceduralCables/Tests/Editor/Pincushioned.Cables.Tests.asmdef`
- Test: `Assets/proceduralCables/Tests/Editor/CableParametersTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `struct CableParameters` with public fields `seed` (uint), `cableSpeed`, `deceleration`, `slack`, `cableNoise`, `noiseScale`, `driftSpeed`, `pathNoise`, `thickness`, `headScale`, `targetScatterRadius`, `shiverDecay`, `shiverFreq` (all float), `nodesPerCable`, `cableCap` (int), `colors` (Color[]). Static property `CableParameters.Default`.

- [ ] **Step 1: Write the failing test**

Create `Assets/proceduralCables/Tests/Editor/CableParametersTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class CableParametersTests
{
    [Test]
    public void Default_HasSixColors()
    {
        var p = CableParameters.Default;
        Assert.IsNotNull(p.colors);
        Assert.AreEqual(6, p.colors.Length);
    }

    [Test]
    public void Default_HasUsableResolutionAndCap()
    {
        var p = CableParameters.Default;
        Assert.GreaterOrEqual(p.nodesPerCable, 2, "a cable needs at least two nodes to form one segment");
        Assert.Greater(p.cableCap, 0);
        Assert.Greater(p.cableSpeed, 0f, "flight duration is distance / cableSpeed and must not divide by zero");
    }

    [Test]
    public void Default_ColorsAreDistinct()
    {
        var p = CableParameters.Default;
        for (int i = 0; i < p.colors.Length; i++)
            for (int j = i + 1; j < p.colors.Length; j++)
                Assert.AreNotEqual(p.colors[i], p.colors[j], $"colors {i} and {j} are identical");
    }
}
```

- [ ] **Step 2: Create both asmdefs**

`Assets/proceduralCables/Core/Pincushioned.Cables.Core.asmdef`:

```json
{
    "name": "Pincushioned.Cables.Core",
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

`Assets/proceduralCables/Tests/Editor/Pincushioned.Cables.Tests.asmdef`:

```json
{
    "name": "Pincushioned.Cables.Tests",
    "rootNamespace": "",
    "references": [
        "Pincushioned.Cables.Core",
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

- [ ] **Step 3: Run to verify it fails**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
```

Expected: FAIL — `CS0103: The name 'CableParameters' does not exist`.

- [ ] **Step 4: Write CableParameters**

Create `Assets/proceduralCables/Core/CableParameters.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Every value a cable is drawn from, in one struct. This is the Inspector
/// surface, the unit a preset would store, and the input a test constructs -
/// keeping them the same type is what stops the three drifting apart.
/// </summary>
[System.Serializable]
public struct CableParameters
{
    [Tooltip("Stream seed. Every cable derives its own sub-seed from this.")]
    public uint seed;

    [Header("Flight")]
    [Tooltip("World units per second. Flight duration is distance / cableSpeed, so a distant target takes proportionally longer.")]
    public float cableSpeed;
    [Tooltip("Easing of the head along its path. 0 is linear; higher decelerates harder into the target. Not drag - nothing is integrated.")]
    public float deceleration;
    [Tooltip("Wander in the head's flight path. Separate from cableNoise: this curves the route, that animates the cable once it arrives.")]
    public float pathNoise;

    [Header("Shape")]
    [Tooltip("Droop toward world down at the cable's midpoint, in world units.")]
    public float slack;
    [Tooltip("Amplitude of the noise along the cable's length. Windowed to zero at both ends.")]
    public float cableNoise;
    [Tooltip("Feature size of that noise. Higher is busier.")]
    public float noiseScale;
    [Tooltip("How fast the idle sway breathes. Never reaches zero, so a settled nest keeps moving.")]
    public float driftSpeed;

    [Header("Impact")]
    [Tooltip("How fast the landing shiver dies away.")]
    public float shiverDecay;
    [Tooltip("Frequency of the landing shiver.")]
    public float shiverFreq;

    [Header("Look")]
    [Tooltip("Ribbon half-width in world units.")]
    public float thickness;
    [Tooltip("Scale applied to the connector mesh.")]
    public float headScale;
    [Tooltip("Palette. Each cable seeds its own pick.")]
    public Color[] colors;

    [Header("Targeting")]
    [Tooltip("Landing points scatter on a sphere of this radius around the target Transform.")]
    public float targetScatterRadius;

    [Header("Budget")]
    [Tooltip("Nodes per cable. Two gives a straight segment; higher resolves the sag and noise.")]
    public int nodesPerCable;
    [Tooltip("Live cables. At the cap the oldest retires by easing its thickness to zero.")]
    public int cableCap;

    public static CableParameters Default => new CableParameters
    {
        seed = 1u,
        cableSpeed = 18f,
        deceleration = 1.5f,
        pathNoise = 1.2f,
        slack = 1.5f,
        cableNoise = 0.35f,
        noiseScale = 2.5f,
        driftSpeed = 0.25f,
        shiverDecay = 3.5f,
        shiverFreq = 14f,
        thickness = 0.05f,
        headScale = 1f,
        targetScatterRadius = 1.5f,
        nodesPerCable = 24,
        cableCap = 64,
        colors = new[]
        {
            new Color(0.90f, 0.15f, 0.15f), // red
            new Color(0.95f, 0.55f, 0.10f), // orange
            new Color(0.95f, 0.85f, 0.15f), // yellow
            new Color(0.20f, 0.70f, 0.30f), // green
            new Color(0.15f, 0.40f, 0.85f), // blue
            new Color(0.55f, 0.25f, 0.75f), // violet
        },
    };
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
unity command run_tests --mode EditMode
unity command test_status
```

Expected: 3 tests PASS, 0 compile errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/proceduralCables
git commit -m "Add the cable parameter surface and its test assembly"
```

---

### Task 2: CableCurve — hash, noise, and the pinned-ends invariant

**Files:**
- Create: `Assets/proceduralCables/Core/CableCurve.cs`
- Test: `Assets/proceduralCables/Tests/Editor/CableCurveTests.cs`

**Interfaces:**
- Consumes: `CableParameters` from Task 1.
- Produces:
  - `static uint CableCurve.Hash(uint x)`
  - `static float CableCurve.Rand01(int id, uint seed, uint stream)`
  - `static float CableCurve.Window(float t)` — `sin(PI*t)`
  - `static Vector3 CableCurve.Position(float t, Vector3 anchorA, Vector3 anchorB, float slack, float noiseAmp, float noiseScale, float drift, uint seed)`

**Why this task is first and largest:** `Window` going wrong detaches every cable from its plug, and that is invisible in code review but obvious in motion. Getting it under test before anything renders is the point of the whole Core-first ordering.

- [ ] **Step 1: Write the failing tests**

Create `Assets/proceduralCables/Tests/Editor/CableCurveTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class CableCurveTests
{
    private static readonly Vector3 A = new Vector3(-4f, 2f, 1f);
    private static readonly Vector3 B = new Vector3(6f, 3f, -2f);

    // Mathf.Sin(Mathf.PI) is 8.7e-8, not 0, so the far anchor lands within
    // float epsilon rather than exactly. Anything larger than this is a bug.
    private const float Eps = 1e-4f;

    [Test]
    public void Window_IsZeroAtBothEnds()
    {
        Assert.AreEqual(0f, CableCurve.Window(0f), Eps);
        Assert.AreEqual(0f, CableCurve.Window(1f), Eps);
        Assert.AreEqual(1f, CableCurve.Window(0.5f), Eps);
    }

    [Test]
    public void Position_EndsStayPinned_ForAnySeedTimeAndNoise()
    {
        // The load-bearing assertion. If noise leaks past the window, every
        // cable detaches from its source and its plug.
        for (uint seed = 0; seed < 25; seed++)
        {
            for (float time = 0f; time < 6f; time += 1.3f)
            {
                var a = CableCurve.Position(0f, A, B, 5f, 10f, 3f, time, seed);
                var b = CableCurve.Position(1f, A, B, 5f, 10f, 3f, time, seed);
                Assert.Less(Vector3.Distance(a, A), Eps, $"near end drifted: seed {seed} t {time}");
                Assert.Less(Vector3.Distance(b, B), Eps, $"far end drifted: seed {seed} t {time}");
            }
        }
    }

    [Test]
    public void Position_IsDeterministicForTheSameInputs()
    {
        var x = CableCurve.Position(0.37f, A, B, 2f, 1f, 3f, 4.2f, 9u);
        var y = CableCurve.Position(0.37f, A, B, 2f, 1f, 3f, 4.2f, 9u);
        Assert.AreEqual(x, y);
    }

    [Test]
    public void Position_DiffersAcrossSeeds()
    {
        int same = 0;
        for (int i = 0; i < 200; i++)
        {
            float t = (i + 1) / 202f;
            var x = CableCurve.Position(t, A, B, 2f, 1f, 3f, 0f, 1u);
            var y = CableCurve.Position(t, A, B, 2f, 1f, 3f, 0f, 2u);
            if (Vector3.Distance(x, y) < 1e-5f) same++;
        }
        Assert.Less(same, 20, "two seeds produced near-identical cables");
    }

    [Test]
    public void Position_DriftMovesTheCableButNotItsEnds()
    {
        var mid0 = CableCurve.Position(0.5f, A, B, 0f, 1f, 3f, 0f, 5u);
        var mid1 = CableCurve.Position(0.5f, A, B, 0f, 1f, 3f, 9f, 5u);
        Assert.Greater(Vector3.Distance(mid0, mid1), 1e-3f, "drift did not move the middle");

        var end0 = CableCurve.Position(1f, A, B, 0f, 1f, 3f, 0f, 5u);
        var end1 = CableCurve.Position(1f, A, B, 0f, 1f, 3f, 9f, 5u);
        Assert.Less(Vector3.Distance(end0, end1), Eps, "drift moved the far end");
    }

    [Test]
    public void Position_SagsTowardWorldDownAtTheMidpoint()
    {
        // No noise, so sag is the only term acting on the midpoint.
        var chord = Vector3.Lerp(A, B, 0.5f);
        var mid = CableCurve.Position(0.5f, A, B, 3f, 0f, 3f, 0f, 1u);
        Assert.AreEqual(chord.y - 3f, mid.y, Eps, "midpoint should hang exactly slack below the chord");
        Assert.AreEqual(chord.x, mid.x, Eps, "sag must not move the cable sideways");
        Assert.AreEqual(chord.z, mid.z, Eps, "sag must not move the cable in depth");
    }

    [Test]
    public void Position_SagIsSymmetricAndPeaksInTheMiddle()
    {
        float DropAt(float t)
        {
            var chord = Vector3.Lerp(A, B, t);
            return chord.y - CableCurve.Position(t, A, B, 3f, 0f, 3f, 0f, 1u).y;
        }
        Assert.AreEqual(DropAt(0.25f), DropAt(0.75f), Eps, "sag is not symmetric");
        Assert.Greater(DropAt(0.5f), DropAt(0.25f), "sag does not peak in the middle");
        Assert.Greater(DropAt(0.25f), DropAt(0.05f), "sag is not monotonic on the way up");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
```

Expected: FAIL — `CS0103: The name 'CableCurve' does not exist`.

- [ ] **Step 3: Write CableCurve**

Create `Assets/proceduralCables/Core/CableCurve.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Where every point on a cable sits. A pure closed-form function of
/// (t, seed, time) with no Unity object references and nothing integrated frame
/// to frame, so a cable can be asserted in tests rather than judged by eye, and
/// a rebuild reproduces a composition exactly.
///
/// There is no solver. The project wants the appearance of gravity, not
/// accurate rope physics - see the design doc for why a Verlet chain was
/// considered and dropped.
/// </summary>
public static class CableCurve
{
    /// <summary>
    /// Integer hash, matching BackdropLattice.Hash. Deterministic across
    /// machines and Unity versions, unlike UnityEngine.Random.
    /// </summary>
    public static uint Hash(uint x)
    {
        unchecked
        {
            x ^= x >> 16; x *= 0x7feb352du;
            x ^= x >> 15; x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }
    }

    public static float Rand01(int id, uint seed, uint stream)
    {
        unchecked
        {
            uint h = Hash((uint)id * 0x9e3779b9u + seed * 0x85ebca6bu + stream * 0xc2b2ae35u);
            return h / 4294967295f;
        }
    }

    /// <summary>
    /// Forces noise to zero at t=0 and t=1, welding the cable to its source and
    /// its plug while leaving the middle free to wander.
    ///
    /// This is the single most important function in the system. Without it the
    /// ends jitter, the cable detaches from the connector, and the illusion
    /// collapses immediately.
    /// </summary>
    public static float Window(float t) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));

    /// <summary>One coherent noise channel in [-1,1], offset per stream so the three axes differ.</summary>
    private static float Noise1(float x, uint seed, uint stream)
    {
        float off = Rand01(0, seed, stream) * 997f;
        return Mathf.PerlinNoise(x + off, off) * 2f - 1f;
    }

    /// <summary>
    /// A point on the cable. t runs 0 at anchorA to 1 at anchorB.
    ///
    /// Three terms: the chord, a parabolic droop toward world down, and noise
    /// windowed to zero at both ends. 4t(1-t) is zero at the anchors and 1 at
    /// the midpoint - a true catenary differs by a fraction of a percent at
    /// realistic droop and costs a cosh.
    /// </summary>
    public static Vector3 Position(
        float t, Vector3 anchorA, Vector3 anchorB,
        float slack, float noiseAmp, float noiseScale,
        float drift, uint seed)
    {
        t = Mathf.Clamp01(t);

        Vector3 chord = Vector3.Lerp(anchorA, anchorB, t);

        // Sag is toward world down, not perpendicular to the chord: a cable
        // droops toward the floor however its ends are oriented, and a
        // perpendicular sag would swing a near-vertical cable sideways.
        float sag = slack * 4f * t * (1f - t);

        float u = t * noiseScale + drift;
        Vector3 n = new Vector3(
            Noise1(u, seed, 1u),
            Noise1(u, seed, 2u),
            Noise1(u, seed, 3u));

        return chord + Vector3.down * sag + n * (noiseAmp * Window(t));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
unity command run_tests --mode EditMode
unity command test_status
```

Expected: all `CableCurveTests` PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/proceduralCables
git commit -m "Add the cable evaluator, with its ends welded to the anchors"
```

---

### Task 3: Flight easing and the shiver

**Files:**
- Modify: `Assets/proceduralCables/Core/CableCurve.cs`
- Modify: `Assets/proceduralCables/Tests/Editor/CableCurveTests.cs`

**Interfaces:**
- Consumes: `CableCurve` from Task 2.
- Produces:
  - `static float CableCurve.Ease(float x, float deceleration)`
  - `static float CableCurve.Shiver(float age, float decay, float freq)`

- [ ] **Step 1: Write the failing tests**

Append to `CableCurveTests.cs`, inside the class:

```csharp
    [Test]
    public void Ease_IsLinearAtZeroDeceleration()
    {
        for (float x = 0f; x <= 1f; x += 0.1f)
            Assert.AreEqual(x, CableCurve.Ease(x, 0f), Eps, $"x {x}");
    }

    [Test]
    public void Ease_SpansZeroToOneAndIsMonotonic()
    {
        foreach (float d in new[] { 0f, 1.5f, 6f })
        {
            Assert.AreEqual(0f, CableCurve.Ease(0f, d), Eps, $"deceleration {d}");
            Assert.AreEqual(1f, CableCurve.Ease(1f, d), Eps, $"deceleration {d}");

            float prev = -1f;
            for (float x = 0f; x <= 1f; x += 0.05f)
            {
                float v = CableCurve.Ease(x, d);
                Assert.GreaterOrEqual(v, prev, $"not monotonic at x {x}, deceleration {d}");
                prev = v;
            }
        }
    }

    [Test]
    public void Ease_DeceleratesMeaningTheHeadIsAlreadyPastHalfwayAtHalfTime()
    {
        Assert.Greater(CableCurve.Ease(0.5f, 3f), 0.5f,
            "a decelerating flight covers more than half the distance in the first half of the time");
    }

    [Test]
    public void Shiver_StartsAtZeroAndDecaysAway()
    {
        Assert.AreEqual(0f, CableCurve.Shiver(0f, 3.5f, 14f), Eps, "no shiver before any time has passed");
        Assert.Greater(Mathf.Abs(CableCurve.Shiver(0.1f, 3.5f, 14f)), 1e-3f, "shiver never started");
        Assert.Less(Mathf.Abs(CableCurve.Shiver(8f, 3.5f, 14f)), 1e-3f, "shiver never died away");
    }

    [Test]
    public void Shiver_IsFiniteForPathologicalInputs()
    {
        foreach (float decay in new[] { 0f, 1e6f })
            foreach (float freq in new[] { 0f, 1e6f })
            {
                float v = CableCurve.Shiver(2f, decay, freq);
                Assert.IsFalse(float.IsNaN(v), $"NaN at decay {decay} freq {freq}");
                Assert.IsFalse(float.IsInfinity(v), $"Inf at decay {decay} freq {freq}");
            }
    }
```

- [ ] **Step 2: Run to verify they fail**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
```

Expected: FAIL — `CS0117: 'CableCurve' does not contain a definition for 'Ease'`.

- [ ] **Step 3: Implement Ease and Shiver**

Add to `CableCurve.cs` inside the class:

```csharp
    /// <summary>
    /// Flight easing. x is normalised age along the flight.
    ///
    /// deceleration 0 is linear; higher values make the head cover ground early
    /// and ease into the target. This is named for what it feels like, not for
    /// a mechanism - nothing is integrated, so there is no drag here.
    /// </summary>
    public static float Ease(float x, float deceleration)
    {
        x = Mathf.Clamp01(x);
        float p = 1f + Mathf.Max(0f, deceleration);
        return 1f - Mathf.Pow(1f - x, p);
    }

    /// <summary>
    /// The landing shiver: a damped oscillation that scales noise amplitude for
    /// a moment after impact, then vanishes. Distinct from the idle sway, which
    /// never decays - a settled cable must keep breathing.
    /// </summary>
    public static float Shiver(float age, float decay, float freq)
    {
        if (age <= 0f) return 0f;
        return Mathf.Exp(-Mathf.Max(0f, decay) * age) * Mathf.Sin(freq * age);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
unity command run_tests --mode EditMode
unity command test_status
```

Expected: all `CableCurveTests` PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/proceduralCables
git commit -m "Ease the cable head along its flight, and shiver it on impact"
```

---

### Task 4: CableShot — per-cable state, landing point, head aim

**Files:**
- Create: `Assets/proceduralCables/Core/CableShot.cs`
- Test: `Assets/proceduralCables/Tests/Editor/CableShotTests.cs`

**Interfaces:**
- Consumes: `CableCurve`, `CableParameters`.
- Produces:
  - `struct CableShot` with fields `seed` (uint), `source`, `landing` (Vector3), `age`, `flightDuration` (float), `colorIndex`, `meshIndex` (int).
  - `static CableShot CableShot.Create(int id, Vector3 source, Vector3 target, in CableParameters p, int meshCount)`
  - `float CableShot.Flight01` — 1 while flying, 0 once landed.
  - `Vector3 CableShot.HeadAnchor(in CableParameters p)`
  - `static Vector3 CableShot.HeadDirection(Vector3 travel, Vector3 endTangent, float flight01, Vector3 fallback)`

- [ ] **Step 1: Write the failing tests**

Create `Assets/proceduralCables/Tests/Editor/CableShotTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class CableShotTests
{
    private static readonly Vector3 Src = new Vector3(0f, 1f, 0f);
    private static readonly Vector3 Tgt = new Vector3(20f, 4f, 5f);

    [Test]
    public void Create_ScattersLandingWithinTheRadius()
    {
        var p = CableParameters.Default;
        p.targetScatterRadius = 2f;

        for (int id = 0; id < 200; id++)
        {
            var s = CableShot.Create(id, Src, Tgt, p, 3);
            Assert.LessOrEqual(Vector3.Distance(s.landing, Tgt), 2f + 1e-4f, $"id {id} landed outside the radius");
        }
    }

    [Test]
    public void Create_ScattersRatherThanStackingOnTheTarget()
    {
        var p = CableParameters.Default;
        p.targetScatterRadius = 2f;

        var a = CableShot.Create(1, Src, Tgt, p, 3);
        var b = CableShot.Create(2, Src, Tgt, p, 3);
        Assert.Greater(Vector3.Distance(a.landing, b.landing), 1e-3f, "two shots landed on the same point");
    }

    [Test]
    public void Create_IsDeterministicForTheSameId()
    {
        var p = CableParameters.Default;
        var a = CableShot.Create(7, Src, Tgt, p, 3);
        var b = CableShot.Create(7, Src, Tgt, p, 3);
        Assert.AreEqual(a.landing, b.landing);
        Assert.AreEqual(a.colorIndex, b.colorIndex);
        Assert.AreEqual(a.meshIndex, b.meshIndex);
    }

    [Test]
    public void Create_PicksIndicesInRange()
    {
        var p = CableParameters.Default;
        for (int id = 0; id < 300; id++)
        {
            var s = CableShot.Create(id, Src, Tgt, p, 3);
            Assert.GreaterOrEqual(s.colorIndex, 0);
            Assert.Less(s.colorIndex, p.colors.Length);
            Assert.GreaterOrEqual(s.meshIndex, 0);
            Assert.Less(s.meshIndex, 3);
        }
    }

    [Test]
    public void Create_SurvivesAnEmptyMeshLibrary()
    {
        // CableLibrary degrades to an empty set when no connectors are imported.
        // The ribbon must still render headless rather than throwing.
        var p = CableParameters.Default;
        var s = CableShot.Create(0, Src, Tgt, p, 0);
        Assert.AreEqual(-1, s.meshIndex, "an empty library should yield a sentinel, not an index");
    }

    [Test]
    public void Create_SetsFlightDurationFromDistanceOverSpeed()
    {
        var p = CableParameters.Default;
        p.cableSpeed = 10f;
        p.targetScatterRadius = 0f;

        var s = CableShot.Create(0, Vector3.zero, new Vector3(50f, 0f, 0f), p, 1);
        Assert.AreEqual(5f, s.flightDuration, 1e-3f, "a distant target should take proportionally longer");
    }

    [Test]
    public void Flight01_GoesFromOneToZeroAndStaysThere()
    {
        var p = CableParameters.Default;
        var s = CableShot.Create(0, Src, Tgt, p, 1);

        s.age = 0f;
        Assert.AreEqual(1f, s.Flight01, 1e-4f);

        s.age = s.flightDuration * 2f;
        Assert.AreEqual(0f, s.Flight01, 1e-4f);

        s.age = s.flightDuration * 100f;
        Assert.AreEqual(0f, s.Flight01, 1e-4f, "Flight01 must clamp, not go negative");
    }

    [Test]
    public void HeadAnchor_ReachesTheLandingPointAndStops()
    {
        var p = CableParameters.Default;
        var s = CableShot.Create(0, Src, Tgt, p, 1);

        s.age = 0f;
        Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.source), 1e-4f, "the head should start at the source");

        s.age = s.flightDuration;
        Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.landing), 1e-4f, "the head should arrive at the landing point");

        s.age = s.flightDuration * 10f;
        Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.landing), 1e-4f, "a landed head must not drift past its plug");
    }

    [Test]
    public void HeadDirection_IsFiniteEvenWithNoTravelAndNoTangent()
    {
        // The NaN this exists to prevent: at the arrival instant travel is zero,
        // and normalising a zero vector yields NaN that propagates into the
        // head's rotation and blanks the mesh.
        var d = CableShot.HeadDirection(Vector3.zero, Vector3.zero, 0f, Vector3.forward);
        Assert.IsFalse(float.IsNaN(d.x) || float.IsNaN(d.y) || float.IsNaN(d.z), "NaN direction");
        Assert.AreEqual(1f, d.magnitude, 1e-4f, "direction must stay unit length");
    }

    [Test]
    public void HeadDirection_FollowsTravelWhileFlying()
    {
        var d = CableShot.HeadDirection(new Vector3(3f, 0f, 0f), Vector3.up, 1f, Vector3.forward);
        Assert.Less(Vector3.Distance(d, Vector3.right), 1e-3f, "a flying plug should point where it is going");
    }

    [Test]
    public void HeadDirection_SettlesOntoTheCableTangent()
    {
        var d = CableShot.HeadDirection(Vector3.zero, new Vector3(0f, 5f, 0f), 0f, Vector3.forward);
        Assert.Less(Vector3.Distance(d, Vector3.up), 1e-3f, "a landed plug should seat along the cable, not freeze mid-flight");
    }

    [Test]
    public void HeadDirection_IsAlwaysUnitLengthAcrossTheBlend()
    {
        for (float f = 0f; f <= 1f; f += 0.05f)
        {
            var d = CableShot.HeadDirection(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), f, Vector3.forward);
            Assert.AreEqual(1f, d.magnitude, 1e-3f, $"flight01 {f}");
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
```

Expected: FAIL — `CS0103: The name 'CableShot' does not exist`.

- [ ] **Step 3: Write CableShot**

Create `Assets/proceduralCables/Core/CableShot.cs`:

```csharp
using UnityEngine;

/// <summary>
/// One cable. Everything here is either fixed at creation or derived from age,
/// so a shot carries no simulation state and can be recreated exactly from its
/// id and the parameters it was fired with.
/// </summary>
[System.Serializable]
public struct CableShot
{
    public uint seed;
    public Vector3 source;
    public Vector3 landing;
    public float age;
    public float flightDuration;
    public int colorIndex;
    /// <summary>Index into CableLibrary, or -1 when no connectors are imported.</summary>
    public int meshIndex;

    private const float MinFlight = 1e-3f;
    private const float Eps = 1e-10f;

    /// <summary>
    /// Fire a cable. The landing point is sampled once, seeded, on a sphere
    /// around the target, so a nest converges on one place without every cable
    /// ending identically.
    /// </summary>
    public static CableShot Create(int id, Vector3 source, Vector3 target, in CableParameters p, int meshCount)
    {
        uint s = p.seed;

        // Even distribution in the ball: the cube root is what stops points
        // bunching at the centre, the same correction a spherical cap needs.
        float u1 = CableCurve.Rand01(id, s, 11u);
        float u2 = CableCurve.Rand01(id, s, 12u);
        float u3 = CableCurve.Rand01(id, s, 13u);

        float z = u1 * 2f - 1f;
        float theta = u2 * Mathf.PI * 2f;
        float r = Mathf.Pow(Mathf.Clamp01(u3), 1f / 3f) * Mathf.Max(0f, p.targetScatterRadius);
        float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));

        Vector3 offset = new Vector3(ring * Mathf.Cos(theta), ring * Mathf.Sin(theta), z) * r;
        Vector3 landing = target + offset;

        float speed = Mathf.Max(1e-3f, p.cableSpeed);
        int colors = (p.colors != null && p.colors.Length > 0) ? p.colors.Length : 1;

        return new CableShot
        {
            seed = CableCurve.Hash((uint)id * 0x9e3779b9u + s),
            source = source,
            landing = landing,
            age = 0f,
            flightDuration = Mathf.Max(MinFlight, Vector3.Distance(source, landing) / speed),
            colorIndex = Mathf.FloorToInt(CableCurve.Rand01(id, s, 21u) * colors) % colors,
            meshIndex = meshCount > 0
                ? Mathf.FloorToInt(CableCurve.Rand01(id, s, 22u) * meshCount) % meshCount
                : -1,
        };
    }

    /// <summary>1 the instant it is fired, 0 once it has landed. Never negative.</summary>
    public float Flight01 => 1f - Mathf.Clamp01(age / Mathf.Max(MinFlight, flightDuration));

    /// <summary>
    /// Where the far end of the cable is right now. Flight and settle are the
    /// same expression - the cable has "arrived" simply because this stops
    /// moving, which is why there is no handoff and no pop at impact.
    /// </summary>
    public Vector3 HeadAnchor(in CableParameters p)
    {
        float x = Mathf.Clamp01(age / Mathf.Max(MinFlight, flightDuration));
        return Vector3.Lerp(source, landing, CableCurve.Ease(x, p.deceleration));
    }

    /// <summary>
    /// Which way the plug points. While flying it aims along travel; as speed
    /// decays it blends onto the cable's end tangent, which is what makes it
    /// read as seated in a socket rather than frozen mid-flight.
    ///
    /// Every normalise here is guarded. At the arrival instant travel is exactly
    /// zero, and an unguarded normalize yields NaN that propagates into the
    /// head's rotation and blanks the mesh.
    /// </summary>
    public static Vector3 HeadDirection(Vector3 travel, Vector3 endTangent, float flight01, Vector3 fallback)
    {
        Vector3 safeFallback = fallback.sqrMagnitude > Eps ? fallback.normalized : Vector3.forward;
        Vector3 dir = travel.sqrMagnitude > Eps ? travel.normalized : safeFallback;
        Vector3 tan = endTangent.sqrMagnitude > Eps ? endTangent.normalized : dir;

        Vector3 blended = Vector3.Slerp(tan, dir, Mathf.Clamp01(flight01));
        return blended.sqrMagnitude > Eps ? blended.normalized : safeFallback;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
unity command run_tests --mode EditMode
unity command test_status
```

Expected: all `CableShotTests` PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/proceduralCables
git commit -m "Give a cable its landing point, its flight, and a plug that aims"
```

---

### Task 5: CableRibbonBuilder — node positions to vertex arrays

**Files:**
- Create: `Assets/proceduralCables/Core/CableRibbonBuilder.cs`
- Test: `Assets/proceduralCables/Tests/Editor/CableRibbonBuilderTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (takes plain `Vector3[]`).
- Produces:
  - `static int CableRibbonBuilder.VertexCount(int nodes)` — `nodes * 2`
  - `static int CableRibbonBuilder.IndexCount(int nodes)` — `(nodes - 1) * 6`
  - `static void CableRibbonBuilder.Append(Vector3[] nodes, int nodeCount, Color color, float uvTiling, List<Vector3> positions, List<Vector3> tangents, List<Vector2> uvs, List<Color> colors, List<int> indices)`

**The vertex layout, which the shader in Task 6 depends on:**

| Channel | Carries |
|---|---|
| `position` | the node's world position, **un-offset** — the shader does the widening |
| `normal` | the along-cable tangent (repurposed; there is no surface normal to store) |
| `uv.x` | cumulative world length along the cable, times `uvTiling` |
| `uv.y` | 0 or 1 — **doubles as the side sign** via `v * 2 - 1`, and as the cylinder coordinate |
| `color` | the cable's colour, flat across both its vertices |

`uv.y` serving as side sign *and* cylinder coordinate is deliberate: they are the same number, and storing it twice invites them drifting apart.

- [ ] **Step 1: Write the failing tests**

Create `Assets/proceduralCables/Tests/Editor/CableRibbonBuilderTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class CableRibbonBuilderTests
{
    private static Vector3[] Line(int n, float spacing = 1f)
    {
        var a = new Vector3[n];
        for (int i = 0; i < n; i++) a[i] = new Vector3(i * spacing, 0f, 0f);
        return a;
    }

    private class Sink
    {
        public readonly List<Vector3> pos = new List<Vector3>();
        public readonly List<Vector3> tan = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color> col = new List<Color>();
        public readonly List<int> idx = new List<int>();

        public void Append(Vector3[] nodes, int count, Color c, float tiling) =>
            CableRibbonBuilder.Append(nodes, count, c, tiling, pos, tan, uv, col, idx);
    }

    [Test]
    public void Counts_MatchTheDeclaredFormulas()
    {
        Assert.AreEqual(48, CableRibbonBuilder.VertexCount(24));
        Assert.AreEqual(138, CableRibbonBuilder.IndexCount(24));
        Assert.AreEqual(0, CableRibbonBuilder.IndexCount(1), "a single node forms no segment");
    }

    [Test]
    public void Append_EmitsTwoVerticesPerNodeAndSixIndicesPerSegment()
    {
        var s = new Sink();
        s.Append(Line(10), 10, Color.red, 1f);

        Assert.AreEqual(20, s.pos.Count);
        Assert.AreEqual(20, s.tan.Count);
        Assert.AreEqual(20, s.uv.Count);
        Assert.AreEqual(20, s.col.Count);
        Assert.AreEqual(54, s.idx.Count);
    }

    [Test]
    public void Append_LeavesPositionsUnoffset()
    {
        // Widening happens in the vertex shader so one mesh serves all 32
        // split-screen cameras. If the CPU offsets here, the ribbon is correct
        // for one camera and a sliver in every other cell.
        var s = new Sink();
        var nodes = Line(4);
        s.Append(nodes, 4, Color.white, 1f);

        for (int i = 0; i < 4; i++)
        {
            Assert.AreEqual(nodes[i], s.pos[i * 2]);
            Assert.AreEqual(nodes[i], s.pos[i * 2 + 1]);
        }
    }

    [Test]
    public void Append_PairsEachNodeAsSideZeroAndSideOne()
    {
        var s = new Sink();
        s.Append(Line(6), 6, Color.white, 1f);

        for (int i = 0; i < 6; i++)
        {
            Assert.AreEqual(0f, s.uv[i * 2].y, 1e-5f, $"node {i} low side");
            Assert.AreEqual(1f, s.uv[i * 2 + 1].y, 1e-5f, $"node {i} high side");
        }
    }

    [Test]
    public void Append_ScalesUByWorldLengthNotByT()
    {
        // Otherwise a short patch cable and a long run get identical repeat
        // counts, and the texel-density mismatch is the most obvious tell that
        // the cable is a stretched ribbon.
        var shortSink = new Sink();
        shortSink.Append(Line(5, 1f), 5, Color.white, 1f);

        var longSink = new Sink();
        longSink.Append(Line(5, 4f), 5, Color.white, 1f);

        float shortU = shortSink.uv[shortSink.uv.Count - 1].x;
        float longU = longSink.uv[longSink.uv.Count - 1].x;

        Assert.AreEqual(4f, shortU, 1e-4f);
        Assert.AreEqual(16f, longU, 1e-4f);
        Assert.AreEqual(4f, longU / shortU, 1e-3f, "u must be proportional to world length");
    }

    [Test]
    public void Append_UIsMonotonicAndStartsAtZero()
    {
        var s = new Sink();
        s.Append(Line(8, 1.7f), 8, Color.white, 2f);

        Assert.AreEqual(0f, s.uv[0].x, 1e-5f);
        for (int i = 1; i < 8; i++)
            Assert.Greater(s.uv[i * 2].x, s.uv[(i - 1) * 2].x, $"u went backwards at node {i}");
    }

    [Test]
    public void Append_GivesEveryVertexTheCableColor()
    {
        var s = new Sink();
        var c = new Color(0.2f, 0.4f, 0.6f, 1f);
        s.Append(Line(5), 5, c, 1f);
        foreach (var v in s.col) Assert.AreEqual(c, v);
    }

    [Test]
    public void Append_TangentsPointAlongTheCableAndAreUnitLength()
    {
        var s = new Sink();
        s.Append(Line(6), 6, Color.white, 1f);
        foreach (var t in s.tan)
        {
            Assert.AreEqual(1f, t.magnitude, 1e-3f);
            Assert.Less(Vector3.Distance(t, Vector3.right), 1e-3f, "a straight cable along +x should tangent along +x");
        }
    }

    [Test]
    public void Append_SurvivesDuplicateNodesWithoutNaNTangents()
    {
        // Two coincident nodes give a zero-length difference. Unguarded, that
        // normalises to NaN and silently destroys the whole mesh - Unity drops
        // a mesh containing any NaN vertex rather than erroring.
        var nodes = new[] { Vector3.zero, Vector3.zero, Vector3.zero, new Vector3(1f, 0f, 0f) };
        var s = new Sink();
        s.Append(nodes, 4, Color.white, 1f);

        foreach (var t in s.tan)
            Assert.IsFalse(float.IsNaN(t.x) || float.IsNaN(t.y) || float.IsNaN(t.z), "NaN tangent");
        foreach (var u in s.uv)
            Assert.IsFalse(float.IsNaN(u.x), "NaN uv");
    }

    [Test]
    public void Append_IndicesStayInRangeAcrossMultipleCables()
    {
        // The second cable's indices must be offset by the first cable's vertex
        // count, or every cable after the first draws on top of cable zero.
        var s = new Sink();
        s.Append(Line(5), 5, Color.red, 1f);
        s.Append(Line(5), 5, Color.blue, 1f);

        Assert.AreEqual(20, s.pos.Count);
        foreach (int i in s.idx)
        {
            Assert.GreaterOrEqual(i, 0);
            Assert.Less(i, s.pos.Count);
        }

        int maxFirst = 0;
        for (int i = 0; i < 24; i++) maxFirst = Mathf.Max(maxFirst, s.idx[i]);
        Assert.Less(maxFirst, 10, "the first cable's triangles reach into the second cable's vertices");

        int minSecond = int.MaxValue;
        for (int i = 24; i < s.idx.Count; i++) minSecond = Mathf.Min(minSecond, s.idx[i]);
        Assert.GreaterOrEqual(minSecond, 10, "the second cable's triangles were not offset");
    }

    [Test]
    public void Append_EachSegmentsTrianglesShareItsDiagonal()
    {
        var s = new Sink();
        s.Append(Line(3), 3, Color.white, 1f);

        // Segment 0 spans vertices 0,1 (node 0) and 2,3 (node 1).
        var t1 = new[] { s.idx[0], s.idx[1], s.idx[2] };
        var t2 = new[] { s.idx[3], s.idx[4], s.idx[5] };

        int shared = 0;
        foreach (int a in t1) foreach (int b in t2) if (a == b) shared++;
        Assert.AreEqual(2, shared, "a quad's two triangles must share exactly one edge");
    }

    [Test]
    public void Append_IgnoresDegenerateNodeCounts()
    {
        var s = new Sink();
        s.Append(Line(4), 1, Color.white, 1f);
        s.Append(Line(4), 0, Color.white, 1f);
        Assert.AreEqual(0, s.pos.Count, "fewer than two nodes cannot form a ribbon");
        Assert.AreEqual(0, s.idx.Count);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
```

Expected: FAIL — `CS0103: The name 'CableRibbonBuilder' does not exist`.

- [ ] **Step 3: Write CableRibbonBuilder**

Create `Assets/proceduralCables/Core/CableRibbonBuilder.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a cable's node positions into vertex and index data, appending into
/// shared lists so every live cable lands in one mesh and one draw.
///
/// Positions are emitted un-offset and the ribbon is widened in the vertex
/// shader. That is not an optimisation - this project renders up to 32
/// split-screen cameras, and a CPU-billboarded ribbon is correct for exactly
/// one of them and collapses to an invisible sliver in the rest.
/// </summary>
public static class CableRibbonBuilder
{
    private const float Eps = 1e-10f;

    public static int VertexCount(int nodes) => nodes < 2 ? 0 : nodes * 2;
    public static int IndexCount(int nodes) => nodes < 2 ? 0 : (nodes - 1) * 6;

    /// <summary>
    /// Append one cable. uvTiling is repeats per world unit, so u is
    /// proportional to the cable's actual length rather than to t - otherwise a
    /// short cable and a long one get the same repeat count and the texel
    /// density mismatch gives the ribbon away.
    /// </summary>
    public static void Append(
        Vector3[] nodes, int nodeCount, Color color, float uvTiling,
        List<Vector3> positions, List<Vector3> tangents, List<Vector2> uvs,
        List<Color> colors, List<int> indices)
    {
        if (nodes == null || nodeCount < 2) return;

        int baseVertex = positions.Count;
        float travelled = 0f;
        Vector3 lastTangent = Vector3.forward;

        for (int i = 0; i < nodeCount; i++)
        {
            Vector3 node = nodes[i];

            // Central difference along the interior, one-sided at the ends.
            Vector3 delta = i == 0 ? nodes[1] - nodes[0]
                          : i == nodeCount - 1 ? nodes[i] - nodes[i - 1]
                          : nodes[i + 1] - nodes[i - 1];

            // Coincident nodes give a zero-length delta. Unguarded this
            // normalises to NaN, and Unity silently drops any mesh containing
            // one rather than reporting it.
            Vector3 tangent = delta.sqrMagnitude > Eps ? delta.normalized : lastTangent;
            lastTangent = tangent;

            if (i > 0) travelled += Vector3.Distance(nodes[i - 1], node);
            float u = travelled * uvTiling;

            positions.Add(node); positions.Add(node);
            tangents.Add(tangent); tangents.Add(tangent);
            uvs.Add(new Vector2(u, 0f)); uvs.Add(new Vector2(u, 1f));
            colors.Add(color); colors.Add(color);
        }

        for (int i = 0; i < nodeCount - 1; i++)
        {
            int a = baseVertex + i * 2;
            int b = a + 2;

            indices.Add(a); indices.Add(a + 1); indices.Add(b);
            indices.Add(a + 1); indices.Add(b + 1); indices.Add(b);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
unity command run_tests --mode EditMode
unity command test_status
```

Expected: all `CableRibbonBuilderTests` PASS. **All of Core is now proven before a single shader, mesh, or scene exists.**

- [ ] **Step 5: Commit**

```bash
git add Assets/proceduralCables
git commit -m "Build cable ribbons into shared buffers, widened later by the shader"
```

---

### Task 6: CableRibbon.shader — billboard, analytic cylinder, procedural braid

**Files:**
- Create: `Assets/proceduralCables/CableRibbon.shader`
- Create: `Assets/proceduralCables/CableRibbon.mat` (via the eval step below)

**Interfaces:**
- Consumes: the Task 5 vertex layout — `POSITION` (un-offset node), `NORMAL` (along-cable tangent), `TEXCOORD0` (`u` = world length × tiling, `v` = 0/1), `COLOR`.
- Produces: a material named `CableRibbon.mat` with float properties `_HalfWidth`, `_BraidTiling`, `_BraidDepth`, `_Smoothness`, `_Metallic`.

**There is no normal map texture.** The cylinder normal is derived from `v`. See the spec for why that beats a painted strip.

- [ ] **Step 1: Write the shader**

Create `Assets/proceduralCables/CableRibbon.shader`:

```hlsl
Shader "Pincushioned/CableRibbon"
{
    Properties
    {
        _HalfWidth   ("Half Width", Float) = 0.05
        _BraidTiling ("Braid Tiling", Float) = 8.0
        _BraidDepth  ("Braid Depth", Range(0,1)) = 0.35
        _Smoothness  ("Smoothness", Range(0,1)) = 0.45
        _Metallic    ("Metallic", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // A cable is thin enough that backface culling buys nothing, and
        // leaving it off means the ribbon's winding can never flip it invisible.
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _HalfWidth;
                float _BraidTiling;
                float _BraidDepth;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 tangentOS  : NORMAL;   // repurposed: along-cable direction
                float2 uv         : TEXCOORD0; // x = world length * tiling, y = 0/1 side
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 tangentWS  : TEXCOORD1;
                float3 sideWS     : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float4 color      : COLOR;
                float  fogCoord   : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 nodeWS    = TransformObjectToWorld(IN.positionOS.xyz);
                float3 tangentWS = normalize(TransformObjectToWorldDir(IN.tangentOS));

                // Billboard here, not on the CPU. One mesh build must serve
                // every split-screen camera correctly.
                float3 toCam = _WorldSpaceCameraPos - nodeWS;
                float  lenSq = dot(toCam, toCam);
                toCam = lenSq > 1e-12 ? toCam * rsqrt(lenSq) : float3(0, 0, 1);

                float3 side  = cross(tangentWS, toCam);
                float  sideSq = dot(side, side);
                // Degenerate when the cable points straight at the camera. Any
                // perpendicular will do there, since the ribbon is edge-on.
                side = sideSq > 1e-8 ? side * rsqrt(sideSq)
                                     : normalize(cross(tangentWS, float3(0, 1, 0)) + float3(1e-4, 0, 0));

                float sideSign = IN.uv.y * 2.0 - 1.0;
                float3 posWS = nodeWS + side * (_HalfWidth * sideSign);

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.tangentWS  = tangentWS;
                OUT.sideWS     = side;
                OUT.uv         = IN.uv;
                OUT.color      = IN.color;
                OUT.fogCoord   = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // The cylinder normal, derived rather than sampled. x is the
                // sideways bow across the ribbon, and sqrt(1-x*x) is the bulge
                // toward the viewer. Smooth at any width, with none of the
                // sRGB / flipGreenChannel import traps a painted strip carries.
                float x = saturate(IN.uv.y) * 2.0 - 1.0;
                float bulge = sqrt(saturate(1.0 - x * x));

                // Procedural braid: a diagonal ridge running along the jacket.
                // Perturbs the bow rather than adding a separate normal, so it
                // reads as moulded into the cable instead of printed on it.
                float braid = sin((IN.uv.x * _BraidTiling + IN.uv.y * 3.0) * 6.2831853);
                float bow = clamp(x + braid * _BraidDepth * 0.25, -1.0, 1.0);

                float3 viewWS = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 faceWS = normalize(cross(IN.sideWS, IN.tangentWS));
                float3 normalWS = normalize(IN.sideWS * bow + faceWS * bulge);
                if (dot(normalWS, viewWS) < 0.0) normalWS = -normalWS; // Cull Off: light the side we see

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogCoord;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.bakedGI = SampleSH(normalWS);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = IN.color.rgb;
                surfaceData.alpha = 1.0;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0;
                surfaceData.normalTS = float3(0, 0, 1);

                half4 col = UniversalFragmentPBR(inputData, surfaceData);
                col.rgb = MixFog(col.rgb, IN.fogCoord);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _HalfWidth;
                float _BraidTiling;
                float _BraidDepth;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            float3 _LightDirection;

            struct SAttributes { float4 positionOS : POSITION; float3 tangentOS : NORMAL; float2 uv : TEXCOORD0; };

            float4 shadowVert(SAttributes IN) : SV_POSITION
            {
                // The shadow pass must widen the ribbon the same way the lit
                // pass does, or cables cast hairline shadows that do not match
                // what is on screen. The shadow camera is the camera here.
                float3 nodeWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 tangentWS = normalize(TransformObjectToWorldDir(IN.tangentOS));

                float3 toCam = _WorldSpaceCameraPos - nodeWS;
                float lenSq = dot(toCam, toCam);
                toCam = lenSq > 1e-12 ? toCam * rsqrt(lenSq) : float3(0, 0, 1);

                float3 side = cross(tangentWS, toCam);
                float sideSq = dot(side, side);
                side = sideSq > 1e-8 ? side * rsqrt(sideSq) : float3(1, 0, 0);

                float3 posWS = nodeWS + side * (_HalfWidth * (IN.uv.y * 2.0 - 1.0));
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, side, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 shadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
```

- [ ] **Step 2: Import and verify it compiles**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command eval --code "var s = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(\"Assets/proceduralCables/CableRibbon.shader\"); UnityEngine.Debug.Log(s == null ? \"SHADER MISSING\" : (UnityEditor.ShaderUtil.ShaderHasError(s) ? \"SHADER ERRORS\" : \"shader ok\"));"
unity command get_console_logs
```

Expected: `shader ok`. If it reports `SHADER ERRORS`, read the message in the console — shader errors do **not** appear in `recompile_status`, which only covers C#.

- [ ] **Step 3: Create the material**

```bash
unity command eval --code "var s = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(\"Assets/proceduralCables/CableRibbon.shader\"); var m = new Material(s); UnityEditor.AssetDatabase.CreateAsset(m, \"Assets/proceduralCables/CableRibbon.mat\"); UnityEditor.AssetDatabase.SaveAssets(); UnityEngine.Debug.Log(\"material created\");"
```

Expected: `material created`.

- [ ] **Step 4: Commit**

```bash
git add Assets/proceduralCables
git commit -m "Draw the cable ribbon, billboarded per camera with a derived tube normal"
```

---

### Task 7: CableLibrary — folder-scanned connector meshes

**Files:**
- Create: `Assets/proceduralCables/CableLibrary.cs`
- Create: `Assets/proceduralCables/Meshes/Connectors/.gitkeep`

**Interfaces:**
- Consumes: nothing.
- Produces: `class CableLibrary : MonoBehaviour` with `Mesh[] Meshes { get; }`, `int Count { get; }`, `Mesh Get(int index)` (null-safe, returns null for -1 or out of range), and an editor-only `Scan()`.

**Why a serialized array and not a runtime scan:** `AssetDatabase` does not exist in a player build. The scan is an editor-time authoring action that fills a serialized field, exactly as `BackdropLibrary` does.

- [ ] **Step 1: Write CableLibrary**

Create `Assets/proceduralCables/CableLibrary.cs`:

```csharp
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// The connector meshes a cable can be tipped with. Scanned from a folder at
/// author time into a serialized array, because AssetDatabase does not exist in
/// a player build.
///
/// An empty library is a supported state: cables render headless rather than
/// throwing, so the system is usable before any plug has been modelled.
/// </summary>
public class CableLibrary : MonoBehaviour
{
    [Tooltip("Folder scanned by the Scan button. Every mesh found becomes a selectable connector.")]
    public string folder = "Assets/proceduralCables/Meshes/Connectors";

    [Tooltip("Meshes above this triangle count are skipped - a connector is a prop, not a scan.")]
    public int maxTriangles = 20000;

    [SerializeField] private Mesh[] meshes = new Mesh[0];

    public Mesh[] Meshes => meshes ?? (meshes = new Mesh[0]);
    public int Count => Meshes.Length;

    /// <summary>Null-safe lookup. Index -1 means "no library", and is expected.</summary>
    public Mesh Get(int index) =>
        (index < 0 || index >= Count) ? null : meshes[index];

#if UNITY_EDITOR
    /// <summary>Editor-only. Rebuilds the serialized array from the folder.</summary>
    public void Scan()
    {
        var found = new System.Collections.Generic.List<Mesh>();
        int skipped = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(obj is Mesh m)) continue;
                if (m.triangles.Length / 3 > maxTriangles) { skipped++; continue; }
                found.Add(m);
            }
        }

        meshes = found.ToArray();
        EditorUtility.SetDirty(this);
        Debug.Log($"CableLibrary: {meshes.Length} connector(s) in {folder}"
                + (skipped > 0 ? $", {skipped} skipped over {maxTriangles} triangles" : ""));
    }
#endif
}
```

- [ ] **Step 2: Create the folder and import**

```bash
mkdir -p "Assets/proceduralCables/Meshes/Connectors"
touch "Assets/proceduralCables/Meshes/Connectors/.gitkeep"
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
```

Expected: 0 errors.

- [ ] **Step 3: Verify an empty library degrades rather than throwing**

```bash
unity command eval --code "var go = new GameObject(\"probe\"); var lib = go.AddComponent<CableLibrary>(); UnityEngine.Debug.Log($\"count {lib.Count}, get(-1) null: {lib.Get(-1) == null}, get(5) null: {lib.Get(5) == null}\"); Object.DestroyImmediate(go);"
unity command get_console_logs
```

Expected: `count 0, get(-1) null: True, get(5) null: True`.

- [ ] **Step 4: Commit**

```bash
git add Assets/proceduralCables
git commit -m "Scan a folder for connector meshes, tolerating an empty one"
```

---

### Task 8: CableInstrument — the ring buffer, the mesh, the draws

**Files:**
- Create: `Assets/proceduralCables/CableInstrument.cs`

**Interfaces:**
- Consumes: `CableParameters`, `CableCurve`, `CableShot`, `CableRibbonBuilder`, `CableLibrary`.
- Produces: `class CableInstrument : MonoBehaviour` with public `void Fire()`, `void Clear()`, `void RerollSeed()`, `int LiveCount { get; }`, and public fields `parameters`, `source`, `target`, `library`, `ribbonMaterial`, `headMaterial`, `uvTiling`.

**Sole owner of state, sole issuer of draws** — the same contract as `PinDensityController`, `PoseInstrument` and `BackdropInstrument`. Drivers call `Fire()` and nothing else.

- [ ] **Step 1: Write CableInstrument**

Create `Assets/proceduralCables/CableInstrument.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns every live cable and issues every draw. Drivers call Fire() and know
/// nothing else - the same instrument/driver split as PinDensityController and
/// PoseInstrument, which is what lets the look be judged with no hardware
/// attached.
///
/// Nothing integrates. Each cable's shape is recomputed from its age every
/// frame, so there is no drift to accumulate and no state to resynchronise.
/// </summary>
[ExecuteAlways]
public class CableInstrument : MonoBehaviour
{
    [Tooltip("Where cables are fired from. Defaults to this object.")]
    public Transform source;
    [Tooltip("What cables are fired at. Landing points scatter around it.")]
    public Transform target;

    public CableParameters parameters = CableParameters.Default;

    [Tooltip("Connector meshes. Leave empty and cables render headless.")]
    public CableLibrary library;

    [Tooltip("Uses Pincushioned/CableRibbon.")]
    public Material ribbonMaterial;
    [Tooltip("Any URP material. Used for the connector meshes.")]
    public Material headMaterial;

    [Tooltip("Jacket repeats per world unit along the cable.")]
    public float uvTiling = 2f;

    private readonly List<CableShot> _shots = new List<CableShot>();
    private int _nextId;
    private float _drift;

    private Mesh _mesh;
    private Vector3[] _nodes;
    private readonly List<Vector3> _positions = new List<Vector3>();
    private readonly List<Vector3> _tangents = new List<Vector3>();
    private readonly List<Vector2> _uvs = new List<Vector2>();
    private readonly List<Color> _colors = new List<Color>();
    private readonly List<int> _indices = new List<int>();
    // Parallel lists. A cable with no connector contributes to neither, so
    // these must not be indexed against _shots.
    private readonly List<Matrix4x4> _heads = new List<Matrix4x4>();
    private readonly List<int> _headMesh = new List<int>();

    public int LiveCount => _shots.Count;

    private Vector3 SourcePos => source != null ? source.position : transform.position;
    private Vector3 TargetPos => target != null ? target.position : transform.position + transform.forward * 10f;

    /// <summary>Fire one cable. At the cap the oldest retires.</summary>
    public void Fire()
    {
        int cap = Mathf.Max(1, parameters.cableCap);
        while (_shots.Count >= cap) _shots.RemoveAt(0);

        int meshCount = library != null ? library.Count : 0;
        _shots.Add(CableShot.Create(_nextId++, SourcePos, TargetPos, parameters, meshCount));
    }

    public void Clear() => _shots.Clear();

    public void RerollSeed()
    {
        // Not UnityEngine.Random: this project treats that global stream as
        // off-limits because anything else can perturb it. A reroll wants one
        // arbitrary value, so take it from the clock and hash it.
        parameters.seed = CableCurve.Hash((uint)System.DateTime.Now.Ticks);
        Clear();
    }

    private void OnDisable()
    {
        if (_mesh == null) return;
        if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
        _mesh = null;
    }

    private void Update()
    {
        float dt = Application.isPlaying ? Time.deltaTime : 1f / 60f;

        // The idle sway. Amplitude never reaches zero, so a settled nest keeps
        // breathing - the same integration PinDensityController does for pins.
        _drift += parameters.driftSpeed * dt;

        for (int i = 0; i < _shots.Count; i++)
        {
            var s = _shots[i];
            s.age += dt;
            _shots[i] = s;
        }

        BuildMesh();
        Draw();
    }

    private void BuildMesh()
    {
        _positions.Clear(); _tangents.Clear(); _uvs.Clear(); _colors.Clear(); _indices.Clear();
        _heads.Clear(); _headMesh.Clear();

        int nodeCount = Mathf.Max(2, parameters.nodesPerCable);
        if (_nodes == null || _nodes.Length < nodeCount) _nodes = new Vector3[nodeCount];

        int colorCount = (parameters.colors != null && parameters.colors.Length > 0) ? parameters.colors.Length : 0;

        foreach (var shot in _shots)
        {
            Vector3 head = shot.HeadAnchor(parameters);
            float flight01 = shot.Flight01;

            // Whippy while flying, easing to the authored resting values. The
            // shiver rides on top for a moment after landing.
            float settleAge = Mathf.Max(0f, shot.age - shot.flightDuration);
            float noiseAmp = parameters.cableNoise
                           * (1f + parameters.pathNoise * flight01
                                 + Mathf.Abs(CableCurve.Shiver(settleAge, parameters.shiverDecay, parameters.shiverFreq)));
            float slack = parameters.slack * (1f - flight01);

            for (int i = 0; i < nodeCount; i++)
            {
                float t = i / (float)(nodeCount - 1);
                _nodes[i] = CableCurve.Position(
                    t, SourcePos, head,
                    slack, noiseAmp, parameters.noiseScale, _drift, shot.seed);
            }

            Color c = colorCount > 0 ? parameters.colors[Mathf.Clamp(shot.colorIndex, 0, colorCount - 1)] : Color.white;
            CableRibbonBuilder.Append(_nodes, nodeCount, c, uvTiling,
                _positions, _tangents, _uvs, _colors, _indices);

            if (shot.meshIndex >= 0 && library != null)
            {
                _heads.Add(HeadMatrix(shot, head, flight01, nodeCount));
                _headMesh.Add(shot.meshIndex);
            }
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "Cables (generated)" };
            _mesh.MarkDynamic();
        }

        _mesh.Clear();
        if (_positions.Count == 0) return;

        // 16-bit indices top out at 65,535 vertices - a cap of 64 cables at 24
        // nodes is 3,072, but the cap is an Inspector field and can be raised.
        _mesh.indexFormat = _positions.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        _mesh.SetVertices(_positions);
        _mesh.SetNormals(_tangents);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetColors(_colors);
        _mesh.SetTriangles(_indices, 0, true);

        // The ribbon widens in the vertex shader, so the mesh bounds must be
        // padded by the half-width or cables cull at the screen edge.
        var b = _mesh.bounds;
        b.Expand(parameters.thickness * 2f);
        _mesh.bounds = b;
    }

    private Matrix4x4 HeadMatrix(in CableShot shot, Vector3 head, float flight01, int nodeCount)
    {
        // Travel is sampled over a short step rather than differenced against
        // last frame, so the aim is a function of age like everything else.
        float step = 1f / 60f;
        var prev = shot;
        prev.age = Mathf.Max(0f, shot.age - step);
        Vector3 travel = head - prev.HeadAnchor(parameters);

        Vector3 endTangent = head - _nodes[Mathf.Max(0, nodeCount - 2)];
        Vector3 dir = CableShot.HeadDirection(travel, endTangent, flight01, Vector3.forward);

        return Matrix4x4.TRS(head, Quaternion.LookRotation(dir, Vector3.up), Vector3.one * parameters.headScale);
    }

    private void Draw()
    {
        if (ribbonMaterial != null && _mesh != null && _positions.Count > 0)
        {
            ribbonMaterial.SetFloat("_HalfWidth", parameters.thickness);
            Graphics.RenderMesh(new RenderParams(ribbonMaterial), _mesh, 0, Matrix4x4.identity);
        }

        if (headMaterial == null || library == null || _heads.Count == 0) return;

        // One instanced batch per distinct connector mesh - the same call the
        // scatter system and the backdrop already use.
        for (int m = 0; m < library.Count; m++)
        {
            Mesh mesh = library.Get(m);
            if (mesh == null) continue;

            var batch = new List<Matrix4x4>();
            for (int i = 0; i < _heads.Count; i++)
                if (_headMesh[i] == m) batch.Add(_heads[i]);

            if (batch.Count > 0)
                Graphics.RenderMeshInstanced(new RenderParams(headMaterial), mesh, 0, batch);
        }
    }
}
```

- [ ] **Step 2: Import and verify it compiles**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
unity command run_tests --mode EditMode
unity command test_status
```

Expected: 0 compile errors, all Core tests still pass.

- [ ] **Step 3: Verify the ring buffer caps and the mesh builds**

```bash
unity command eval --code "var go = new GameObject(\"probe\"); var inst = go.AddComponent<CableInstrument>(); inst.parameters.cableCap = 5; for (int i = 0; i < 20; i++) inst.Fire(); UnityEngine.Debug.Log($\"live {inst.LiveCount} (expect 5)\"); Object.DestroyImmediate(go);"
unity command get_console_logs
```

Expected: `live 5 (expect 5)`.

- [ ] **Step 4: Commit**

```bash
git add Assets/proceduralCables
git commit -m "Own every live cable in one instrument and one dynamic mesh"
```

---

### Task 9: KeyboardCableDriver and the explorer scene

**Files:**
- Create: `Assets/proceduralCables/KeyboardCableDriver.cs`
- Create: `Assets/proceduralCables/cableExplorer.unity` (via the eval step below)

**Interfaces:**
- Consumes: `CableInstrument.Fire()`, `RerollSeed()`, `Clear()`, `CableLibrary`.
- Produces: nothing downstream.

**Input note:** this project has `InputSystem_Actions.inputactions`, so the new Input System may be active. `UnityEngine.Input` throws under `Active Input Handling = Input System Package (New)`. The driver below compiles against whichever is enabled.

- [ ] **Step 1: Write the driver**

Create `Assets/proceduralCables/KeyboardCableDriver.cs`:

```csharp
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// How the cable instrument is played without hardware. A dumb driver: it reads
/// keys and calls the instrument, and holds no state of its own.
///
/// X fires, R rerolls the seed, C clears.
/// </summary>
public class KeyboardCableDriver : MonoBehaviour
{
    public CableInstrument instrument;

    private void Reset() => instrument = GetComponent<CableInstrument>();

    private void Update()
    {
        if (instrument == null) return;

        if (Pressed_X()) instrument.Fire();
        if (Pressed_R()) instrument.RerollSeed();
        if (Pressed_C()) instrument.Clear();
    }

#if ENABLE_INPUT_SYSTEM
    private static bool Pressed_X() => Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame;
    private static bool Pressed_R() => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
    private static bool Pressed_C() => Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
#else
    private static bool Pressed_X() => Input.GetKeyDown(KeyCode.X);
    private static bool Pressed_R() => Input.GetKeyDown(KeyCode.R);
    private static bool Pressed_C() => Input.GetKeyDown(KeyCode.C);
#endif
}
```

- [ ] **Step 2: Import and verify it compiles**

```bash
unity command eval --code "UnityEditor.AssetDatabase.Refresh();"
unity command recompile && unity command recompile_status
```

Expected: 0 errors.

- [ ] **Step 3: Build the explorer scene**

Save the following to `scratchpad/build_cable_scene.cs` and run it with `unity command eval_file`. Building the scene in code rather than by hand keeps the whole pipeline CLI-driven, the way the backdrop's is.

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

var camGo = GameObject.Find("Main Camera");
camGo.transform.position = new Vector3(0f, 6f, -22f);
camGo.transform.rotation = Quaternion.Euler(8f, 0f, 0f);

var sourceGo = new GameObject("Cable Source");
sourceGo.transform.position = new Vector3(-12f, 8f, 0f);

var targetGo = new GameObject("Cable Target");
targetGo.transform.position = new Vector3(12f, 1f, 0f);

var rig = new GameObject("Cable Rig");
var lib = rig.AddComponent<CableLibrary>();
var inst = rig.AddComponent<CableInstrument>();
inst.source = sourceGo.transform;
inst.target = targetGo.transform;
inst.library = lib;
inst.ribbonMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/proceduralCables/CableRibbon.mat");
inst.headMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/proceduralCables/CableRibbon.mat");

var driver = rig.AddComponent<KeyboardCableDriver>();
driver.instrument = inst;

// A floor, so the sag reads against something.
var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
floor.name = "Floor";
floor.transform.localScale = Vector3.one * 6f;

EditorSceneManager.SaveScene(scene, "Assets/proceduralCables/cableExplorer.unity");
Debug.Log("cableExplorer.unity written");
```

```bash
unity command eval_file --path scratchpad/build_cable_scene.cs
unity command get_console_logs
```

Expected: `cableExplorer.unity written`.

- [ ] **Step 4: Fire cables and confirm they render**

```bash
unity command eval --code "var i = Object.FindFirstObjectByType<CableInstrument>(); for (int n = 0; n < 12; n++) i.Fire(); UnityEngine.Debug.Log($\"live {i.LiveCount}\");"
unity command eval --code "UnityEngine.ScreenCapture.CaptureScreenshot(\"scratchpad/cables.png\"); UnityEngine.Debug.Log(\"captured\");"
```

**The editor must be focused.** `CableInstrument` draws from `[ExecuteAlways] Update()`, and an unfocused editor does not tick — cables will be absent from the capture, which is a capture artifact and not a broken instrument. The same trap `MeshSurfaceScatter` has.

Then read `scratchpad/cables.png` and check: cables span source to target, they sag downward, they read as round rather than flat, and the colours vary between cables.

- [ ] **Step 5: Commit**

```bash
git add Assets/proceduralCables
git commit -m "Play the cable instrument from the keyboard in an explorer scene"
```

---

## Acceptance

Done when all of the following hold:

- [ ] `unity command recompile_status` reports 0 errors
- [ ] `unity command test_status` reports all EditMode tests passing
- [ ] Pressing `X` in `cableExplorer.unity` fires a cable that flies from source to target
- [ ] A landed cable hangs with visible slack and **keeps moving** — the idle sway never stops
- [ ] Cables read as round cable, not flat ribbon, from multiple camera angles
- [ ] Cables vary in colour across the six-entry palette
- [ ] With `CableLibrary` empty, cables still render headless without throwing
- [ ] Firing past `cableCap` retires the oldest cable rather than growing without bound

## Known deviation from the spec

**Retirement is a hard removal, not a fade.** The spec says a cable at the cap
retires "by easing `thickness` to zero". This plan removes it outright, because
a per-cable width would need its own vertex channel and a second width path
through the shader — real complexity for a pop that only occurs once the cap is
reached, and at the default cap of 64 is rare. If the pop proves visible in
practice, the fix is a per-vertex width scale folded into `uv.y`'s sign
magnitude, which is a contained change to Task 5 and Task 6.

## Follow-up, explicitly out of scope

- Connector meshes (XLR, quarter-inch, USB). The library is empty until they exist; cables render headless meanwhile.
- MIDI drivers. The pad budget is contested — see the root CLAUDE.md.
- Main-scene integration and `FloorVisibilityController` interaction.
- Build-measured performance. Editor frame times are meaningless in this project (5.2x inflation); measure in a player build.
