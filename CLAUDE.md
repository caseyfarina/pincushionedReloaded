# pincushioned Reloaded

Unity 6 (6000.3.7f1), URP. An 8-floor vertical building; each floor holds a
different isometric scene of pin-scattered museum artifacts, performed live with
a Midi Fighter 64 and an Akai MIDI Mix.

Repo: `github.com/caseyfarina/pincushionedReloaded`. Successor to
`pincushionedSpring2026`, which is **not** a working clone — see *Content
recovery* below. That history is deliberately not carried over.

## State — read this first

Everything below compiles clean with zero console errors, and the scene reports
zero missing prefabs. Performance has been measured and addressed — floor culling
plus a scatter bounds fix took the frame from 125 ms CPU to 35 ms (see *Floors*).

**Performance is fine.** Measured in a player build with 8 split-screen cells,
all shadows on, one floor active: **9.17 ms CPU / 6.55 ms GPU — about 109 fps.**

**Never trust editor frame times on this project.** The same scene measures
47.51 ms in the editor — 5.2x slower — because two open Scene views re-render
everything, and the editor loop inflates even the PlayerLoop marker (19.03 ms in
editor vs 8.55 ms in build). Use `ProfilerProbe` in a build before concluding
anything is slow.

**Nothing here has been run with the MIDI hardware or live audio connected.**
That is the one remaining open risk.

## Graphics API — MUST USE D3D11
This project must run under **Direct3D 11**, not D3D12. D3D12 causes unrecoverable GPU device loss (TDR crash) due to conflicts between Unity's D3D12 device and the native D3D11 plugins (Adobe Substance, KlakHap). This is already enforced in `ProjectSettings/ProjectSettings.asset` (`WindowsStandaloneSupport` → D3D11 only). If crashes return, add `-force-d3d11` to Unity Hub → project → Advanced Settings → Additional command line arguments.

## Scene Loading — Open Manually
The main scene (`pincushionededed.unity`) is heavy. **Always open Unity first to an empty scene**, then open the main scene manually. If Unity starts auto-loading the heavy scene and crashes, delete `Library/LastSceneManagerSetup.txt` and relaunch. An 8-floor vertical building where each floor contains a different isometric scene, navigated via MIDI controllers.

## Project Structure

```
Assets/
  Scenes/pincushionededed.unity          Main scene (MIDI Controller + Pincushioned Rig objects)
  midiSupport/                           App-side MIDI consumer code only
    Samples/TestScene/                   App controllers + spawners (+ Editor/ inspectors)
    Samples/Resources/                   ScriptableObject assets (build-safe)
  proceduralPincushioning/               GPU-instanced pin scatter system (see its CLAUDE.md)
  ScatterData/                           Baked SurfaceSampleData assets (52)
  ScatterPrefabs/                        Batch-tool output prefabs (41; the importer writes _Pinned directly)
  pinnedMeshes/                          *_Pinned prefabs used by the main scene
  Prefabs/Pincushioned Rig.prefab        All 8 app controllers, Inspector-configured
  SplitScreen/                           Mosaic split-screen camera system
  mixamoDance/                           PoseInstrument prototype — isolated from the main scene
  Artifacts/                             51 scans + materials (see Artifact inventory)
  buildingFloor.fbx                      The room shell each floor is built from
  Plugins/Demigiant/DOTween/             DOTween Pro (DLL, no asmdef, globally accessible)
Packages/manifest.json                   com.caseyfarina.midifighter64 (git URL, pinned tag)
3DObjectProcessing/                      Python scan2unity pipeline (source only; data gitignored)
```

## Content recovery — resolved

The art assets missing from `pincushionedSpring2026` were recovered from the
laptop project on 2026-08-30 and are now tracked here:
`Assets/Artifacts/` (15 recovered scans + materials; 25 more were added from
the Smithsonian on 2026-09-05, bringing it to 40), `Assets/buildingFloor.fbx`
(the room shell), `Assets/pincushionedCharacterMeshes/`, and the two referenced
`Assets/substances/` materials. The scene reports **0 missing prefabs**.

`3DObjectProcessing/` holds the pipeline **source only**. Its bulk outputs
(`raw_scans/`, `unity_assets*/`, `tex_test/`, ~750 MB) are gitignored — they are
derived data, re-fetchable from the Smithsonian API by the pipeline. The API key
files that live in that folder are gitignored; **never commit them**.

**33 GUID references remain unresolved and are not recoverable** — they were
absent from the laptop too. Almost all are ParticlePack-internal. The one that
touches live content: the three `*_Pinned 1` duplicate prefabs reference
`SurfaceSampleData` assets that no longer exist. Only the Allosaurus duplicate is
used by the scene (13 references); it renders its mesh but no pins. Fix by
pointing its `sampleData` at the surviving `Allosaurus…LOD0_SampleData`, or
delete the duplicate.

## MIDI package — consumed, not vendored

The MIDI layer is the **`midiFighterForUnity` package**, installed by git URL and
pinned to a tag in `Packages/manifest.json`:

```json
"com.caseyfarina.midifighter64": "https://github.com/caseyfarina/midiFighterForUnity.git#v2.3.0"
```

It requires Keijiro's scoped registry for `jp.keijiro.minis` / `jp.keijiro.rtmidi`
— already declared in this project's manifest.

This project previously vendored an early fork of that package under
`Assets/midiSupport/midiFighterForUnity-claude-add-midi-test-scene-vtbqj/`. That
fork is **gone**. Do not re-add MIDI plumbing under `Assets/` — the boundary is:

- **Package** (`Packages/com.caseyfarina.midifighter64/`) — the MIDI bridge.
  Read-only here. Raw MIDI → typed C# events. Never edit from this project; fix
  it in its own repo and bump the tag.
- **App** (`Assets/midiSupport/Samples/TestScene/`) — everything that *consumes*
  those events. All scene behaviour lives here.

The package's own `CLAUDE.md` (in `Packages/com.caseyfarina.midifighter64/`) is
the authority on its API, the split-half grid layout, the LED palette, and its
gotchas. Read it before touching MIDI code.

### How the rig comes up — scene-authored, no bootstrapper

**There is no bootstrapper. Nothing about the rig is created at runtime.** Two
objects sit in the main scene and everything is configured in the Inspector:

