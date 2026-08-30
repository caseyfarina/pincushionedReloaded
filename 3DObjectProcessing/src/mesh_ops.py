"""Mesh loading, cleaning, and decimation via PyMeshLab."""

import logging
from dataclasses import dataclass
from pathlib import Path

import pymeshlab

from .config import PipelineConfig

logger = logging.getLogger(__name__)


@dataclass
class MeshStats:
    """Stats from a mesh at a given processing stage."""
    vertices: int = 0
    faces: int = 0
    has_textures: bool = False
    has_wedge_tc: bool = False
    has_vertex_color: bool = False


def load_mesh(path: str | Path) -> tuple[pymeshlab.MeshSet, MeshStats]:
    """
    Load a mesh into a new MeshSet. Returns the set and initial stats.
    Raises on failure — caller should handle.
    """
    ms = pymeshlab.MeshSet()
    ms.load_new_mesh(str(path))
    m = ms.current_mesh()

    stats = MeshStats(
        vertices=m.vertex_number(),
        faces=m.face_number(),
        has_textures=len(m.textures()) > 0,
        has_wedge_tc=m.has_wedge_tex_coord(),
        has_vertex_color=m.has_vertex_color(),
    )
    logger.info(
        f"  Loaded: {stats.vertices:,} verts, {stats.faces:,} faces | "
        f"tex={stats.has_textures} uv={stats.has_wedge_tc} vcol={stats.has_vertex_color}"
    )
    return ms, stats


def clean_mesh(ms: pymeshlab.MeshSet) -> MeshStats:
    """
    Run non-destructive cleanup filters. Returns stats after cleaning.
    Individual filter failures are logged but do not abort.
    """
    cleanup_filters = [
        ("meshing_remove_duplicate_faces", {}),
        ("meshing_remove_duplicate_vertices", {}),
        ("meshing_remove_unreferenced_vertices", {}),
        ("meshing_remove_folded_faces", {}),
    ]

    for name, kwargs in cleanup_filters:
        try:
            getattr(ms, name)(**kwargs)
        except Exception as e:
            logger.debug(f"  Cleanup filter '{name}' skipped: {e}")

    m = ms.current_mesh()
    stats = MeshStats(
        vertices=m.vertex_number(),
        faces=m.face_number(),
        has_textures=len(m.textures()) > 0,
        has_wedge_tc=m.has_wedge_tex_coord(),
        has_vertex_color=m.has_vertex_color(),
    )
    logger.info(f"  After cleanup: {stats.vertices:,} verts, {stats.faces:,} faces")
    return stats


def decimate(
    ms: pymeshlab.MeshSet,
    target_faces: int,
    stats: MeshStats,
    config: PipelineConfig,
) -> MeshStats:
    """
    Decimate the current mesh to approximately target_faces.
    Uses texture-aware algorithm when UVs are present.
    Falls back to basic decimation on failure.
    Returns post-decimation stats.
    """
    m = ms.current_mesh()
    if m.face_number() <= target_faces:
        logger.info(
            f"  Already under target ({m.face_number():,} <= {target_faces:,}), skipping"
        )
        return stats

    # Try texture-aware decimation first if mesh has UVs
    if stats.has_wedge_tc or stats.has_textures:
        try:
            ms.meshing_decimation_quadric_edge_collapse_with_texture(
                targetfacenum=target_faces,
                qualitythr=config.quality_threshold,
                preserveboundary=config.preserve_boundary,
                preservenormal=config.preserve_normals,
                optimalplacement=config.optimal_placement,
                planarquadric=config.planar_quadric,
                extratcoordw=1.0,
            )
            m = ms.current_mesh()
            logger.info(
                f"  Decimated (texture-aware): {m.vertex_number():,} verts, "
                f"{m.face_number():,} faces"
            )
            return MeshStats(
                vertices=m.vertex_number(),
                faces=m.face_number(),
                has_textures=stats.has_textures,
                has_wedge_tc=stats.has_wedge_tc,
                has_vertex_color=stats.has_vertex_color,
            )
        except Exception as e:
            logger.warning(f"  Texture-aware decimation failed: {e}")
            logger.info(f"  Falling back to basic decimation...")

    # Basic decimation (no texture awareness)
    try:
        ms.meshing_decimation_quadric_edge_collapse(
            targetfacenum=target_faces,
            qualitythr=config.quality_threshold,
            preserveboundary=config.preserve_boundary,
            preservenormal=config.preserve_normals,
            optimalplacement=config.optimal_placement,
            planarquadric=config.planar_quadric,
        )
    except Exception as e:
        # Last resort: minimal parameters
        logger.warning(f"  Standard decimation failed: {e}")
        logger.info(f"  Trying minimal decimation...")
        ms.meshing_decimation_quadric_edge_collapse(
            targetfacenum=target_faces,
            preservenormal=True,
        )

    m = ms.current_mesh()
    logger.info(
        f"  Decimated (basic): {m.vertex_number():,} verts, {m.face_number():,} faces"
    )
    return MeshStats(
        vertices=m.vertex_number(),
        faces=m.face_number(),
        has_textures=stats.has_textures,
        has_wedge_tc=stats.has_wedge_tc,
        has_vertex_color=stats.has_vertex_color,
    )


def save_obj(
    ms: pymeshlab.MeshSet,
    output_path: str | Path,
    stats: MeshStats,
) -> Path:
    """
    Save current mesh as OBJ. Tries to preserve all available attributes.
    Returns the actual output path.
    """
    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    try:
        ms.save_current_mesh(
            str(output_path),
            save_vertex_color=stats.has_vertex_color,
            save_vertex_normal=True,
            save_face_color=False,
            save_wedge_texcoord=stats.has_wedge_tc,
            save_wedge_normal=True,
        )
    except Exception as e:
        logger.warning(f"  Save with full options failed ({e}), trying simple save")
        ms.save_current_mesh(str(output_path))

    return output_path
