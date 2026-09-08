# FEATURE: PoseInstrument — Play/Freeze modes, adjustable rate, expandable pose bank

**Target:** Unity 6.3 · Humanoid Mixamo character · MIDI Fighter 64 driven
**Status:** **Built and verified in the prototype scene, 2026-09-06.** Acceptance
criteria 1, 2, 3, 5 and 6 pass under measurement (see *Verification*). Not yet in
the main scene, and not yet run against MIDI hardware.

> This document was originally written without access to this Unity project. It
> claimed `PoseInstrument` already existed, named MIDI events that do not exist
> in the package, and required a Bones Stimulator hat rig. All three have been
> corrected below. The floppy hat was dropped as an aesthetic decision.

---

## 1. Where it lives

Everything is in `Assets/mixamoDance/`, isolated from the main scene:

| File | Role |
|---|---|
| `mixamoDanceDebug.unity` | Prototype scene: Dancer, Ground, camera, light |
| `PoseInstrument.cs` | Playable graph, blend math, layer pool. **No MIDI knowledge.** |
| `PoseBank.cs` | `PoseBank` SO + `PoseEntry` struct + `PlayOverride` enum |
| `KeyboardPoseDriver.cs` | Keys → the instrument. How the prototype is played without hardware. |
| `MidiFighterPoseDriver.cs` | MF64 pads → the instrument, plus LED feedback |
| `DancePoseBank.asset` | The 4 dance clips |
| `DUG_Surface.mat` | URP/Lit, base + normal + metallic-smoothness |

Global namespace with `using MidiFighter64;`, matching `MidiMixPinDensityDriver`
— which is the closest existing precedent, not the `MidiFighter64.Samples`
convention in `Assets/midiSupport/`.

---

## 2. Structure — and why it differs from the original plan

The original plan put router subscriptions inside `PoseInstrument`. This project
already has a better pattern: `PinDensityController` owns a value and
`MidiMixPinDensityDriver` / `AudioPinDensityDriver` are dumb drivers feeding it.
The same split is used here:

```
KeyboardPoseDriver ─┐
                    ├─► PoseInstrument ──► PlayableGraph ──► Animator
MidiFighterPoseDriver ┘   (input-agnostic)
```

Three further departures from the original:

1. **Pad bindings are serialized data**, not a `NoteToPoseIndex()` function, so
   the reserved pad region is Inspector-visible and auditable against
   `MidiFighterInteriorSpawner`.
2. **No `inlinePoses` fallback.** `PoseBank` or nothing. Two sources of truth for
   one list, with a silent `Count > 0` precedence rule, is a staleness trap.
3. **A trigger resolves into one immutable `TriggerSettings`** rather than
   mutable `playing` / `rateLocked` / `rate` fields on the layer. The original's
   `rateLocked = play && e.usePlaybackRateOverride` silently discarded a rate
   override on any layer triggered in Freeze mode, so a later `ResumeAllNow()`
   ran it at the global rate.

Two smaller corrections: `UnityEngine.Random` → a seeded `System.Random` (project
rule — the global stream is shared with the scatter and spawner systems), and
`Reset()` now covers every serialized field rather than six of twelve.

`GrowMixer` is capped by `maxMixerInputs` (64) so capacity cannot ratchet upward
across a long set.

---

## 3. Behaviour

### Play / Freeze
Mode is **captured at trigger time** — the mode you were in when the pad fired
governs that layer for its lifetime.

- **Freeze (default):** clip blends in but sits at `speed = 0` on a **random
  frame**. Re-pressing the same pad picks a new frame, so hammering one pad
  cycles frozen poses out of a single clip. `newRandomFrameEachPress` toggles
  this; `freezeNormalizedTime` is the fixed fallback.
- **Play:** clip blends in and keeps running at `playbackRate`.

### Rate
`playbackRate` **live-syncs** to already-playing layers, so a knob bends the
tempo of the current motion without retriggering. Negative reverses. Note the
deliberate asymmetry: freeze-vs-play is per-trigger, rate is live.

