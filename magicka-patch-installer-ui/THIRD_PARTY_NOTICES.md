# Third-Party Notices

This Flutter installer UI includes shader effects adapted from public shader
examples. The local versions were ported to Flutter runtime effects and adjusted
for the Magicka Community Patch UI.

## Shadertoy Shader Adaptations

### Star effect

- Source: https://www.shadertoy.com/view/tllfRX
- Used by:
  - `shaders/install_star_button.frag`
  - `shaders/diamond_edge_star.frag`
- Notes: simplified into a single UI star/glow runtime effect.

### Fire sparks

- Source: https://www.shadertoy.com/view/wl2Gzc
- Author: Jan Mróz (jaszunio15)
- License noted in source: CC BY 3.0
- License URL: https://creativecommons.org/licenses/by/3.0/
- Used by: `shaders/patreon_fire_sparks.frag`
- Notes: ported to Flutter runtime effects and parameterized for UI controls.

### Fire flames

- Source: https://www.shadertoy.com/view/MlKSWm
- Used by:
  - `shaders/patreon_heart_flame.frag`
  - `shaders/flame_button.frag`
  - `shaders/patreon_sparks.frag` (currently not listed in `pubspec.yaml`)
- Notes: used as the basis for the heart flame column and edge flame effects,
  with Flutter uniforms, alpha handling, clipping and UI-specific shaping.

## Shared GLSL Noise/PRNG Snippets

### Ashima WebGL noise

- Source: https://github.com/ashima/webgl-noise
- Authors noted upstream: Ian McEwan, Ashima Arts
- License noted upstream: MIT
- Used by: fire-flame-derived shader effects.

### Shadertoy PRNG snippet

- Source: https://www.shadertoy.com/view/4djSRW
- Used by: spark logic derived from the fire flames shader.
