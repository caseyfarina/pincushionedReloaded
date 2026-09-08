# Provenance — the archival record

**This folder is tracked in git. The scans it describes are not.**

`raw_scans/` and `unity_assets*/` are gitignored because the meshes and textures
are derived data, re-fetchable from the source APIs. These records are neither.
They are ~24 KB of plain text against ~1.4 GB of binaries, and they are the only
account of where each artifact came from and what this project is permitted to
do with it.

If a museum delists or relicenses a model, the mesh can no longer be re-fetched
*and* the record of the terms it was obtained under is gone too. That is why
this is the one part of the download output that lives in version control.

## MAINTENANCE — read this before adding artifacts

**Every new download must be recorded here.** A record is not optional
bookkeeping; for anything under a CC-BY or CC-BY-NC licence it is the only thing
that tells you who must be credited and whether the piece may be shown
commercially.

After any acquisition run:

1. Write or extend the JSON for that source (one file per source).
2. Regenerate `ATTRIBUTION.md` — see below.
3. If the new material is **non-commercial**, say so in the root `CLAUDE.md`
   artifact inventory as well. That count is quoted in several places.

**A downloaded artifact with no record here should be treated as unusable**
until its licence is established. Guessing is not acceptable for work shown in
public.

## Files

| File | Source |
|---|---|
| `smithsonian.json` | Smithsonian Open Access — 25 specimens, uniformly CC0 |
| `sketchfab-nhmwien.json` | Naturhistorisches Museum Wien — 10 skeletons, CC BY-NC |
| `sketchfab-noe3d.json` | noe-3d.at — `Flusspferd mit Jungem`, CC BY-NC |
| `ATTRIBUTION.md` | **Generated.** Human-readable credits; do not hand-edit |

## Record shape

```json
{
  "stem": "Giant_Deer",
  "title": "Giant Deer (NHMW-GEO-1876/0030/0003)",
  "source": "Sketchfab / Naturhistorisches Museum Wien",
  "model_uid": "7ef4a780764b4784ac0c928e90fcd18d",
  "model_url": "https://sketchfab.com/3d-models/...",
  "author": "Naturhistorisches Museum Wien (NHMWien)",
  "author_url": "https://sketchfab.com/NHMWien",
  "license": "CC-BY-NC-4.0",
  "commercial_use": false,
  "attribution_required": true,
  "attribution": "\"Giant Deer ...\" by Naturhistorisches Museum Wien, licensed CC BY-NC 4.0",
  "original_faces": 250000,
  "fetched": "2026-09-07",
  "normal_map": "derived from albedo (strength 3, highpass 12) - source had none"
}
```

`commercial_use` and `attribution_required` are the two fields that carry
obligations. `normal_map` and `texture_note` record **modifications made by this
pipeline** rather than facts about the source — e.g. `Diplodocus_carnegii`'s
base colour is a reconstruction from its occlusion map, not source data.

## Regenerating ATTRIBUTION.md

```bash
cd 3DObjectProcessing/provenance
python - <<'EOF'
import json, glob, collections
recs = []
for f in sorted(glob.glob("*.json")):
    recs += [dict(r, _file=f) for r in json.load(open(f, encoding="utf-8"))]
needs  = [r for r in recs if r.get("attribution_required")]
noncom = [r for r in recs if r.get("commercial_use") is False]
print(f"{len(recs)} records, {len(needs)} need attribution, {len(noncom)} non-commercial")
EOF
```

The full generator lives in the session that produced it; the counts above are
the check that matters. If `noncom` is greater than zero, the root `CLAUDE.md`
inventory must say so.

## Current state (2026-09-08)

36 downloaded artifacts on record — 25 CC0, **11 CC BY-NC**.

The other 15 artifacts in `Assets/Artifacts/` predate this system. They were
recovered from a laptop already processed, and **their provenance is unknown**.
They are Ashmolean/Giza-class Egyptian material whose licensing has never been
established. Worth resolving before any public showing.
