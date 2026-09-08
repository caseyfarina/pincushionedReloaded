# scan2unity — Claude Code Project Memory

## Project Overview
CLI batch pipeline converting 3D archaeological/art scans from public databases into Unity 6-ready assets. Handles mesh decimation, embedded texture extraction, URP PBR channel conversion, and format conversion.

## Target Environment
- **OS:** Windows 11
- **Hardware:** Intel i9, Nvidia RTX 5060 Laptop
- **Python:** 3.14 (3.10+ syntax throughout)
- **Unity:** 6 (6000.3.7f1), URP
- **Blender:** 5.0 (headless — at `C:/Program Files/Blender Foundation/Blender 5.0/blender.exe`)
- **Smithsonian API key:** stored in `govDataApiKey.txt` (gitignored)

---

## Project Structure

```
3DObjectProcessing/
├── CLAUDE.md
├── README.md
├── pyproject.toml          scan2unity = "src.cli:main"
├── src/
│   ├── __init__.py
│   ├── __main__.py         enables `python -m src`
│   ├── cli.py              argparse entry point → batch_process()
│   ├── pipeline.py         per-mesh orchestration (all stages)
│   ├── mesh_ops.py         PyMeshLab load / clean / decimate / save_obj
│   ├── texture_ops.py      Pillow resize + URP PBR channel conversion
│   ├── blender_bridge.py   Blender headless: convert() + decimate()
│   ├── formats.py          format detection, discover_meshes, discover_textures
│   └── config.py           PipelineConfig dataclass + defaults
├── scripts/
│   └── scan2unity.bat      Windows launcher: `python -m src %*`
├── tests/
│   ├── test_mesh_ops.py
│   ├── test_texture_ops.py
│   └── test_formats.py
├── raw_scans/              downloaded source GLBs (gitignored)
└── unity_assets/           pipeline output (gitignored)
```

---

## Pipeline Stages

```
GLB/FBX/OBJ/PLY/...
       │
Stage 0: Blender pre-convert (glTF/GLB/FBX/3DS → OBJ)
         + extract embedded textures → {stem}_{role}.png
       │
Stage 1: PyMeshLab load
       │
Stage 2: PyMeshLab clean (dupes, unreferenced, folded faces)
       │
Stage 3: URP texture conversion (if embedded textures extracted)
         OR discover_textures fallback (for formats with external textures)
       │
Stage 4: Decimate per LOD
         → PyMeshLab texture-aware QEC (best, requires clean UVs)
         → PyMeshLab basic QEC (fallback)
         → Blender DECIMATE modifier (fallback when PyMeshLab can't reduce)
       │
Stage 5: Save OBJ intermediate
       │
Stage 6: Blender OBJ → FBX (with embedded textures)
```

---

## Key API Details

### `blender_bridge.convert()`
```python
def convert(blender_exe, input_file, output_file, timeout=300,
            extract_textures=True) -> tuple[bool, dict[str, str]]:
    """Returns (success, extracted_textures) where extracted_textures maps
    role -> filepath. Roles: 'basecolor', 'normal', 'orm', 'roughness',
    'metallic', 'unknown'. Textures are saved as {stem}_{role}.png
    alongside the output file during GLB import.

    extract_textures=False skips extraction entirely. Stage 6 (OBJ->FBX) MUST
    pass False whenever Stage 0/3 already produced maps, or Blender's
    placeholder for the dummy.png MTL reference overwrites the real
    {stem}_BaseColor.png on Windows. See the Stage 6 section below."""
```

**Important:** return type changed from `bool` to `tuple[bool, dict]`. Always unpack:
```python
ok, extracted_textures = blender_convert(blender_exe, input_path, temp_obj)
```

### `blender_bridge.decimate()`
```python
def decimate(blender_exe, input_obj, output_obj, target_faces, timeout=300) -> bool:
    """Decimate using Blender's DECIMATE modifier. Used as fallback when
    PyMeshLab cannot reduce face count (e.g. Triceratops Smithsonian model).
    Both input and output are OBJ files."""
```

### `texture_ops.convert_to_urp()`
```python
def convert_to_urp(extracted, output_dir, stem, max_size=2048, fmt="png") -> dict[str, Path]:
    """Convert Blender-extracted PBR maps to Unity URP format.

    Input roles → Output files:
      basecolor  → {stem}_BaseColor.png       (sRGB, resize only)
      normal     → {stem}_Normal.png          (OpenGL tangent-space; enable
                                               Flip Green Channel in Unity)
      orm        → {stem}_MetallicSmoothness.png  R=Metallic(B), A=Smoothness(1-G)
                 → {stem}_Occlusion.png       R=AO(R channel of ORM)
      roughness  → {stem}_MetallicSmoothness.png  (with metallic if present)
    """
```

