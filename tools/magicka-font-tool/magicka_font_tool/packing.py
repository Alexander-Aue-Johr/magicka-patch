from __future__ import annotations

import hashlib
import math
from dataclasses import dataclass

from PIL import Image

from .model import BitmapFont


@dataclass
class PackingResult:
    font: BitmapFont
    old_width: int
    old_height: int
    unique_images: int

    @property
    def old_bytes(self) -> int:
        return self.old_width * self.old_height * 4

    @property
    def new_bytes(self) -> int:
        return self.font.texture_bytes


def _next_power_of_two(value: int) -> int:
    if value <= 1:
        return 1
    return 1 << (value - 1).bit_length()


@dataclass(frozen=True)
class _Rectangle:
    x: int
    y: int
    width: int
    height: int

    @property
    def right(self) -> int:
        return self.x + self.width

    @property
    def bottom(self) -> int:
        return self.y + self.height


def _intersects(left: _Rectangle, right: _Rectangle) -> bool:
    return not (
        left.right <= right.x
        or right.right <= left.x
        or left.bottom <= right.y
        or right.bottom <= left.y
    )


def _split_free_rectangle(free: _Rectangle, used: _Rectangle) -> list[_Rectangle]:
    if not _intersects(free, used):
        return [free]

    result = []
    if used.x > free.x:
        result.append(_Rectangle(free.x, free.y, used.x - free.x, free.height))
    if used.right < free.right:
        result.append(_Rectangle(used.right, free.y, free.right - used.right, free.height))
    if used.y > free.y:
        result.append(_Rectangle(free.x, free.y, free.width, used.y - free.y))
    if used.bottom < free.bottom:
        result.append(_Rectangle(free.x, used.bottom, free.width, free.bottom - used.bottom))
    return result


def _contains(outer: _Rectangle, inner: _Rectangle) -> bool:
    return (
        inner.x >= outer.x
        and inner.y >= outer.y
        and inner.right <= outer.right
        and inner.bottom <= outer.bottom
    )


def _prune_free_rectangles(rectangles: list[_Rectangle]) -> list[_Rectangle]:
    result = []
    for index, rectangle in enumerate(rectangles):
        if any(
            index != other_index
            and _contains(other, rectangle)
            and (other != rectangle or other_index < index)
            for other_index, other in enumerate(rectangles)
        ):
            continue
        result.append(rectangle)
    return result


def _maxrects_pack(
    rectangles: list[tuple[str, int, int]],
    width: int,
    height: int,
    order: tuple[int, ...],
) -> dict[str, tuple[int, int]] | None:
    free_rectangles = [_Rectangle(0, 0, width, height)]
    positions: dict[str, tuple[int, int]] = {}

    for rectangle_index in order:
        key, rectangle_width, rectangle_height = rectangles[rectangle_index]
        candidates = []
        for free in free_rectangles:
            if rectangle_width <= free.width and rectangle_height <= free.height:
                leftover_width = free.width - rectangle_width
                leftover_height = free.height - rectangle_height
                candidates.append(
                    (
                        min(leftover_width, leftover_height),
                        max(leftover_width, leftover_height),
                        free.width * free.height - rectangle_width * rectangle_height,
                        free.y,
                        free.x,
                        free,
                    )
                )
        if not candidates:
            return None

        free = min(candidates, key=lambda item: item[:5])[-1]
        used = _Rectangle(free.x, free.y, rectangle_width, rectangle_height)
        positions[key] = (used.x, used.y)
        split = []
        for current in free_rectangles:
            split.extend(_split_free_rectangle(current, used))
        free_rectangles = _prune_free_rectangles(split)

    return positions


