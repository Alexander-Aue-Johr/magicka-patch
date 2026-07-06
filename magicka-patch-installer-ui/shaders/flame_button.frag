#include <flutter/runtime_effect.glsl>

// Edge flame effect adapted for the installer UI from the Shadertoy fire flames shader:
// https://www.shadertoy.com/view/MlKSWm
// See ../THIRD_PARTY_NOTICES.md for attribution details.

precision highp float;

out vec4 fragColor;

uniform vec2 uResolution;
uniform float uTime;
uniform float uHover;
uniform float uIntensity;
uniform vec3 uAccent;

float hash(vec2 p) {
  return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float noise(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  float a = hash(i);
  float b = hash(i + vec2(1.0, 0.0));
  float c = hash(i + vec2(0.0, 1.0));
  float d = hash(i + vec2(1.0, 1.0));
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float fbm(vec2 p) {
  float v = 0.0;
  float a = 0.5;
  for (int i = 0; i < 5; i++) {
    v += a * noise(p);
    p *= 2.03;
    a *= 0.5;
  }
  return v;
}

float rectMask(vec2 uv, vec2 minUv, vec2 maxUv, float feather) {
  vec2 a = smoothstep(minUv, minUv + vec2(feather), uv);
  vec2 b = smoothstep(maxUv, maxUv - vec2(feather), uv);
  return a.x * a.y * b.x * b.y;
}

void main() {
  vec2 frag = FlutterFragCoord().xy;
  vec2 uv = frag / max(uResolution, vec2(1.0));

  float inner = rectMask(uv, vec2(0.045, 0.18), vec2(0.955, 0.82), 0.025);
  float outer = rectMask(uv, vec2(0.0, 0.0), vec2(1.0, 1.0), 0.055);
  float activeArea = mix(inner, outer, clamp(uHover, 0.0, 1.0));

  float base = 1.0 - uv.y;
  vec2 flow = vec2(uv.x * 7.5, uv.y * 4.2 - uTime * (1.25 + uHover * 0.75));
  float flame = fbm(flow + vec2(sin(uTime + uv.x * 9.0) * 0.25, 0.0));
  flame += 0.35 * fbm(flow * 2.0 + vec2(0.0, -uTime * 1.7));
  flame = smoothstep(0.34, 0.92, flame + base * 0.55);

  float edge = 1.0 - abs(uv.x - 0.5) * 2.0;
  float lick = pow(max(0.0, edge), 0.45) * flame;
  float alpha = lick * activeArea * (0.25 + uIntensity * 0.7);
  alpha *= mix(0.45, 1.0, uHover);

  vec3 hot = vec3(1.0, 0.91, 0.52);
  vec3 ember = vec3(1.0, 0.21, 0.04);
  vec3 accent = max(uAccent, vec3(0.08));
  vec3 color = mix(accent, ember, flame * 0.55);
  color = mix(color, hot, pow(flame, 3.2) * (0.4 + uHover * 0.35));

  fragColor = vec4(color, clamp(alpha, 0.0, 0.92));
}
