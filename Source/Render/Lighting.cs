using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// One light setup for every scene shader (see <see cref="Lighting"/>): the key light, hemisphere ambient, fog strength
/// and the sky gradient fog fades towards. <see cref="Lighting.Default"/> is the game's classic look (maps without a sky);
/// <see cref="SkySettings.Light"/> builds one from a map's "skybox" section.
/// </summary>
/// <param name="SunDir">Unit vector towards the key light (the sun, or the moon at night).</param>
/// <param name="SunColor">Key light colour times strength.</param>
/// <param name="SkyFog">Fog takes the sky gradient's colour along each view ray (false: the flat fog colour).</param>
/// <param name="HazeDir">Towards the real sun (the horizon glow is on its side, even when the moon is the key light).</param>
/// <param name="Haze">Horizon glow colour (xyz) and strength (w).</param>
/// <param name="OuterFog">
/// Flat maps with a boundary (<see cref="Map.OuterFog"/>): centre x, z and a start / end radius (end &lt;= start: off).
/// Past the start radius everything fades fully (not just <see cref="FogMax"/>) into the sky's horizon colour, measured
/// from the map's centre so it doesn't depend on the camera, and the sky gradient below the horizon is the horizon
/// colour, so the far ground meets the sky without a dark band. Zero for every other map (their look is unchanged).
/// </param>
/// <param name="OuterStarts">
/// With <see cref="OuterFog"/>: where the fade starts in each direction (<see cref="Map.OuterFogStarts"/>); the fade
/// then runs over OuterFog's end minus start. Null: the start radius OuterFog.z in every direction.
/// </param>
/// <param name="Up">
/// Which way is up for the hemisphere ambient (sky light from above, ground bounce from below). Zero: world +Y. On a
/// planetoid in a star system it is away from the ground's centre at the camera (SkySettings.Light).
/// </param>
public readonly record struct LightSetup(
    Vector3 SunDir, Vector3 SunColor, Vector3 AmbSky, Vector3 AmbGround, float FogMax,
    bool SkyFog, Vector3 Zenith, Vector3 Horizon, Vector3 Ground, Vector3 HazeDir, Vector4 Haze, Vector4 OuterFog = default,
    float[]? OuterStarts = null, Vector3 Up = default);

/// <summary>
/// The scene's one light setup, shared by every renderer so boxes, terrain, foliage, props and meshes are lit the
/// same way: a sun (direction + colour), hemisphere ambient (sky light from above, ground bounce from below,
/// tinted a little by the map's sky colour) and linear distance fog towards the sky.
/// All of it is cheap per-pixel maths, no shadow maps.
///
/// Shaders include <see cref="Glsl"/> right after their #version line and call <see cref="Upload"/> once after
/// linking. Upload remembers the program, and <see cref="Use"/> (called when the map's sky changes, see
/// <see cref="SkyRenderer.Use"/>) re-sends the light to every remembered program, so the per-frame cost is nothing.
/// Per-frame values (camera position, fog colour and range) are set by the renderer that owns the program.
///
/// Maps without a "skybox" section get <see cref="Default"/>: exactly the constants the game always had, and fog
/// towards the flat sky colour. Maps with one get their sky's sun and ambient, and fog that fades into the sky's
/// gradient in each direction (SkyGradient below, the same maths as <see cref="SkySettings.Gradient(Vector3)"/>).
/// </summary>
public static unsafe class Lighting
{
    /// <summary>Unit vector pointing <em>towards</em> the classic sun (high in the sky, from the north-west).</summary>
    public static readonly Vector3 DefaultSunDirection = Vector3.Normalize(new Vector3(-0.4f, 0.8f, -0.3f));

    /// <summary>Classic sunlight colour, slightly warm. Multiplied by <see cref="DefaultSunStrength"/>.</summary>
    public static readonly Vector3 DefaultSunColor = new(1.0f, 0.95f, 0.86f);

    /// <summary>How bright full sun on a surface facing it is, on top of the ambient light.</summary>
    public const float DefaultSunStrength = 0.55f;

    /// <summary>Ambient from the sky (faces pointing up), a little cool.</summary>
    public static readonly Vector3 DefaultSkyAmbient = new(0.50f, 0.52f, 0.56f);

    /// <summary>Ambient bounced off the ground (faces pointing down), a little warm.</summary>
    public static readonly Vector3 DefaultGroundAmbient = new(0.40f, 0.38f, 0.34f);

    /// <summary>How far the sky ambient leans towards the hue of the map's sky colour (0 = not at all).</summary>
    public const float SkyTint = 0.2f;

    /// <summary>Fog never fully hides things: at the fog end distance colours are this much sky.</summary>
    public const float DefaultFogMax = 0.85f;

    /// <summary>The sky colour assumed before a map sets its own (the same as <see cref="Map.Sky"/>'s default).</summary>
    public static readonly Vector3 DefaultSky = new(0.36f, 0.42f, 0.50f);

