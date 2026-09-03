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

**Nothing here has been run with the MIDI hardware or live audio connected.**
That is the remaining open risk. At 35 ms CPU with 8 split-screen cells the frame
is still CPU-bound on per-camera work, so cell count is the knob to watch.

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
  ScatterData/                           Baked SurfaceSampleData assets (16)
  ScatterPrefabs/                        Batch-tool output prefabs (16)
  pinnedMeshes/                          *_Pinned prefabs used by the main scene
  Prefabs/Pincushioned Rig.prefab        All 8 app controllers, Inspector-configured
  SplitScreen/                           Mosaic split-screen camera system
  Artifacts/                             15 Smithsonian scans + materials
  buildingFloor.fbx                      The room shell each floor is built from
  Plugins/Demigiant/DOTween/             DOTween Pro (DLL, no asmdef, globally accessible)
Packages/manifest.json                   com.caseyfarina.midifighter64 (git URL, pinned tag)
3DObjectProcessing/                      Python scan2unity pipeline (source only; data gitignored)
```

## Content recovery — resolved

The art assets missing from `pincushionedSpring2026` were recovered from the
laptop project on 2026-08-30 and are now tracked here:
`Assets/Artifacts/` (15 Smithsonian scans + materials), `Assets/buildingFloor.fbx`
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
| **Ch 1, Row 2 knob** | Pin density ceiling, 0-2000 pins | `MidiMixPinDensityDriver` |
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
| `PinDensityController` | **Single owner** of scatter count. Applies `Ceiling01 × Envelope01`. |
| `MidiMixPinDensityDriver` | MIDI Mix Ch1/Row2 knob → `Ceiling01` (performer sets intensity) |
| `AudioPinDensityDriver` | LASP level → `Envelope01` (music drives motion) |

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

### Performance — the untested risk

**Each cell is a full scene render.** Fragment cost is bounded by cell size, but
per-camera culling and the post-processing stack are **not** and do not shrink.
Sub-camera post-processing and shadows are **off by default** for this reason;
they are the first knobs to reach for. This has never been profiled under load —
with the pins, 40 lights and shadow-atlas pressure, high cell counts may not be
viable. Measure before trusting it live.

### Reroll actions

Layout and placement carry **separate seeds**, which is what makes these
independent:

| Method | Changes | Leaves alone |
|---|---|---|
| `ResetCameraPositions()` | camera angles | cell layout |
| `RerollLayout()` | cell layout | placement seed |
| `RerollAll()` | both | — |

## Working on this project

### Verifying changes

Drive the **already-open editor** via the global `unity` CLI. Never batch mode
while the editor holds the project lock.

```bash
unity command recompile && unity command recompile_status   # CS errors surface here
unity command get_console_logs                              # or `console` for level filtering
```

Three traps that cost real time, all confirmed on CLI `1.0.0-beta.3`:

- **Only *required* command params are forwarded.** Args are positional in schema
  order; optional ones silently do nothing. `create_gameobject "Name"` still makes
  "New Game Object", and `eval_file <path> 120000` still times out at the 5000 ms
  default. Read schemas from `unity --json list` → `data.tools[].parameters`.
- **`recompile` does NOT refresh the AssetDatabase.** Auto Refresh is off here, so
  new files on disk are never imported — nothing compiles and no `.meta` appears.
  Use `eval_file` calling `AssetDatabase.Refresh(...)`, or **`write_text_file`,
  which writes *and* imports** (the right tool for creating scripts).
- **An unfocused editor does not tick**, so main-thread commands time out even
  though `unity status` says ready. Foreground the window first.

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
   or delete the duplicate.
5. The MIDI Mix is nearly unused: 23 knobs, 9 faders, 24 buttons free.

## Subsystem Docs

Each major subsystem has its own CLAUDE.md with detailed docs:
- `Packages/com.caseyfarina.midifighter64/CLAUDE.md` — MIDI package API, event flow, grid/note layouts, LED palette, gotchas
- `Assets/proceduralPincushioning/CLAUDE.md` — GPU scatter system, bake pipeline, render pass
- `3DObjectProcessing/CLAUDE.md` — Python mesh processing, Smithsonian API, Blender bridge (source is tracked; its ~750 MB of scan output is gitignored)

## Conventions

- New standalone scripts preferred over bloating existing ones
- 1-based everywhere user-facing (row, col, channel); 0-based only in internal arrays
- Namespace: `MidiFighter64` (Runtime), `MidiFighter64.Samples` (Samples)
- Always add `using MidiFighter64;` explicitly in Samples files
- ScriptableObject + `[InitializeOnLoad]` + Resources.Load pattern for build-safe asset refs
