using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws a planetoid's city (docs/planets/CITY.md, <see cref="CityGeometry"/>): every chunk's triangles sit on the GPU
/// once, and a frame culls the chunks by their spheres and draws each visible one in a single call, near first, the
/// rooftop clutter, lamps and cornices only within <see cref="DetailRange"/>. The look is in the shader, not the
/// geometry: windows, floors, shopfronts, lit rooms, reflections of the sky, road markings and the pools under the street
/// lamps are worked out per pixel from each face's own metres, so a tower is a dozen faces and still reads as a building.
/// Nothing allocates in a steady frame.
/// </summary>
public sealed unsafe partial class CityRenderer
{
    /// <summary>Within this many metres of the camera a chunk's detail is drawn too.</summary>
    public static float DetailRange = 420f;

    private const string VertexSrc = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUv;
        layout(location = 3) in vec4 aColor;
        layout(location = 4) in vec4 aPar;
        uniform mat4 uViewProjRel;
        uniform vec3 uChunkRel;
        uniform vec3 uChunkWorld;
        uniform mat3 uRot;      // identity for the city's chunks; a train car's turn
        out vec3 vWorld;
        out vec3 vNormal;
        out vec2 vUv;
        out vec3 vColor;
        flat out vec4 vPar;

        void main()
        {
            vec3 p = uRot * aPos;
            vWorld = uChunkWorld + p;
            vNormal = uRot * aNormal;
            vUv = aUv;
            vColor = aColor.rgb;
            vPar = aPar;
            gl_Position = uViewProjRel * vec4(p + uChunkRel, 1.0);
        }
        """;

    private static readonly string FragmentSrc = "#version 410 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vNormal;
        in vec2 vUv;
        in vec3 vColor;
        flat in vec4 vPar;
        uniform float uTime;
        uniform float uGlow;    // 1: what is lit marks the back buffer's alpha for the bloom
        uniform vec3 uPlanet;   // the planetoid's centre: "up" is away from it
        uniform float uGroundR; // the city's ground's radius: under it (the metro) the sun doesn't reach
        out vec4 FragColor;
        bool gUnder = false;
        float gOut = 1.0;

        // The light on a surface: the map's sun and sky above ground, the metro's strip lights below it.
        vec3 Lit(vec3 albedo, vec3 n, float wrap, vec3 p)
        {
            if (!gUnder)
                return ApplyLightAt(albedo, n, wrap, p);
            vec3 up = normalize(p - uPlanet);
            return albedo * vec3(0.9, 0.95, 1.0) * (0.55 + 0.25 * max(dot(n, -up), 0.0) + 0.15 * abs(dot(n, up)));
        }

        uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return x;
        }
        float H01(uint x) { return float(Hash(x) & 0xffffffu) / 16777216.0; }
        float Noise(vec2 p)
        {
            vec2 i = floor(p);
            vec2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            uvec2 q = uvec2(ivec2(i) + 32768);
            float a = H01(q.x * 73856093u ^ q.y * 19349663u);
            float b = H01((q.x + 1u) * 73856093u ^ q.y * 19349663u);
            float c = H01(q.x * 73856093u ^ (q.y + 1u) * 19349663u);
            float d = H01((q.x + 1u) * 73856093u ^ (q.y + 1u) * 19349663u);
            return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
        }
        // 1 inside [a, b] along t, softened by the pixel's footprint w (no shimmer at a distance).
        float Band(float t, float a, float b, float w)
        {
            return clamp(min(t - a, b - t) / max(w, 1e-4) + 0.5, 0.0, 1.0);
        }

        vec3 Up() { return normalize(vWorld - uPlanet); }

        // The sky seen in a mirror facing n; under the horizon, the dark city across the street.
        vec3 Reflection(vec3 n, vec3 v, vec3 tint)
        {
            vec3 r = reflect(-v, n);
            float t = dot(r, Up());
            vec3 sky = t >= 0.0 ? mix(uSkyHorizon, uSkyZenith, sqrt(min(t, 1.0))) : uSkyGround * 0.6;
            // Other towers across the way fill the low part of the reflection.
            float city = 1.0 - smoothstep(0.02, 0.32, t);
            sky = mix(sky, vec3(0.05, 0.055, 0.065) + uSkyHorizon * 0.06, city * 0.85);
            float sun = pow(max(dot(r, uSunDir), 0.0), 220.0) * 6.0;
            return sky * tint * 0.7 + uSunColor * sun;
        }

        // A lit room's colour, or zero.
        vec3 Room(uint seed, float floorI, float bay, float lit, vec2 inWin)
        {
            uint id = Hash(seed * 7919u + uint(floorI) * 131u + uint(bay + 4096.0));
            float h = float(id & 0xffffu) / 65536.0;
            if (h >= lit)
                return vec3(0.0);
            float kind = float((id >> 16) & 255u) / 255.0;
            vec3 c = kind < 0.62 ? vec3(1.0, 0.74, 0.46) : kind < 0.9 ? vec3(0.86, 0.92, 1.0) : vec3(0.45, 0.6, 1.0);
            float k = 0.55 + 0.9 * float((id >> 24) & 255u) / 255.0;
            // A ceiling light: brighter towards the top; a blind drawn some way down on some; curtains drawn back to the
            // sides; desks and chairs a dark band along the bottom.
            float blind = float((id >> 4) & 15u) / 15.0;
            float shade = inWin.y > 1.0 - blind * 0.5 ? 0.4 : 1.0;
            float cl = float((id >> 8) & 7u) / 28.0, cr = float((id >> 11) & 7u) / 28.0;
            float curtain = inWin.x < cl || inWin.x > 1.0 - cr ? 0.45 : 1.0;
            float desk = inWin.y < 0.24 ? 0.4 + 0.3 * step(0.5, fract(inWin.x * 3.0 + kind * 7.0)) : 1.0;
            return c * k * 0.75 * (0.4 + 0.6 * inWin.y) * shade * curtain * desk;
        }

        vec3 Facade(int style, uint seed, float floorH, float lit, bool shops, vec3 wall, vec3 n, vec3 v, out vec3 glow)
        {
            glow = vec3(0.0);
            float u = vUv.x;
            float h = vUv.y;
            float fw = max(fwidth(u), fwidth(h));
            const float Ground = 4.8;
            vec3 lightWall = Lit(wall, n, 0.0, vWorld);
            // Glass: tinted per building.
            float gt = H01(seed * 31u + 7u);
            vec3 tint = gt < 0.3 ? vec3(0.62, 0.78, 0.92) : gt < 0.5 ? vec3(0.35, 0.5, 0.78) : gt < 0.65 ? vec3(0.6, 0.82, 0.76) : gt < 0.82 ? vec3(0.86, 0.74, 0.58) : vec3(0.7, 0.72, 0.76);
            vec3 refl = Reflection(n, v, tint);
            float fres = 0.08 + 0.92 * pow(1.0 - max(dot(n, v), 0.0), 5.0);
            // The bays and windows of each style: [bay width, window left, window right, sill, head] (metres in a bay and a floor).
            float bayW = style == 0 ? 1.5 : style == 1 ? 2.4 + 0.6 * H01(seed) : style == 2 ? 3.0 : 1.25;
            float l = style == 0 ? 0.05 : style == 1 ? 0.32 : style == 2 ? bayW * 0.3 : 0.04;
            float r = style == 0 ? bayW : style == 1 ? bayW - 0.32 : style == 2 ? bayW * 0.7 : bayW;
            float sill = style == 0 ? 0.0 : style == 1 ? 0.95 : style == 2 ? 0.95 : 0.95;
            float head = style == 0 ? floorH - 0.85 : style == 1 ? floorH - 0.45 : style == 2 ? floorH - 0.7 : 2.35;
            float floorI, fv, bay, fu, winShare, inWin;
            vec3 col;
            if (h < Ground)
            {
                // The ground floor: shopfronts behind a fascia with the shop's colour, or the lobby's tall glass.
                floorI = 0.0;
                fv = h;
                float sw = 5.0;
                bay = floor(u / sw);
                fu = u - bay * sw;
                float win = Band(fu, 0.35, sw - 0.35, fw) * Band(fv, 0.75, 3.55, fw);
                vec3 inside = vec3(0.02);
                // A shop: lit inside, the shelves and the counter darker bands across it; one in five is shut for the night.
                uint sh = Hash(seed * 5u + uint(bay + 4096.0));
                float open = shops ? step(0.2, float(sh & 255u) / 255.0) : step(0.55, float(sh & 255u) / 255.0);
                vec3 shopLight = mix(vec3(1.0, 0.8, 0.55), vec3(0.85, 0.95, 1.0), float((sh >> 8) & 1u)) * (0.35 + 0.25 * float((sh >> 9) & 255u) / 255.0);
                float shelves = 1.0 - 0.55 * step(0.5, fract((fv - 0.75) / 0.7)) * step(fv, 2.6) * step(0.25, fract(fu / 1.7));
                vec3 room = shopLight * open * shelves;
                vec3 glass = mix(inside + room, refl, fres * 0.6);
                col = mix(lightWall * 0.8, glass, win);
                glow = room * win * 0.6;
                if (shops)
                {
                    // The fascia over the window: the shop's own colour, lit at dusk.
                    uint s = Hash(seed * 17u + uint(bay));
                    vec3 sign = vec3(float(s & 255u), float((s >> 8) & 255u), float((s >> 16) & 255u)) / 255.0;
                    sign = normalize(sign + 0.15) * 0.9;
                    float band = Band(fv, 3.75, 4.5, fw) * Band(fu, 0.2, sw - 0.2, fw);
                    float letters = step(0.45, Noise(vec2(u * 2.2, fv * 3.0 + float(bay)))) * Band(fv, 3.9, 4.35, fw) * Band(fu, 0.9, sw - 0.9, fw);
                    col = mix(col, sign * 0.25, band);
                    glow += (sign * 0.5 * band + vec3(1.0, 0.95, 0.85) * letters * 0.9) * step(0.3, float((s >> 24) & 255u) / 255.0);
                }
                winShare = 0.6;
            }
            else
            {
                float hh = h - Ground;
                floorI = floor(hh / floorH) + 1.0;
                fv = hh - (floorI - 1.0) * floorH;
                bay = floor(u / bayW);
                fu = u - bay * bayW;
                float win = Band(fu, l, r, fw) * Band(fv, sill, head, fw);
                float roomBay = style == 0 || style == 3 ? floor(bay / 3.0) : bay;
                vec3 room = Room(seed, floorI, roomBay, lit, vec2((fu - l) / max(r - l, 0.1), (fv - sill) / max(head - sill, 0.1)));
                vec3 inside = vec3(0.015, 0.017, 0.02);
                vec3 glass = room.r + room.g > 0.01 ? room + refl * fres * 0.6 : mix(inside, refl, max(fres, style == 0 ? 0.45 : 0.3));
                // The wall round the windows: the floor's slab edge, a reveal's shadow along a window's head and side.
                vec3 w = lightWall;
                if (style == 0)
                {
                    // Curtain wall: the spandrel is darker glass, the mullions the building's metal.
                    vec3 spandrel = mix(wall * 0.25, refl * 0.6, 0.5);
                    w = mix(lightWall, spandrel, Band(fv, head + 0.08, floorH - 0.02, fw) * Band(fu, 0.05, bayW, fw));
                }
                else
                {
                    float reveal = Band(fv, head, head + 0.12, fw) + Band(fu, l - 0.1, l, fw) * Band(fv, sill, head, fw);
                    w *= 1.0 - 0.45 * clamp(reveal, 0.0, 1.0);
                    w *= 1.0 - 0.12 * Band(fv, 0.0, 0.18, fw); // the slab's edge
                    if (style == 2)
                    {
                        // Brick: courses and a pale stone lintel and sill.
                        float course = Band(fract(h / 0.075), 0.0, 0.14, fwidth(h / 0.075));
                        w *= 1.0 - 0.12 * course * clamp(1.0 - fw * 20.0, 0.0, 1.0);
                        float stone = Band(fv, head, head + 0.25, fw) * Band(fu, l - 0.15, r + 0.15, fw) + Band(fv, sill - 0.12, sill, fw) * Band(fu, l - 0.1, r + 0.1, fw);
                        w = mix(w, Lit(vec3(0.72, 0.68, 0.6), n, 0.0, vWorld), clamp(stone, 0.0, 1.0));
                    }
                }
                col = mix(w, glass, win);
                glow = room * win;
                winShare = (r - l) * (head - sill) / (bayW * floorH);
            }
            // Far off a floor is a few pixels: blend to the facade's average so it doesn't shimmer.
            float px = min(floorH, bayW) / max(fw, 1e-4);
            float detail = smoothstep(0.8, 2.0, px);
            vec3 farLit = vec3(1.0, 0.8, 0.58) * lit * 0.75 * winShare;
            vec3 avgGlass = mix(vec3(0.02), refl, style == 0 ? 0.5 : 0.35);
            vec3 avg = mix(lightWall * 0.8, avgGlass, winShare) + farLit;
            glow *= detail;
            glow += (1.0 - detail) * farLit;
            return mix(avg, col, detail);
        }

        // The light of the street lamps on the ground of a street: lamps both sides every "spacing" metres from 4 m.
        vec3 LampPools(float along, float across, float car, float spacing)
        {
            if (spacing < 1.0)
                return vec3(0.0);
            float s = along - 4.0;
            float ds = s - spacing * clamp(floor(s / spacing + 0.5), 0.0, 1e6);
            float head = car - 0.65;
            float d1 = ds * ds + (across - head) * (across - head);
            float d2 = ds * ds + (across + head) * (across + head);
            float f1 = 1.0 - clamp(d1 / 110.0, 0.0, 1.0);
            float f2 = 1.0 - clamp(d2 / 110.0, 0.0, 1.0);
            return vec3(1.0, 0.72, 0.42) * (f1 * f1 * f1 + f2 * f2 * f2) * 1.6;
        }

        void main()
        {
            gUnder = length(vWorld - uPlanet) < uGroundR - 2.5;
            gOut = gUnder ? 0.0 : 1.0;
            int style = int(vPar.x + 0.5) & 127;
            bool flag = int(vPar.x + 0.5) >= 128;
            uint seed = uint(vPar.y + 0.5);
            vec3 n = normalize(vNormal);
            vec3 v = normalize(uCam - vWorld);
            vec3 col;
            vec3 glow = vec3(0.0);
            float fw = max(fwidth(vUv.x), fwidth(vUv.y));
            if (style <= 3)
            {
                uint bseed = seed * 2654435761u + uint(vColor.r * 255.0 + 0.5) * 97u + uint(vColor.b * 255.0 + 0.5); // (rounded: an interpolated colour wobbles by a hair)
                col = Facade(style, bseed, vPar.z / 20.0, vPar.w / 255.0, flag, vColor, n, v, glow);
            }
            else if (style == 4) // plain: cornices, plant rooms, parapets
            {
                float k = 0.9 + 0.2 * Noise(vWorld.xz * 0.7 + vWorld.y * 0.3);
                col = Lit(vColor * k, n, 0.0, vWorld);
            }
            else if (style == 5) // roofing felt and gravel
            {
                float k = 0.82 + 0.25 * Noise(vUv * 1.7) + 0.1 * Noise(vUv * 9.0);
                float seam = Band(fract(vUv.x / 1.2), 0.0, 0.04, fwidth(vUv.x / 1.2));
                col = Lit(vColor * k * (1.0 - 0.15 * seam), n, 0.0, vWorld);
            }
            else if (style == 6 || style == 7) // a street's carriageway, or a crossing
            {
                float along = vUv.x, across = vUv.y;
                float k = 0.85 + 0.2 * Noise(vUv * 0.9) + 0.12 * Noise(vUv * 6.0);
                vec3 asphalt = vColor * k;
                float paint = 0.0;
                vec3 paintCol = vec3(0.82, 0.82, 0.78);
                float car = vPar.z / 20.0;
                if (style == 6)
                {
                    bool avenue = seed == 1u;
                    float a = abs(across);
                    if (avenue)
                    {
                        paint = Band(a, 0.12, 0.26, fw);
                        paintCol = mix(paintCol, vec3(0.85, 0.66, 0.2), paint);
                        paint += Band(a, car * 0.5 - 0.07, car * 0.5 + 0.07, fw) * step(fract(along / 9.0), 0.4);
                    }
                    else
                        paint = Band(a, -0.08, 0.08, fw) * step(fract(along / 8.0), 0.45);
                    paint += Band(a, car - 0.45, car - 0.33, fw);
                }
                else
                {
                    // Zebra crossings on the four arms, just outside the middle square.
                    float cx = vPar.z / 20.0, cz = vPar.w / 20.0;
                    vec2 p = abs(vUv);
                    float armX = Band(p.x, cx + 0.6, cx + 3.2, fw) * step(p.y, cz - 0.3) * step(fract(vUv.y / 1.1), 0.5);
                    float armZ = Band(p.y, cz + 0.6, cz + 3.2, fw) * step(p.x, cx - 0.3) * step(fract(vUv.x / 1.1), 0.5);
                    paint = max(armX, armZ);
                }
                if (style == 6 && seed == 2u)
                    paint = 0.0; // (the apron under the city: no markings)
                paint *= 0.55 + 0.45 * Noise(vUv * 3.1); // worn
                vec3 albedo = mix(asphalt, paintCol, clamp(paint, 0.0, 1.0));
                col = Lit(albedo, n, 0.0, vWorld);
                if (style == 6)
                    col += albedo * LampPools(along, across, car, vPar.w / 4.0);
                else
                    col += albedo * vec3(1.0, 0.72, 0.42) * gOut * 0.35;
            }
            else if (style == 8) // pavement: slabs
            {
                vec2 t = vUv / 0.9;
                vec2 g = abs(fract(t) - 0.5);
                float joint = 1.0 - Band(max(g.x, g.y), 0.0, 0.46, fwidth(t.x) + fwidth(t.y));
                float k = 0.9 + 0.15 * H01(uint(floor(t.x) + 4096.0) * 7919u + uint(floor(t.y) + 4096.0)) + 0.08 * Noise(vUv * 3.0);
                vec3 albedo = vColor * k * (1.0 - 0.3 * joint * clamp(1.0 - fw * 8.0, 0.0, 1.0));
                col = Lit(albedo, n, 0.0, vWorld);
                if (!flag)
                    col += albedo * LampPools(vUv.x, vUv.y, vPar.z / 20.0, vPar.w / 4.0);
                else
                    col += albedo * vec3(1.0, 0.72, 0.42) * gOut * 0.3;
            }
            else if (style == 9) // kerb
                col = Lit(vColor * (0.9 + 0.15 * Noise(vUv * 4.0)), n, 0.0, vWorld) + vColor * vec3(1.0, 0.72, 0.42) * gOut * 0.2;
            else if (style == 10) // a court or the square: big flags of stone
            {
                float size = seed == 1u ? 2.0 : 1.2;
                vec2 t = vUv / size;
                vec2 g = abs(fract(t) - 0.5);
                float joint = 1.0 - Band(max(g.x, g.y), 0.0, 0.47, fwidth(t.x) + fwidth(t.y));
                float k = 0.88 + 0.18 * H01(uint(floor(t.x) + 4096.0) * 7919u + uint(floor(t.y) + 4096.0));
                vec3 albedo = vColor * k * (1.0 - 0.25 * joint * clamp(1.0 - fw * 6.0, 0.0, 1.0));
                col = Lit(albedo, n, 0.0, vWorld) + albedo * vec3(1.0, 0.75, 0.48) * gOut * (seed == 1u ? 0.45 : 0.1);
            }
            else if (style == 11) // a lamp's head
            {
                col = vColor * 1.6;
                glow = col;
            }
            else if (style == 14) // an aircraft warning lamp: blinking
            {
                float on = step(fract(uTime * 0.6 + H01(uint(abs(vWorld.x) * 3.0)) ), 0.18);
                col = mix(vColor * 0.25, vColor * 2.5, on);
                glow = col * on;
            }
            else if (style == 15) // the railway's track bed: ballast, sleepers and two tracks of rails
            {
                float along = vUv.x, a = vUv.y;
                float k = 0.75 + 0.35 * Noise(vUv * 7.0);
                vec3 albedo = vColor * k;
                float spec = 0.0;
                for (int t = -1; t <= 1; t += 2)
                {
                    float x = a - float(t) * 1.9;
                    float sleeper = Band(abs(x), -1.0, 1.25, fw) * Band(fract(along / 0.65), 0.0, 0.38, fwidth(along / 0.65));
                    albedo = mix(albedo, vec3(0.42, 0.4, 0.37), sleeper * clamp(1.0 - fw * 3.0, 0.3, 1.0));
                    float rail = Band(abs(abs(x) - 0.72), -0.04, 0.04, fw);
                    albedo = mix(albedo, vec3(0.3, 0.29, 0.28), rail);
                    spec += rail;
                }
                col = Lit(albedo, n, 0.0, vWorld) + spec * Reflection(n, v, vec3(0.7)) * 0.5;
            }
            else if (style == 16) // stairs: treads with a pale nosing
            {
                float t = vUv.y / 0.36;
                float nose = Band(fract(t), 0.0, 0.12, fwidth(t));
                vec3 albedo = vColor * (0.9 + 0.1 * Noise(vUv * 3.0)) * (1.0 - 0.35 * step(0.5, fract(t)));
                albedo = mix(albedo, vec3(0.85, 0.7, 0.25), nose * clamp(1.0 - fw * 4.0, 0.0, 1.0));
                col = Lit(albedo, n, 0.0, vWorld) + albedo * vec3(1.0, 0.75, 0.48) * gOut * 0.2;
            }
            else if (style == 17) // leaves: clumps lit from above, darker inside
            {
                float k = 0.65 + 0.5 * Noise(vWorld.xz * 2.3 + vWorld.y) * Noise(vWorld.zy * 1.7);
                vec3 albedo = vColor * k * (0.75 + 0.25 * max(dot(n, Up()), 0.0));
                col = Lit(albedo, n, 0.4, vWorld) + albedo * vec3(1.0, 0.72, 0.42) * gOut * 0.25;
            }
            else if (style == 18) // a railway car's side: livery, a band of lit windows with people in them, two doors
            {
                float x = vUv.x, y = vUv.y;
                vec3 paint = seed == 1u ? vec3(0.72, 0.74, 0.77) : vColor;
                vec3 band = seed == 1u ? vec3(0.1, 0.32, 0.62) : seed == 2u ? vec3(0.08, 0.08, 0.09) : vec3(0.55, 0.1, 0.08);
                paint = mix(paint, band, Band(y, 0.5, 1.05, fw));
                paint = mix(paint, band, Band(y, 2.55, 2.68, fw));
                float door = Band(abs(abs(x) - 4.0), -0.65, 0.65, fw) * Band(y, 0.55, 2.45, fw);
                float win = Band(fract((x + 100.0) / 1.9), 0.12, 0.88, fwidth(x / 1.9)) * Band(y, 1.35, 2.35, fw) * (1.0 - step(abs(abs(x) - 4.0), 0.9)) * step(abs(x), 6.9);
                float dwin = Band(abs(abs(x) - 4.0), -0.35, 0.35, fw) * Band(y, 1.35, 2.3, fw);
                vec3 body = Lit(mix(paint, vec3(0.2, 0.22, 0.25), door * 0.85), n, 0.0, vWorld);
                float w = max(win, dwin);
                // Inside: a warm light and the riders' heads and shoulders against it.
                float slot = floor((x + 100.0) / 0.95);
                uint hs = Hash(uint(slot) * 2654435761u + seed * 97u);
                float person = step(0.45, float(hs & 255u) / 255.0);
                float hx = (fract((x + 100.0) / 0.95) - 0.5) * 0.95;
                float hy = y - (float((hs >> 8) & 1u) > 0.5 ? 2.0 : 1.72);
                float head = step(hx * hx + hy * hy * 0.8, 0.018) + step(abs(hx), 0.22) * step(hy, -0.16);
                vec3 inside = vec3(1.0, 0.9, 0.72) * 0.85 * (1.0 - 0.8 * clamp(head * person, 0.0, 1.0));
                vec3 glass = inside + Reflection(n, v, vec3(0.8)) * 0.25;
                col = mix(body, glass, w);
                glow = inside * w * 0.6;
            }
            else if (style == 19) // a car's end: a dark windscreen over the coupler
            {
                float screen = Band(vUv.y, 1.6, 2.6, fw) * Band(abs(vUv.x), -1.0, 1.05, fw);
                col = mix(Lit(vColor, n, 0.0, vWorld), vec3(0.03) + Reflection(n, v, vec3(0.8)) * 0.4, screen);
            }
            else if (style == 13) // neon: a blade sign's letters, or a lit billboard's picture; both flicker a little
            {
                uint h = Hash(uint(abs(vWorld.x) * 0.7) * 73856093u ^ uint(abs(vWorld.z) * 0.7) * 19349663u);
                float bright = min(min(vColor.r, vColor.g), vColor.b);
                float t = uTime + float(h & 255u);
                vec3 lit;
                if (bright > 0.6 && seed != 3u)
                {
                    // A billboard: big blocks of colour (a picture from across the street), a pale frame.
                    vec2 cell = floor(vUv / vec2(1.6, 1.1));
                    uint ch = Hash(uint(cell.x + 4096.0) * 7919u + uint(cell.y + 4096.0) * 104729u + h);
                    vec3 c = vec3(float(ch & 255u), float((ch >> 8) & 255u), float((ch >> 16) & 255u)) / 255.0;
                    lit = mix(vColor, c, 0.6) * 0.9;
                }
                else
                {
                    // Letters: strokes on a dark ground, a tube round them.
                    float strokes = step(0.52, Noise(vUv * vec2(2.2, 1.7) + float(h & 63u)));
                    float tube = 1.0 - smoothstep(0.0, 0.25, abs(fract(vUv.y / 0.9) - 0.5) - 0.38);
                    lit = vColor * (strokes * 1.4 + 0.12 + tube * 0.0);
                    // Now and then a sign stutters: off for a moment.
                    float stutter = step(0.985, fract(sin(floor(t * 4.0) * 12.9898 + float(h & 1023u)) * 43758.5453));
                    lit *= 1.0 - stutter * 0.85;
                    lit *= 0.85 + 0.15 * sin(t * 2.0 + vUv.y);
                }
                col = lit;
                glow = lit * 0.8;
            }
            else if (style == 21) // a rooftop pad: a dark deck, its circle and H, lamps blinking round its rim
            {
                vec3 up = Up();
                if (dot(n, up) > 0.8)
                {
                    float r = length(vUv);
                    vec2 a = abs(vUv);
                    float ring = Band(r, 4.6, 5.0, fw);
                    float hbar = Band(a.y, -0.3, 0.3, fw) * Band(a.x, -1.4, 1.4, fw);
                    float hleg = Band(a.x, 1.1, 1.6, fw) * Band(a.y, -2.1, 2.1, fw);
                    float mark = clamp(ring + hbar + hleg, 0.0, 1.0);
                    vec3 deck = vColor * (0.85 + 0.2 * Noise(vUv * 1.3));
                    col = Lit(mix(deck, vec3(0.95, 0.78, 0.2), mark), n, 0.0, vWorld);
                }
                else
                {
                    float dot1 = Band(abs(fract(vUv.x / 1.6) - 0.5), -0.12, 0.12, fw) * Band(vUv.y - floor(vUv.y), 0.0, 1.0, fw);
                    float on = 0.4 + 0.6 * step(0.5, fract(uTime * 0.7 - vUv.x / 40.0));
                    vec3 green = vec3(0.3, 1.0, 0.45) * dot1 * on;
                    col = Lit(vColor, n, 0.0, vWorld) + green * 1.5;
                    glow = green;
                }
            }
            else if (style == 20) // the metro's tiles: small glazed tiles, the line's band, the station's name frieze
            {
                float y = vUv.y;
                vec2 t = vec2(vUv.x / 0.15, y / 0.075);
                t.x += step(0.5, fract(t.y * 0.5)) * 0.5;
                vec2 g = abs(fract(t) - 0.5);
                float grout = 1.0 - Band(max(g.x * 0.15 / 0.075, g.y), 0.0, 0.42, fwidth(t.y));
                vec3 albedo = vColor * (1.0 - 0.1 * grout * clamp(1.0 - fw * 25.0, 0.0, 1.0));
                if (seed == 2u)
                {
                    // A suspended ceiling: 1.2 m panels in a dark grid.
                    vec2 q = abs(fract(vUv / 1.2) - 0.5);
                    float grid = 1.0 - Band(max(q.x, q.y), 0.0, 0.46, fwidth(vUv.x / 1.2) + fwidth(vUv.y / 1.2));
                    albedo = vColor * (1.0 - 0.45 * grid);
                }
                if (seed == 0u)
                {
                    // (the hall: platform tops are 10 m down unless the map says otherwise; the band sits 2 m over them)
                    float floorY = -10.0;
                    albedo = mix(albedo, vec3(0.12, 0.35, 0.58), Band(y, floorY + 2.0, floorY + 2.5, fw));
                    float panel = Band(y, floorY + 2.9, floorY + 3.7, fw) * Band(fract(vUv.x / 14.0), 0.1, 0.9, fwidth(vUv.x / 14.0));
                    float letters = step(0.5, Noise(vec2(vUv.x * 2.4, y * 2.5))) * Band(y, floorY + 3.05, floorY + 3.55, fw) * Band(fract(vUv.x / 14.0), 0.18, 0.82, fwidth(vUv.x / 14.0));
                    albedo = mix(albedo, vec3(0.08, 0.16, 0.3), panel);
                    albedo = mix(albedo, vec3(0.95), letters * panel);
                }
                col = Lit(albedo, n, 0.0, vWorld) + Reflection(n, v, vec3(0.15)) * gOut;
            }
            else // metal and the rest
                col = Lit(vColor * (0.85 + 0.2 * Noise(vUv * 2.0)), n, 0.0, vWorld) + vec3(0.02);
            col += glow;
            float a = uGlow > 0.5 && dot(glow, vec3(0.3333)) > 0.35 ? 0.0 : 1.0;
            FragColor = vec4(ApplyFog(col, vWorld), a);
        }
        """;