**GLTF ORM channel convention:** R=AO, G=Roughness, B=Metallic
**Unity URP MetallicGlossMap:** R=Metallic, A=Smoothness (= 1 − Roughness)

---

## Texture Role Detection

Blender's material node graph is inspected after import. Role is inferred from which socket the TEX_IMAGE node feeds into:

| Destination | Role assigned |
|---|---|
| Principled BSDF.Base Color | `basecolor` |
| Normal Map node | `normal` |
| SEPCOLOR node | `orm` |
| Principled BSDF.Roughness | `roughness` |
| Principled BSDF.Metallic | `metallic` |
| Anything else | `unknown` |

`unknown` textures are saved but not converted. If a third map comes back as `unknown`, check the node graph — it may be an ORM with an unusual wiring (e.g. Smithsonian models sometimes use a Separate Color node differently).

---

## Decimation Fallback Chain

PyMeshLab texture-aware QEC is preferred (best UV preservation). If that fails or doesn't actually reduce the face count (checked at `dec_stats.faces > target * 1.05`), Blender's DECIMATE modifier is used as final fallback. This fixed the Triceratops Smithsonian model which PyMeshLab's basic decimation could not reduce.

---

## Smithsonian 3D API

Base URL: `https://api.si.edu/openaccess/api/v1.0/`
Key: `govDataApiKey.txt` (gitignored). Free from `api.data.gov/signup`.
`DEMO_KEY` works for development (~30 req/hour, 50/day) if the file is missing.

**Verified against the live API on 2026-09-05.** The three bullets that used to
live here were all wrong; do not restore them from memory.

### The media type is `3d_voyager`

`3d_package` is only the **ID prefix**, never the media type. Searching for
`online_media_type:"3d_package"` returns zero rows.

### There is no server-side 3D filter

Both documented filters are silently ignored:

| Attempt | Result |
|---|---|
| `online_media_type=3D` | Ignored — returns plain text matches with no 3D media |
| `fq=online_media_type:"3d_voyager"` | **Ignored entirely** — returns all 14,517,752 records |
| `fq=unit_code:NMNHPALEO` | Ignored — same 14.5M |

So you **cannot** filter server-side. Filter client-side on
`content.descriptiveNonRepeating.online_media.media[].type == '3d_voyager'`.

### `q=3d_package` is the whole catalog

A free-text query for the literal string hits the media `id` field:

```
GET /search?q=3d_package&rows=100&start={0..1300}&api_key={key}
```

**1,378 records, 100/100 precision, ~14 requests.** That is the complete
Smithsonian 3D holding — small enough to harvest once and cache locally.
Topical queries are useless for discovery: 3D is such a thin slice of 14.5M
records that `q=dinosaur fossil` returned **zero** 3D rows in its first 100.

Catalog shape (harvested 2026-09-05):

| | |
|---|---|
| Total 3D objects | **1,378 — all CC0, no exceptions** |
| With a plain non-Draco GLB | **756** |
| Units | NMNHINV 616 · NMNHMAMMALS 569 · NMNHPALEO 111 · NMAAHC 22 · NMAH 18 · OCIO_DPO3D 10 · SAAM/NPG/CHNDM/NMAA |

**Attribution tracking is therefore a Sketchfab-only concern.** Every
Smithsonian record is CC0.

### Filenames must come from the API

Resource filenames are **not derivable** from the object name. The old
documented pattern `{name}-150k-4096_std.glb` 404s; the real Triceratops file is
`Triceratops_horridus_Marsh_1889-150k-4096.glb`. Read `resources[]` off the
media record and pick by `attributes[0]`:

```
MODEL_FILE_TYPE == "glb" and not DRACO_COMPRESSED
```

A typical object offers full-res OBJ, 150k OBJ, glTF, plain GLB, USDZ, and a
Draco GLB. Prefer plain GLB — Draco may not import in Blender.

License is per-media at `media[].usage.access` (e.g. `"CC0"`), so no extra
lookup is needed for a license gate.

### Two collections that look useful and are not

- **NMNHMAMMALS (569)** — not skeletons. 563 are *individual* primate foot and
  hand bones as untextured `.ply` (`USNM143586_femur_right`, `cuneiform2_left`).
  Morphometric research scans: no textures, tiny fragments. Only 1 has a GLB.
