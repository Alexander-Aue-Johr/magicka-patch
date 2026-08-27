from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class Glyph:
    character: str
    x: int
    y: int
    width: int
    height: int
    advance_width: int
    left_side_bearing: int
    force_white: bool


@dataclass
class KerningPair:
    first: str
    second: str
    amount: int


@dataclass
class BitmapFont:
    platform: str
    xnb_version: int
    xnb_flags: int
    readers: list[tuple[str, int]]
    shared_resource_count: int
    root_reader_index: int
    line_height: int
    baseline: int
    default_character: str
    texture_reader_index: int
    surface_format: int
    texture_width: int
    texture_height: int
    texture_data: bytes
    glyphs: list[Glyph] = field(default_factory=list)
    kernings: list[KerningPair] = field(default_factory=list)

    @property
    def texture_bytes(self) -> int:
        return len(self.texture_data)
