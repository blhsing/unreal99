namespace Unreal99.Rendering;

/// <summary>
/// All GLSL sources. Targets #version 330 core so the game runs on integrated GPUs.
/// The main pass is forward+PBR with MRT: attachment 0 = HDR colour, attachment 1 = view-space
/// normal (consumed by SSAO in post). Skinning is the same program with SKINNED defined.
/// </summary>
public static class Shaders
{
    public const int MaxPointLights = 24;
    public const int MaxBones = 64;

    private const string Header = "#version 330 core\n";

    // ---------------------------------------------------------------- shared GLSL snippets

    private const string PbrLib = """
        const float PI = 3.14159265359;

        float distributionGGX(vec3 N, vec3 H, float rough) {
            float a = rough * rough;
            float a2 = a * a;
            float ndh = max(dot(N, H), 0.0);
            float d = ndh * ndh * (a2 - 1.0) + 1.0;
            return a2 / max(PI * d * d, 1e-5);
        }

        float geometrySchlick(float ndv, float rough) {
            float k = (rough + 1.0);
            k = (k * k) / 8.0;
            return ndv / (ndv * (1.0 - k) + k);
        }

        float geometrySmith(float ndv, float ndl, float rough) {
            return geometrySchlick(ndv, rough) * geometrySchlick(ndl, rough);
        }

        vec3 fresnelSchlick(float cosTheta, vec3 f0) {
            return f0 + (1.0 - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
        }

        vec3 fresnelRoughness(float cosTheta, vec3 f0, float rough) {
            vec3 fr = max(vec3(1.0 - rough), f0);
            return f0 + (fr - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
        }

        // Direct lighting for one light. Returns radiance contribution.
        vec3 shadePunctual(vec3 N, vec3 V, vec3 L, vec3 radiance, vec3 albedo, float metallic, float rough) {
            vec3 H = normalize(V + L);
            float ndl = max(dot(N, L), 0.0);
            if (ndl <= 0.0) return vec3(0.0);
            float ndv = max(dot(N, V), 1e-4);
            vec3 f0 = mix(vec3(0.04), albedo, metallic);
            float ndf = distributionGGX(N, H, rough);
            float g = geometrySmith(ndv, ndl, rough);
            vec3 f = fresnelSchlick(max(dot(H, V), 0.0), f0);
            vec3 spec = (ndf * g * f) / max(4.0 * ndv * ndl, 1e-4);
            vec3 kd = (vec3(1.0) - f) * (1.0 - metallic);
            return (kd * albedo / PI + spec) * radiance * ndl;
        }
        """;

    private const string FogLib = """
        uniform vec3 uFogColor;
        uniform float uFogDensity;
        uniform float uFogHeightFalloff;
        uniform float uFogStartHeight;
        uniform vec3 uFogSunColor;

        // Exponential height fog with a forward-scattering lobe toward the sun.
        vec3 applyFog(vec3 color, vec3 worldPos, vec3 camPos, vec3 sunDir) {
            vec3 d = worldPos - camPos;
            float dist = length(d);
            if (dist < 0.001 || uFogDensity <= 0.0) return color;
            vec3 rd = d / dist;

            float hc = max(camPos.y - uFogStartHeight, -400.0);
            float falloff = uFogHeightFalloff;
            float t;
            if (abs(rd.y) > 1e-4) {
                t = uFogDensity * exp(-falloff * hc) * (1.0 - exp(-falloff * rd.y * dist)) / (falloff * rd.y);
            } else {
                t = uFogDensity * exp(-falloff * hc) * dist;
            }
            float f = 1.0 - exp(-max(t, 0.0));

            float sunAmount = max(dot(rd, -sunDir), 0.0);
            vec3 fog = mix(uFogColor, uFogSunColor, pow(sunAmount, 8.0));
            return mix(color, fog, clamp(f, 0.0, 1.0));
        }
        """;

    // ---------------------------------------------------------------- world (main forward pass)