    /// <summary>The classic light: what every map without a "skybox" section is lit with.</summary>
    public static readonly LightSetup Default = new(DefaultSunDirection, DefaultSunColor * DefaultSunStrength, DefaultSkyAmbient, DefaultGroundAmbient,
        DefaultFogMax, false, DefaultSky, DefaultSky, DefaultSky, DefaultSunDirection, Vector4.Zero);

    /// <summary>The light every registered program has now.</summary>
    public static LightSetup Current { get; private set; } = Default;

    /// <summary>How many times the light has changed (for logs and tests).</summary>
    public static int Changes { get; private set; }

    private static readonly List<uint> Programs = new();

    /// <summary>
    /// GLSL shared by the scene shaders: uniforms plus ApplyLight (hemisphere ambient + wrapped sun diffuse),
    /// SkyGradient and ApplyFog. Paste it after the "#version 410 core" line (RenderCaps.Glsl swaps that line for the context's prelude).
    /// </summary>
    public const string Glsl = """
        uniform vec3 uSunDir;     // towards the sun
        uniform vec3 uSunColor;   // colour * strength
        uniform vec3 uAmbSky;
        uniform vec3 uAmbGround;
        uniform vec3 uUp;         // up for the ambient's sky and ground halves (world +Y but on a planetoid: LightSetup.Up)
        uniform float uSkyTint;
        uniform float uFogMax;
        uniform vec3 uCam;
        uniform vec3 uFog;        // fog / sky colour
        uniform vec2 uFogRange;   // start, end (metres); end <= start turns fog off
        uniform float uSkyFog;    // 1: fog takes the sky gradient's colour along the view ray (maps with a sky)
        uniform vec3 uSkyZenith;
        uniform vec3 uSkyHorizon;
        uniform vec3 uSkyGround;
        uniform vec3 uSkyHazeDir; // towards the real sun
        uniform vec4 uSkyHaze;    // horizon glow colour, strength
        uniform vec4 uOuterFog;   // flat shaped maps: centre x, z, start / end radius (w <= z: off); see LightSetup.OuterFog
        uniform vec4 uOuterR[16]; // with uOuterFog: the fade's start radius per direction, 64 packed 4 to a vec4 (Map.OuterFogStarts)

        // wrap: 0 = plain Lambert (solid surfaces), ~0.5 = leaves (light leaks round the edges)
        vec3 ApplyLightVis(vec3 albedo, vec3 n, float wrap, vec3 sunColor)
        {
            // Lean the sky light towards the sky's hue (not its brightness, so dark skies don't darken everything).
            float skyLum = max(dot(uFog, vec3(0.3333)), 0.05);
            vec3 sky = mix(uAmbSky, uFog / skyLum * dot(uAmbSky, vec3(0.3333)), uSkyTint);
            vec3 amb = mix(uAmbGround, sky, dot(n, uUp) * 0.5 + 0.5);
            float diff = max((dot(n, uSunDir) + wrap) / (1.0 + wrap), 0.0);
            return albedo * (amb + sunColor * diff);
        }
        vec3 ApplyLight(vec3 albedo, vec3 n, float wrap) { return ApplyLightVis(albedo, n, wrap, uSunColor); }
        // The same with its ambient scaled by ambK (the dark interiors: RoomShade below).
        vec3 ApplyLightVisAmb(vec3 albedo, vec3 n, float wrap, vec3 sunColor, float ambK)
        {
            float skyLum = max(dot(uFog, vec3(0.3333)), 0.05);
            vec3 sky = mix(uAmbSky, uFog / skyLum * dot(uAmbSky, vec3(0.3333)), uSkyTint);
            vec3 amb = mix(uAmbGround, sky, dot(n, uUp) * 0.5 + 0.5) * ambK;
            float diff = max((dot(n, uSunDir) + wrap) / (1.0 + wrap), 0.0);
            return albedo * (amb + sunColor * diff);
        }

        // ---- Baked lighting (docs/render/LIGHTING.md, Source/Render/LightGrid.cs) ----
        // The map's light grid as three 3D textures, and one block of per-pass values shared by every program.
        layout(std140) uniform LitFrame
        {
            vec4 uLitMode;   // x: 0 classic light, 1 the grid at the pixel, 2 the probe below (first-person gun); y: time; z: normal offset; w: dynamic lights
            vec4 uLitOrigin; // the grid's min corner; w: the normal offset up and down
            vec4 uLitScale;  // 1 / the grid's size
            vec4 uProbeA;    // mode 2: light rgb, flicker share
            vec4 uProbeB;    // mode 2: light direction (view space), ambient
            vec4 uProbeC;    // mode 2: sun, flicker phasor (x, y), flicker pace
            vec4 uDynPos[32]; // dynamic lights: position, range (the size is LightGrid.MaxDynamic: its check below)
            vec4 uDynCol[32]; // colour * intensity
            vec4 uDynSpot[32]; // a spot's direction and the cosine of its half angle (w under -1: all round; ConstructLamps' cones)
            vec4 uSurfQ;     // x: the materials setting (0 Low: the classic light, 1 Medium, 2 High; docs/render/MATERIALS.md); y: 1 while glow is marked for the bloom; z: edge bevels on; w: material contrast on
            mat4 uShadowVP;  // world less uShadowC to the sun shadow map's u, v and stored depth (SunShadows.cs)
            vec4 uShadowC;   // xyz: the map's centre; w: the normal offset (m)
            vec4 uShadowQ;   // x: 1 while the map is live; y: the soft edge's radius (uv); z: the depth sign; w: the depth bias
            vec4 uKeyQ;      // x: the key light's multiplier (0: not set, 1)
            mat4 uRoomVP[8]; // world less uRoomC to each dark-interiors map's u, v and stored depth (RoomShade.cs; the size is RoomShade.Layers)
            vec4 uRoomC;     // xyz: the maps' centre (the camera); w: the normal offset (m)
            vec4 uRoomQ;     // x: 1 while the maps are live; y: the ambient a shut room keeps; z: 1 in the gun's pass (read at the eye); w: the signed depth bias
            vec4 uLampRows[96]; // each lamp shadow slot's world-to-body turn, three rows, the first's w 1 / the distance its maps hold (LampShadows.cs; the size is LampShadows.MaxSlots * 3)
            vec4 uLampQ;     // x: 1 while some slot is live; y: the signed depth bias; z: 1 / the maps' distance range; w: the normal offset a metre out (m)
            vec4 uLampP;     // x: the soft compare's step (uv); y: 1 with reversed depth
        };
        uniform sampler3D uLitA; // rgb: static light reaching the cell, a: the share that flickers
        uniform sampler3D uLitB; // xyz: the light's average direction (length: how one-sided), w: ambient left (sky seen)
        uniform sampler2D uSunShadow; // the sun shadow map's depth (SunShadows.cs; read only while uShadowQ.x is 1)
        uniform sampler2DArrayShadow uRoomShade; // the dark interiors' eight depth maps, compared in hardware (RoomShade.cs; read only while uRoomQ.x is 1)
        uniform sampler2DArrayShadow uLampShadow; // the lamps' cube faces, six layers a slot, compared in hardware (LampShadows.cs; read only while uLampQ.x is 1)
        uniform sampler3D uLitC; // r: sun reaching the cell, g, b: flicker phasor (128/255 = 0), a: flicker pace
        float gLitAmb = 1.0;     // the ambient factor the last ApplyLightAt used (the box shaders scale their rim light by it)
        float gSunVis = 1.0;     // ...how much of the sun it let through (materials: the sun's highlight)
        vec3 gLitDir = vec3(0.0); // ...the baked light's direction and colour there (the same)
        vec3 gLitCol = vec3(0.0);

        """ + LightFlicker.Glsl + """
        // The flicker at a spot (LightFlicker.cs): c = the baked (and filtered) uLitC texel, share = the flickering share.
        // Its average is the old flicker's (1 - 0.3 share); the swing is the cell's blend of each flame's own wave.
        float LitFlicker(float t, vec4 c, float share)
        {
            vec2 z = (c.gb * 255.0 - 128.0) / 127.0 * 0.6;
            return max(1.0 - 0.3 * share + FlameWave(z, min(c.a * (255.0 / 248.0), 1.0), t), 0.0); // (alpha's low bits: the cell's links)
        }

        // The baked grid for a surface at p facing n (LightBake.SampleSurface in C#): read a little over half a cell along n.
        // Most places take the GPU's own trilinear filtering. Where the 8 cells round that point hold a cell inside a box,
        // or something stands between two of them (the low corner's flag: LightBaker.WriteFlags), they are filtered here
        // instead, leaving out cells inside boxes and cells behind the surface, so a thin wall's outside face, or the floor
        // at its foot, never reads the light of the room behind it.
        void LitGrid(vec3 p, vec3 n, out vec4 a, out vec4 b, out vec4 c)
        {
            vec3 uvw = (p + n * vec3(uLitMode.z, uLitOrigin.w, uLitMode.z) - uLitOrigin.xyz) * uLitScale.xyz;
            ivec3 size = textureSize(uLitC, 0);
            vec3 g = uvw * vec3(size) - 0.5;
            ivec3 c0 = clamp(ivec3(floor(g)), ivec3(0), size - 1);
            int flags = int(texelFetch(uLitC, c0, 0).r * 255.0 + 0.5);
            if ((flags & 2) != 0)
            {
                vec3 f = clamp(g - vec3(c0), 0.0, 1.0);
                vec3 cell = 1.0 / (uLitScale.xyz * vec3(size));
                // The home cell (the corner nearest the point): a corner across a blocked link from it (texture C's alpha
                // low bits: +x, +y, +z) is behind a wall that stands between two cell centres.
                ivec3 h = ivec3(greaterThanEqual(f, vec3(0.5)));
                ivec3 hq = min(c0 + h, size - 1);
                bool homeOpen = (int(texelFetch(uLitC, hq, 0).r * 255.0 + 0.5) & 1) == 0;
                ivec3 links = ivec3(int(texelFetch(uLitC, min(c0 + ivec3(0, h.y, h.z), size - 1), 0).a * 255.0 + 0.5) & 1,
                                    int(texelFetch(uLitC, min(c0 + ivec3(h.x, 0, h.z), size - 1), 0).a * 255.0 + 0.5) & 2,
                                    int(texelFetch(uLitC, min(c0 + ivec3(h.x, h.y, 0), size - 1), 0).a * 255.0 + 0.5) & 4);
                bvec3 blocked = bvec3(homeOpen && links.x != 0, homeOpen && links.y != 0, homeOpen && links.z != 0);
                vec4 sa = vec4(0.0);
                vec4 sb = vec4(0.0);
                vec4 sc = vec4(0.0);
                float sum = 0.0;
                for (int i = 0; i < 8; i++)
                {
                    ivec3 o = ivec3(i & 1, (i >> 1) & 1, i >> 2);
                    ivec3 q = min(c0 + o, size - 1);
                    vec3 t = mix(1.0 - f, f, vec3(o));
                    float w = t.x * t.y * t.z;
                    vec4 tc = texelFetch(uLitC, q, 0);
                    vec3 centre = uLitOrigin.xyz + (vec3(q) + 0.5) * cell;
                    bvec3 across = notEqual(o, h);
                    if ((int(tc.r * 255.0 + 0.5) & 1) != 0 || dot(centre - p, n) < 0.0 || w <= 0.0
                        || (blocked.x && across.x) || (blocked.y && across.y) || (blocked.z && across.z))
                        continue;
                    sa += texelFetch(uLitA, q, 0) * w;
                    sb += texelFetch(uLitB, q, 0) * w;
                    sc += tc * w;
                    sum += w;
                }
                if (sum > 1e-4)
                {
                    a = sa / sum;
                    b = sb / sum;
                    c = sc / sum;
                    return;
                }
            }
            a = texture(uLitA, uvw);
            b = texture(uLitB, uvw);
            c = texture(uLitC, uvw);
        }

        // How much of the sun reaches world point p past the ships and vehicles near the camera (SunShadows.cs): 12 taps of the
        // depth map round the point, offset along its normal, fading to 1 at the map's edge. 1 while the map is off.
        float SunShadow(vec3 p, vec3 n)
        {
            if (uShadowQ.x < 0.5)
                return 1.0;
            vec3 s = (uShadowVP * vec4(p - uShadowC.xyz + n * uShadowC.w, 1.0)).xyz;
            vec2 e = abs(s.xy - 0.5) * 2.0;
            float edge = max(e.x, e.y);
            if (edge >= 1.0 || s.z <= 0.0 || s.z >= 1.0) // (outside the map's square or its depth: nothing there was drawn)
                return 1.0;
            const vec2 taps[12] = vec2[12](vec2(-0.326, -0.406), vec2(-0.840, -0.074), vec2(-0.696, 0.457), vec2(-0.203, 0.621),
                vec2(0.962, -0.195), vec2(0.473, -0.480), vec2(0.519, 0.767), vec2(0.185, -0.893), vec2(0.507, 0.064),
                vec2(0.896, 0.412), vec2(-0.322, -0.933), vec2(-0.792, -0.598));
            float a = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715)))) * 6.2832;
            mat2 r = mat2(cos(a), sin(a), -sin(a), cos(a)) * uShadowQ.y;
            float lit = 0.0;
            for (int i = 0; i < 12; i++)
            {
                float d = texture(uSunShadow, s.xy + r * taps[i]).r;
                lit += step(-uShadowQ.w, uShadowQ.z * (d - s.z));
            }
            return mix(lit / 12.0, 1.0, smoothstep(0.8, 1.0, edge));
        }

        // How open the sky is round world point p facing n, past the ships and vehicles near the camera (RoomShade.cs): amb, the
        // share of the eight diagonal directions in front of the surface (weighted by how squarely it faces each) that reach the
        // edge of their map without meeting a box; sun, the same share towards the sun. A shut room: 0 (the floor, for amb). 1 while
        // the maps are off. In the gun's pass every pixel reads the point at the eye with no normal.
        void RoomShade(vec3 p, vec3 n, out float amb, out float sun)
        {
            amb = 1.0;
            sun = 1.0;
            if (uRoomQ.x < 0.5)
                return;
            bool probe = uRoomQ.z > 0.5;
            vec4 q = vec4(probe ? vec3(0.0) : p - uRoomC.xyz + n * uRoomC.w, 1.0);
            float sa = 0.0, wa = 0.0, ss = 0.0, ws = 0.0;
            for (int i = 0; i < 8; i++) // (RoomShade.Dir: bit 0 x, bit 1 y, bit 2 z, set: +)
            {
                vec3 d = vec3((i & 1) == 0 ? -0.57735 : 0.57735, (i & 2) == 0 ? -0.57735 : 0.57735, (i & 4) == 0 ? -0.57735 : 0.57735);
                float w = probe ? 1.0 : max(dot(n, d), 0.0);
                float wsun = max(dot(uSunDir, d), 0.0);
                if (w <= 0.0 && wsun <= 0.0)
                    continue;
                vec3 s = (uRoomVP[i] * q).xyz;
                vec2 e = abs(s.xy - 0.5) * 2.0;
                float edge = max(e.x, e.y);
                float lit = 1.0;
                if (edge < 1.0 && s.z > 0.0 && s.z < 1.0)
                    lit = mix(texture(uRoomShade, vec4(s.xy, float(i), s.z - uRoomQ.w)), 1.0, smoothstep(0.85, 1.0, edge));
                sa += lit * w;
                wa += w;
                ss += lit * wsun;
                ws += wsun;
            }
            float open = wa > 0.0 ? sa / wa : 1.0;
            sun = ws > 0.0 ? ss / ws : 1.0;
            if (probe) // (the gun has no normal: half the directions open, beside a hull outside or in a doorway, is fully lit)
            {
                open = min(open * 2.0, 1.0);
                sun = min(sun * 2.0, 1.0);
            }
            amb = mix(uRoomQ.y, 1.0, open);
        }

        // How much of dynamic light i reaches world point p facing n past the walls round it (LampShadows.cs): 1 for a light with
        // no slot (its colour's alpha is the slot + 1) or while the maps are off. The point, nudged along its normal by more the
        // farther it is, is turned into the lamp body's frame and looked up on the cube face its biggest axis names, four soft taps.
        float LampShadow(int i, vec3 p, vec3 n)
        {
            int slot = int(uDynCol[i].a + 0.5) - 1;
            if (slot < 0 || uLampQ.x < 0.5)
                return 1.0;
            vec3 w = p - uDynPos[i].xyz;
            w += n * (0.01 + length(w) * uLampQ.w);
            vec3 v = vec3(dot(uLampRows[slot * 3].xyz, w), dot(uLampRows[slot * 3 + 1].xyz, w), dot(uLampRows[slot * 3 + 2].xyz, w));
            vec3 a = abs(v);
            int face;
            vec2 uv;
            float ma;
            if (a.x >= a.y && a.x >= a.z) { face = v.x >= 0.0 ? 0 : 1; uv = v.yz; ma = a.x; }
            else if (a.y >= a.z) { face = v.y >= 0.0 ? 2 : 3; uv = v.zx; ma = a.y; }
            else { face = v.z >= 0.0 ? 4 : 5; uv = v.xy; ma = a.z; }
            uv = uv / max(ma, 1e-4) * (0.925926 * 0.5) + 0.5; // (LampShadows.Guard)
            float d = min(length(v) * uLampRows[slot * 3].w, 1.0); // (1 / the slot's own reach: LampShadows.StoreOf)
            float r = (uLampP.y > 0.5 ? 1.0 - d : d) - uLampQ.y;
            float layer = float(slot * 6 + face);
            float t = uLampP.x * 0.75;
            float lit = texture(uLampShadow, vec4(uv + vec2(-t, -t), layer, r)) + texture(uLampShadow, vec4(uv + vec2(t, -t), layer, r))
                      + texture(uLampShadow, vec4(uv + vec2(-t, t), layer, r)) + texture(uLampShadow, vec4(uv + vec2(t, t), layer, r));
            return lit * 0.25;
        }

        // Dynamic light i at world point p facing n (a lamp, a muzzle flash, a blast). All round: a soft inverse-square-like falloff to
        // zero at its range, by how much the surface faces it, past its shadow. A spot (Sean 2026-11-23: high-bay pendants as cones of
        // light on the floor): the same inside its cone, soft over the outer 35% of it in cosine, nothing outside it; plus a flat
        // fill under a wider cone, the light its pool throws back (SpotFill of it), so a lit hall's machines aren't black on their
        // sides. Its shadow is read only where it lights.
        const float SpotFill = 0.14;
        vec3 DynLight(int i, vec3 p, vec3 n)
        {
            vec3 l = uDynPos[i].xyz - p;
            float d = length(l);
            float q = clamp(d / uDynPos[i].w, 0.0, 1.0);
            float f = (1.0 - q * q) * (1.0 - q * q);
            if (f <= 0.0)
                return vec3(0.0);
            vec3 ld = l / max(d, 1e-3);
            float face = max(dot(n, ld) * 0.8 + 0.2, 0.0);
            vec4 s = uDynSpot[i];
            if (s.w < -1.0)
                return uDynCol[i].rgb * f * face * LampShadow(i, p, n);
            float cd = dot(-ld, s.xyz);
            float cone = smoothstep(s.w, mix(s.w, 1.0, 0.35), cd);
            float wide = smoothstep(s.w - 0.35, s.w, cd);
            if (wide <= 0.0)
                return vec3(0.0);
            return uDynCol[i].rgb * f * (face * cone + SpotFill * wide) * LampShadow(i, p, n);
        }

        // ApplyLight plus the baked light at world position p (mode 0: ApplyLight, with the sun shadows and the key light).
        vec3 ApplyLightAt(vec3 albedo, vec3 n, float wrap, vec3 p)
        {
            float key = uKeyQ.x > 0.0 ? uKeyQ.x : 1.0;
            float room, roomSun;
            RoomShade(p, n, room, roomSun);
            gLitAmb = room;
            float shade = SunShadow(p, n);
            if (uShadowQ.x < 0.5)
                shade *= roomSun; // (no sun map: the dark interiors' share towards the sun keeps it out of shut rooms)
            gSunVis = shade;
            if (uLitMode.x < 0.5)
                return ApplyLightVisAmb(albedo, n, wrap, uSunColor * key * shade, room);
            if (uLitMode.x > 4.5) // (an unlit map's classic light plus the frame's dynamic lights: lamps on constructs, ConstructLamps.cs)
            {
                vec3 dl = vec3(0.0);
                int nd5 = int(uLitMode.w + 0.5);
                for (int i = 0; i < nd5; i++) // at most LightGrid.MaxDynamic
                    dl += DynLight(i, p, n);
                // A soft knee (2026-11-14): a lamp's pool stays strong, but a wall or a roof a metre or two from a lamp is no
                // longer burnt white (ConstructLamps' strengths are set for it: a brightest channel of 1 comes out 0.69, 2 comes out 1.05).
                dl = dl / (1.0 + 0.45 * max(max(dl.r, dl.g), dl.b)); // (on the brightest channel: the sodium's amber keeps its hue)
                gSunVis = shade;
                return ApplyLightVisAmb(albedo, n, wrap, uSunColor * key * shade, room) + albedo * dl;
            }
            vec4 a;
            vec4 b;
            vec4 c;
            if (uLitMode.x < 1.5 || uLitMode.x > 2.5)
                LitGrid(p, n, a, b, c);
            else
            {
                a = uProbeA;
                b = uProbeB;
                c = uProbeC;
            }
            b.w *= room;
            gLitAmb = b.w;
            float skyLum = max(dot(uFog, vec3(0.3333)), 0.05);
            vec3 sky = mix(uAmbSky, uFog / skyLum * dot(uAmbSky, vec3(0.3333)), uSkyTint);
            vec3 amb = mix(uAmbGround, sky, dot(n, uUp) * 0.5 + 0.5) * b.w;
            float sunVis = min(c.r * (255.0 / 252.0), 1.0) * shade; // (its low two bits are flags: LightBaker.WriteFlags; shade: the ships' shadows)
            gSunVis = sunVis;
            gLitDir = b.xyz;
            gLitCol = a.rgb;
            float diff = max((dot(n, uSunDir) + wrap) / (1.0 + wrap), 0.0) * sunVis;
            float facing = (1.0 - min(length(b.xyz), 1.0)) * 0.6 + max((dot(n, b.xyz) + wrap) / (1.0 + wrap), 0.0);
            vec3 lit = a.rgb * facing * LitFlicker(uLitMode.y, c, a.a);
            int nd = uLitMode.x < 1.5 ? int(uLitMode.w + 0.5) : 0;
            for (int i = 0; i < nd; i++) // at most LightGrid.MaxDynamic
                lit += DynLight(i, p, n);
            if (uLitMode.x > 2.5) // (--light-debug's pictures: the grid's light alone, or its sky and sun visibility)
                return uLitMode.x < 3.5 ? lit + vec3(0.02) : vec3(b.w * 0.6 + sunVis * 0.4, b.w * 0.6 + sunVis * 0.4, b.w * 0.6);
            return albedo * (amb + uSunColor * key * diff + lit);
        }

        // The sky's colour in direction d (unit) without sun, moon, stars or clouds (SkySettings.Gradient in C#).
        vec3 SkyGradient(vec3 d)
        {
            if (uOuterFog.w > uOuterFog.z) // shaped flat maps: below the horizon is the horizon colour (no dark ground band)
                d = normalize(vec3(d.x, max(d.y, 0.0), d.z) + vec3(0.0, 1e-5, 0.0));
            float y = d.y;
            vec3 c = y >= 0.0 ? mix(uSkyHorizon, uSkyZenith, sqrt(min(y, 1.0)))
                              : mix(uSkyHorizon, uSkyGround, smoothstep(0.0, 0.3, -y));
            float s = max(dot(d, uSkyHazeDir), 0.0);
            float h = uSkyHaze.w * pow(s, 6.0) * (1.0 - 0.6 * min(abs(y), 1.0));
            return mix(c, uSkyHaze.rgb, clamp(h, 0.0, 1.0));
        }

        // The horizon fade's start radius in direction d from the centre (between the two nearest of 64 directions).
        float OuterStart(vec2 d)
        {
            if (dot(d, d) < 1e-8)
                return uOuterR[0].x; // (atan(0, 0) is undefined)
            float a = atan(d.y, d.x) * (64.0 / 6.28318531);
            if (a < 0.0)
                a += 64.0;
            int i = int(a);
            float t = a - float(i);
            i = i % 64;
            int j = (i + 1) % 64;
            return mix(uOuterR[i >> 2][i & 3], uOuterR[j >> 2][j & 3], t);
        }

        vec3 ApplyFog(vec3 col, vec3 world)
        {
            if (uFogRange.y <= uFogRange.x)
                return col;
            vec3 v = world - uCam;
            float dist = length(v);
            float f = clamp((dist - uFogRange.x) / (uFogRange.y - uFogRange.x), 0.0, 1.0) * uFogMax;
            if (uOuterFog.w > uOuterFog.z) // past a shaped map's outline: fully into the horizon, wherever the camera is
            {
                vec2 od = world.xz - uOuterFog.xy;
                float os = OuterStart(od);
                f = max(f, smoothstep(os, os + (uOuterFog.w - uOuterFog.z), length(od)));
            }
            vec3 fogCol = uSkyFog > 0.5 ? SkyGradient(v / max(dist, 1e-4)) : uFog;
            return mix(col, fogCol, f);
        }
        """;

