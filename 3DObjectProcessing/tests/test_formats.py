"""Tests for format detection and discovery."""

from pathlib import Path
import pytest

from src.formats import (
    is_supported, needs_blender_import, is_texture,
    discover_meshes, discover_textures,
)


def test_is_supported():
    assert is_supported("model.obj")
    assert is_supported("scan.PLY")
    assert is_supported("asset.glb")
    assert not is_supported("document.pdf")
    assert not is_supported("image.png")


def test_needs_blender_import():
    assert needs_blender_import("scene.gltf")
    assert needs_blender_import("asset.GLB")
    assert needs_blender_import("rig.fbx")
    assert not needs_blender_import("mesh.obj")
    assert not needs_blender_import("cloud.ply")


def test_is_texture():
    assert is_texture("albedo.png")
    assert is_texture("normal.TGA")
    assert is_texture("rough.jpg")
    assert not is_texture("mesh.obj")


def test_discover_meshes_single_file(tmp_path):
    f = tmp_path / "mesh.obj"
    f.write_text("# obj")
    result = discover_meshes(f)
    assert result == [f]


def test_discover_meshes_unsupported_file(tmp_path):
    f = tmp_path / "doc.pdf"
    f.write_text("pdf")
    assert discover_meshes(f) == []


def test_discover_meshes_directory(tmp_path):
    (tmp_path / "a.obj").write_text("# obj")
    (tmp_path / "b.ply").write_text("ply")
    (tmp_path / "skip.txt").write_text("text")
    result = discover_meshes(tmp_path)
    names = {p.name for p in result}
    assert "a.obj" in names
    assert "b.ply" in names
    assert "skip.txt" not in names


def test_discover_meshes_skips_temp(tmp_path):
    (tmp_path / "_temp_mesh.obj").write_text("# obj")
    assert discover_meshes(tmp_path) == []


def test_discover_textures(tmp_path):
    mesh = tmp_path / "scan.obj"
    mesh.write_text("# obj")
    (tmp_path / "albedo.png").write_bytes(b"")
    (tmp_path / "normal.tga").write_bytes(b"")
    (tmp_path / "mesh.obj").write_text("# obj")
    result = discover_textures(mesh)
    names = {p.name for p in result}
    assert "albedo.png" in names
    assert "normal.tga" in names
    assert "mesh.obj" not in names