    public const string WorldVert = Header + """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 aTangent;
        layout(location = 3) in vec2 aUv;
        layout(location = 4) in vec4 aColor;
        #ifdef SKINNED
        layout(location = 5) in ivec4 aBoneIndex;
        layout(location = 6) in vec4 aBoneWeight;
        uniform mat4 uBones[64];
        #endif

        uniform mat4 uModel;
        uniform mat4 uViewProj;
        uniform mat4 uView;
        uniform mat4 uLightViewProj;
        uniform vec2 uUvScale;
        uniform vec2 uUvOffset;

        out vec3 vWorldPos;
        out vec3 vNormal;
        out vec4 vTangent;
        out vec2 vUv;
        out vec4 vColor;
        out vec4 vShadowPos;
        out vec3 vViewNormal;

        void main() {
            vec4 localPos = vec4(aPos, 1.0);
            vec3 localNrm = aNormal;
            vec3 localTan = aTangent.xyz;
        #ifdef SKINNED
            mat4 skin = uBones[aBoneIndex.x] * aBoneWeight.x
                      + uBones[aBoneIndex.y] * aBoneWeight.y
                      + uBones[aBoneIndex.z] * aBoneWeight.z
                      + uBones[aBoneIndex.w] * aBoneWeight.w;
            localPos = skin * localPos;
            localNrm = mat3(skin) * localNrm;
            localTan = mat3(skin) * localTan;
        #endif
            vec4 world = uModel * localPos;
            vWorldPos = world.xyz;
            mat3 nm = mat3(uModel);
            vNormal = normalize(nm * localNrm);
            vTangent = vec4(normalize(nm * localTan), aTangent.w);
            vUv = aUv * uUvScale + uUvOffset;
            vColor = aColor;
            vShadowPos = uLightViewProj * world;
            vViewNormal = normalize(mat3(uView) * vNormal);
            gl_Position = uViewProj * world;
        }
        """;

    public const string WorldFrag = Header + PbrLib + FogLib + """
        in vec3 vWorldPos;
        in vec3 vNormal;
        in vec4 vTangent;
        in vec2 vUv;
        in vec4 vColor;
        in vec4 vShadowPos;
        in vec3 vViewNormal;

        layout(location = 0) out vec4 oColor;
        layout(location = 1) out vec4 oNormal;

        uniform sampler2D uAlbedoTex;    // rgb = albedo, a = emissive mask
        uniform sampler2D uNormalTex;    // rgb = tangent-space normal, a = roughness
        uniform sampler2D uShadowMap;
        uniform samplerCube uEnvMap;

        uniform vec3 uCamPos;
        uniform vec4 uBaseColor;
        uniform float uMetallic;
        uniform float uRoughnessScale;
        uniform vec3 uEmissive;
        uniform float uNormalStrength;
        uniform float uAlpha;

        uniform vec3 uSunDir;            // points *from* the sun toward the world
        uniform vec3 uSunColor;
        uniform vec3 uAmbientSky;
        uniform vec3 uAmbientGround;
        uniform float uShadowTexel;
        uniform float uShadowStrength;
        uniform float uEnvIntensity;

        uniform int uNumLights;
        uniform vec4 uLightPosRadius[24];
        uniform vec4 uLightColorIntensity[24];

        // Rim term used on characters so silhouettes read against dark arenas.
        uniform float uRimStrength;
        uniform vec3 uRimColor;

        float sampleShadow(vec3 N, vec3 L) {
            vec3 proj = vShadowPos.xyz / max(vShadowPos.w, 1e-5);
            proj = proj * 0.5 + 0.5;
            if (proj.z > 1.0 || proj.z < 0.0) return 1.0;

            // Fade the map out at its border instead of hard-clipping to lit.
            vec2 e = min(proj.xy, 1.0 - proj.xy);
            float edge = clamp(min(e.x, e.y) * 12.0, 0.0, 1.0);
            if (edge <= 0.0) return 1.0;

            float ndl = max(dot(N, L), 0.0);
            float bias = max(0.0035 * (1.0 - ndl), 0.0007);
            float sum = 0.0;
            for (int y = -1; y <= 1; ++y) {
                for (int x = -1; x <= 1; ++x) {
                    float d = texture(uShadowMap, proj.xy + vec2(x, y) * uShadowTexel).r;
                    sum += (proj.z - bias > d) ? 0.0 : 1.0;
                }
            }
            float s = sum / 9.0;
            return mix(1.0, s, edge);
        }

        void main() {
            vec4 albedoTex = texture(uAlbedoTex, vUv);
            vec3 albedo = albedoTex.rgb * uBaseColor.rgb * vColor.rgb;
            float emissiveMask = albedoTex.a;

            vec4 nrmTex = texture(uNormalTex, vUv);
            float rough = clamp(nrmTex.a * uRoughnessScale, 0.045, 1.0);

            vec3 N = normalize(vNormal);
            vec3 T = normalize(vTangent.xyz - N * dot(N, vTangent.xyz));
            vec3 B = cross(N, T) * vTangent.w;
            vec3 tn = nrmTex.rgb * 2.0 - 1.0;
            tn.xy *= uNormalStrength;
            N = normalize(mat3(T, B, N) * normalize(tn));

            vec3 V = normalize(uCamPos - vWorldPos);
            float ao = vColor.a;

            // --- sun ---
            vec3 L = -uSunDir;
            float shadow = mix(1.0, sampleShadow(N, L), uShadowStrength);
            vec3 lit = shadePunctual(N, V, L, uSunColor * shadow, albedo, uMetallic, rough);

            // --- dynamic point lights (weapons, projectiles, level lights) ---
            for (int i = 0; i < uNumLights; ++i) {
                vec3 lp = uLightPosRadius[i].xyz;
                float radius = uLightPosRadius[i].w;
                vec3 dv = lp - vWorldPos;
                float d2 = dot(dv, dv);
                float r2 = radius * radius;
                if (d2 > r2) continue;
                float d = sqrt(max(d2, 1e-6));
                // Windowed inverse-square: physically shaped but reaches exactly zero at the
                // radius. Written as a squared ratio to avoid a per-light pow().
                float x = d2 / r2;
                float w = clamp(1.0 - x * x, 0.0, 1.0);
                // Normalised so `intensity` means "brightness at a quarter of the radius", which
                // keeps authoring intuitive across lamps with wildly different reach.
                float refDist = radius * 0.25;
                float atten = (w * w) / (d2 + 0.25) * (refDist * refDist + 0.25);
                vec3 radiance = uLightColorIntensity[i].rgb * uLightColorIntensity[i].w * atten;
                lit += shadePunctual(N, V, dv / d, radiance, albedo, uMetallic, rough);
            }

            // --- ambient: hemisphere diffuse + env-map specular ---
            float hemi = N.y * 0.5 + 0.5;
            vec3 ambientDiffuse = mix(uAmbientGround, uAmbientSky, hemi) * albedo * ao;
            float ndv = max(dot(N, V), 1e-4);
            vec3 f0 = mix(vec3(0.04), albedo, uMetallic);
            vec3 fr = fresnelRoughness(ndv, f0, rough);
            vec3 R = reflect(-V, N);
            float lod = rough * 5.0;
            vec3 envSpec = textureLod(uEnvMap, R, lod).rgb * uEnvIntensity;
            vec3 ambient = ambientDiffuse * (1.0 - uMetallic) + envSpec * fr * ao;

            // --- rim ---
            float rim = pow(1.0 - ndv, 3.0) * uRimStrength;
            vec3 color = lit + ambient + uRimColor * rim;

            // --- emissive ---
            color += uEmissive * emissiveMask;

            color = applyFog(color, vWorldPos, uCamPos, uSunDir);

            oColor = vec4(color, uAlpha * uBaseColor.a);
            oNormal = vec4(vViewNormal * 0.5 + 0.5, 1.0);
        }
        """;

