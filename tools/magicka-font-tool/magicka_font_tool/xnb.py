from __future__ import annotations

import struct
from pathlib import Path

from ._lzx import LZXDecoder
from .model import BitmapFont, Glyph, KerningPair


_LZX_COMPRESSION = 0x80
_LZ4_COMPRESSION = 0x40
_SUPPORTED_READER = "PolygonHead.Pipeline.BitmapFontReader"
_SUPPORTED_TEXTURE_READER = "Microsoft.Xna.Framework.Content.Texture2DReader"
_COLOR_SURFACE_FORMAT = 1


class FontFormatError(ValueError):
    pass


class _Reader:
    def __init__(self, data: bytes):
        self.data = data
        self.position = 0

    def _take(self, count: int) -> bytes:
        end = self.position + count
        if count < 0 or end > len(self.data):
            raise FontFormatError("The XNB payload ends unexpectedly.")
        value = self.data[self.position:end]
        self.position = end
        return value

    def byte(self) -> int:
        return self._take(1)[0]

    def boolean(self) -> bool:
        value = self.byte()
        if value not in (0, 1):
            raise FontFormatError(f"Invalid Boolean value {value} in glyph record.")
        return bool(value)

    def int32(self) -> int:
        return struct.unpack("<i", self._take(4))[0]

    def uint32(self) -> int:
        return struct.unpack("<I", self._take(4))[0]

    def encoded_int(self) -> int:
        value = 0
        shift = 0
        while shift < 35:
            current = self.byte()
            value |= (current & 0x7F) << shift
            if current < 0x80:
                return value
            shift += 7
        raise FontFormatError("Invalid 7-bit encoded integer.")

    def string(self) -> str:
        length = self.encoded_int()
        try:
            return self._take(length).decode("utf-8")
        except UnicodeDecodeError as error:
            raise FontFormatError("Invalid UTF-8 string in XNB payload.") from error

    def character(self) -> str:
        first = self.data[self.position] if self.position < len(self.data) else None
        if first is None:
            raise FontFormatError("The XNB payload ends inside a character.")
        if first < 0x80:
            length = 1
        elif first < 0xE0:
            length = 2
        elif first < 0xF0:
            length = 3
        else:
            raise FontFormatError("Bitmap fonts can only contain BMP characters.")
        try:
            value = self._take(length).decode("utf-8")
        except UnicodeDecodeError as error:
            raise FontFormatError("Invalid UTF-8 character in font data.") from error
        if len(value) != 1:
            raise FontFormatError("Invalid character in font data.")
        return value


class _Writer:
    def __init__(self):
        self.data = bytearray()

    def byte(self, value: int) -> None:
        self.data.append(value)

    def boolean(self, value: bool) -> None:
        self.byte(1 if value else 0)

    def int32(self, value: int) -> None:
        self.data.extend(struct.pack("<i", value))

    def encoded_int(self, value: int) -> None:
        if value < 0:
            raise ValueError("A 7-bit encoded integer cannot be negative.")
        while value >= 0x80:
            self.byte((value & 0x7F) | 0x80)
            value >>= 7
        self.byte(value)

    def string(self, value: str) -> None:
        encoded = value.encode("utf-8")
        self.encoded_int(len(encoded))
        self.data.extend(encoded)

    def character(self, value: str) -> None:
        if len(value) != 1 or ord(value) > 0xFFFF:
            raise ValueError(f"Not a BMP character: {value!r}")
        self.data.extend(value.encode("utf-8"))


def _decompress_lzx(data: bytes, expected_size: int) -> bytes:
    decoder = LZXDecoder(16)
    position = 0
    output = bytearray()

    while len(output) < expected_size:
        if position + 2 > len(data):
            raise FontFormatError("The LZX block header is truncated.")
        high = data[position]
        position += 1
        if high == 0xFF:
            if position + 4 > len(data):
                raise FontFormatError("The extended LZX block header is truncated.")
            frame_size = (data[position] << 8) | data[position + 1]
            compressed_size = (data[position + 2] << 8) | data[position + 3]
            position += 4
        else:
            compressed_size = (high << 8) | data[position]
            position += 1
            frame_size = min(0x8000, expected_size - len(output))

        if compressed_size == 0 or frame_size == 0:
            raise FontFormatError("Invalid empty LZX block.")
        end = position + compressed_size
        if end > len(data):
            raise FontFormatError("The LZX block is truncated.")
        output.extend(decoder.decompress(data[position:end], frame_size))
        position = end

    if len(output) != expected_size:
        raise FontFormatError(
            f"LZX size mismatch: expected {expected_size}, decoded {len(output)}."
        )
    return bytes(output)


def _read_container(path: Path) -> tuple[str, int, int, bytes]:
    data = path.read_bytes()
    if len(data) < 10 or data[:3] != b"XNB":
        raise FontFormatError(f"{path} is not an XNB file.")
    try:
        platform = chr(data[3])
    except ValueError as error:
        raise FontFormatError("Invalid XNB platform identifier.") from error
    version = data[4]
    flags = data[5]
    declared_size = struct.unpack_from("<I", data, 6)[0]
    if declared_size != len(data):
        raise FontFormatError(
            f"XNB length mismatch: header says {declared_size}, file has {len(data)} bytes."
        )
    if flags & _LZ4_COMPRESSION:
        raise FontFormatError("LZ4-compressed XNB files are not supported.")
    if flags & _LZX_COMPRESSION:
        if len(data) < 14:
            raise FontFormatError("The compressed XNB header is truncated.")
        expected_size = struct.unpack_from("<I", data, 10)[0]
        payload = _decompress_lzx(data[14:], expected_size)
    else:
        payload = data[10:]
    return platform, version, flags, payload