    private bool _ready, _broken;
    private GlProgram _program;
    private GlVertexArray _vao;
    private GlBuffer _vbo, _ibo;
    private int _locVp, _locChunkRel, _locChunkWorld, _locTime, _locGlow, _locPlanet, _locGroundR, _locCam, _locFog, _locFogRange;
    private CityGeometry? _geo;
    private int[] _order = [];
    private float[] _dist = [];
    private readonly Vector4[] _planes = new Vector4[6];

    /// <summary>Chunks and triangles drawn in the last frame (the perf line).</summary>
    public int DrawnChunks, DrawnTriangles;

    private double _logAt, _cpuMs, _gpuMs;
    private int _frames, _gpuSamples, _query;
    private const int QueryRing = 4;
    private readonly GlQuery[] _queries = new GlQuery[QueryRing];
    private readonly bool[] _queryPending = new bool[QueryRing];
    private bool _timers;

    /// <summary>Reads the oldest timer if it is done and starts this frame's (GL_TIME_ELAPSED, never stalling).</summary>
    private bool BeginTimer()
    {
        if (!_timers)
            return false;
        var q = _queries[_query];
        if (_queryPending[_query])
        {
            int available;
            Native.glGetQueryObjectiv(q.Id, GL_QUERY_RESULT_AVAILABLE, &available);
            if (available == 0)
                return false;
            ulong ns;
            Native.glGetQueryObjectui64v(q.Id, GL_QUERY_RESULT, &ns);
            _gpuMs += ns / 1e6;
            _gpuSamples++;
            _queryPending[_query] = false;
        }
        Native.glBeginQuery(GL_TIME_ELAPSED, q);
        return true;
    }