`FreezeAllNow()` / `ResumeAllNow()` are live stabs independent of that.

### Weight normalization — load-bearing
The mixer does not auto-normalize, and un-normalized humanoid weights pull the
pose toward rest. At trigger time every live layer's weight is captured as its
baseline; the newest rides the curve at `f` and the rest scale by `(1 - f)`, so
the sum stays at 1. **Do not simplify the `baseline * (1 - f)` fade.**

The exception is the very first trigger of a session, where baselines sum to 0
and the total is only `f` for one blend. `primePoseIndex` snaps past that so the
rig never flashes a T-pose.

### Bank size vs mixer inputs
Independent axes. **Bank size** = how many poses exist (unlimited, in the SO).
**Mixer inputs** = how many layers may overlap mid-blend (16, auto-grows to 64,
force-culls the weakest fading layer first).

---

## 4. Asset prerequisites — all four Unity defaults are wrong

Same shape as the artifact ingestion chain. Configured 2026-09-06.

| Asset | Setting | Required | Why |
|---|---|---|---|
| Character FBX | `animationType` | **Humanoid**, Create From This Model | Retargeting goes through the avatar |
| Character FBX | `optimizeGameObjects` | **off** | Flattening hides bone Transforms |
| Clip FBXs | `animationType` | **Humanoid**, **Copy From Other Avatar** → character | Create-from-self builds a *different* avatar; proportions drift subtly |
| Clip FBXs | `loopTime` + `loopPose` | **on** | Non-looping clips clamp at their last frame in Play mode |
| Clip FBXs | Root Position XZ / Y / Rotation | **Bake Into Pose** | Otherwise the dancer walks out of the room |
| Clip FBXs | clip `name` | renamed off `mixamo.com` | All four otherwise import under the same take name |
| `*_Normal.png` | `textureType` | **NormalMap** | Default type is not a normal map |
| `*_MetallicSmoothness.png` | `sRGB` | **off** | Data map, not colour |

`Animator.runtimeAnimatorController` must be **None** — `AnimationPlayableOutput`
takes the graph over, and a leftover controller is a reliable source of confusion.
`applyRootMotion` off; `cullingMode = AlwaysAnimate`.

**Unresolved:** `_Normal.png` is left at `flipGreenChannel = false`. The artifact
pipeline needs the flip because its Python exporter emits OpenGL-convention
normals; this texture is not from that pipeline, so the default is the right
starting point. Eyeball it under a moving light and flip if the shading reads
inverted.

---

## 5. MIDI wiring — the actual package API

The original plan's `midiFighter.OnPadDown` and `midiMix.OnControlChange` do not
exist. The real surface, from
`Packages/com.caseyfarina.midifighter64/Runtime/`:

| Need | Actual API |
|---|---|
| Pad press | `MidiFighterButtonRouter.OnButtonPress(GridButton, float)` — **static** |
| Pad release | `MidiFighterButtonRouter.OnButtonRelease(GridButton)` — **static** |
| Knob | `MidiMixRouter.OnKnob(int channel, int row, float value)` — **static**, value already 0-1 |
| LED | `MidiFighterOutput.SetLED(noteNumber, MidiFighterLEDColor)` |
| Note number | `MidiFighter64InputMap.ToNote(row, col)` |

`GridButton` carries `row` / `col` (1-based, row 1 = top, col 1 = left),
`linearIndex` and `noteNumber`.

**All router events are static**, so every `+=` needs its matching `-=` in
`OnDisable` or a destroyed dancer keeps receiving MIDI.

### Pad budget — the integration blocker

`MidiFighterInteriorSpawner` responds to **every** pad it is not explicitly told
to skip. Prototype allocation is **row 1, columns 1-7** (column 8 stays with
floor navigation), 4 bound and 3 spare.

**When this moves to the main scene**, those 7 row/col pairs must be added to
`Extra Reserved Pads` on the `Pincushioned Rig` prefab, or every pose pad will
also toggle an interior object. The main scene is deliberately untouched until
then.