| Scene object | What it is |
|---|---|
| `MIDI Controller` | Instance of the package's wired prefab (`MidiEventManager`, routers, outputs, drawer) |
| `Pincushioned Rig` | Instance of `Assets/Prefabs/Pincushioned Rig.prefab` — all 8 app controllers |

`MidiSceneBootstrapper`, `MidiRigRefs`, and `MidiRigRefsInit` were **deleted**.
They existed only to create these objects at runtime, which made every serialized
field unreachable and every tweak vanish on exiting Play mode.

**Do not reintroduce runtime construction for anything with artistic
parameters.** `new GameObject(...).AddComponent<T>()` and
`[RuntimeInitializeOnLoadMethod]` both produce components whose Inspector fields
can never be edited or persisted. If a system needs tuning, it belongs in the
scene or on a prefab.

Settings that were previously bootstrapper toggles now live on the scene
instances, which is what the package's own docs recommend:

| Setting | Where | Value |
|---|---|---|
| Device allow-list | `MidiEventManager` on `MIDI Controller` | `Fighter`, `MIDI Mix` — blocks a duplicate MIDI port, the #1 cause of "latching is broken" |
| `Latch Mute` / `Latch Rec Arm` | `MidiMixRouter` | **off** — matches this project's original momentary behaviour |
| `MidiStatusDrawer` | `MIDI Controller` | present but **disabled**, since `MidiDebugUI` is the active overlay. Tick it to switch. |

Both prefab instances keep their prefab link, so package updates flow through and
per-scene overrides are normal Unity prefab behaviour.

## MIDI Hardware

- **Akai MIDI Mix** — 8-channel mixer (24 knobs, 8+1 faders, 24 buttons)
- **DJ Tech Tools Midi Fighter 64** — 8x8 button grid (64 buttons)
- Both connected via USB; WinMM exclusive access (close DAWs before Play)
- Use a **powered USB 3.0 multi-TT hub** for both devices simultaneously

## Floors — unequal sizes and visibility culling

**`FloorVolume` is the source of truth** for where a floor is and how big it is.
Rooms may differ in height and footprint: resize a floor's trigger collider and
the camera re-frames automatically, because `CameraPosition` derives from the
live bounds. For full control (a room needing a different distance or angle, not
just height) assign `Camera Anchor` and the camera moves there exactly.

**Nothing derives a floor position from `index * uniformHeight`.** That
arithmetic survives only as a fallback in `FloorCameraController` for scenes with
no FloorVolumes. Floor *order* is by `floorIndex`, never by height, so MF64
column 8 keeps working however the rooms are sized.

### Visibility culling — measured 3.6x

`FloorVisibilityController` (on `Pincushioned Rig`) enables only the floor you
are on. Measured with the split-screen rig running:

| | all floors | culled | change |
|---|---:|---:|---|
| draw calls | 7,661 | 402 | 19x fewer |
| triangles | 113.8M | 4.69M | 24x fewer |
| CPU frame | 125.3 ms | 35.2 ms | **3.6x faster** |
| GPU frame | 27.2 ms | 3.1 ms | 8.8x faster |

The cost was never geometry sitting in memory — it was 20 scatter instances
submitting pins and 40 lights fighting for the shadow atlas, for rooms nobody
could see.

**Why `SetActive` and not additive scene loading.** A whole-GameObject disable
stops Update, rendering, lights and shadows in one call, costs nothing at steady
state, and is instant. Async scene loading would put unpredictable hitches inside
a 0.6 s camera move during a live set, and memory was never the constraint.
Separate scenes would still be worth it for *authoring* — but load them all
additively at startup and toggle, rather than loading on demand.

Knobs: `Neighbour Range` (extra floors either side; raise if rooms overlap
vertically), `Always Active` (floors never disabled), `Keep Source During Move`
(on — the camera travels through the gap between floors), `Cull Floors` (master
off switch for A/B testing).

### Scatter render bounds — do not reintroduce a fixed cube

`MeshSurfaceScatter` used to submit a hardcoded **1000-unit bounds cube** while
the building is 64 units tall, so Unity could **never** frustum-cull a scatter
instance. It now computes a tight local-space bounds per `Scatter()`, padded by
the largest pin's reach. This is why draw calls fell further than floor culling
alone achieved — each split-screen camera can now cull independently.

### Floor change interactions

Two things must react when the floor changes, and both are wired:

- `SplitScreenFloorSync` re-evaluates the POI and re-places the split-screen
  cameras. **Without it every cell renders black** — the cameras keep aiming at
  the previous floor's POI, which is now disabled. Looks like the split screen
  broke; the cause is stale aim.
- `CloseUpCameraController` caches FloorVolumes **including inactive**, since a
  disabled floor is still a valid navigation target.

Anything new that looks up floors must use `FindObjectsInactive.Include`.

## MIDI mappings — the authoritative table

Rows 1 = top, columns 1 = left. Verified against the live scene, not defaults.

### Midi Fighter 64

| Pad | Action | Owner |
|---|---|---|
| **Col 8** (all rows) | Floor navigation. R1 = top floor, R8 = floor 0 | `FloorCameraController` |
| **R8 C1** | **Hold** for close-up camera; release returns to the floor cam | `CloseUpCameraController` |
| **R8 C2** | Reposition close-up on a random object in the current room | `CloseUpCameraController` |
| **R8 C3** | Split-screen: reroll layout **and** camera angles | `SplitScreenMidiBinding` |
| All other 53 pads | Toggle one interior object on/off | `MidiFighterInteriorSpawner` |

### Akai MIDI Mix

| Control | Action | Owner |
|---|---|---|
| **Ch 1, Row 2 knob** | Pin count, 0-2000 pins. Independent of the wave. | `MidiMixPinDensityDriver` |
| *(unassigned pad)* | Audio wave on/off — rearranges pins without changing the count | `MidiFighterWaveToggle` |
| Ch 2, Row 2 knob | Split-screen cell count 2-32 — **disabled** by default | `SplitScreenMidiBinding` |
| Mute / Rec-Arm / faders | Routed, nothing bound. **Momentary**, not latching | — |

### Unassigned split-screen actions

Three bindings sit at `R0C0`, which means off: **Reroll Layout**, **Reroll
Cameras**, **Re-evaluate POI**. Point any at a pad to separate them from the
combined R8 C3 action.

### Pad conflict rule