    public static string WorldVertSkinned => Header + "#define SKINNED 1\n" + WorldVert.Substring(Header.Length);

    // ---------------------------------------------------------------- shadow depth pass

    public const string ShadowVert = Header + """
        layout(location = 0) in vec3 aPos;
        #ifdef SKINNED
        layout(location = 5) in ivec4 aBoneIndex;
        layout(location = 6) in vec4 aBoneWeight;
        uniform mat4 uBones[64];
        #endif
        uniform mat4 uModel;
        uniform mat4 uLightViewProj;
        void main() {
            vec4 p = vec4(aPos, 1.0);
        #ifdef SKINNED
            mat4 skin = uBones[aBoneIndex.x] * aBoneWeight.x
                      + uBones[aBoneIndex.y] * aBoneWeight.y
                      + uBones[aBoneIndex.z] * aBoneWeight.z
                      + uBones[aBoneIndex.w] * aBoneWeight.w;
            p = skin * p;
        #endif
            gl_Position = uLightViewProj * uModel * p;
        }
        """;

    public static string ShadowVertSkinned => Header + "#define SKINNED 1\n" + ShadowVert.Substring(Header.Length);

    public const string ShadowFrag = Header + """
        void main() { }
        """;

    // ---------------------------------------------------------------- procedural sky