    /// <summary>Sets the light uniforms on <paramref name="program"/> (binds it) and remembers it for <see cref="Use"/>.</summary>
    public static void Upload(GlProgram program)
    {
        program.Use();
        if (!Programs.Contains(program.Id))
            Programs.Add(program.Id);
        LightGrid.Register(program); // the baked light's block and textures (LightGrid.cs)
        SurfaceGpu.Register(program); // the shared material table (Surfaces.cs)
        SurfaceDetail.Register(program); // the materials' detail textures (SurfaceDetail.cs)
        Send(program.Id, Current);
        // A neutral sky until the first SetFog, so menus drawn before any map (gun previews) aren't tinted.
        Native.glUniform3f(Loc(program.Id, "uFog\0"u8), DefaultSky.X, DefaultSky.Y, DefaultSky.Z);
    }

    /// <summary>
    /// Makes <paramref name="light"/> the light of every scene shader. Does nothing when it already is; otherwise re-sends
    /// it to every program <see cref="Upload"/> saw (a few dozen glUniform calls) and puts the bound program back.
    /// Returns true if anything changed.
    /// </summary>
    public static bool Use(in LightSetup light)
    {
        if (light == Current)
            return false;
        Current = light;
        Changes++;
        if (Programs.Count == 0)
            return true;
        int bound;
        Native.glGetIntegerv(GL_CURRENT_PROGRAM, &bound);
        for (var i = Programs.Count - 1; i >= 0; i--)
        {
            if (!Native.glIsProgram(new GlProgram(Programs[i])))
            {
                Programs.RemoveAt(i); // deleted by its renderer
                continue;
            }
            Native.glUseProgram(new GlProgram(Programs[i]));
            Send(Programs[i], light);
        }
        Native.glUseProgram(new GlProgram((uint)bound));
        return true;
    }