`MidiFighterInteriorSpawner` responds to **every** pad that isn't reserved. It
reserves column 8 and R8 C1-C2 via `Reserve Navigation Pads`, plus an explicit
`Extra Reserved Pads` list — currently `R8 C3`. **Bind a pad anywhere else and it
will also toggle an interior object.** `SplitScreenMidiBinding`'s inspector
detects the clash and offers a one-click fix; it does not happen automatically.

## Key scripts

`Assets/midiSupport/Samples/TestScene/`

| Script | Role |
|--------|------|
| `FloorCameraController.cs` | MF64 col 8 → DOTween vertical move. Floor height, offset, count, duration, easing all Inspector fields; floor-stop gizmos. |
| `MidiFighterInteriorSpawner.cs` | Pad → toggle interior instance. **Seeded layout** — same seed rebuilds the same arrangement. |
| `CloseUpCameraController.cs` | R8 C1 hold → close-up; C2 → reposition. **Seeded** shot selection. |
| `FloorVolume.cs` | Source of truth for a floor's bounds and camera framing. Supports unequal rooms. |
| `FloorVisibilityController.cs` | Enables only the visible floor — the 3.6x win. |
| `MidiDebugUI.cs` | Device status + raw event overlay. The active overlay. |

`Assets/proceduralPincushioning/` — `PinDensityController`,
`MidiMixPinDensityDriver`, `AudioPinDensityDriver` (see *Audio-reactive pin
density*), plus the scatter system itself.

`Assets/SplitScreen/` — see *Split-screen mosaic* below.

### Live vs dormant controls

Everything live sits on **`Pincushioned Rig`** or **`Split Screen Rig`** in the
scene. Both controllers are in use: the MF64 drives navigation, interiors and the
split-screen reroll; the MIDI Mix drives pin density.

`MidiMixCloner`, `MidiMixDataVisualizer`, and `MidiMixCameraRig` compile but are
on no scene object — dormant since before the package migration, left that way
deliberately. To revive one, add it as a component on `Pincushioned Rig`. Two
warnings: `MidiMixCameraRig.Start()` **disables every pre-existing camera in the
scene**, so it will take over the view; and these three still carry hardcoded
`const` art parameters (spread, FOV, the 8-entry camera VIEWS table, font sizes)
that were never promoted to Inspector fields like the live scripts were.

## Audio-reactive pin density (LASP)

Live audio input drives pin density alongside the MIDI knob. Package:
`jp.keijiro.lasp` 2.1.8 (pulls `jp.keijiro.libsoundio` + `com.unity.burst`),
resolved through the same Keijiro scoped registry as minis/rtmidi.

Three scripts in `Assets/proceduralPincushioning/`, all authored on the
`Pincushioned Rig` object in the scene:

| Script | Role |
|---|---|
| `PinDensityController` | **Single owner** of scatter count *and* noise drift. |
| `MidiMixPinDensityDriver` | MIDI Mix Ch1/Row2 knob → `Ceiling01` — **how many pins** |
| `AudioScatterAgitationDriver` | LASP level → `Agitation01` — **how much they rearrange** |
| `MidiFighterWaveToggle` | MF64 pad → wave on/off (freezes in place) |
| `AudioPinDensityDriver` | LASP level → `Envelope01`. **Disabled** — the old amplitude→count mapping. |

### Amplitude drives configuration, not count (2026-09-07)

Mapping amplitude to *density* made loud passages add mass, which read as the
model swelling rather than reacting. It now drives the **noise field's drift
velocity** instead, so the count stays wherever the knob put it and the music
decides how fast the visible selection turns over:

```csharp
_driftOffset += DriftAxis * (_agitation01 * driftSpeed * Time.deltaTime);
```

Quiet passages barely advance the field, so the same samples keep winning
selection and the pins sit still. Loud ones sweep it across the mesh.

**The two controls are independent, and that is a property of the selection
algorithm, not a coincidence.** Selection is a pure function of
`(seed, count, noiseOffset)`, and both the uniform and weighted paths draw from
a fresh `System.Random(seed)` in a fixed order — so the first N picks are
identical whether you ask for N or N+500. Changing the count with the offset
frozen **adds or removes pins from a stable ordering rather than reshuffling**.

Verified by capture on the Giant Moa: 400 pins → 900 → back to 400 returns a
**pixel-identical frame** (max diff 0), and at 900, **92.5%** of the 400-frame's
pin pixels are still pins (the remainder is occlusion by the added ones).

**Off means frozen, not reset.** The toggle stops the controller integrating
drift, leaving `noiseOffset` where it was, so the arrangement on screen is held.
Switching back on resumes from that offset — no jump.

**Requires `DensityMode.Noise`.** `noiseOffset` is never read in Uniform mode.
All 55 `*_Pinned` prefabs were switched on 2026-09-07 (`noiseScale 5`,
`octaves 2`, `contrast 1.4`). Side effect worth knowing: noise selection makes
pins **cluster** rather than spread evenly.

**The toggle pad is unassigned (`row 0 / col 0`).** When you bind it, add that
pad to `MidiFighterInteriorSpawner`'s Extra Reserved Pads or it will also toggle
an interior object.

**Why a single owner.** `MeshSurfaceScatter.SetCount()` runs a full `Scatter()`,
which rebuilds every instance matrix and allocates a `Matrix4x4[]` per batch. Two
components calling it independently would rescatter twice a frame and fight over
the value. The controller funnels both inputs into one rate-limited write:
`maxUpdatesPerSecond` (default 30) and `countDeadband` (default 8 pins) bound the
cost. **Do not call `SetCount` directly from new code** — set `Ceiling01` or
`Envelope01` and let the controller apply it.

Both inputs default to **1.0**, so either driver is optional: with no audio the
knob sweeps the full range exactly as before, and `AudioPinDensityDriver.OnDisable`
resets the envelope to 1 so density doesn't stay latched if audio drops out mid-set.

**Device selection.** Set on `AudioPinDensityDriver` in the Inspector. Defaults
to this rig's **Yeti Nano**. Empty = system default.
Enumerate devices with `Lasp.AudioSystem.InputDevices`. IDs are machine-specific
and must be reconfigured per machine. This rig has a Yeti Nano (default) and a
**VoiceMeeter VAIO** output — the latter is the route for capturing whatever is
playing rather than room sound.

**Band.** `_band` defaults to `FilterType.LowPass` (kick/bass). `BandPass` = mids,
`HighPass` = hats, `Bypass` = full range. `_floor` (default 0.15) keeps some pins
alive through quiet passages; `_contrast` shapes punchiness.