    public void Draw(Map map, Renderer gfx, double now, double clock)
    {
        PlanetRenderer.CityClip = default;
        if (_broken || map.CityGeometry is not { } geo || geo.Chunks.Length == 0)
            return;
        if (map.City is { } clipPlan)
        {
            // (the planetoid's ground under the city's apron is left out: CityGeometry.Apron)
            PlanetRenderer.CityClip = new Vector4(clipPlan.Up, (float)Math.Cos(geo.Reach / clipPlan.GroundRadius));
            PlanetRenderer.CityTop = (float)clipPlan.GroundRadius + 0.6f;
        }
        if (!_ready && !Init())
            return;
        if (geo != _geo)
            Upload(geo);
        var t0 = Stopwatch.GetTimestamp();
        ExtractPlanes(gfx.ViewProj);
        var cam = gfx.CameraPosition;
        // Visible chunks, nearest first (a few dozen: an insertion sort).
        var n = 0;
        for (var i = 0; i < geo.Chunks.Length; i++)
        {
            ref readonly var c = ref geo.Chunks[i];
            if (!SphereVisible(c.Centre, c.Radius))
                continue;
            var d = MathF.Max(Vector3.Distance(cam, c.Centre) - c.Radius, 0);
            var k = n++;
            while (k > 0 && _dist[k - 1] > d)
            {
                _dist[k] = _dist[k - 1];
                _order[k] = _order[k - 1];
                k--;
            }
            _dist[k] = d;
            _order[k] = i;
        }
        DrawnChunks = n;
        DrawnTriangles = 0;
        if (n == 0)
            return;
        var timing = BeginTimer();
        _program.Use();
        var vp = gfx.ViewProjRelative;
        Native.glUniformMatrix4fv(_locVp, 1, false, &vp.M11);
        Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
        var fog = gfx.FogColor;
        var fr = gfx.FogRange;
        Native.glUniform3f(_locFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_locFogRange, fr.X, fr.Y);
        Native.glUniform1f(_locTime, (float)(now % 3600));
        Native.glUniform1f(_locGlow, gfx.MarkGlow ? 1f : 0f);
        var pc = map.Planetoid?.Center ?? Vector3.Zero;
        Native.glUniform3f(_locPlanet, pc.X, pc.Y, pc.Z);
        Native.glUniform1f(_locGroundR, map.City is { } cityPlan ? (float)cityPlan.GroundRadius : 0f);
        _vao.Bind();
        Native.glEnable(GlCapability.CullFace);
        Native.glCullFace(GlFace.Back);
        var origin = gfx.Origin;
        // The apron first, pushed back in depth: at a distance the depth buffer can't tell 15 cm apart.
        Native.glEnable(GlCapability.PolygonOffsetFill);
        Native.glPolygonOffset(4f, 64f);
        for (var k = 0; k < n; k++)
        {
            ref readonly var c = ref geo.Chunks[_order[k]];
            if (c.Under == 0)
                continue;
            var rel = c.Origin - origin;
            Native.glUniform3f(_locChunkRel, rel.X, rel.Y, rel.Z);
            Native.glUniform3f(_locChunkWorld, c.Origin.X, c.Origin.Y, c.Origin.Z);
            Native.glDrawElements(GL_TRIANGLES, c.Under, GL_UNSIGNED_INT, (void*)((nint)c.UnderFirst * sizeof(uint)));
        }
        Native.glPolygonOffset(0f, 0f);
        Native.glDisable(GlCapability.PolygonOffsetFill);
        for (var k = 0; k < n; k++)
        {
            ref readonly var c = ref geo.Chunks[_order[k]];
            var count = c.Main + (_dist[k] < DetailRange ? c.Detail : 0);
            if (count == 0)
                continue;
            var rel = c.Origin - origin;
            Native.glUniform3f(_locChunkRel, rel.X, rel.Y, rel.Z);
            Native.glUniform3f(_locChunkWorld, c.Origin.X, c.Origin.Y, c.Origin.Z);
            PerfCounters.Draw(count, 1);
            Native.glDrawElements(GL_TRIANGLES, count, GL_UNSIGNED_INT, (void*)((nint)c.First * sizeof(uint)));
            DrawnTriangles += count / 3;
        }
        DrawLife(map, gfx, geo, clock);
        Native.glDisable(GlCapability.CullFace);
        if (timing)
        {
            Native.glEndQuery(GL_TIME_ELAPSED);
            _queryPending[_query] = true;
            _query = (_query + 1) % QueryRing;
        }
        gfx.Rebind();
        _frames++;
        _cpuMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (now - _logAt > 5)
        {
            if (_logAt > 0)
                Console.WriteLine($"[city] {map.Name}: {n} of {geo.Chunks.Length} chunks, {DrawnTriangles / 1000}k triangles, CPU {_cpuMs / _frames:0.000} ms, GPU {(_gpuSamples > 0 ? $"{_gpuMs / _gpuSamples:0.000} ms" : "n/a")} a frame, {_frames / (now - _logAt):0} fps");
            _logAt = now;
            _frames = 0;
            _cpuMs = 0;
            _gpuMs = 0;
            _gpuSamples = 0;
        }
    }