    private static void Send(uint program, in LightSetup l)
    {
        Native.glUniform3f(Loc(program, "uSunDir\0"u8), l.SunDir.X, l.SunDir.Y, l.SunDir.Z);
        Native.glUniform3f(Loc(program, "uSunColor\0"u8), l.SunColor.X, l.SunColor.Y, l.SunColor.Z);
        Native.glUniform3f(Loc(program, "uAmbSky\0"u8), l.AmbSky.X, l.AmbSky.Y, l.AmbSky.Z);
        Native.glUniform3f(Loc(program, "uAmbGround\0"u8), l.AmbGround.X, l.AmbGround.Y, l.AmbGround.Z);
        var up = l.Up == default ? Vector3.UnitY : l.Up;
        Native.glUniform3f(Loc(program, "uUp\0"u8), up.X, up.Y, up.Z);
        Native.glUniform1f(Loc(program, "uSkyTint\0"u8), SkyTint);
        Native.glUniform1f(Loc(program, "uFogMax\0"u8), l.FogMax);
        Native.glUniform1f(Loc(program, "uSkyFog\0"u8), l.SkyFog ? 1f : 0f);
        Native.glUniform3f(Loc(program, "uSkyZenith\0"u8), l.Zenith.X, l.Zenith.Y, l.Zenith.Z);
        Native.glUniform3f(Loc(program, "uSkyHorizon\0"u8), l.Horizon.X, l.Horizon.Y, l.Horizon.Z);
        Native.glUniform3f(Loc(program, "uSkyGround\0"u8), l.Ground.X, l.Ground.Y, l.Ground.Z);
        Native.glUniform3f(Loc(program, "uSkyHazeDir\0"u8), l.HazeDir.X, l.HazeDir.Y, l.HazeDir.Z);
        Native.glUniform4f(Loc(program, "uSkyHaze\0"u8), l.Haze.X, l.Haze.Y, l.Haze.Z, l.Haze.W);
        Native.glUniform4f(Loc(program, "uOuterFog\0"u8), l.OuterFog.X, l.OuterFog.Y, l.OuterFog.Z, l.OuterFog.W);
        var starts = stackalloc float[Map.OuterFogDirections];
        for (var i = 0; i < Map.OuterFogDirections; i++)
            starts[i] = l.OuterStarts is { Length: Map.OuterFogDirections } r ? r[i] : l.OuterFog.Z;
        Native.glUniform4fv(Loc(program, "uOuterR\0"u8), Map.OuterFogDirections / 4, starts); // packed 4 per vec4 (fewer uniform slots)
    }

