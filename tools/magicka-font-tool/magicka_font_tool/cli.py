from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image

from .packing import compact_font
from .workspace import export_workspace, import_workspace
from .xnb import FontFormatError, read_font, write_font


def _mib(value: int) -> str:
    return f"{value / 1_048_576:.2f} MiB"


def _print_font(path: Path, font) -> None:
    area = sum(glyph.width * glyph.height for glyph in font.glyphs)
    texture_area = font.texture_width * font.texture_height
    occupancy = area / texture_area * 100 if texture_area else 0
    print(path)
    print(f"  texture: {font.texture_width}x{font.texture_height} RGBA ({_mib(font.texture_bytes)})")
    print(f"  glyphs: {len(font.glyphs)}; kernings: {len(font.kernings)}")
    print(f"  line height: {font.line_height}; baseline: {font.baseline}")
    print(f"  summed glyph area: {occupancy:.1f}% of texture area")


def _packing_options(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--padding", type=int, default=0, help="transparent pixels around each glyph")
    parser.add_argument("--max-size", type=int, default=4096, help="maximum power-of-two atlas dimension")
    parser.add_argument(
        "--keep-duplicates",
        action="store_true",
        help="do not let identical glyph bitmaps share one atlas rectangle",
    )


def _glyph_pixels(atlas, glyph):
    return atlas.crop(
        (glyph.x, glyph.y, glyph.x + glyph.width, glyph.y + glyph.height)
    ).tobytes()


def _verify_equivalent(before, after) -> None:
    font_metadata = (
        "line_height",
        "baseline",
        "default_character",
        "surface_format",
    )
    for name in font_metadata:
        if getattr(before, name) != getattr(after, name):
            raise ValueError(f"Font metadata differs: {name}.")
    if before.kernings != after.kernings:
        raise ValueError("Kerning pairs differ.")
    if len(before.glyphs) != len(after.glyphs):
        raise ValueError("Glyph counts differ.")
    before_atlas = Image.frombytes(
        "RGBA", (before.texture_width, before.texture_height), before.texture_data
    )
    after_atlas = Image.frombytes(
        "RGBA", (after.texture_width, after.texture_height), after.texture_data
    )
    for old, new in zip(before.glyphs, after.glyphs):
        old_metrics = (
            old.character,
            old.width,
            old.height,
            old.advance_width,
            old.left_side_bearing,
            old.force_white,
        )
        new_metrics = (
            new.character,
            new.width,
            new.height,
            new.advance_width,
            new.left_side_bearing,
            new.force_white,
        )
        if old_metrics != new_metrics:
            raise ValueError(f"Glyph metadata differs for {old.character!r}.")
        if _glyph_pixels(before_atlas, old) != _glyph_pixels(after_atlas, new):
            raise ValueError(f"Glyph pixels differ for {old.character!r}.")


def _report_result(output: Path, result) -> None:
    difference = result.old_bytes - result.new_bytes
    percentage = difference / result.old_bytes * 100 if result.old_bytes else 0
    print(
        f"{output}: {result.old_width}x{result.old_height} ({_mib(result.old_bytes)}) -> "
        f"{result.font.texture_width}x{result.font.texture_height} "
        f"({_mib(result.new_bytes)}), saved {_mib(difference)} ({percentage:.1f}%)"
    )
    if result.unique_images != len(result.font.glyphs):
        print(
            f"  {len(result.font.glyphs)} glyphs use {result.unique_images} distinct bitmap rectangles."
        )


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="magicka-font-tool",
        description="Extract, edit, inspect, and compact Magicka bitmap-font XNB files.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    inspect_parser = subparsers.add_parser("inspect", help="show font and texture information")
    inspect_parser.add_argument("inputs", type=Path, nargs="+")

    export_parser = subparsers.add_parser(
        "export", help="export the atlas, individual glyph PNGs, and editable metadata"
    )
    export_parser.add_argument("input", type=Path)
    export_parser.add_argument("directory", type=Path)

    verify_parser = subparsers.add_parser(
        "verify", help="verify that two fonts render every glyph identically"
    )
    verify_parser.add_argument("before", type=Path)
    verify_parser.add_argument("after", type=Path)

    pack_parser = subparsers.add_parser(
        "pack", help="pack an exported and possibly edited workspace into an XNB"
    )
    pack_parser.add_argument("directory", type=Path)
    pack_parser.add_argument("output", type=Path)
    _packing_options(pack_parser)

    optimize_parser = subparsers.add_parser(
        "optimize", help="repack one XNB without changing glyph pixels or metrics"
    )
    optimize_parser.add_argument("input", type=Path)
    optimize_parser.add_argument("output", type=Path)
    _packing_options(optimize_parser)

    batch_parser = subparsers.add_parser(
        "batch-optimize", help="repack every XNB in a font directory"
    )
    batch_parser.add_argument("input_directory", type=Path)
    batch_parser.add_argument("output_directory", type=Path)
    _packing_options(batch_parser)
    return parser


def _compact(font, images, arguments):
    return compact_font(
        font,
        images,
        padding=arguments.padding,
        max_size=arguments.max_size,
        deduplicate=not arguments.keep_duplicates,
    )


def main(argv: list[str] | None = None) -> int:
    parser = _build_parser()
    arguments = parser.parse_args(argv)
    try:
        if arguments.command == "inspect":
            for path in arguments.inputs:
                _print_font(path, read_font(path))
            return 0
        if arguments.command == "export":
            font = read_font(arguments.input)
            export_workspace(font, arguments.directory, arguments.input.name)
            print(
                f"Exported {len(font.glyphs)} glyphs, atlas.png, and font.json to "
                f"{arguments.directory}."
            )
            return 0
        if arguments.command == "verify":
            before = read_font(arguments.before)
            after = read_font(arguments.after)
            _verify_equivalent(before, after)
            print(
                f"Equivalent: {len(before.glyphs)} glyphs and "
                f"{len(before.kernings)} kerning pairs match."
            )
            return 0
        if arguments.command == "pack":
            font, images = import_workspace(arguments.directory)
            result = _compact(font, images, arguments)
            arguments.output.parent.mkdir(parents=True, exist_ok=True)
            write_font(result.font, arguments.output)
            _report_result(arguments.output, result)
            return 0
        if arguments.command == "optimize":
            font = read_font(arguments.input)
            result = _compact(font, None, arguments)
            arguments.output.parent.mkdir(parents=True, exist_ok=True)
            write_font(result.font, arguments.output)
            _report_result(arguments.output, result)
            return 0
        if arguments.command == "batch-optimize":
            paths = sorted(arguments.input_directory.glob("*.xnb"))
            if not paths:
                parser.error(f"No XNB files found in {arguments.input_directory}.")
            old_total = 0
            new_total = 0
            for path in paths:
                font = read_font(path)
                result = _compact(font, None, arguments)
                output = arguments.output_directory / path.name
                output.parent.mkdir(parents=True, exist_ok=True)
                write_font(result.font, output)
                _report_result(output, result)
                old_total += result.old_bytes
                new_total += result.new_bytes
            print(f"Total texture payload: {_mib(old_total)} -> {_mib(new_total)}")
            return 0
    except (FontFormatError, OSError, ValueError, KeyError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