- **NMNHINV (616)** — taxonomic type specimens; millimetre-scale invertebrates.

Complete articulated skeletons are essentially **absent** from this catalog.
NMNHPALEO is overwhelmingly isolated skulls and elements. NMAH's 3D holdings are
mostly textiles and sports memorabilia, not instruments.

### Batch downloaded 2026-09-05 — 25 specimens

Sculpture (SAAM/NPG/NMAA), technological instruments (NMAH/OCIO_DPO3D), and
paleo crania (NMNHPALEO). 213 MB, all CC0. Provenance for every one — source
URL, package UUID, unit, licence, attribution string, fetch date — is written to
`raw_scans/_provenance.json` by the download step and is the archival record.

Two are source-side limited: `Chandra_X_ray_Observatory` has **no textures and
no UVs** (Uniform/Noise/Attractor density only), and
`Odobenocetops_peruvianus` has a normal map but **no BaseColor**.


---

## Provenance — tracked, unlike everything else it describes

`3DObjectProcessing/provenance/` is **in git**. `raw_scans/` and
`unity_assets*/` are not.

The meshes are derived data, re-fetchable from the APIs — correctly gitignored.
The provenance records are neither: ~24 KB of text against ~1.4 GB of binaries,
and the only account of where each artifact came from and what may be done with
it. If a museum delists or relicenses a model, the mesh becomes unfetchable
*and* the record of the terms would be gone with it.

| File | Source |
|---|---|
| `provenance/smithsonian.json` | 25 specimens, uniformly CC0 |
| `provenance/sketchfab-nhmwien.json` | 10 skeletons, CC BY-NC |
| `provenance/sketchfab-noe3d.json` | the hippo, CC BY-NC |
| `provenance/ATTRIBUTION.md` | generated credits, do not hand-edit |

**Any acquisition run must write a record here.** For CC-BY or CC-BY-NC material
this file is the only thing that says who must be credited and whether the piece
may be shown commercially. An artifact with no record should be treated as
unusable until its licence is established. `provenance/README.md` carries the
full maintenance procedure.

Note the filenames deliberately avoid a leading underscore: `.gitignore` excludes
`3DObjectProcessing/_*.json` as API scratch, and these are the opposite of scratch.

**The 15 recovered artifacts have no provenance at all.** They arrived
pre-processed from the laptop and their licensing has never been established.

---

## Sketchfab API

Token: `sketchfabToken.txt` (gitignored). Free from a Sketchfab account under
Settings > Password & API.

**Discovery needs no credentials.** Verified 2026-09-06/07:

| Operation | Auth |
|---|---|
| `GET /v3/search?type=models` | **none** (HTTP 200) |
| `license=cc0` / `license=by` filter | **none** — and it actually works, unlike the Smithsonian's ignored filters |
| `archives` block (size, faceCount, textureCount, textureMaxResolution) | **none** |
| `GET /v3/models/{uid}` (licence detail) | **none** |
| `GET /v3/models/{uid}/download` | **token required** (401 without) |

So an adapter can search, filter by licence, and reject models on poly count or
texture size **before spending a byte**. Only the bytes need the token.

### textureCount predicts whether a model has a normal map

`archives.glb.textureCount` is readable anonymously and tells you what you are
getting. Sampled across 96 downloadable museum-ish models:

| Textures | Share | Meaning |
|---|---:|---|
| 0-1 | **52%** | albedo only, no normal map |
| 2 | 17% | usually albedo + normal |
| 3+ | **31%** | normal + ORM/metallic-roughness |

Filter on `textureCount >= 2` when you want real surface relief instead of a
derived one. Photogrammetry rigs commonly bake lighting into albedo and ship a
single texture.

### The API cannot choose texture resolution

The download endpoint returns exactly four flavours — `source`, `gltf`, `usdz`,
`glb` — with **no size variants**. The web UI's 8k / 4k / 1k GLB buttons are
**web-only**; the API's `glb` is the smallest (1k). To get high-res textures,
take the **`gltf`** flavour (a zip carrying the original full-size maps) and let
`--texture-size` downsize. On `Flusspferd mit Jungem` that meant a 119 MB gltf
with an 8192px master resized to 4096 — a better result than the 4k GLB, since
the downsample happens once from the master.

The `gltf` flavour is a **zip** (`scene.gltf` + `scene.bin` + `textures/`), so it
needs extracting before the pipeline sees it. `.gltf` with external buffers goes
through Stage 0 normally.