    private void Upload(CityGeometry geo)
    {
        _vao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        fixed (CityVertex* p = geo.Vertices)
            Native.glBufferData(GL_ARRAY_BUFFER, (nint)geo.Vertices.Length * CityVertex.SizeInBytes, p, GL_STATIC_DRAW);
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ibo);
        fixed (uint* p = geo.Indices)
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, (nint)geo.Indices.Length * sizeof(uint), p, GL_STATIC_DRAW);
        _order = new int[geo.Chunks.Length];
        _dist = new float[geo.Chunks.Length];
        _geo = geo;
        Console.WriteLine($"[city] uploaded {geo.Vertices.Length} vertices, {geo.Triangles} triangles ({(geo.Vertices.Length * CityVertex.SizeInBytes + geo.Indices.Length * 4) / 1024} KB)");
    }

    private static int Uniform(GlProgram program, ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(program, p);
    }

    private bool Init()
    {
        try
        {
            var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(VertexSrc)).Compile();
            var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();
            _program = CreateProgram();
            Native.glAttachShader(_program, vs);
            Native.glAttachShader(_program, fs);
            Native.glLinkProgram(_program);
            GlDebug.Label(GL_PROGRAM, _program.Id, "CityRenderer");
            int linked;
            Native.glGetProgramiv(_program, GL_LINK_STATUS, &linked);
            if (linked == 0)
            {
                var log = stackalloc byte[2048];
                int len;
                Native.glGetProgramInfoLog(_program, 2048, &len, log);
                throw new InvalidOperationException("shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
            }
            vs.Dispose();
            fs.Dispose();
            Lighting.Upload(_program);
            _program.Use();
            _locRot = Uniform(_program, "uRot\0"u8);
            float* rotId = stackalloc float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            Native.glUniformMatrix3fv(_locRot, 1, false, rotId);
            _locVp = Uniform(_program, "uViewProjRel\0"u8);
            _locChunkRel = Uniform(_program, "uChunkRel\0"u8);
            _locChunkWorld = Uniform(_program, "uChunkWorld\0"u8);
            _locTime = Uniform(_program, "uTime\0"u8);
            _locGlow = Uniform(_program, "uGlow\0"u8);
            _locPlanet = Uniform(_program, "uPlanet\0"u8);
            _locGroundR = Uniform(_program, "uGroundR\0"u8);
            _locCam = Uniform(_program, "uCam\0"u8);
            _locFog = Uniform(_program, "uFog\0"u8);
            _locFogRange = Uniform(_program, "uFogRange\0"u8);
            _vao = GenVertexArray();
            _vao.Bind();
            _vbo = GenBuffer();
            _ibo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
            const int stride = CityVertex.SizeInBytes;
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)12);
            Native.glVertexAttribPointer(2, 2, GL_FLOAT, false, stride, (void*)24);
            Native.glVertexAttribPointer(3, 4, GL_UNSIGNED_BYTE, true, stride, (void*)32);
            Native.glVertexAttribPointer(4, 4, GL_UNSIGNED_BYTE, false, stride, (void*)36);
            for (uint a = 0; a < 5; a++)
                EnableVertexAttribArray(a);
            Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ibo);
            if (Native.glGenQueries != null && Native.glBeginQuery != null && Native.glGetQueryObjectui64v != null && Native.glGetQueryObjectiv != null)
            {
                fixed (GlQuery* q = _queries)
                    Native.glGenQueries(QueryRing, q);
                _timers = true;
            }
            _ready = true;
            Console.WriteLine("[city] renderer ready");
            return true;
        }
        catch (Exception ex)
        {
            _broken = true;
            Console.WriteLine($"[city] renderer failed, no city drawn: {ex.Message}");
            return false;
        }
    }

    private void ExtractPlanes(in Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        _planes[0] = c4 + c1;
        _planes[1] = c4 - c1;
        _planes[2] = c4 + c2;
        _planes[3] = c4 - c2;
        _planes[4] = c4 + c3;
        _planes[5] = c4 - c3;
        for (var i = 0; i < 6; i++)
        {
            var len = new Vector3(_planes[i].X, _planes[i].Y, _planes[i].Z).Length();
            if (len > 1e-12f)
                _planes[i] /= len;
        }
    }

    private bool SphereVisible(Vector3 c, float r)
    {
        foreach (var p in _planes)
            if (p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W < -r)
                return false;
        return true;
    }
}
