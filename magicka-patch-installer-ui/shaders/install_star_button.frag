#include <flutter/runtime_effect.glsl>

// Adapted for the installer UI from the Shadertoy star shader:
// https://www.shadertoy.com/view/tllfRX
// See ../THIRD_PARTY_NOTICES.md for attribution details.

precision highp float;

out vec4 fragColor;

uniform vec2 uResolution;
uniform float uTime;
uniform float uAlpha;
uniform float uStarSize;
uniform float uGlowStrength;
uniform float uRayStrength;
uniform float uRaySharpness;
uniform float uPulseSpeed;
uniform float uPulseAmount;
uniform vec3 uStarColor;

float star(vec2 uv) {
  float distanceToCenter = length(uv);
  float glow = uGlowStrength / max(distanceToCenter, 0.001);
  float rays = max(0.0, 0.5 - abs(uv.x * uv.y * uRaySharpness));
  rays *= uRayStrength;
  float mask = 1.0 - smoothstep(0.0, 1.0, distanceToCenter);
  return (glow + rays) * mask;
}

void main() {
  vec2 frag = FlutterFragCoord().xy;
  vec2 resolution = max(uResolution, vec2(1.0));
  vec2 uv = (frag - 0.5 * resolution) / resolution.y;
  vec2 starUv = uv / max(uStarSize, 0.001);
  float pulse = 1.0 + sin(uTime * uPulseSpeed) * uPulseAmount;
  float value = star(starUv) * pulse;
  vec3 colour = value * uStarColor * uAlpha;
  float alpha = clamp(value * uAlpha, 0.0, 1.0);
  fragColor = vec4(colour, alpha);
}