### Naturhistorisches Museum Wien (`sketchfab.com/NHMWien`)

**1,011 models, 980 downloadable** — far more than the download icons on the
profile page suggest. The single richest source of **articulated animal
skeletons** found so far, which the Smithsonian catalog essentially lacks.

Finding them: names are inconsistent ("Giant Deer" is a mounted skeleton,
"Terror Bird" is a fleshed reconstruction), so filter descriptions and tags on
`skelett|skeleton|schädel|skull|cranium` — the museum is Austrian, so include the
German. A broader `fossil` match is useless: it pulls in plants, corals and rocks.
Verify visually from `thumbnails` before committing to a download.

**Everything is CC BY-NC.** 14 of 14 randomly sampled downloadables came back
`by-nc`, so this is museum-wide policy, not a per-model quirk. There is no CC-BY
subset to prefer. See the licence warning in the root CLAUDE.md.

Batch downloaded 2026-09-07 — 10 skeletons, 255 MB, processed in 57 s:
Giant Deer, Plateosaurus, Saber-Toothed Cat, Hoe Tusker, Protoceratops,
Diplodocus carnegii (1.0M faces), Psittacosaurus, South Island Giant Moa,
Gharial Skull, Primeval Horse. Only Plateosaurus shipped a real normal map; the
other nine have derived ones.

---

## Running the Pipeline

```bash
cd 3DObjectProcessing

# Full run with LODs
python -m src raw_scans/ -o unity_assets/ --target-faces 50000 --lods 3 \
    --blender-path "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"

# Single file, no FBX (faster, no Blender needed for output)
python -m src raw_scans/artifact.glb --skip-fbx

# Skip texture resize (keep originals)
python -m src raw_scans/ --skip-texture-resize
```

---

## Supported Input Formats
- **Direct (PyMeshLab):** OBJ, PLY, STL, OFF, DAE, PTS/XYZ
- **Via Blender bridge:** glTF, GLB, FBX, 3DS

---

## Coding Conventions
- Python 3.10+ — type hints everywhere, match statements OK
- Modules independently testable
- Never crash a batch on a single file failure — catch, log, continue
- Log to stderr via `logging`; JSON report written to `output_dir/_processing_report.json`
- `blender_bridge` functions write a temp `_blender_*.py` script, run subprocess, delete script in `finally`

---

## Stage 6 destroyed base color textures — fixed 2026-09-05

**This silently replaced real albedo with a grey UV checkerboard on 17 of 25
models, and had already corrupted `sarcophagus_of_duaenre` in an earlier run.**
The entry that used to sit in the TODO list below called it "harmless but
wasteful." It was neither.

The chain:

1. Stage 5 saves the OBJ via PyMeshLab, which writes an MTL referencing
   **`map_Kd dummy.png`** — the real texture reference is discarded here.
2. Stage 6 re-imports that OBJ in Blender, which invents a placeholder image
   for the unresolved `dummy.png`.
3. The extraction loop writes it as `{stem}_basecolor.png`, which on Windows
   **collides case-insensitively** with the URP `{stem}_BaseColor.png` from
   Stage 3 — replacing a 5 MB scan texture with an 18 KB checker.

Only bites when `num_lods == 1` (empty suffix). With LODs it merely litters.

**Fix:** `blender_bridge.convert()` takes `extract_textures: bool = True`,
forwarded to the Blender script as `argv[3]`. `pipeline.py` Stage 6 passes
`extract_textures=not tex_map` — re-extract only when nothing upstream produced
textures.

**Detection:** all placeholders are byte-identical. MD5 the `*_BaseColor.png`
files; any duplicate group is this bug.

```bash
python -c "import hashlib,glob,collections;h=collections.defaultdict(list)
[h[hashlib.md5(open(p,'rb').read()).hexdigest()].append(p) for p in glob.glob('unity_assets/*/*_BaseColor.png')]
print([v for v in h.values() if len(v)>1] or 'all unique')"
```

**Related, still open:** `discover_textures()` (`formats.py:60`) only looks in
the source directory and **does not parse the MTL**, so any OBJ/DAE/PLY whose
textures live in a subfolder loses them entirely. Combined with the Stage 5
`dummy.png` rewrite above, texture references never survive the OBJ round-trip.
Inert for GLB sources (Stage 0/3 handle those); it only matters for non-GLB
input. Verified by fixture test, not theoretical.

## Blender subprocess decoding — fixed 2026-09-05