    public const string SkyVert = Header + """
        layout(location = 0) in vec3 aPos;
        uniform mat4 uViewProjNoTranslate;
        out vec3 vDir;
        void main() {
            vDir = aPos;
            vec4 p = uViewProjNoTranslate * vec4(aPos, 1.0);
            gl_Position = p.xyww;   // force depth to the far plane
        }
        """;

    public const string SkyFrag = Header + """
        in vec3 vDir;
        layout(location = 0) out vec4 oColor;
        layout(location = 1) out vec4 oNormal;

        uniform vec3 uSunDir;
        uniform vec3 uSunColor;
        uniform vec3 uSkyTop;
        uniform vec3 uSkyHorizon;
        uniform vec3 uSkyGround;
        uniform float uTime;
        uniform float uStarStrength;
        uniform float uCloudStrength;

        float hash(vec3 p) {
            p = fract(p * vec3(0.1031, 0.1030, 0.0973));
            p += dot(p, p.yxz + 33.33);
            return fract((p.x + p.y) * p.z);
        }

        float noise(vec3 x) {
            vec3 i = floor(x);
            vec3 f = fract(x);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(mix(hash(i + vec3(0,0,0)), hash(i + vec3(1,0,0)), f.x),
                           mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
                       mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x),
                           mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y), f.z);
        }

        float fbm(vec3 p) {
            float v = 0.0, a = 0.5;
            for (int i = 0; i < 4; ++i) { v += a * noise(p); p *= 2.03; a *= 0.5; }
            return v;
        }

        void main() {
            vec3 d = normalize(vDir);
            float h = d.y;

            // Base gradient: ground haze -> horizon band -> zenith.
            vec3 sky = mix(uSkyHorizon, uSkyTop, pow(clamp(h, 0.0, 1.0), 0.55));
            sky = mix(sky, uSkyGround, clamp(-h * 3.0, 0.0, 1.0));

            // Stars, masked out near the horizon and washed out by the sun.
            if (uStarStrength > 0.0 && h > 0.0) {
                vec3 sp = d * 220.0;
                float s = hash(floor(sp));
                float star = smoothstep(0.9965, 1.0, s) * uStarStrength;
                float tw = 0.65 + 0.35 * sin(uTime * 2.7 + s * 90.0);
                sky += vec3(star * tw) * smoothstep(0.0, 0.35, h);
            }

            // Drifting cloud deck projected onto the dome.
            if (uCloudStrength > 0.0 && h > 0.01) {
                vec3 cp = d / max(h, 0.06);
                cp.xz += uTime * 0.012;
                float c = fbm(cp * 0.55);
                c = smoothstep(0.48, 0.95, c) * uCloudStrength * smoothstep(0.0, 0.22, h);
                vec3 cloudCol = mix(uSkyHorizon * 1.3, uSunColor * 0.55 + vec3(0.35), 0.5);
                sky = mix(sky, cloudCol, c);
            }

            // Sun disc + broad halo.
            float sd = max(dot(d, -uSunDir), 0.0);
            sky += uSunColor * pow(sd, 900.0) * 40.0;
            sky += uSunColor * pow(sd, 12.0) * 0.30;
            sky += uSunColor * pow(sd, 3.0) * 0.06;

            oColor = vec4(sky, 1.0);
            oNormal = vec4(0.5, 0.5, 1.0, 0.0);
        }
        """;

    // ---------------------------------------------------------------- full-screen triangle

    public const string FullscreenVert = Header + """
        out vec2 vUv;
        void main() {
            // Oversized triangle covering the viewport; no vertex buffer required.
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    // ---------------------------------------------------------------- SSAO

    public const string SsaoFrag = Header + """
        in vec2 vUv;
        out vec4 oColor;

        uniform sampler2D uDepthTex;
        uniform sampler2D uNormalTex;
        uniform mat4 uProj;
        uniform mat4 uInvProj;
        uniform vec2 uNoiseScale;
        uniform float uRadius;
        uniform float uBias;
        uniform float uNear;
        uniform float uFar;
        uniform vec3 uKernel[16];
        uniform int uSamples;
        uniform float uTime;

        vec3 viewPosFromDepth(vec2 uv) {
            float d = texture(uDepthTex, uv).r * 2.0 - 1.0;
            vec4 clip = vec4(uv * 2.0 - 1.0, d, 1.0);
            vec4 view = uInvProj * clip;
            return view.xyz / view.w;
        }

        float rand(vec2 c) { return fract(sin(dot(c, vec2(12.9898, 78.233))) * 43758.5453); }