**Platform.** `Lasp.Runtime` is Editor + desktop standalone only, so
`AudioPinDensityDriver` is wrapped in a platform `#if`. Assembly-CSharp compiles
for every target and would otherwise fail to resolve the `Lasp` namespace.

## Split-screen mosaic (`Assets/SplitScreen/`)

Divides the game view into rectangles of varied size, each a camera aimed at a
shared point of interest. On the **`Split Screen Rig`** object in the scene.

| Script | Role |
|---|---|
| `MosaicLayout` | Pure partition math. Entry point for both layout modes. |
| `QuadtreeLayout` | Strict quadtree, kept as an option; also owns `Inset`. |
| `PointOfInterestFinder` | Resolves what every camera looks at. |
| `SplitScreenCameraRig` | Camera pool, placement, viewport rects, borders. |
| `SplitScreenMidiBinding` | MF64 pads → reroll actions. |

### Layout — guillotine partition, not a quadtree

Default mode **Random Rectangles** cuts an existing rectangle at a random ratio
and repeats, so N cuts give exactly N+1 cells and sizes vary freely. The strict
**Quadtree** mode is retained but restricts every edge to a half or quarter,
which makes sizes repeat.

Two rules keep it from degenerating into slivers, and both matter:

- **Min Split Ratio** clamps cuts away from a cell's edges.
- **Squareness Bias** prefers cutting the longer axis measured in **screen**
  space. A 0.5 × 0.5 viewport cell is *not* square on a 16:9 display — it is
  16:9 — so the aspect has to be folded in or every cell drifts wide.
- Which cell gets cut is **area-weighted**, so large rectangles break up first.
  Uniform picking keeps slicing an already-tiny cell and reads as noise.

### Point of interest — renderer bounds first, physics second

**This ordering is deliberate and must not be flipped.** The room shell has a
collider; the artifacts do **not** (the pinned scans carry only MeshFilter +
MeshRenderer). Physics-first therefore always wins with the wrong answer — the
floor slab instead of a sculpture.

Also: `FloorVolume`'s per-floor boxes are **triggers**, excluded via
`QueryTriggerInteraction.Ignore`, or they would supply an invisible POI floating
mid-room. A **Max Renderer Size** ceiling keeps structural geometry from becoming
the subject. And the pins can never be hit at all — they are drawn with
`Graphics.RenderMeshInstanced` and have no GameObjects.

Update Mode defaults to **Manual**: continuous re-evaluation makes every
sub-camera chase the main camera and reads as twitchy.

### Camera placement — the 180-degree constraint

Every sub-camera sits on a hemisphere centred on the POI and facing the main
camera, at a radius strictly under the main camera's distance. So no view is ever
behind the subject, and none is further out than the master shot. Sampling is an
even spherical cap, not rejection sampling, which would bias toward the pole.

`Enforce Axis Side` adds the **true** 180-degree rule: all cameras on one side of
the axis line, so screen direction stays consistent and the subject does not flip
left-right between cells. Off by default for variety. The hemisphere alone permits
that flip — the two constraints are not the same thing.

Verified in Play mode at 10 cells: 9 cameras inside the hemisphere, 0 behind the
perpendicular plane, 0 beyond the master-shot distance.

### Rendering

One URP **base** camera per cell with its own viewport rect. Overlay cameras are
not usable — they ignore their rect and inherit the base camera's. Borders are
the gaps left by insetting each rect, over a full-screen background camera
clearing to the border colour.

Sub-cameras get **no CinemachineBrain**, so the floor and close-up rigs keep
driving the main camera untouched. The main camera takes the largest cell by
default and keeps its own framing.

### Performance — measured

**Each cell is a full scene render**, and per-camera culling does not shrink with
cell size. Sub-camera post-processing and shadows are off by default for that
reason.

Measured in a **build** at 8 cells, one floor active: **9.17 ms CPU, 6.55 ms GPU,
~109 fps**, 401 draw calls, 5.8M triangles. Scripts are 1.19 ms; shadows 0.58 ms.
There is comfortable headroom — raise the cell count deliberately and re-measure
rather than assuming a ceiling.

**Cell count is a multiplier on everything in the room.** The pose instrument
sweep found frame cost linear in `characters x cells`, with 24x4 and 12x8 costing
identically — see *Pose instrument > Performance*. Treat that as the general
shape for any repeated animated prop, not a fact about dancers.

The same scene reads 47.51 ms in the **editor**. That number is meaningless: two
open Scene views re-render the scene, and the editor inflates even PlayerLoop
(19.03 ms editor vs 8.55 ms build). Always measure in a build.

### Reroll actions

Layout and placement carry **separate seeds**, which is what makes these
independent:

| Method | Changes | Leaves alone |
|---|---|---|
| `ResetCameraPositions()` | camera angles | cell layout |
| `RerollLayout()` | cell layout | placement seed |
| `RerollAll()` | both | — |

## Pose instrument — dancing segment (`Assets/mixamoDance/`)

**Prototype, isolated from the main scene on purpose.** A Mixamo humanoid driven
as an instrument: each MF64 pad adds a clip on a fresh `AnimationMixerPlayable`
input and blends it in over a 60 ms quadratic ease-out, while older layers freeze
at their live weight and fade under the newcomer.

Scene: `Assets/mixamoDance/mixamoDanceDebug.unity`. Full design, measured
acceptance results and the pickup list live in
`FEATURE-pose-instrument-play-freeze.md`.

| Script | Role |
|---|---|
| `PoseInstrument` | Playable graph, blend math, layer pool. **No MIDI knowledge.** |
| `PoseBank` (SO) | The pose set + per-pose overrides. Reusable across characters. |
| `KeyboardPoseDriver` | Keys 1-9 → poses. How the prototype is played without hardware. |
| `MidiFighterPoseDriver` | MF64 pads → poses, LED feedback. **Never run against hardware.** |

Same input-driver split as `PinDensityController` — the instrument owns the
state, dumb drivers feed it. That is what lets the blend math be verified with no
MF64 attached.

### Weight normalization is load-bearing

The mixer does **not** auto-normalize, and un-normalized humanoid weights pull
the pose toward rest. At trigger time every live layer's weight is captured as
its baseline; the newest rides the curve at `f` and the rest scale by `(1 - f)`,
so the sum stays at 1. **Do not "simplify" the `baseline * (1 - f)` fade.**