`subprocess.run(..., text=True)` decoded Blender's output as Windows **cp1252**,
which crashed on Blender 5.0's output (`0x8f`) inside the reader thread. That
left `stdout` as `None` and failed with the misleading
`'NoneType' object has no attribute 'splitlines'`. Both call sites now pass
`encoding="utf-8", errors="replace"`.

---

## Deriving a normal map from albedo

`src/normal_from_albedo.py`, enabled with `--normal-from-albedo`. Writes
`{stem}_Normal.png` **only when none exists**, so a real normal map is never
overwritten.

```bash
python -m src raw_scans/ -o unity_assets/ --normal-from-albedo     --normal-strength 3.0 --normal-highpass 12
```

**This is a heuristic and a fallback.** Prefer a high-to-low poly bake whenever
the source mesh survives — `raw_scans/` keeps them, so that is usually possible
for anything this pipeline downloaded. Use this only when no high-poly source
exists (e.g. the Egyptian pieces, which arrived already processed).

How it works: Rec.709 luminance as height, optional pre-smooth, a **high-pass**
that subtracts a blurred copy to strip broad colour variation, then central
differences to slopes. Output is OpenGL convention (+Y up), matching the rest of
the pipeline, so Unity needs Flip Green Channel — which `ArtifactImportWindow`
sets automatically.

### Measured on `statue_of_ptolemy_iv_or_v`

| Setting | Mean render diff | Verdict |
|---|---|---|
| `strength 1, hp 24` | 0.27/255 | Invisible — normal map std only 2.8 |
| **`strength 3, hp 12`** | 0.74/255 | **Default.** Carving gains definition, plinth stays clean |
| `strength 8, hp 6` | 1.67/255 | Too far — see failure mode |

**The failure mode is flat man-made surfaces.** At strength 8 the statue's white
plinth went crumpled and its painted "1" label embossed, because that albedo
variation is dirt and labels, not geometry. Sharp albedo edges also threw false
specular streaks. Carved organic stone improves; flat surfaces degrade, and most
museum pieces contain both in one texture. Hence the conservative default.

**Best case is shallow carved relief** — the `lintel_of_the_scribe` hieroglyphs
read as properly incised carving, because decimation to 50k destroyed the real
relief while the albedo retained it through baked occlusion in the incisions.

### Guards that matter

Do not run this over an albedo that is a **placeholder checkerboard** (see the
Stage 6 section) — it embosses a grid of fake relief. `sarcophagus_of_duaenre`
is currently in that state and must be skipped. Also skip models with **no UVs**
(`Chandra_X_ray_Observatory`), since a normal map cannot be sampled without them.

Applied 2026-09-06 to 11 artifacts at `strength 3 / hp 12`; **38 of 41 artifacts
now carry a normal map.** The three without are Chandra and Odobenocetops (no
albedo at all) and sarcophagus_of_duaenre (corrupted albedo).

---

## Known Limitations / TODOs
- `unknown` texture role for some Smithsonian ORM maps — node graph detection needs refinement for non-standard wiring
- `discover_textures` does not parse MTL `map_Kd` paths (see Stage 6 section above)
- Role collision: the extraction loop writes `{stem}_{role}.png`, so two images resolving to the same role overwrite each other, last one wins. Not observed in practice; flagged during review, not confirmed exploitable.
- A standalone `occlusion` role is extracted but **not** URP-converted, so it stays as `_temp_{stem}_occlusion.png`
- Temp cleanup misses `_temp_{stem}_occlusion.png` and `_temp_{stem}_emissive.png`
- `dummy.png` (~6 KB) is left in the asset dir — PyMeshLab's placeholder MTL reference
- `.obj`/`.mtl` intermediates are not cleaned up (~7.5 MB per model)
- The role-classification block still runs when `extract_textures=False`; its output is unused
- E57 (LiDAR) needs CloudCompare CLI pre-stage
- No GPU-accelerated texture resize (`texconv` from DirectXTex would use the GPU)
- No Smithsonian or Sketchfab batch download scripts yet — the 25-model run on 2026-09-05 was driven by an ad-hoc script, not a pipeline verb
- No vertex color → texture bake for PLY files without UVs
- Normal maps output in OpenGL convention — Unity import requires "Flip Green Channel" enabled manually (or could automate channel flip in convert_to_urp)

---

## Testing

```bash
pip install pytest pymeshlab Pillow
pytest                          # runs all tests
pytest tests/test_formats.py    # single module
```

Blender bridge tests require Blender on PATH (`pytest.mark.skipif` guards).
