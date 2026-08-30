"""Texture resizing and format conversion using Pillow."""

import logging
import os
from pathlib import Path

from PIL import Image

from .config import PipelineConfig
from .formats import discover_textures

logger = logging.getLogger(__name__)


def nearest_pot(value: int, max_val: int) -> int:
    """Find the largest power-of-two that is <= both value and max_val."""
    pot = 1
    while pot * 2 <= min(value, max_val):
        pot *= 2
    return pot


def resize_texture(
    src: str | Path,
    dst: str | Path,
    max_size: int = 2048,
    fmt: str = "png",
) -> Path | None:
    """
    Resize a single texture to power-of-two dimensions capped at max_size.
    Returns output path on success, None on failure.
    """
    try:
        img = Image.open(src)
    except Exception as e:
        logger.warning(f"  Could not open texture {src}: {e}")
        return None

    w, h = img.size
    new_w = nearest_pot(w, max_size)
    new_h = nearest_pot(h, max_size)

    if new_w != w or new_h != h:
        logger.info(f"  Resizing texture {w}x{h} -> {new_w}x{new_h}")
        img = img.resize((new_w, new_h), Image.LANCZOS)
    else:
        logger.info(f"  Texture already within bounds: {w}x{h}")

    # Ensure correct extension
    dst = Path(dst).with_suffix(f".{fmt.lower()}")

    # RGB conversion if needed for JPEG
    if fmt.lower() in ("jpg", "jpeg") and img.mode == "RGBA":
        img = img.convert("RGB")

    dst.parent.mkdir(parents=True, exist_ok=True)
    img.save(str(dst), quality=95)
    return dst


def process_textures(
    mesh_path: str | Path,
    output_dir: str | Path,
    config: PipelineConfig,
) -> dict[str, Path]:
    """
    Discover and resize all textures associated with a mesh file.

    Returns:
        Mapping of original filename -> resized output path.
    """
    if config.skip_texture_resize:
        return {}

    texture_files = discover_textures(mesh_path)
    if not texture_files:
        logger.info("  No adjacent texture files found")
        return {}

    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    tex_map: dict[str, Path] = {}

    for tex in texture_files:
        out_name = f"{tex.stem}.{config.texture_format}"
        out_path = output_dir / out_name
        result = resize_texture(
            tex, out_path,
            max_size=config.texture_size,
            fmt=config.texture_format,
        )
        if result:
            tex_map[tex.name] = result

    logger.info(f"  Processed {len(tex_map)} texture(s)")
    return tex_map


def convert_to_urp(
    extracted: dict[str, str],
    output_dir: str | Path,
    stem: str,
    max_size: int = 2048,
    fmt: str = "png",
) -> dict[str, Path]:
    """
    Convert Blender-extracted PBR textures into Unity URP-ready maps.

    Input roles (from blender_bridge):
        basecolor  — sRGB albedo                   → resized as-is
        normal     — OpenGL tangent-space normal    → resized as-is (mark as Normal in Unity)
        orm        — GLTF packed R=AO G=Rough B=Met → split into URP channels
        roughness  — single-channel roughness       → inverted to smoothness
        metallic   — single-channel metallic        → kept as-is

    Output files (Unity URP naming convention):
        {stem}_BaseColor.png
        {stem}_Normal.png          (OpenGL; enable "Flip Green Channel" in Unity import)
        {stem}_MetallicSmoothness.png   R=Metallic, A=Smoothness (1-Roughness)
        {stem}_Occlusion.png       (if ORM present)

    Returns mapping of unity_role -> Path.
    """
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    out: dict[str, Path] = {}

    def _resize(src: str | Path, dst: Path) -> Path | None:
        return resize_texture(src, dst, max_size=max_size, fmt=fmt)

    # --- Base color ---
    if "basecolor" in extracted:
        dst = output_dir / f"{stem}_BaseColor.{fmt}"
        result = _resize(extracted["basecolor"], dst)
        if result:
            out["BaseColor"] = result
            logger.info(f"  URP BaseColor -> {result.name}")

    # --- Normal map ---
    if "normal" in extracted:
        dst = output_dir / f"{stem}_Normal.{fmt}"
        result = _resize(extracted["normal"], dst)
        if result:
            out["Normal"] = result
            logger.info(f"  URP Normal -> {result.name} (OpenGL; Flip Green in Unity)")

    # --- ORM → MetallicSmoothness + Occlusion ---
    if "orm" in extracted:
        try:
            orm = Image.open(extracted["orm"]).convert("RGBA")
            w = nearest_pot(orm.width,  max_size)
            h = nearest_pot(orm.height, max_size)
            if (w, h) != orm.size:
                orm = orm.resize((w, h), Image.LANCZOS)

            r, g, b, _ = orm.split()   # R=AO, G=Roughness, B=Metallic

            # MetallicSmoothness: R=Metallic, A=Smoothness(=1-Roughness)
            import PIL.ImageChops as chops
            from PIL import ImageOps
            smoothness = ImageOps.invert(g.convert("L"))
            ms = Image.merge("RGBA", (b, b, b, smoothness))
            ms_path = output_dir / f"{stem}_MetallicSmoothness.{fmt}"
            ms.save(str(ms_path))
            out["MetallicSmoothness"] = ms_path
            logger.info(f"  URP MetallicSmoothness -> {ms_path.name}")

            # Occlusion: R channel
            ao = Image.merge("RGB", (r, r, r))
            ao_path = output_dir / f"{stem}_Occlusion.{fmt}"
            ao.save(str(ao_path))
            out["Occlusion"] = ao_path
            logger.info(f"  URP Occlusion -> {ao_path.name}")

        except Exception as e:
            logger.warning(f"  ORM split failed: {e}")

    # --- Standalone roughness (no ORM) ---
    elif "roughness" in extracted:
        try:
            rough = Image.open(extracted["roughness"]).convert("L")
            w = nearest_pot(rough.width,  max_size)
            h = nearest_pot(rough.height, max_size)
            if (w, h) != rough.size:
                rough = rough.resize((w, h), Image.LANCZOS)
            from PIL import ImageOps
            smoothness = ImageOps.invert(rough)
            metal_src = extracted.get("metallic")
            if metal_src:
                metal = Image.open(metal_src).convert("L").resize((w, h), Image.LANCZOS)
            else:
                metal = Image.new("L", (w, h), 0)
            ms = Image.merge("RGBA", (metal, metal, metal, smoothness))
            ms_path = output_dir / f"{stem}_MetallicSmoothness.{fmt}"
            ms.save(str(ms_path))
            out["MetallicSmoothness"] = ms_path
            logger.info(f"  URP MetallicSmoothness (from roughness) -> {ms_path.name}")
        except Exception as e:
            logger.warning(f"  Roughness conversion failed: {e}")

    return out
