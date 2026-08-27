from __future__ import annotations

import json
from pathlib import Path

from PIL import Image

from .model import BitmapFont, Glyph, KerningPair


_FORMAT_VERSION = 1


def _glyph_filename(index: int, character: str) -> str:
    return f"{index:05d}_U+{ord(character):04X}.png"


def export_workspace(font: BitmapFont, directory: str | Path, source_name: str) -> None:
    target = Path(directory)
    glyph_directory = target / "glyphs"
    glyph_directory.mkdir(parents=True, exist_ok=True)
    atlas = Image.frombytes(
        "RGBA", (font.texture_width, font.texture_height), font.texture_data
    )
    atlas.save(target / "atlas.png")
    preview_background = Image.new("RGBA", atlas.size, (32, 32, 32, 255))
    Image.alpha_composite(preview_background, atlas).convert("RGB").save(
        target / "atlas-preview-dark.png"
    )

    glyph_records = []
    for index, glyph in enumerate(font.glyphs):
        image = atlas.crop(
            (glyph.x, glyph.y, glyph.x + glyph.width, glyph.y + glyph.height)
        )
        file_name = None
        if image.width > 0 and image.height > 0:
            filename = _glyph_filename(index, glyph.character)
            image.save(glyph_directory / filename)
            file_name = f"glyphs/{filename}"
        glyph_records.append(
            {
                "character": glyph.character,
                "codepoint": f"U+{ord(glyph.character):04X}",
                "file": file_name,
                "source_x": glyph.x,
                "source_y": glyph.y,
                "width": glyph.width,
                "height": glyph.height,
                "advance_width": glyph.advance_width,
                "left_side_bearing": glyph.left_side_bearing,
                "force_white": glyph.force_white,
            }
        )

    manifest = {
        "format_version": _FORMAT_VERSION,
        "source": source_name,
        "xnb": {
            "platform": font.platform,
            "version": font.xnb_version,
            "flags": font.xnb_flags,
            "readers": [
                {"name": name, "version": version} for name, version in font.readers
            ],
            "shared_resource_count": font.shared_resource_count,
            "root_reader_index": font.root_reader_index,
        },
        "font": {
            "line_height": font.line_height,
            "baseline": font.baseline,
            "default_character": font.default_character,
            "texture_reader_index": font.texture_reader_index,
            "surface_format": font.surface_format,
            "source_texture_width": font.texture_width,
            "source_texture_height": font.texture_height,
        },
        "glyphs": glyph_records,
        "kernings": [
            {"first": pair.first, "second": pair.second, "amount": pair.amount}
            for pair in font.kernings
        ],
    }
    (target / "font.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )


def import_workspace(directory: str | Path) -> tuple[BitmapFont, list[Image.Image]]:
    source = Path(directory)
    manifest = json.loads((source / "font.json").read_text(encoding="utf-8"))
    if manifest.get("format_version") != _FORMAT_VERSION:
        raise ValueError(
            f"Unsupported workspace format {manifest.get('format_version')!r}."
        )
    xnb = manifest["xnb"]
    metadata = manifest["font"]
    glyphs = []
    images = []
    for record in manifest["glyphs"]:
        if record["file"] is None:
            image = Image.new(
                "RGBA", (int(record["width"]), int(record["height"])), (0, 0, 0, 0)
            )
        else:
            image = Image.open(source / record["file"]).convert("RGBA")
            image.load()
        images.append(image)
        glyphs.append(
            Glyph(
                character=record["character"],
                x=0,
                y=0,
                width=image.width,
                height=image.height,
                advance_width=int(record["advance_width"]),
                left_side_bearing=int(record["left_side_bearing"]),
                force_white=bool(record["force_white"]),
            )
        )
    kernings = [
        KerningPair(pair["first"], pair["second"], int(pair["amount"]))
        for pair in manifest["kernings"]
    ]
    font = BitmapFont(
        platform=xnb["platform"],
        xnb_version=int(xnb["version"]),
        xnb_flags=int(xnb["flags"]),
        readers=[(reader["name"], int(reader["version"])) for reader in xnb["readers"]],
        shared_resource_count=int(xnb["shared_resource_count"]),
        root_reader_index=int(xnb["root_reader_index"]),
        line_height=int(metadata["line_height"]),
        baseline=int(metadata["baseline"]),
        default_character=metadata["default_character"],
        texture_reader_index=int(metadata["texture_reader_index"]),
        surface_format=int(metadata["surface_format"]),
        texture_width=int(metadata["source_texture_width"]),
        texture_height=int(metadata["source_texture_height"]),
        texture_data=b"",
        glyphs=glyphs,
        kernings=kernings,
    )
    return font, images