        void main() {
            float rawDepth = texture(uDepthTex, vUv).r;
            if (rawDepth >= 0.9999) { oColor = vec4(1.0); return; }

            vec3 P = viewPosFromDepth(vUv);
            vec3 N = normalize(texture(uNormalTex, vUv).xyz * 2.0 - 1.0);

            float ang = rand(vUv * uNoiseScale) * 6.2831853;
            vec3 randVec = vec3(cos(ang), sin(ang), 0.0);
            vec3 T = normalize(randVec - N * dot(randVec, N));
            vec3 B = cross(N, T);
            mat3 tbn = mat3(T, B, N);

            float occlusion = 0.0;
            for (int i = 0; i < uSamples; ++i) {
                vec3 sp = P + (tbn * uKernel[i]) * uRadius;
                vec4 off = uProj * vec4(sp, 1.0);
                off.xyz /= off.w;
                vec2 suv = off.xy * 0.5 + 0.5;
                if (suv.x < 0.0 || suv.x > 1.0 || suv.y < 0.0 || suv.y > 1.0) continue;
                float sampleDepth = viewPosFromDepth(suv).z;
                float rangeCheck = smoothstep(0.0, 1.0, uRadius / max(abs(P.z - sampleDepth), 1e-4));
                occlusion += (sampleDepth >= sp.z + uBias ? 1.0 : 0.0) * rangeCheck;
            }
            oColor = vec4(vec3(clamp(1.0 - occlusion / float(uSamples), 0.0, 1.0)), 1.0);
        }
        """;

    public const string BlurFrag = Header + """
        in vec2 vUv;
        out vec4 oColor;
        uniform sampler2D uTex;
        uniform vec2 uTexel;
        uniform vec2 uDir;
        uniform float uRadius;

        void main() {
            // 9-tap Gaussian; weights sum to 1.
            float w[5] = float[](0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162);
            vec4 sum = texture(uTex, vUv) * w[0];
            for (int i = 1; i < 5; ++i) {
                vec2 o = uDir * uTexel * float(i) * uRadius;
                sum += texture(uTex, vUv + o) * w[i];
                sum += texture(uTex, vUv - o) * w[i];
            }
            oColor = sum;
        }
        """;

    public const string BoxBlurFrag = Header + """
        in vec2 vUv;
        out vec4 oColor;
        uniform sampler2D uTex;
        uniform vec2 uTexel;
        void main() {
            vec4 s = vec4(0.0);
            for (int y = -2; y <= 2; ++y)
                for (int x = -2; x <= 2; ++x)
                    s += texture(uTex, vUv + vec2(x, y) * uTexel);
            oColor = s / 25.0;
        }
        """;

    // ---------------------------------------------------------------- bloom

    public const string BrightFrag = Header + """
        in vec2 vUv;
        out vec4 oColor;
        uniform sampler2D uTex;
        uniform float uThreshold;
        uniform float uSoftKnee;
        void main() {
            vec3 c = texture(uTex, vUv).rgb;
            float br = max(c.r, max(c.g, c.b));
            float knee = uThreshold * uSoftKnee + 1e-5;
            float soft = clamp(br - uThreshold + knee, 0.0, 2.0 * knee);
            soft = soft * soft / (4.0 * knee);
            float contrib = max(soft, br - uThreshold) / max(br, 1e-5);
            oColor = vec4(c * contrib, 1.0);
        }
        """;

    /// <summary>Anamorphic horizontal streak; three passes at increasing stride give a wide flare.</summary>
    public const string StreakFrag = Header + """
        in vec2 vUv;
        out vec4 oColor;
        uniform sampler2D uTex;
        uniform vec2 uTexel;
        uniform float uStride;
        uniform vec3 uTint;
        void main() {
            vec3 sum = vec3(0.0);
            float total = 0.0;
            for (int i = -6; i <= 6; ++i) {
                float w = exp(-float(i * i) * 0.06);
                sum += texture(uTex, vUv + vec2(float(i) * uStride * uTexel.x, 0.0)).rgb * w;
                total += w;
            }
            oColor = vec4(sum / total * uTint, 1.0);
        }
        """;

    /// <summary>Radial light shafts, marching toward the sun's screen position.</summary>
    public const string GodRayFrag = Header + """
        in vec2 vUv;
        out vec4 oColor;
        uniform sampler2D uTex;
        uniform vec2 uSunUv;
        uniform float uDensity;
        uniform float uDecay;
        uniform float uWeight;
        uniform float uExposure;
        void main() {
            vec2 uv = vUv;
            vec2 delta = (uv - uSunUv) * (uDensity / 24.0);
            float illum = 1.0;
            vec3 accum = vec3(0.0);
            for (int i = 0; i < 24; ++i) {
                uv -= delta;
                vec3 s = texture(uTex, clamp(uv, vec2(0.0), vec2(1.0))).rgb;
                accum += s * illum * uWeight;
                illum *= uDecay;
            }
            oColor = vec4(accum * uExposure, 1.0);
        }
        """;

    // ---------------------------------------------------------------- final composite

    public const string CompositeFrag = Header + """
        in vec2 vUv;
        out vec4 oColor;