The first trigger of a session is the exception — baselines sum to 0, so the
total is only `f` for one blend. `primePoseIndex` snaps past it so the rig never
flashes a T-pose.

### Freeze vs Play — the asymmetry is deliberate

Freeze-vs-play is **captured at trigger time** (the mode you were in when the pad
fired governs that layer for life). `playbackRate` is **live** — it syncs to
already-playing layers, so a knob bends the tempo of the current motion without
retriggering. In Freeze mode the clip sits at `speed = 0` on a random frame, so
hammering one pad cycles frozen poses out of a single clip. That is the feature,
not a bug.

### Mixamo import — every default is wrong

Same shape as the artifact chain. The trap that costs the most time: clip FBXs
need **Copy From Other Avatar** pointed at the character's avatar, not Create
From This Model — the latter silently builds a different avatar and proportions
drift. Also required: Loop Time + Loop Pose, all three Root Transform channels
**Bake Into Pose** (or the dancer walks out of the room), and a clip rename off
the shared `mixamo.com` take name. `Animator.runtimeAnimatorController` must be
**None** — `AnimationPlayableOutput` takes the graph over.

### Performance — measured, and it is not what it looks like

Swept in a **build** (dev build, 1280x720, vsync off, RTX 3090) with
`DancerStressRig` + `mixamoDanceStress.unity`, one config per process:
`DancerStress.exe -dancers N -cameras N [-unbindhat]`.

| dancers | cells | dancers x cells | CPU | GPU | draws | tris | shadow casters |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 2 | 1 | 2 | 1.02 | 0.42 | 27 | 0.7M | 9 |
| 6 | 1 | 6 | 1.16 | 0.46 | 51 | 2.1M | 17 |
| 12 | 1 | 12 | 1.18 | 0.56 | 89 | 4.2M | 31 |
| 0 | 8 | 0 | 3.88 | 3.06 | 105 | 0.02M | 32 |
| **12** | **8** | **96** | **4.75** | **4.49** | 959 | 48.2M | 502 |
| 24 | 4 | 96 | 4.75 | 4.61 | 908 | 48.2M | 487 |
| 16 | 8 | 128 | 5.91 | 5.83 | 1,244 | 64.2M | 659 |
| 24 | 8 | 192 | 8.33 | 8.19 | 1,800 | 95.5M | 959 |
| 12 | 16 | 192 | 8.33 | 8.18 | 1,916 | 96.3M | 1,003 |

**Only the product `dancers x cells` matters.** 24x4 and 12x8 both cost 4.75 ms;
24x8 and 12x16 both cost 8.33 ms, to two decimals. Cost is linear in that product
at roughly **0.037 ms per dancer-cell** across the measured range. Budget by the
product, not the dancer count: if a reroll doubles the cell count, halve the
dancers.

**Three intuitions that the measurement killed:**

