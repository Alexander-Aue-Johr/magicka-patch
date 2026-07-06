#include <flutter/runtime_effect.glsl>

// Alternate spark effect adapted from the Shadertoy fire flames shader:
// https://www.shadertoy.com/view/MlKSWm
// Includes Ashima/Ian McEwan simplex-noise and Shadertoy PRNG code;
// see ../THIRD_PARTY_NOTICES.md.

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
uniform float uBottomSpeed;
uniform float uTopSpeed;
uniform float uBottomCrop;

vec3 mod289(vec3 x) {
  return x - floor(x * (1.0 / 289.0)) * 289.0;
}

vec4 mod289(vec4 x) {
  return x - floor(x * (1.0 / 289.0)) * 289.0;
}

vec4 permute(vec4 x) {
  return mod289(((x * 34.0) + 1.0) * x);
}

float snoise(vec3 v) {
  const vec2 C = vec2(1.0 / 6.0, 1.0 / 3.0);
  const vec4 D = vec4(0.0, 0.5, 1.0, 2.0);

  vec3 i = floor(v + dot(v, C.yyy));
  vec3 x0 = v - i + dot(i, C.xxx);

  vec3 g = step(x0.yzx, x0.xyz);
  vec3 l = 1.0 - g;
  vec3 i1 = min(g.xyz, l.zxy);
  vec3 i2 = max(g.xyz, l.zxy);

  vec3 x1 = x0 - i1 + C.xxx;
  vec3 x2 = x0 - i2 + C.yyy;
  vec3 x3 = x0 - D.yyy;

  i = mod289(i);

  vec4 p = permute(
    permute(
      permute(i.z + vec4(0.0, i1.z, i2.z, 1.0))
      + i.y + vec4(0.0, i1.y, i2.y, 1.0)
    )
    + i.x + vec4(0.0, i1.x, i2.x, 1.0)
  );

  float n_ = 0.142857142857;
  vec3 ns = n_ * D.wyz - D.xzx;

  vec4 j = p - 49.0 * floor(p * ns.z * ns.z);
  vec4 x_ = floor(j * ns.z);
  vec4 y_ = floor(j - 7.0 * x_);

  vec4 x = x_ * ns.x + ns.yyyy;
  vec4 y = y_ * ns.x + ns.yyyy;
  vec4 h = 1.0 - abs(x) - abs(y);

  vec4 b0 = vec4(x.xy, y.xy);
  vec4 b1 = vec4(x.zw, y.zw);

  vec4 s0 = floor(b0) * 2.0 + 1.0;
  vec4 s1 = floor(b1) * 2.0 + 1.0;
  vec4 sh = -step(h, vec4(0.0));

  vec4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
  vec4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

  vec3 p0 = vec3(a0.xy, h.x);
  vec3 p1 = vec3(a0.zw, h.y);
  vec3 p2 = vec3(a1.xy, h.z);
  vec3 p3 = vec3(a1.zw, h.w);

  vec4 norm = inversesqrt(vec4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
  p0 *= norm.x;
  p1 *= norm.y;
  p2 *= norm.z;
  p3 *= norm.w;

  vec4 m = max(0.6 - vec4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
  m = m * m;

  return 42.0 * dot(m * m, vec4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}

float prng(vec2 seed) {
  seed = fract(seed * vec2(5.3983, 5.4427));
  seed += dot(seed.yx, seed.xy + vec2(21.5351, 14.3137));
  return fract(seed.x * seed.y * 95.4337);
}

vec2 noiseUV(vec3 pos) {
  float a = snoise(pos);
  float b = snoise(pos + vec3(3984.293, 423.21, 5235.19));
  return 0.5 + 0.5 * vec2(a, b);
}

void main() {
  vec2 frag = FlutterFragCoord().xy;
  frag.y = uResolution.y - frag.y;

  vec2 resolution = max(uResolution.xy, vec2(1.0));
  float xpart = frag.x / resolution.x;
  float ypart = frag.y / resolution.y;

  float hover = clamp(uHover, 0.0, 1.0);
  float clipHeight = resolution.y * mix(0.62, 0.94, hover);
  float ypartClipped = min(frag.y / max(clipHeight, 1.0), 1.0);
  float ypartClippedn = 1.0 - ypartClipped;

  float xfuel = pow(max(0.0, 1.0 - abs(2.0 * xpart - 1.0)), mix(0.18, 0.08, hover));
  float speed = max(0.05, uSpeed);
  float realTime = uTime * speed;

  float bottomSparkSpeed = mix(max(0.0, uBottomSpeed), max(0.0, uTopSpeed), smoothstep(0.0, 0.55, ypartClipped));
  float sparkTime = realTime * bottomSparkSpeed;

  vec3 flow = vec3(
    4.1 * (0.5 - xpart) * pow(ypartClippedn, 4.0) * max(0.25, uSpread),
    -2.0 * xfuel * pow(ypartClippedn, 64.0),
    0.0
  );
  flow.xy *= bottomSparkSpeed;

  float sparkGridSize = mix(44.0, 13.0, clamp(uDensity, 0.0, 1.0));
  vec2 sparkCoord = frag - vec2(0.0, 190.0 * sparkTime);
  sparkCoord -= (12.0 + 34.0 * uTurbulence) * noiseUV(0.01 * vec3(sparkCoord, 30.0 * sparkTime));
  sparkCoord += 100.0 * flow.xy;

  if (mod(sparkCoord.y / sparkGridSize, 2.0) < 1.0) {
    sparkCoord.x += 0.5 * sparkGridSize;
  }

  vec2 sparkGridIndex = floor(sparkCoord / sparkGridSize);
  float sparkRandom = prng(sparkGridIndex);
  float lifeLimit = 24.0 - 20.0 * sparkRandom;
  float sparkLife = min(10.0 * (1.0 - min((sparkGridIndex.y + (190.0 * sparkTime / sparkGridSize)) / lifeLimit, 1.0)), 1.0);

  vec3 sparks = vec3(0.0);
  if (sparkLife > 0.0) {
    float sparkSize = xfuel * xfuel * sparkRandom * (0.035 + 0.080 * uSize);
    float sparkRadians = 999.0 * sparkRandom * 6.2831853 + 2.0 * sparkTime;
    vec2 sparkCircular = vec2(sin(sparkRadians), cos(sparkRadians));
    vec2 sparkOffset = (0.5 - sparkSize) * sparkGridSize * sparkCircular;
    vec2 sparkModulus = mod(sparkCoord + sparkOffset, sparkGridSize) - 0.5 * vec2(sparkGridSize);

    float sparkLength = length(sparkModulus);
    float sparksGray = max(0.0, 1.0 - sparkLength / max(sparkSize * sparkGridSize, 0.001));
    sparksGray = pow(sparksGray, 1.25);

    vec3 ember = vec3(1.0, 0.30, 0.0);
    vec3 hot = vec3(1.0, 0.86, 0.42);
    sparks = sparkLife * sparksGray * mix(ember, hot, smoothstep(0.72, 1.0, sparkRandom));
  }

  float sideFade = smoothstep(0.0, 0.16, xpart) * (1.0 - smoothstep(0.84, 1.0, xpart));
  float topFade = 1.0 - smoothstep(0.80, 1.0, ypart);
  float bottomCrop = clamp(uBottomCrop, 0.0, 0.95);
  float bottomFade = smoothstep(bottomCrop, min(bottomCrop + 0.035, 1.0), ypart);
  float alpha = clamp(max(max(sparks.r, sparks.g), sparks.b) * sideFade * topFade * bottomFade * uIntensity, 0.0, 1.0);

  fragColor = vec4(sparks * sideFade * topFade * bottomFade * uIntensity, alpha);
}