        uniform sampler2D uScene;
        uniform sampler2D uBloom;
        uniform sampler2D uStreak;
        uniform sampler2D uGodRays;
        uniform sampler2D uSsao;

        uniform float uExposure;
        uniform float uBloomIntensity;
        uniform float uStreakIntensity;
        uniform float uGodRayIntensity;
        uniform float uSsaoStrength;
        uniform float uVignette;
        uniform float uChromatic;
        uniform float uGrain;
        uniform float uTime;
        uniform float uSaturation;
        uniform float uContrast;
        uniform vec3 uColorLift;
        uniform vec3 uColorGain;
        uniform float uDamageFlash;
        uniform vec3 uDamageColor;
        uniform vec2 uTexel;
        uniform float uFxaa;

        // Narkowicz ACES approximation: cheap and holds highlights well.
        vec3 acesFilm(vec3 x) {
            const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
            return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
        }

        float luma(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

        vec3 fxaa(sampler2D tex, vec2 uv, vec2 texel) {
            vec3 rgbM = texture(tex, uv).rgb;
            vec3 rgbNW = texture(tex, uv + vec2(-1.0, -1.0) * texel).rgb;
            vec3 rgbNE = texture(tex, uv + vec2( 1.0, -1.0) * texel).rgb;
            vec3 rgbSW = texture(tex, uv + vec2(-1.0,  1.0) * texel).rgb;
            vec3 rgbSE = texture(tex, uv + vec2( 1.0,  1.0) * texel).rgb;
            float lM = luma(rgbM), lNW = luma(rgbNW), lNE = luma(rgbNE), lSW = luma(rgbSW), lSE = luma(rgbSE);
            float lMin = min(lM, min(min(lNW, lNE), min(lSW, lSE)));
            float lMax = max(lM, max(max(lNW, lNE), max(lSW, lSE)));
            if (lMax - lMin < max(0.0312, lMax * 0.125)) return rgbM;

            vec2 dir = vec2(-((lNW + lNE) - (lSW + lSE)), ((lNW + lSW) - (lNE + lSE)));
            float dirReduce = max((lNW + lNE + lSW + lSE) * 0.03125, 0.0078125);
            float rcpDirMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);
            dir = clamp(dir * rcpDirMin, vec2(-8.0), vec2(8.0)) * texel;

            vec3 rgbA = 0.5 * (texture(tex, uv + dir * (1.0 / 3.0 - 0.5)).rgb +
                               texture(tex, uv + dir * (2.0 / 3.0 - 0.5)).rgb);
            vec3 rgbB = rgbA * 0.5 + 0.25 * (texture(tex, uv - dir * 0.5).rgb +
                                             texture(tex, uv + dir * 0.5).rgb);
            float lB = luma(rgbB);
            return (lB < lMin || lB > lMax) ? rgbA : rgbB;
        }