def _packing_orders(rectangles: list[tuple[str, int, int]]) -> list[tuple[int, ...]]:
    indices = range(len(rectangles))
    sort_keys = (
        lambda index: (
            -max(rectangles[index][1], rectangles[index][2]),
            -(rectangles[index][1] * rectangles[index][2]),
            -min(rectangles[index][1], rectangles[index][2]),
            rectangles[index][0],
        ),
        lambda index: (
            -(rectangles[index][1] * rectangles[index][2]),
            -max(rectangles[index][1], rectangles[index][2]),
            rectangles[index][0],
        ),
        lambda index: (-rectangles[index][2], -rectangles[index][1], rectangles[index][0]),
        lambda index: (-rectangles[index][1], -rectangles[index][2], rectangles[index][0]),
    )
    orders = []
    for sort_key in sort_keys:
        order = tuple(sorted(indices, key=sort_key))
        if order not in orders:
            orders.append(order)
    return orders


def _choose_layout(
    rectangles: list[tuple[str, int, int]], max_size: int
) -> tuple[dict[str, tuple[int, int]], int, int]:
    if not rectangles:
        return {}, 1, 1

    minimum_width = max(width for _, width, _ in rectangles)
    minimum_height = max(height for _, _, height in rectangles)
    total_area = sum(width * height for _, width, height in rectangles)
    candidates = []
    width = max(64, _next_power_of_two(minimum_width))
    while width <= max_size:
        height = max(64, _next_power_of_two(minimum_height))
        while height <= max_size:
            if width * height >= total_area:
                candidates.append(
                    (
                        width * height,
                        abs(math.log2(width / height)),
                        max(width, height),
                        width,
                        height,
                    )
                )
            height *= 2
        width *= 2

    orders = _packing_orders(rectangles)
    for _, _, _, width, height in sorted(candidates):
        for order in orders:
            positions = _maxrects_pack(rectangles, width, height, order)
            if positions is not None:
                return positions, width, height
    raise ValueError(f"The glyphs do not fit into a {max_size}x{max_size} texture.")


def compact_font(
    font: BitmapFont,
    glyph_images: list[Image.Image] | None = None,
    *,
    padding: int = 0,
    max_size: int = 4096,
    deduplicate: bool = True,
) -> PackingResult:
    if padding < 0:
        raise ValueError("Padding cannot be negative.")
    if max_size <= 0 or max_size & (max_size - 1):
        raise ValueError("Maximum texture size must be a positive power of two.")

    if glyph_images is None:
        source = Image.frombytes(
            "RGBA", (font.texture_width, font.texture_height), font.texture_data
        )
        glyph_images = [
            source.crop((glyph.x, glyph.y, glyph.x + glyph.width, glyph.y + glyph.height))
            for glyph in font.glyphs
        ]
    if len(glyph_images) != len(font.glyphs):
        raise ValueError("The number of glyph images does not match the font.")

    unique: dict[str, Image.Image] = {}
    glyph_keys = []
    for index, image in enumerate(glyph_images):
        rgba = image.convert("RGBA")
        key_material = (
            rgba.width.to_bytes(4, "little")
            + rgba.height.to_bytes(4, "little")
            + rgba.tobytes()
        )
        digest = hashlib.sha256(key_material).hexdigest() if deduplicate else f"{index:08d}"
        if digest not in unique:
            unique[digest] = rgba
        glyph_keys.append(digest)

    rectangles = [
        (key, image.width + padding * 2, image.height + padding * 2)
        for key, image in unique.items()
    ]
    positions, width, height = _choose_layout(rectangles, max_size)
    atlas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for key, image in unique.items():
        x, y = positions[key]
        atlas.paste(image, (x + padding, y + padding))

    for glyph, image, key in zip(font.glyphs, glyph_images, glyph_keys):
        x, y = positions[key]
        glyph.x = x + padding
        glyph.y = y + padding
        glyph.width = image.width
        glyph.height = image.height

    old_width = font.texture_width
    old_height = font.texture_height
    font.texture_width = width
    font.texture_height = height
    font.texture_data = atlas.tobytes()
    return PackingResult(font, old_width, old_height, len(unique))