- **Skinning is not the cost.** `PlayerSettings.gpuSkinning = True`, so unbinding
  the 37k-vert single-bone Hat from its SkinnedMeshRenderer to a static
  MeshRenderer bought **0.19 ms GPU at the worst config and nothing on CPU**. Do
  not spend time on it. (Decimating the hat is still worth it — 64,584 triangles
  for a rigid prop, more than the body's 48,461, multiplied by dancers x cells.)
- **Animation and retargeting never appear.** 2 -> 12 dancers at one camera moved
  the main thread 1.02 -> 1.18 ms. The playable graphs, humanoid retargeting and
  per-frame `SetSpeed` writes are all noise. Do not micro-optimise `Update()`.
- **Dancers are nearly free; re-rendering them per cell is the entire cost.** The
  curve is driven by draw calls (105 -> 959) and shadow casters (32 -> 502).

**These numbers are pessimistic for the real rig.** The stress cameras were
`CopyFrom(main)`, so every cell cast and received shadows;
`SplitScreenCameraRig` disables sub-camera shadows and post by default, and
shadow casters are exactly what drives the curve.

**Budget.** Against the measured 9.17 ms full-scene baseline, 12 dancers at 8
cells lands near 10 ms (~100 fps). A 16.67 ms target leaves room for a product of
roughly 200 — 24 dancers at 8 cells, or 12 at 16.

### VFX Graph particles off the dancer — vertex colour is a dead end

`com.unity.visualeffectgraph` 17.3.0 with the Learning Templates is installed,
and a `skinnedMesh.vfx` sits on a `Visual Effect` object in the debug scene.

**Sample the albedo texture through UV, not the vertex colour stream.** The FBX
does carry a vertex-colour channel — 29,945 entries on the body — but every one
is `RGBA(0,0,0,0)`, only 2 distinct values across the whole mesh. Mixamo wrote
the array without data. Bind `Set Color` to it and every particle is black. Both
meshes do have full UV sets, so the working chain is:

```
Sample Skinned Mesh  (Surface mode, bind the BODY SkinnedMeshRenderer)
  ├─ position ──────────────────────► Set Position
  ├─ normal   ──────────────────────► Set Velocity   (push off the surface)
  └─ texCoord ──► Sample Texture2D ──► Set Color
                  (pinchushioned_DUG2_RED_AlbedoTransparency)
```

Bind `pinchusioned_ DUG`, **not** `Hat` — the hat is a separate renderer with its
own UVs into the same texture, so binding it gives hat-coloured particles off the
body. `isReadable = false` on the albedo is fine; that is a CPU-read flag and
this is a GPU sample. If `texCoord` reads back zero, set `isReadable = true` on
the FBX — the same switch every artifact already uses for the scatter bake.

Simulate in **World** space, or particles ride along with the dancer instead of
being left behind.

For mesh particles, **do not add `Orient: Face Camera`** — that is what makes
them tumble. An un-oriented cube stays world-axis aligned, so a `Set Scale` of
roughly `(0.25, 0.012, 0.012)` is horizontal by construction; randomise `Set
Angle` around Y only to vary them without losing that.

### Before this moves to the main scene

Pads are **row 1, columns 1-7** (column 8 stays with floor navigation). Those
seven row/col pairs must be added to `Extra Reserved Pads` on the
`Pincushioned Rig` prefab, or `MidiFighterInteriorSpawner` will also toggle an
interior object on every pose pad. Decide too whether the dancer is
`Always Active`: `FloorVisibilityController` will otherwise `SetActive(false)`
its floor, which stops `Update` mid-blend and unsubscribes the pads.

## Working on this project

### Verifying changes

Drive the **already-open editor** via the global `unity` CLI. Never batch mode
while the editor holds the project lock.

```bash
unity command recompile && unity command recompile_status   # CS errors surface here
unity command get_console_logs                              # or `console` for level filtering
```

Traps that cost real time:

- **Optional params need `--name <value>`.** As of CLI `1.0.0-beta.6` they *are*
  forwarded, but only in flag form — `unity command recompile focus=true` is
  rejected, `--focus true` works. (On `1.0.0-beta.3` optional args were dropped
  entirely; that is fixed.) Read schemas from `unity --json list` →
  `data.tools[].parameters`.
- **`build --scenes` silently falls back to Build Settings.** A single string is
  not the `string[]` the server wants, and the build proceeds with
  `EditorBuildSettings` instead of erroring — so you profile the wrong scene. Set
  `EditorBuildSettings.scenes` via `eval_file`, build, then restore it.
  `build` also requires `--confirm true`.
- **`recompile` does NOT refresh the AssetDatabase.** Auto Refresh is off here, so
  new files on disk are never imported — nothing compiles and no `.meta` appears.
  Use `eval_file` calling `AssetDatabase.Refresh(...)`, or **`write_text_file`,
  which writes *and* imports** (the right tool for creating scripts).
- **An unfocused editor does not tick**, so main-thread commands time out even
  though `unity status` says ready. Foreground the window first.
- **Console "Error Pause" will pause Play mode on a loop and look like broken
  input.** This project ships orphaned editor windows that log
  `Invalid editor window of type: ArtifactImportWindow` in bursts on repaint and
  domain reload. With Error Pause on, every one of those re-pauses Play mode
  instantly, so unpausing appears not to work and every `Update`-driven system
  goes dead at once — which reads as "the keyboard stopped responding", not as a
  console setting. Check `UnityEditor.LogEntries.consoleFlags` (bit 4 = ErrorPause)
  before debugging the component. `editor_status` reporting
  `"playMode":"paused"` is the tell. Note `ClearOnPlay` (bit 2) is also on here,
  so the Console looks innocent when you go to check it.
- **`PoseInstrument` and friends are not `[ExecuteAlways]`.** In edit mode their
  `Awake` never runs, so the PlayableGraph is null and `Trigger` throws
  "The Playable is null". That is expected, not a bug — confirm
  `EditorApplication.isPlaying` before concluding anything from a probe.
- **An unfocused *player* does not tick either.** A build launched from a script
  never gets focus, so it renders nothing and never reaches your logging — it
  just hangs until killed. Set `Application.runInBackground = true` in the
  harness. While profiling also set `QualitySettings.vSyncCount = 0`, or every
  config reads 16.67 ms and the sweep tells you nothing.

`eval_file` is effectively capped at 5 s of main-thread time — keep eval scripts
cheap. For screenshots, `capture_game_view` only renders one camera; use
`ScreenCapture.CaptureScreenshot` via `eval_file` to get the composited frame.

### Architectural rules

- **No bootstrapping.** `new GameObject(...).AddComponent<T>()` and
  `[RuntimeInitializeOnLoadMethod]` produce components whose Inspector fields can
  never be edited or persisted. Anything with artistic parameters belongs in the
  scene or on a prefab. The old `MidiSceneBootstrapper` was deleted for exactly
  this reason.
- **Seed anything random.** Use `System.Random` with a serialized seed, never
  `UnityEngine.Random` — that is a global shared stream anything else can perturb,
  so compositions are not reproducible. `MeshSurfaceScatter`,
  `MidiFighterInteriorSpawner`, `CloseUpCameraController` and the split-screen rig
  all follow this.
- **Router events are static.** Every `+=` needs its matching `-=` in `OnDisable`
  or destroyed objects keep receiving MIDI.
- **Never commit** `3DObjectProcessing/`'s API key files or its ~750 MB of derived
  scan data. Both are gitignored.

### Artifact inventory — 51 scans, three sources

| Source | Count | Licence | Notes |
|---|---:|---|---|
| Recovered originals (Ashmolean / Giza / misc) | 15 | mixed | Arrived pre-processed; no high-poly source survives |
| **Smithsonian** (2026-09-05) | 25 | **CC0** | Sculpture, instruments, paleo crania |
| **Sketchfab — NHM Wien** (2026-09-07) | 10 | **CC BY-NC** | Articulated animal skeletons |
| **Sketchfab — noe-3d.at** (2026-09-06) | 1 | **CC BY-NC** | `Flusspferd mit Jungem` |

**11 assets are NON-COMMERCIAL.** Every NHM Wien model plus the hippo is
CC BY-NC. Fine for personal and non-commercial exhibition; if pincushioned is
ever ticketed, commissioned or sold, all 11 have to come out. Provenance with
per-model attribution lives in `3DObjectProcessing/raw_scans/nhmwien/_provenance.json`
and `raw_scans/_provenance_sketchfab.json`.

The Smithsonian set is uniformly CC0 and carries no such restriction — but it has
essentially no articulated skeletons, which is exactly why the NHM Wien set was
worth the licence trade.

**`Diplodocus_carnegii` has a reconstructed base colour.** Its GLB ships **no
`baseColorTexture`** — only a near-black `baseColorFactor` and one image wired to
both metallicRoughness and occlusion (verified: R varies, G/B flat at 255, i.e. a
pure AO map). Imported literally it is a black silhouette. The AO map was promoted
to BaseColor with a warm bone tint. That is an artistic reconstruction, not source
data, and it is flagged as such in the provenance.

### Tooling decisions — evaluated and rejected

**Smithsonian `dpo-cook`** (`smithsonian.github.io/dpo-cook`, Apache-2.0,
actively maintained). A Node.js job server wrapping ~a dozen 3D tools. **Not
adopted.** Its free tool wrappers are the same ones `scan2unity` already drives
directly (Blender, Meshlab, ImageMagick, InstantMeshes, Meshfix, FBX2glTF,
XNormal); the rest are commercial (RapidCompact, RizomUV, Metashape,
RealityCapture). Critically it has **no URP channel-conversion stage**, which is
the entire reason `texture_ops.convert_to_urp` exists. Adopting it would add a
Node server and lose the one thing our pipeline is for. Its *recipes* remain
worth reading as reference for decimation and UV parameters, since they are
tuned on this exact class of museum scan — a reading task, not a dependency.

**Sketchfab's Unity plugin** (`github.com/sketchfab/unity-plugin`). **Not
adopted** — last commit 2022-06-08, predating Unity 6. It also imports straight
into the scene, bypassing the URP conversion and the scatter bake, so it would
produce a model that renders but cannot be pinned. Sketchfab's REST API is the
right integration point instead: search is anonymous (HTTP 200 without auth),
only *download* requires a token (401).

### Package Manager troubleshooting

If Package Manager reports it cannot reach the internet, **check the Unity
account session before suspecting the network.** All of this was verified
healthy on 2026-09-05 while PM was failing: `packages.unity.com` 200,
`registry.npmjs.org` 200, `registry.npmjs.com/jp.keijiro.minis` 200 (the
manifest's `.com` registry URL is valid and serves real metadata), `git` on PATH
for the git-URL dependencies, and `upm.log` showing **zero errors** with every
request returning 200. A Unity Hub update resolved it.
`%LOCALAPPDATA%/Unity/Editor/upm.log` is the place to look — not the editor
console.

**`Packages/Coplay/` is a log directory, not a broken package.** It holds
`Editor/CoplayLogs/*.log` and a `.gitignore`, with no `package.json` and no
`.cs`/`.dll` — the actual plugin resolves from its git URL. Because anything
directly under `Packages/` is scanned as an embedded package, this logs
`[WARN] Package folder [...Coplay] missing package manifest` on **every**
package operation. Noisy, but it does not break resolution.

Worth removing if unused: `com.coplaydev.coplay` is pinned to `#beta`, a
**floating branch** that can change on any resolve, while everything else here
is version- or tag-pinned. The CLI bridge is a separate package
(`com.unity.pipeline`) and is unaffected by removing it.

### Adding an artifact — the chain

Steps 3-6 used to have no tooling and were done by hand. They are now one
action: **Tools > Scatter > Import Processed Artifacts**
(`Editor/ArtifactImportWindow.cs`). Point it at a scan2unity output folder and
it produces finished, pinnable artifacts.

| # | Step | Tooling |
|---|---|---|
| 1 | Find + download GLB | **none** — see the pipeline's Smithsonian API section |
| 2 | `scan2unity` → FBX + URP textures | `python -m src raw_scans/ -o unity_assets/ --target-faces 50000` |
| 3 | Copy into `Assets/Artifacts/{stem}/` | **Import Processed Artifacts** |
| 4 | Fix import settings | **Import Processed Artifacts** |
| 5 | Create + assign `{stem}_Surface.mat` | **Import Processed Artifacts** |
| 6 | Bake + emit `{stem}_Pinned.prefab` | **Import Processed Artifacts** |

Verified on the full 25-model batch: **40/40 artifacts** ended with a `_Pinned`
prefab carrying sample data, a material and 6 pin variants, zero duplicates.
Each step is individually toggleable, so it doubles as a repair tool — re-run
with only "Build materials" ticked to rebuild every material, for instance.

Only **step 1 remains manual.** Downloading is still an ad-hoc script.

**Import settings — all four Unity defaults are wrong for this project**, and
the tool fixes each one:

| Asset | Default | Required | Why |
|---|---|---|---|
| FBX | `isReadable = false` | **true** | The scatter bake reads vertices/normals/UVs |
| `*_Normal.png` | type `Default` | **NormalMap** + `flipGreenChannel` | Pipeline emits OpenGL-convention normals |

**48 of 51 artifacts carry a normal map.** Most came from their source scans; 11
Egyptian pieces and 9 NHM Wien skeletons have one *derived from albedo* (see the
pipeline CLAUDE.md) because no high-poly source survives for them. The 3 without
cannot take one: Chandra and Odobenocetops have no albedo at all, and
sarcophagus_of_duaenre still has the corrupted checkerboard.
| `*_MetallicSmoothness.png` | `sRGB = true` | **false** | Data map, not colour — lights wrongly otherwise |
| `*_Occlusion.png` | `sRGB = true` | **false** | Same |

The importer also **warns when a `_BaseColor.png` is under 50 KB**, which is the
signature of the Stage 6 placeholder-checkerboard bug (see the pipeline's
CLAUDE.md). That guard exists because the bug shipped silently once already.

`_Scatter` vs `_Pinned`: `BatchScatterProcessorWindow` still outputs
`Assets/ScatterPrefabs/{stem}_Scatter.prefab` for ad-hoc use, but the scenes
consume `Assets/pinnedMeshes/{stem}_Pinned.prefab`, which the importer writes
directly — pin variants (`Assets/pins/Pin.fbx` × every `pinMaterial_*`) and the
house look (`RelativeToModel`, 0.03-0.20, bias 3, tilt 12) already applied.

### Verifying a batch

`PinDebugRig.RebuildGrid()` **early-returns when its `prefabs` list is empty** —
it does not self-populate, despite the tooltip. Population lives in
`PinDebugSceneBuilder.LoadPinnedPrefabs()`, behind the inspector button. Clear
the list and call `RebuildGrid()` and you get an empty scene.

`PinDebugRig.BuildProblemReport()` is the fastest correctness check — it names
any model with no pin variants, a missing mesh/material, or no sample data.

**Pins do not render while the editor is unfocused.** `MeshSurfaceScatter` draws
via `Graphics.RenderMeshInstanced` from `[ExecuteAlways] Update()`, and an
unfocused editor does not tick — so CLI screenshots of `PinDebug` show the
models bare. That is a capture artifact, not a broken scatter. Judge pins with
the editor focused.

### Where to pick up

Untested, in priority order:

1. **Hardware pass.** Nothing has run with the MF64, MIDI Mix or live audio
   connected. Verify the mapping table above, then pin density on Ch 1 Row 2.
   If a control feels dead, suspect a duplicate MIDI port before the code — the
   allow-list is `Fighter` / `MIDI Mix`.
2. **Split-screen profiling.** Raise cell count and watch the frame time.
3. **POI tuning.** It currently resolves to walls and floors because that is
   genuinely what the ray hits. To prefer sculptures, put the artifacts on their
   own layer and filter to it.
4. `Allosaurus_fragilis_maxilla-150k_Pinned 1` renders without pins — its
   `SurfaceSampleData` is unrecoverable. Repoint it at the surviving LOD0 asset
   or delete the duplicate. Same for the Stegosaurus and Triceratops `_Pinned 1`
   duplicates; those three are the only entries in `BuildProblemReport()`.
5. The MIDI Mix is nearly unused: 23 knobs, 9 faders, 24 buttons free.
6. **All 36 new artifacts are staged in `PinDebug`, not in the main scene.**
   Placing them on floors and binding MF64 pads is untouched. Remember the
   53-pad budget and that `MidiFighterInteriorSpawner` claims every unreserved
   pad. The grid is at 55 models.
7. **`sarcophagus_of_duaenre` has a checkerboard base colour** — corrupted by
   the Stage 6 bug in an earlier run, before that bug was found. Its source GLB
   is not in `raw_scans/`, so it must be re-fetched from wherever it came from.
8. **Automate step 1.** Steps 3-6 are now `ArtifactImportWindow`; only
   discovery and download remain ad-hoc. A `scan2unity acquire` verb driving the
   Smithsonian/Sketchfab adapters would close the last gap — both APIs are now
   fully characterised in the pipeline CLAUDE.md, so this is plumbing, not research.
9. **Bind the wave toggle pad.** `MidiFighterWaveToggle` sits at row 0 / col 0
   (unassigned). When you assign it, add that pad to `MidiFighterInteriorSpawner`'s
   Extra Reserved Pads or it will also toggle an interior object.
10. **Hardware-test the new audio mapping.** Amplitude now drives pin
    *configuration*, not pin count, and none of it has run against a live signal.
11. **Judge the chromatic displacement in motion.** It has only been evaluated on
    still frames; the height field slides as the camera moves, so the character
    changes. Fly the close-up camera before settling on a strength.
12. **`Flusspferd_mit_Jungem` deserves a real normal bake.** Its 593k-face source
    is still in `raw_scans/`, so a high-to-low poly bake would beat the derived
    normal it currently has.
9. **The pose instrument is prototype-only.** `Assets/mixamoDance/` is built and
   verified by measurement, but has never seen the MF64 and is not in the main
   scene. Hardware pass, then the pad reservation described above.

## Chromatic displacement post effect (`Assets/PostFX/`)

A Red Giant "Chromatic Displacement" style effect: the image's own luminance is
levelled, blurred, and read as a **height field**; pixels slide along its
**gradient** while the sample offset sweeps a spectrum. Built 2026-09-07.

| File | Role |
|---|---|
| `ChromaticDisplacementVolume.cs` | Volume parameters — levels, blur, strength, spread, tint |
| `ChromaticDisplacementFeature.cs` | `ScriptableRendererFeature` on `PC_Renderer` |
| `ChromaticDisplacementPass.cs` | Render Graph pass chain |
| `ChromaticDisplacement.shader` | 5 passes: DownsampleLevels, BlurH, BlurV, Displace, CopyBack |
| `ChromaticDisplacement.asset` | Volume profile, referenced by a global Volume in `PinDebug` |

**Gradient, not value.** Displacement follows the *slope* of the height field,
so it peaks at the **edges** of bright regions and is near zero in the middle of
a large flat bright area. That is what makes it read as refraction rather than a
smear, and it is the thing to remember when tuning: `Height Gamma` reshapes the
slope and is the most expressive dial.

**Levels run before the blur**, so contrast shaping produces smooth ramps.
Doing it after would re-sharpen what the blur just softened.

**Use `View Height Field` to tune.** It renders the blurred, levelled source
directly. Every problem during development was diagnosable from that view in
seconds and invisible from the final image.

### Measured cost (editor A/B, 2026-09-07)

| Condition | GPU delta | SetPass delta |
|---|---|---|
| 1 camera | **+0.02 ms** (inside the ±0.6 ms noise floor) | +5 |
| 32 split-screen cells, **all cameras** | **+9.26 ms GPU / +16.72 ms CPU** | +225 |

**Leave `All Cameras` OFF.** A prediction that proved wrong: post cost does *not*
stay constant as cells tile the screen, because a sub-camera with a viewport
rect still allocates **full-size** render targets. Cost scales with camera
count, not screen area. If the effect is ever wanted across a full mosaic, apply
it once to the composited result rather than per sub-camera.

### Four traps this cost real time

1. **Render Graph is mandatory in URP 17.** `Execute()` / `OnCameraSetup()` are
   obsolete and silently do nothing. Nearly every tutorial online predates this.
2. **Blit passes are RECORDED now and EXECUTED later against the shared
   material.** Setting a direction uniform per blur pass meant every pass used
   whichever value was written last — a vertical-only smear that looked like a
   blur bug. Bake direction into separate shader passes instead.
3. **Blur tap spacing must stay ~1 texel.** Scaling spacing with the radius
   produces nine discrete ghosts, not a blur. Radius comes from **iterations**
   (variance adds, so radius grows with sqrt(iterations)) and the downsample
   factor.
4. **`VolumeProfile.Add<T>()` is not enough** — the component must also be added
   as a sub-asset with `AssetDatabase.AddObjectToAsset`, or the profile silently
   reloads with zero components and the effect never runs.

Also: do not redeclare `_BlitTexture_TexelSize`, it comes from `Blit.hlsl`; and
`SetGlobalTexture` inside a raster pass needs
`builder.AllowGlobalStateModification(true)`.

Reference stills from Maxon's product page are in
`%TEMP%/cd_ref/` for look comparison.

## Subsystem Docs

Each major subsystem has its own CLAUDE.md with detailed docs:
- `Packages/com.caseyfarina.midifighter64/CLAUDE.md` — MIDI package API, event flow, grid/note layouts, LED palette, gotchas
- `Assets/proceduralPincushioning/CLAUDE.md` — GPU scatter system, bake pipeline, render pass
- `FEATURE-pose-instrument-play-freeze.md` — pose instrument design, Mixamo import chain, measured acceptance results
- `3DObjectProcessing/CLAUDE.md` — Python mesh processing, Smithsonian API, Blender bridge (source is tracked; its ~750 MB of scan output is gitignored)

## Conventions

- New standalone scripts preferred over bloating existing ones
- 1-based everywhere user-facing (row, col, channel); 0-based only in internal arrays
- Namespace: `MidiFighter64` (Runtime), `MidiFighter64.Samples` (Samples)
- Always add `using MidiFighter64;` explicitly in Samples files
- ScriptableObject + `[InitializeOnLoad]` + Resources.Load pattern for build-safe asset refs
