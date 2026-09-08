"""
Derive a tangent-space normal map from an albedo texture.

This is a HEURISTIC, used only when a model ships no real normal map and no
high-poly source survives to bake one from. It treats image luminance as
height, which for photogrammetry is partly principled -- captured albedo has
ambient occlusion baked in, so crevices genuinely are darker -- but it cannot
tell pigment from geometry. A dark paint trace or mineral stain becomes phantom
relief.

The high-pass stage is what makes it usable on museum stone: subtracting a
heavily blurred copy removes broad colour variation (a discoloured half of a
statue, a stained patch) and keeps only fine detail, which correlates far
better with real surface relief.

Output convention is OpenGL (+Y up), matching the rest of this pipeline, so
Unity must import it with "Flip Green Channel" enabled -- which
ArtifactImportWindow does automatically.

Prefer a real high-to-low poly bake whenever the source mesh still exists.
This module is the fallback, not the first choice.
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

# Rec.709 luma weights: matches perceived brightness better than a flat mean,
# so coloured-but-equally-bright regions do not read as height changes.
_LUMA = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def _luminance(img: Image.Image) -> np.ndarray:
    arr = np.asarray(img.convert("RGB"), dtype=np.float32) / 255.0
    return arr @ _LUMA


def generate(
    albedo_path: str | Path,
    out_path: str | Path,
    strength: float = 1.0,
    highpass_radius: float = 24.0,
    presmooth: float = 0.5,
) -> Path:
    """
    Write an OpenGL-convention normal map derived from `albedo_path`.

    strength         Slope multiplier. 0.5 subtle, 1.0 moderate, 2.0+ pronounced.
    highpass_radius  Blur radius (px) subtracted to kill broad colour shifts.
                     0 disables, which is rarely what you want on stone.
    presmooth        Small blur applied first to stop texture noise becoming
                     a field of pits. 0 disables.
    """
    albedo_path = Path(albedo_path)
    out_path = Path(out_path)

    img = Image.open(albedo_path)
    if presmooth > 0:
        img = img.filter(ImageFilter.GaussianBlur(presmooth))

    height = _luminance(img)

    if highpass_radius > 0:
        low = np.asarray(
            Image.fromarray((height * 255).astype(np.uint8))
            .filter(ImageFilter.GaussianBlur(highpass_radius)),
            dtype=np.float32,
        ) / 255.0
        height = height - low + 0.5

    # Central differences, wrapping at the edges. Wrapping is not strictly
    # correct across UV island borders, but it avoids a hard seam artefact and
    # islands are a small fraction of the texel count.
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5

    # Image rows increase downward, so world-up is -y_image; that sign flip is
    # why +dy (not -dy) gives OpenGL's +Y-up green channel.
    nx = -dx * strength
    ny = dy * strength
    nz = np.ones_like(height)

    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    nx, ny, nz = nx / length, ny / length, nz / length

    rgb = np.stack(
        [(nx * 0.5 + 0.5), (ny * 0.5 + 0.5), (nz * 0.5 + 0.5)], axis=-1
    )
    out = (np.clip(rgb, 0.0, 1.0) * 255.0).astype(np.uint8)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(out, mode="RGB").save(out_path)
    return out_path


def generate_if_missing(
    asset_dir: str | Path,
    stem: str,
    strength: float = 1.0,
    highpass_radius: float = 24.0,
) -> Path | None:
    """
    Create `{stem}_Normal.png` from `{stem}_BaseColor.png` only when no normal
    map already exists. Returns the path written, or None if nothing was done.
    """
    asset_dir = Path(asset_dir)
    normal = asset_dir / f"{stem}_Normal.png"
    if normal.exists():
        return None

    albedo = asset_dir / f"{stem}_BaseColor.png"
    if not albedo.exists():
        return None

    return generate(albedo, normal, strength=strength, highpass_radius=highpass_radius)
