#include <flutter/runtime_effect.glsl>

// Adapted for the priority-supporter diamond from the same Shadertoy star
// effect used elsewhere in the installer. See ../THIRD_PARTY_NOTICES.md.

precision highp float;

out vec4 fragColor;

uniform vec2 uResolution;
uniform float uFlare;
uniform float uAlpha;
uniform vec3 uColor;

mat2 Rot(float angle) {
  float sine = sin(angle);
  float cosine = cos(angle);
  return mat2(cosine, -sine, sine, cosine);
}

float Star(vec2 uv, float flare) {
  float d = length(uv);
  float m = 0.02 / max(d, 0.001);

  float rays = max(0.0, 1.0 - abs(uv.x * uv.y * 1000.0));
  m += rays * flare;
  uv *= Rot(3.1415 / 4.0);
  rays = max(0.0, 1.0 - abs(uv.x * uv.y * 1000.0));
  m += rays * 0.3 * flare;

  // Equivalent to the supplied reverse-edge smoothstep, without relying on
  // undefined edge ordering in GLSL implementations.
  m *= 1.0 - smoothstep(0.2, 1.0, d);
  return m;
}

void main() {
  vec2 resolution = max(uResolution, vec2(1.0));
  vec2 uv = (FlutterFragCoord().xy - 0.5 * resolution) /
      max(min(resolution.x, resolution.y) * 0.5, 1.0);
  float value = Star(uv, uFlare);
  float alpha = clamp(value * uAlpha, 0.0, 1.0);
  vec3 tint = mix(uColor, vec3(1.0), clamp(value * 0.55, 0.0, 1.0));

  // Flutter expects premultiplied output; the painter uses additive blending.
  fragColor = vec4(tint * alpha, alpha);
}
