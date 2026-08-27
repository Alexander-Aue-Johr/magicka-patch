from __future__ import annotations

import tempfile
import unittest
from copy import deepcopy
from pathlib import Path

from PIL import Image

from magicka_font_tool.model import BitmapFont, Glyph, KerningPair
from magicka_font_tool.packing import (
    _Rectangle,
    _maxrects_pack,
    _prune_free_rectangles,
    compact_font,
)
from magicka_font_tool.workspace import export_workspace, import_workspace
from magicka_font_tool.xnb import read_font, write_font


def _font() -> BitmapFont:
    atlas = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
    atlas.putpixel((0, 0), (255, 255, 255, 255))
    atlas.putpixel((4, 4), (255, 255, 255, 128))
    return BitmapFont(
        platform="w",
        xnb_version=4,
        xnb_flags=0x80,
        readers=[
            ("PolygonHead.Pipeline.BitmapFontReader, PolygonHead", 0),
            ("Microsoft.Xna.Framework.Content.Texture2DReader", 0),
        ],
        shared_resource_count=0,
        root_reader_index=1,
        line_height=12,
        baseline=9,
        default_character="?",
        texture_reader_index=2,
        surface_format=1,
        texture_width=8,
        texture_height=8,
        texture_data=atlas.tobytes(),
        glyphs=[
            Glyph("?", 0, 0, 2, 2, 3, 0, False),
            Glyph("中", 4, 4, 2, 2, 3, 0, True),
        ],
        kernings=[KerningPair("?", "中", -1)],
    )


class FontToolTests(unittest.TestCase):
    def test_free_rectangle_pruning_keeps_one_duplicate(self):
        rectangle = _Rectangle(0, 0, 16, 16)
        self.assertEqual(_prune_free_rectangles([rectangle, rectangle]), [rectangle])

    def test_maxrects_layout_does_not_overlap(self):
        rectangles = [("a", 7, 4), ("b", 4, 7), ("c", 5, 5), ("d", 3, 3)]
        positions = _maxrects_pack(rectangles, 16, 16, tuple(range(len(rectangles))))
        self.assertIsNotNone(positions)
        placed = []
        for key, width, height in rectangles:
            x, y = positions[key]
            current = _Rectangle(x, y, width, height)
            self.assertLessEqual(current.right, 16)
            self.assertLessEqual(current.bottom, 16)
            for previous in placed:
                self.assertTrue(
                    current.right <= previous.x
                    or previous.right <= current.x
                    or current.bottom <= previous.y
                    or previous.bottom <= current.y
                )
            placed.append(current)

    def test_uncompressed_xnb_round_trip(self):
        original = _font()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "font.xnb"
            write_font(original, path)
            restored = read_font(path)
        self.assertEqual(restored.line_height, original.line_height)
        self.assertEqual(restored.texture_data, original.texture_data)
        self.assertEqual(restored.glyphs, original.glyphs)
        self.assertEqual(restored.kernings, original.kernings)

    def test_workspace_preserves_an_empty_glyph(self):
        original = _font()
        original.glyphs.append(Glyph(" ", 0, 0, 0, 0, 4, 0, False))
        with tempfile.TemporaryDirectory() as directory:
            export_workspace(original, directory, "font.xnb")
            restored, images = import_workspace(directory)
            manifest = (Path(directory) / "font.json").read_text(encoding="utf-8")
            self.assertTrue((Path(directory) / "atlas-preview-dark.png").is_file())
        self.assertIn('"file": null', manifest)
        self.assertEqual((images[-1].width, images[-1].height), (0, 0))
        self.assertEqual(restored.glyphs[-1], original.glyphs[-1])

    def test_compaction_preserves_each_glyph_bitmap(self):
        original = _font()
        original_image = Image.frombytes(
            "RGBA", (original.texture_width, original.texture_height), original.texture_data
        )
        expected = [
            original_image.crop((g.x, g.y, g.x + g.width, g.y + g.height)).tobytes()
            for g in original.glyphs
        ]
        result = compact_font(deepcopy(original), max_size=64)
        packed = Image.frombytes(
            "RGBA",
            (result.font.texture_width, result.font.texture_height),
            result.font.texture_data,
        )
        actual = [
            packed.crop((g.x, g.y, g.x + g.width, g.y + g.height)).tobytes()
            for g in result.font.glyphs
        ]
        self.assertEqual(actual, expected)
        self.assertEqual(result.font.kernings, original.kernings)


if __name__ == "__main__":
    unittest.main()