        void main() {
            vec2 uv = vUv;
            vec2 fromCenter = uv - 0.5;
            float r2 = dot(fromCenter, fromCenter);

            // Chromatic aberration grows toward the frame edge.
            vec3 scene;
            if (uChromatic > 0.0) {
                vec2 ca = fromCenter * uChromatic * r2;
                scene.r = texture(uScene, uv + ca).r;
                scene.g = texture(uScene, uv).g;
                scene.b = texture(uScene, uv - ca).b;
            } else {
                scene = texture(uScene, uv).rgb;
            }

            scene += texture(uBloom, uv).rgb * uBloomIntensity;
            scene += texture(uStreak, uv).rgb * uStreakIntensity;
            scene += texture(uGodRays, uv).rgb * uGodRayIntensity;

            if (uSsaoStrength > 0.0) {
                float ao = texture(uSsao, uv).r;
                scene *= mix(1.0, ao, uSsaoStrength);
            }

            scene *= uExposure;
            vec3 col = acesFilm(scene);

            // Grade: lift/gain, contrast around mid grey, saturation.
            col = col * uColorGain + uColorLift;
            col = (col - 0.5) * uContrast + 0.5;
            float l = luma(col);
            col = mix(vec3(l), col, uSaturation);

            if (uDamageFlash > 0.0)
                col = mix(col, uDamageColor, clamp(uDamageFlash, 0.0, 0.85));

            float vig = 1.0 - uVignette * smoothstep(0.15, 0.85, r2 * 2.0);
            col *= vig;

            if (uGrain > 0.0) {
                float n = fract(sin(dot(uv * 1024.0 + uTime, vec2(12.9898, 78.233))) * 43758.5453);
                col += (n - 0.5) * uGrain;
            }

            col = clamp(col, 0.0, 1.0);
            // Encode to sRGB here; the default framebuffer is treated as linear-write.
            col = pow(col, vec3(1.0 / 2.2));
            oColor = vec4(col, 1.0);
        }
        """;

    /// <summary>Second composite stage: FXAA over the already-graded LDR image.</summary>
    public const string FxaaFrag = Header + """
        in vec2 vUv;
        out vec4 oColor;
        uniform sampler2D uTex;
        uniform vec2 uTexel;
        uniform float uEnabled;

        float luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

        void main() {
            vec3 rgbM = texture(uTex, vUv).rgb;
            if (uEnabled < 0.5) { oColor = vec4(rgbM, 1.0); return; }
            vec2 texel = uTexel;
            vec3 rgbNW = texture(uTex, vUv + vec2(-1.0, -1.0) * texel).rgb;
            vec3 rgbNE = texture(uTex, vUv + vec2( 1.0, -1.0) * texel).rgb;
            vec3 rgbSW = texture(uTex, vUv + vec2(-1.0,  1.0) * texel).rgb;
            vec3 rgbSE = texture(uTex, vUv + vec2( 1.0,  1.0) * texel).rgb;
            float lM = luma(rgbM), lNW = luma(rgbNW), lNE = luma(rgbNE), lSW = luma(rgbSW), lSE = luma(rgbSE);
            float lMin = min(lM, min(min(lNW, lNE), min(lSW, lSE)));
            float lMax = max(lM, max(max(lNW, lNE), max(lSW, lSE)));
            if (lMax - lMin < max(0.0312, lMax * 0.125)) { oColor = vec4(rgbM, 1.0); return; }

            vec2 dir = vec2(-((lNW + lNE) - (lSW + lSE)), ((lNW + lSW) - (lNE + lSE)));
            float dirReduce = max((lNW + lNE + lSW + lSE) * 0.03125, 0.0078125);
            float rcpDirMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);
            dir = clamp(dir * rcpDirMin, vec2(-8.0), vec2(8.0)) * texel;