    private static int Loc(uint program, ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(new GlProgram(program), p);
    }

    /// <summary>The uniform names <see cref="Glsl"/> declares (the self-test checks the shader and the C# agree); the array uOuterR is apart.</summary>
    public static readonly string[] UniformNames =
        ["uSunDir", "uSunColor", "uAmbSky", "uAmbGround", "uUp", "uSkyTint", "uFogMax", "uCam", "uFog", "uFogRange", "uSkyFog", "uSkyZenith", "uSkyHorizon", "uSkyGround", "uSkyHazeDir", "uSkyHaze", "uOuterFog", "uLitA", "uLitB", "uLitC", "uSunShadow"];

    /// <summary>
    /// C# mirror of <see cref="Glsl"/>'s SkyGradient under <paramref name="l"/> (the self-test checks horizons with it).
    /// </summary>
    public static Vector3 SkyGradient(in LightSetup l, Vector3 d)
    {
        if (l.OuterFog.W > l.OuterFog.Z)
            d = Vector3.Normalize(new Vector3(d.X, MathF.Max(d.Y, 0f), d.Z) + new Vector3(0f, 1e-5f, 0f));
        return SkySettings.Gradient(d, l.Zenith, l.Horizon, l.Ground, l.HazeDir, new Vector3(l.Haze.X, l.Haze.Y, l.Haze.Z), l.Haze.W);
    }

