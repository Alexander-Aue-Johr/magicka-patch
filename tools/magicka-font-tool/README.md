# Magicka Font Tool

`MagickaFontTool` extracts, edits, and repacks the custom bitmap-font XNB files
read by `PolygonHead.Pipeline.BitmapFontReader`. It supports the XNA 4 Windows
files shipped with Magicka, including their LZX-compressed form.

The tool keeps the glyph metrics, default character, kerning pairs, and pixel
data. Repacking changes only glyph coordinates and the size of the shared atlas.
It writes a valid uncompressed XNB because XNA supports both compressed and
uncompressed content. XNB compression changes disk size and loading time, but
not the texture memory used after loading.

## Install

Python 3.10 or newer is required.

```sh
python3 -m venv .venv
.venv/bin/pip install -e tools/magicka-font-tool
```

On Windows, use `.venv\Scripts\python` and `.venv\Scripts\pip` instead.

## Inspect and compact a font

```sh
magicka-font-tool inspect Content/Languages/zho/Font/MenuTitle.xnb
magicka-font-tool optimize input.xnb output.xnb
magicka-font-tool verify input.xnb output.xnb
```

Atlas dimensions remain powers of two. The default maximum is 4096 pixels.
Identical glyph bitmaps share one rectangle unless `--keep-duplicates` is used.
The default uses no added padding, matching the supplied Magicka atlases, whose
glyph rectangles already touch. Use `--padding 1` only when building glyphs that
need an explicit transparent border; it can require a substantially larger
power-of-two texture.

## Edit individual glyphs

```sh
magicka-font-tool export input.xnb work/MenuTitle
# Edit work/MenuTitle/glyphs/*.png and, when needed, font.json.
magicka-font-tool pack work/MenuTitle output.xnb
```

`atlas.png` contains the exact RGBA data. `atlas-preview-dark.png` composites it
over a dark background so white glyphs remain visible in image viewers that
otherwise display transparent pixels on white.

`font.json` contains the character mapping, advance width, side bearing,
baseline, line height, force-white flag, and kerning pairs. A changed glyph PNG
may have different dimensions; packing updates its texture rectangle while
leaving its advance and bearing under explicit control in `font.json`.

To add a glyph, add its PNG and a matching record to the `glyphs` array in
`font.json`. The pack command assigns its atlas position automatically. Magicka's
format has no per-glyph vertical bearing, so preserve the intended line-relative
image height when replacing or adding glyphs.

An intentionally empty 0×0 glyph has `"file": null` and keeps its advance and
bearing without creating an invalid empty PNG. Give the record a PNG path when
turning it into a visible glyph.

## Compact a complete language font directory

```sh
magicka-font-tool batch-optimize \
  Content/Languages/zho/Font \
  build/zho/Font
```

Keep the original files until the rebuilt set has been tested in the game.
`verify` proves that existing glyph pixels and metrics survived repacking; it
does not replace a visual runtime test of filtering, scaling, and every font
role used by the game.

## Third-party code

The LZX decoder in `magicka_font_tool/_lzx.py` is adapted from the MIT-licensed
`lzx_decompress.py` in sp00nznet/360tools. Its license and attribution are kept
in `LICENSE.lzx-decompress.txt`.
