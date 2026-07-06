#include <flutter/runtime_effect.glsl>

// Adapted for the installer UI from the Shadertoy fire sparks shader:
// https://www.shadertoy.com/view/wl2Gzc
// Original source notes: Author Jan Mróz (jaszunio15), CC BY 3.0.
// See ../THIRD_PARTY_NOTICES.md for attribution details.

precision highp float;

out vec4 fragColor;

uniform vec2 uResolution;
uniform float uTime;
uniform float uHover;
uniform float uSpeed;
uniform float uDensity;
uniform float uSize;
uniform float uSpread;
uniform float uTurbulence;
uniform float uIntensity;
uniform float uBottomCrop;
uniform float uMotionX;
uniform float uMotionY;
uniform float uSmoke;
uniform float uBloom;
uniform float uLayerSize;
uniform float uLayerAlpha;
uniform float uLayers;

#define MAX_PARTICLE_LAYERS 24
#define MAX_NOISE_LAYERS 8

float hash1(vec2 p) {
  return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

vec2 hash2(vec2 p) {
  return fract(sin(vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)))) * 43758.5453123);
}

float noise1(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  float a = hash1(i);
  float b = hash1(i + vec2(1.0, 0.0));
  float c = hash1(i + vec2(0.0, 1.0));
  float d = hash1(i + vec2(1.0, 1.0));
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

vec2 noise2(vec2 p) {
  return vec2(noise1(p), noise1(p + vec2(37.2, 17.7)));
}

float layeredNoise(vec2 uv, float sizeMod, float alphaMod, float layers, float animation, vec2 direction, float speed) {
  float noise = 0.0;
  float alpha = 1.0;
  float size = 1.0;
  float total = 0.0;
  vec2 offset = vec2(0.0);

  for (int i = 0; i < MAX_NOISE_LAYERS; i++) {
    float layerMask = step(float(i) + 0.5, layers);
    offset += hash2(vec2(alpha, size)) * 10.0;
    noise += noise1(uv * size + uTime * animation * 8.0 * direction * speed + offset) * alpha * layerMask;
    total += alpha * layerMask;
    alpha *= alphaMod;
    size *= sizeMod;
  }

  return noise / max(total, 0.001);
}

vec2 rotate2(vec2 point, float radians) {
  float s = sin(radians);
  float c = cos(radians);
  return mat2(c, -s, s, c) * point;
}

vec2 voronoiPointFromRoot(vec2 root, float radians) {
  vec2 point = hash2(root) - 0.5;
  point = rotate2(point, radians) * 0.66;
  return point + root + 0.5;
}

vec2 randomAround(vec2 point, vec2 range, vec2 uv) {
  return point + (hash2(uv) - 0.5) * range;
}

vec3 fireParticles(
  vec2 uv,
  vec2 originalUV,
  vec2 direction,
  float speed,
  float particleSize,
  vec2 particleScale,
  vec2 particleBloomScale,
  float bloomAmount,
  float animationSpeed,
  float spread
) {
  vec2 rootUV = floor(uv);
  float radians = uTime * animationSpeed * (hash1(rootUV) - 0.5) * 2.0;
  vec2 pointUV = voronoiPointFromRoot(rootUV, radians);

  vec2 tempUV = uv + (noise2(uv * 2.0) - 0.5) * (0.06 + 0.08 * uTurbulence);
  tempUV -= (noise2(uv * 3.0 + uTime) - 0.5) * (0.04 + 0.08 * uTurbulence);

  vec2 sparkScale = randomAround(particleScale, vec2(0.25, 0.20), rootUV);
  vec2 bloomScale = randomAround(particleBloomScale, vec2(0.30, 0.10), rootUV);
  float dist = length(rotate2(tempUV - pointUV, 0.7) * sparkScale);
  float distBloom = length(rotate2(tempUV - pointUV, 0.7) * bloomScale);

  vec3 sparkColor = vec3(1.0, 0.40, 0.05) * 1.55;
  vec3 bloomColor = vec3(1.0, 0.40, 0.05) * 0.78 * bloomAmount;
  vec3 particles = vec3(0.0);
  particles += (1.0 - smoothstep(particleSize * 0.6, particleSize * 3.0, dist)) * sparkColor;
  particles += pow(1.0 - smoothstep(0.0, particleSize * 6.0, distBloom), 3.0) * bloomColor;

  float upperBorder = (hash1(rootUV) - 0.5) * 1.8;
  float disappear = 1.0 - smoothstep(upperBorder, upperBorder + 0.52, originalUV.y);
  float lowerBorder = (hash1(rootUV + 0.214) - 1.8) * 0.70;
  float appear = smoothstep(lowerBorder, lowerBorder + 0.40, originalUV.y);
  float sideMask = 1.0 - smoothstep(0.58 + spread * 0.15, 1.12 + spread * 0.18, abs(originalUV.x));

  return particles * disappear * appear * sideMask;
}

vec3 layeredParticles(
  vec2 uv,
  vec2 originalUV,
  float sizeMod,
  float alphaMod,
  float layers,
  float smoke,
  vec2 direction,
  float speed,
  float particleSize,
  vec2 particleScale,
  vec2 particleBloomScale,
  float bloomAmount,
  float animationSpeed,
  float spread
) {
  vec3 particles = vec3(0.0);
  float size = 1.0;
  float alpha = 1.0;
  vec2 offset = vec2(0.0);

  for (int i = 0; i < MAX_PARTICLE_LAYERS; i++) {
    float layerMask = step(float(i) + 0.5, layers);
    vec2 noiseOffset = (noise2(uv * size * 2.0 + 0.5) - 0.5) * (0.08 + 0.12 * uTurbulence);
    vec2 particleUV = uv * size + uTime * direction * speed + offset + noiseOffset;
    float smokeCull = 1.0 - smoothstep(0.0, 1.0, smoke) * (float(i) / max(layers, 1.0));
    particles += fireParticles(
      particleUV,
      originalUV,
      direction,
      speed,
      particleSize,
      particleScale,
      particleBloomScale,
      bloomAmount,
      animationSpeed,
      spread
    ) * alpha * smokeCull * layerMask;
    offset += hash2(vec2(alpha, alpha)) * 10.0;
    alpha *= alphaMod;
    size *= sizeMod;
  }

  return particles;
}

void main() {
  vec2 frag = FlutterFragCoord().xy;
  frag.y = uResolution.y - frag.y;

  vec2 resolution = max(uResolution.xy, vec2(1.0));
  float xpart = frag.x / resolution.x;
  float ypart = frag.y / resolution.y;
  vec2 originalUV = vec2(xpart * 2.0 - 1.0, ypart * 2.0 - 1.0);
  vec2 uv = (2.0 * frag - resolution.xy) / resolution.x;

  vec2 direction = vec2(uMotionX, uMotionY);
  direction /= max(length(direction), 0.001);

  float speed = max(0.02, uSpeed);
  float density = clamp(uDensity, 0.0, 1.0);
  float spread = clamp(uSpread, 0.0, 3.20);
  float layers = clamp(uLayers, 1.0, 24.0);
  float sizeMod = clamp(uLayerSize, 0.50, 2.50);
  float alphaMod = clamp(uLayerAlpha, 0.20, 0.99);
  float animationSpeed = 1.5 + uTurbulence * 0.75;

  uv *= mix(1.25, 2.35, density);
  uv.x *= mix(1.35, 0.68, clamp(spread / 3.20, 0.0, 1.0));

  float particleSize = (0.004 + 0.008 * clamp(uSize, 0.0, 3.0)) * mix(1.15, 0.72, density);
  vec2 particleScale = vec2(0.50 + spread * 0.18, 1.60);
  vec2 particleBloomScale = vec2(0.50 + spread * 0.12, 0.80) * max(0.10, uBloom);

  float vignette = 1.0 - smoothstep(0.42, 1.48, length(originalUV + vec2(0.0, 0.25)));
  float smokeIntensity = layeredNoise(uv * 10.0 + uTime * 4.0 * direction * speed, 1.7, 0.70, 6.0, 0.2, direction, speed);
  smokeIntensity *= pow(1.0 - smoothstep(-1.0, 1.6, originalUV.y), 2.0);
  smokeIntensity *= clamp(uSmoke, 0.0, 4.0);
  vec3 smokeColor = vec3(1.0, 0.43, 0.10) * 0.72;
  vec3 smoke = smokeIntensity * smokeColor * 0.34 * vignette;
  smoke *= pow(layeredNoise(uv * 4.0 + uTime * 0.5 * direction * speed, 1.8, 0.5, 3.0, 0.2, direction, speed), 2.0) * 1.5;

  vec3 particles = layeredParticles(
    uv,
    originalUV,
    sizeMod,
    alphaMod,
    layers,
    smokeIntensity,
    direction,
    speed,
    particleSize,
    particleScale,
    particleBloomScale,
    uBloom,
    animationSpeed,
    spread
  );

  float bottomCrop = clamp(uBottomCrop, 0.0, 0.95);
  float bottomFade = smoothstep(bottomCrop, min(bottomCrop + 0.04, 1.0), ypart);
  float topFade = 1.0 - smoothstep(0.96, 1.0, ypart);
  float sideFade = smoothstep(0.0, 0.035, xpart) * (1.0 - smoothstep(0.965, 1.0, xpart));
  float fade = bottomFade * topFade * sideFade * vignette;

  vec3 col = particles * (0.42 + density * 1.25) + smoke + smokeColor * 0.018 * uSmoke;
  col *= fade * uIntensity;
  col = smoothstep(vec3(-0.08), vec3(1.0), col);

  float alpha = clamp(max(max(col.r, col.g), col.b), 0.0, 1.0);
  fragColor = vec4(col, alpha);
}