    /// <summary>C# mirror of <see cref="Glsl"/>'s OuterStart: the horizon fade's start radius in direction <paramref name="d"/> (x, z) from its centre.</summary>
    public static float OuterStart(in LightSetup l, Vector2 d)
    {
        const int n = Map.OuterFogDirections;
        if (l.OuterStarts is not { Length: n } r)
            return l.OuterFog.Z;
        var a = MathF.Atan2(d.Y, d.X) * (n / MathF.Tau);
        if (a < 0f)
            a += n;
        var i = (int)a;
        var t = a - i;
        i %= n;
        return r[i] + (r[(i + 1) % n] - r[i]) * t;
    }

    /// <summary>
    /// C# mirror of <see cref="Glsl"/>'s ApplyFog: <paramref name="col"/> at <paramref name="world"/> seen from
    /// <paramref name="cam"/>, with the flat fog colour <paramref name="fog"/> and range (start, end) as SetFog gives them.
    /// </summary>
    public static Vector3 ApplyFog(in LightSetup l, Vector3 col, Vector3 world, Vector3 cam, Vector3 fog, Vector2 range)
    {
        if (range.Y <= range.X)
            return col;
        var v = world - cam;
        var dist = v.Length();
        var f = Math.Clamp((dist - range.X) / (range.Y - range.X), 0f, 1f) * l.FogMax;
        if (l.OuterFog.W > l.OuterFog.Z)
        {
            var od = new Vector2(world.X - l.OuterFog.X, world.Z - l.OuterFog.Y);
            var os = OuterStart(l, od);
            var t = Math.Clamp((od.Length() - os) / (l.OuterFog.W - l.OuterFog.Z), 0f, 1f);
            f = MathF.Max(f, t * t * (3f - 2f * t));
        }
        var fogCol = l.SkyFog ? SkyGradient(l, v / MathF.Max(dist, 1e-4f)) : fog;
        return Vector3.Lerp(col, fogCol, f);
    }
}