def read_font(path: str | Path) -> BitmapFont:
    source = Path(path)
    platform, version, flags, payload = _read_container(source)
    reader = _Reader(payload)

    readers = []
    for _ in range(reader.encoded_int()):
        readers.append((reader.string(), reader.int32()))
    if not readers or _SUPPORTED_READER not in readers[0][0]:
        name = readers[0][0] if readers else "<none>"
        raise FontFormatError(f"Unsupported root content reader: {name}")
    if len(readers) < 2 or _SUPPORTED_TEXTURE_READER not in readers[1][0]:
        name = readers[1][0] if len(readers) > 1 else "<none>"
        raise FontFormatError(f"Unsupported texture content reader: {name}")

    shared_resource_count = reader.encoded_int()
    if shared_resource_count != 0:
        raise FontFormatError("Bitmap fonts with shared XNB resources are not supported.")
    root_reader_index = reader.encoded_int()
    line_height = reader.int32()
    baseline = reader.int32()
    default_character = reader.character()
    texture_reader_index = reader.encoded_int()
    surface_format = reader.int32()
    texture_width = reader.int32()
    texture_height = reader.int32()
    mip_count = reader.int32()
    if surface_format != _COLOR_SURFACE_FORMAT:
        raise FontFormatError(
            f"Unsupported texture surface format {surface_format}; expected Color (1)."
        )
    if mip_count != 1:
        raise FontFormatError(f"Unsupported mip count {mip_count}; expected 1.")
    texture_size = reader.int32()
    expected_texture_size = texture_width * texture_height * 4
    if texture_size != expected_texture_size:
        raise FontFormatError(
            f"Texture data has {texture_size} bytes; expected {expected_texture_size} "
            f"for {texture_width}x{texture_height} RGBA."
        )
    texture_data = reader._take(texture_size)

    glyphs = []
    for _ in range(reader.int32()):
        glyphs.append(
            Glyph(
                character=reader.character(),
                x=reader.int32(),
                y=reader.int32(),
                width=reader.int32(),
                height=reader.int32(),
                advance_width=reader.int32(),
                left_side_bearing=reader.int32(),
                force_white=reader.boolean(),
            )
        )

    kernings = []
    for _ in range(reader.int32()):
        kernings.append(
            KerningPair(
                first=reader.character(),
                second=reader.character(),
                amount=reader.int32(),
            )
        )
    if reader.position != len(payload):
        raise FontFormatError(
            f"Unexpected {len(payload) - reader.position} bytes after the font data."
        )

    font = BitmapFont(
        platform=platform,
        xnb_version=version,
        xnb_flags=flags,
        readers=readers,
        shared_resource_count=shared_resource_count,
        root_reader_index=root_reader_index,
        line_height=line_height,
        baseline=baseline,
        default_character=default_character,
        texture_reader_index=texture_reader_index,
        surface_format=surface_format,
        texture_width=texture_width,
        texture_height=texture_height,
        texture_data=texture_data,
        glyphs=glyphs,
        kernings=kernings,
    )
    validate_font(font)
    return font


def validate_font(font: BitmapFont) -> None:
    if font.texture_width <= 0 or font.texture_height <= 0:
        raise FontFormatError("The font texture dimensions must be positive.")
    if len(font.texture_data) != font.texture_width * font.texture_height * 4:
        raise FontFormatError("The RGBA texture size does not match its dimensions.")
    seen = set()
    for glyph in font.glyphs:
        if glyph.character in seen:
            raise FontFormatError(f"Duplicate glyph {glyph.character!r}.")
        seen.add(glyph.character)
        if glyph.width < 0 or glyph.height < 0:
            raise FontFormatError(f"Glyph {glyph.character!r} has a negative size.")
        if (
            glyph.x < 0
            or glyph.y < 0
            or glyph.x + glyph.width > font.texture_width
            or glyph.y + glyph.height > font.texture_height
        ):
            raise FontFormatError(f"Glyph {glyph.character!r} lies outside the texture.")
    if font.default_character not in seen:
        raise FontFormatError(
            f"Default character {font.default_character!r} has no glyph record."
        )


def _write_payload(font: BitmapFont) -> bytes:
    writer = _Writer()
    writer.encoded_int(len(font.readers))
    for name, version in font.readers:
        writer.string(name)
        writer.int32(version)
    writer.encoded_int(font.shared_resource_count)
    writer.encoded_int(font.root_reader_index)
    writer.int32(font.line_height)
    writer.int32(font.baseline)
    writer.character(font.default_character)
    writer.encoded_int(font.texture_reader_index)
    writer.int32(font.surface_format)
    writer.int32(font.texture_width)
    writer.int32(font.texture_height)
    writer.int32(1)
    writer.int32(len(font.texture_data))
    writer.data.extend(font.texture_data)
    writer.int32(len(font.glyphs))
    for glyph in font.glyphs:
        writer.character(glyph.character)
        writer.int32(glyph.x)
        writer.int32(glyph.y)
        writer.int32(glyph.width)
        writer.int32(glyph.height)
        writer.int32(glyph.advance_width)
        writer.int32(glyph.left_side_bearing)
        writer.boolean(glyph.force_white)
    writer.int32(len(font.kernings))
    for kerning in font.kernings:
        writer.character(kerning.first)
        writer.character(kerning.second)
        writer.int32(kerning.amount)
    return bytes(writer.data)


def write_font(font: BitmapFont, path: str | Path) -> None:
    validate_font(font)
    payload = _write_payload(font)
    flags = font.xnb_flags & ~(_LZX_COMPRESSION | _LZ4_COMPRESSION)
    header = b"XNB" + font.platform.encode("ascii") + bytes((font.xnb_version, flags))
    container = header + struct.pack("<I", 10 + len(payload)) + payload
    Path(path).write_bytes(container)
