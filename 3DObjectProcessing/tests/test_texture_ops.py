"""Tests for texture_ops — uses synthetic PIL images."""

import pytest
from pathlib import Path

PIL = pytest.importorskip("PIL")
from PIL import Image

from src.texture_ops import nearest_pot, resize_texture


def make_image(tmp_path: Path, size=(3000, 2000), name="tex.png") -> Path:
    p = tmp_path / name
    Image.new("RGB", size, color=(128, 64, 32)).save(str(p))
    return p


def test_nearest_pot_basic():
    assert nearest_pot(1024, 2048) == 1024
    assert nearest_pot(1000, 2048) == 512
    assert nearest_pot(3000, 2048) == 2048
    assert nearest_pot(100,  2048) == 64


def test_nearest_pot_capped_by_max():
    assert nearest_pot(4096, 2048) == 2048


def test_resize_texture_downscales(tmp_path):
    src = make_image(tmp_path, size=(3000, 2000))
    dst = tmp_path / "out.png"
    result = resize_texture(src, dst, max_size=2048, fmt="png")
    assert result is not None
    img = Image.open(result)
    assert img.size[0] <= 2048
    assert img.size[1] <= 2048


def test_resize_texture_already_small(tmp_path):
    src = make_image(tmp_path, size=(512, 512))
    dst = tmp_path / "out.png"
    result = resize_texture(src, dst, max_size=2048, fmt="png")
    assert result is not None
    img = Image.open(result)
    assert img.size == (512, 512)


def test_resize_texture_bad_file(tmp_path):
    bad = tmp_path / "notanimage.png"
    bad.write_text("garbage")
    result = resize_texture(bad, tmp_path / "out.png", max_size=2048)
    assert result is None