---

## 6. Verification — measured, not assumed

Run through the live editor via the `unity` CLI, in Play mode.

| AC | Result |
|---|---|
| **1** Freeze: same pad → different frozen frame | ✅ Three presses of pad 0, settled 1 s apart: hips `(0.159, 0.151, …)` → `(0.124, 0.167, …)` → `(0.084, -0.183, …)`. Layers culled back to 1 each time. |
| **2** Play: keeps running; rate live-bends and reverses | ✅ Forward at rate 1: t = 2.615 → 5.475 → 8.382. Rate set to −1 mid-motion **without retriggering** (layers stayed at 1): t = 7.324 → 4.276 → 1.152. Negative time wraps correctly on a looping clip; verified visually at t = −30.5. |
| **3** Rapid mash is pop-free, no T-pose | ✅ Three stacked triggers reached 4 layers and culled back to 1 with no rest-pose tug. |
| **4** Bank expandable | Partially — 4 poses, well under the 16-input floor. Overlap-driven `GrowMixer` is untested. |
| **5** Per-pose overrides beat the global toggle | ✅ Global mode PLAY, rate 1, pose 3 set to `ForceFreeze`: t held at 1.141 across three samples. Reverted to `Inherit`. |
| **6** `FreezeAllNow` / `ResumeAllNow` | ✅ Frozen: t held at −12.363 exactly. Resumed: −15.298 → −18.200, continuing at the live rate. |
| **7** ~~Hat bones under Bones Stimulator~~ | **Dropped** — the floppy hat was cut as an aesthetic decision. |

All four clips import with `humanMotion = True`, confirming retargeting at the
asset level. Zero compile errors, zero pose-related console errors.

---

## 7. Prototype controls

`KeyboardPoseDriver` — this project is **Input System package only**
(`activeInputHandler: 1`), so legacy `Input` throws; everything goes through
`Keyboard.current`.

| Key | Action |
|---|---|
| `1`-`9` | Trigger pose 0-8 |
| `Space` | Toggle Play / Freeze |
| `F` / `R` | `FreezeAllNow` / `ResumeAllNow` |
| `↑` / `↓` | Playback rate ± 0.25, crossing zero into reverse |
| `0` | Rate back to 1 |

An on-screen overlay shows pose count, live layer count, mode and rate.

---

## 8. Where to pick up

1. **Hardware pass.** `MidiFighterPoseDriver` is written and compiles but has
   never seen a MF64. Add it to the Dancer, assign a `MidiFighterOutput`, and
   bring a `MIDI Controller` prefab instance into the prototype scene.
2. **More clips.** 4 poses across 7 pads. The bank takes more with no code change.
3. **Exercise the overrides artistically** — hard-cut pads
   (`useBlendTimeOverride` + `blendTimeOverride = 0`) alongside morphing ones is
   the most expressive part of the design and is currently all set to Inherit.
4. **Main-scene integration.** Pick a floor, reserve row 1 cols 1-7, and decide
   whether the dancer is `Always Active` — `FloorVisibilityController` will
   otherwise `SetActive(false)` it, which stops `Update` mid-blend and
   unsubscribes the pads.
5. **MIDI Mix knob → rate.** Not yet written; `MidiMixRouter.OnKnob` →
   `SetPlaybackRate`, mirroring `MidiMixPinDensityDriver`.
6. **VFX Graph burst on trigger.** `com.unity.visualeffectgraph` 17.3.0 is
   installed and `skinnedMesh.vfx` is in the debug scene. The intended hook is a
   public `event Action<int> OnPoseTriggered` on `PoseInstrument` plus a small
   `VfxPoseBurstDriver` calling `VisualEffect.SendEvent`, so the burst follows
   whatever fired the pose rather than re-subscribing to MIDI and adding a second
   claimant to the pad-conflict table. **Neither is written yet.** Graph-side
   notes (vertex colour is unusable, sample the albedo through UV) are in
   CLAUDE.md under *VFX Graph particles off the dancer*.