            vec3 rgbA = 0.5 * (texture(uTex, vUv + dir * (1.0 / 3.0 - 0.5)).rgb +
                               texture(uTex, vUv + dir * (2.0 / 3.0 - 0.5)).rgb);
            vec3 rgbB = rgbA * 0.5 + 0.25 * (texture(uTex, vUv - dir * 0.5).rgb +
                                             texture(uTex, vUv + dir * 0.5).rgb);
            float lB = luma(rgbB);
            oColor = vec4((lB < lMin || lB > lMax) ? rgbA : rgbB, 1.0);
        }
        """;

    // ---------------------------------------------------------------- particles (billboards)

    /// <summary>Instanced billboards: one vertex stream of corner ids, one per-particle stream.</summary>
    public const string ParticleVert = Header + """
        layout(location = 0) in vec2 aCorner;   // per-vertex, divisor 0: (-1,-1) .. (1,1)
        layout(location = 1) in vec3 aCenter;   // per-instance
        layout(location = 2) in vec4 aColor;    // per-instance
        layout(location = 3) in vec3 aParams;   // per-instance: x = size, y = rotation, z = atlas index
        uniform mat4 uViewProj;
        uniform vec3 uCamRight;
        uniform vec3 uCamUp;
        out vec4 vColor;
        out vec2 vUv;
        out float vAtlas;
        void main() {
            vUv = aCorner * 0.5 + 0.5;
            float s = sin(aParams.y), c = cos(aParams.y);
            vec2 ro = vec2(aCorner.x * c - aCorner.y * s, aCorner.x * s + aCorner.y * c) * aParams.x;
            vec3 world = aCenter + uCamRight * ro.x + uCamUp * ro.y;
            vColor = aColor;
            vAtlas = aParams.z;
            gl_Position = uViewProj * vec4(world, 1.0);
        }
        """;

    public const string ParticleFrag = Header + """
        in vec4 vColor;
        in vec2 vUv;
        in float vAtlas;
        layout(location = 0) out vec4 oColor;
        layout(location = 1) out vec4 oNormal;
        uniform sampler2D uAtlas;
        uniform float uAtlasCols;
        void main() {
            float idx = floor(vAtlas + 0.5);
            float cols = uAtlasCols;
            vec2 cell = vec2(mod(idx, cols), floor(idx / cols));
            vec2 uv = (cell + vUv) / cols;
            vec4 t = texture(uAtlas, uv);
            vec4 c = t * vColor;
            if (c.a < 0.004) discard;
            oColor = c;
            oNormal = vec4(0.5, 0.5, 1.0, 0.0);
        }
        """;

    // ---------------------------------------------------------------- beams / tracers / decals

    public const string UnlitVert = Header + """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 aTangent;
        layout(location = 3) in vec2 aUv;
        layout(location = 4) in vec4 aColor;
        uniform mat4 uModel;
        uniform mat4 uViewProj;
        out vec2 vUv;
        out vec4 vColor;
        out vec3 vWorldPos;
        void main() {
            vec4 w = uModel * vec4(aPos, 1.0);
            vWorldPos = w.xyz;
            vUv = aUv;
            vColor = aColor;
            gl_Position = uViewProj * w;
        }
        """;

    public const string UnlitFrag = Header + """
        in vec2 vUv;
        in vec4 vColor;
        in vec3 vWorldPos;
        layout(location = 0) out vec4 oColor;
        layout(location = 1) out vec4 oNormal;
        uniform sampler2D uTex;
        uniform vec4 uTint;
        uniform float uUseTexture;
        uniform vec3 uCamPos;
        uniform float uFadeDistance;
        void main() {
            vec4 c = vColor * uTint;
            if (uUseTexture > 0.5) c *= texture(uTex, vUv);
            if (uFadeDistance > 0.0) {
                float d = distance(vWorldPos, uCamPos);
                c.a *= clamp(d / uFadeDistance, 0.0, 1.0);
            }
            if (c.a < 0.004) discard;
            oColor = c;
            oNormal = vec4(0.5, 0.5, 1.0, 0.0);
        }
        """;

    // ---------------------------------------------------------------- 2D UI (HUD, text, menus)

    public const string UiVert = Header + """
        layout(location = 0) in vec2 aPos;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec4 aColor;
        uniform mat4 uProj;
        out vec2 vUv;
        out vec4 vColor;
        void main() {
            vUv = aUv;
            vColor = aColor;
            gl_Position = uProj * vec4(aPos, 0.0, 1.0);
        }
        """;

    public const string UiFrag = Header + """
        in vec2 vUv;
        in vec4 vColor;
        out vec4 oColor;
        uniform sampler2D uTex;
        uniform float uIsText;      // 1 = sample coverage from the red channel
        uniform float uUseTexture;
        void main() {
            vec4 c = vColor;
            if (uIsText > 0.5) {
                float a = texture(uTex, vUv).r;
                c.a *= a;
            } else if (uUseTexture > 0.5) {
                c *= texture(uTex, vUv);
            }
            if (c.a < 0.002) discard;
            oColor = c;
        }
        """;

    /// <summary>Blits a viewport's finished image into the correct split-screen rectangle.</summary>
    public const string BlitFrag = Header + """
        in vec2 vUv;
        out vec4 oColor;
        uniform sampler2D uTex;
        void main() { oColor = vec4(texture(uTex, vUv).rgb, 1.0); }
        """;

    // ---------------------------------------------------------------- silhouette (studio alpha)

    /// <summary>
    /// Positions only. Used to stamp a subject's coverage into the alpha channel of an already
    /// composited frame, so a documentation turntable can be exported with a real transparent
    /// background instead of being keyed out of a flat colour afterwards.
    /// </summary>
    public const string SilhouetteVert = Header + """
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMvp;
        void main() { gl_Position = uMvp * vec4(aPos, 1.0); }
        """;

    public const string SilhouetteFrag = Header + """
        out vec4 oColor;
        void main() { oColor = vec4(0.0, 0.0, 0.0, 1.0); }
        """;
}
