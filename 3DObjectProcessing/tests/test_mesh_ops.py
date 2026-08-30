"""Tests for mesh_ops — uses PyMeshLab primitives to avoid needing real scan files."""

import pytest

pymeshlab = pytest.importorskip("pymeshlab")

from src.mesh_ops import load_mesh, clean_mesh, decimate, save_obj, MeshStats
from src.config import PipelineConfig


@pytest.fixture
def sphere_obj(tmp_path):
    """Write a small sphere OBJ via PyMeshLab and return the path."""
    import pymeshlab
    ms = pymeshlab.MeshSet()
    ms.create_sphere(radius=1.0, subdiv=2)
    p = tmp_path / "sphere.obj"
    ms.save_current_mesh(str(p))
    return p


def test_load_mesh(sphere_obj):
    ms, stats = load_mesh(sphere_obj)
    assert stats.vertices > 0
    assert stats.faces > 0


def test_clean_mesh(sphere_obj):
    ms, _ = load_mesh(sphere_obj)
    stats = clean_mesh(ms)
    assert stats.faces > 0


def test_decimate_reduces_faces(sphere_obj):
    ms, orig = load_mesh(sphere_obj)
    clean_mesh(ms)
    config = PipelineConfig(target_faces=20)
    dec = decimate(ms, target_faces=20, stats=orig, config=config)
    assert dec.faces <= orig.faces


def test_decimate_skips_if_under_target(sphere_obj):
    ms, orig = load_mesh(sphere_obj)
    clean_mesh(ms)
    config = PipelineConfig(target_faces=999999)
    dec = decimate(ms, target_faces=999999, stats=orig, config=config)
    assert dec.faces == orig.faces


def test_save_obj(sphere_obj, tmp_path):
    ms, stats = load_mesh(sphere_obj)
    out = tmp_path / "out.obj"
    save_obj(ms, out, stats)
    assert out.exists()
    assert out.stat().st_size > 0
