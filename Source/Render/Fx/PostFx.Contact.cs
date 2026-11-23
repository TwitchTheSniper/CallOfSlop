using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Contact shadows (Settings > Video > Contact shadows; Karl, 2026-11-21, from the Ship Render Bench): a soft dark patch on the
/// ground under each construct near the camera, darkest where the hull is closest, so a landed ship sits on the ground instead of
/// floating over it. Each body gets a small footprint once per rebuild (<see cref="Foot"/>: how much of each cell of its plan its
/// boxes cover, blurred, and the lowest box bottom over it), made on the CPU from its parts' boxes in its own frame. After the
/// opaque world, the depth drawn so far is read back to the body's frame pixel by pixel (inside the screen rectangle the body's
/// footprint box covers): a pixel below the hull and within reach of it is darkened by the cover over it and how near the hull is.
/// It multiplies the colour only, so the bloom's mark in the alpha is left alone. Cost: one depth copy (shared with ambient
/// occlusion), and per body a pass over its rectangle; at most <see cref="MaxBodies"/> bodies, nearest first.
/// </summary>
public static unsafe partial class PostFx
{
    public const int MaxBodies = 8, FootCells = 64;

    /// <summary>The setting; <see cref="ContactForcedOff"/>: --no-contact.</summary>
    public static bool ContactOn = true, ContactForcedOff;

    /// <summary>How far below the hull the shadow reaches (m), and how dark it is right under it.</summary>
    public const float ContactReach = 3f, ContactDark = 0.7f;

    private struct Offer
    {
        public Construct Body;
        public Matrix4x4 Model; // the body's frame from the render origin, as drawn
        public float Dist2;
    }

    private sealed class Foot
    {
        public uint Tex;
        public long Shape = -1; // (the body's boxes it was made from: ConstructShapeVersion.Both)
        public int Used;
        public Vector4 Rect; // min x, min z, 1 / size x, 1 / size z (the body's frame)
        public Vector3 Min, Max; // the footprint box in the body's frame: the plan's rectangle, from the reach under the lowest box to its top
        public bool Empty;
    }

    private static readonly Offer[] Offers = new Offer[MaxBodies];
    private static int _offerCount, _contactFrame;
    private static readonly Dictionary<int, Foot> Feet = new();
    private static readonly List<int> FeetGone = new();
    private static readonly float[] FootData = new float[FootCells * FootCells * 2], FootCover = new float[FootCells * FootCells], FootLow = new float[FootCells * FootCells], FootTmp = new float[FootCells * FootCells];
    private static GlProgram _contact;
    private static int _lCtProj, _lCtToBody, _lCtRect, _lCtShade;
    private static bool _contactBroken, _depthFresh;

    /// <summary>Number of bodies the last <see cref="ApplyContact"/> shaded (the F9 window and checks).</summary>
    public static int LastContactBodies;

    /// <summary>ConstructRender.Draw offers each body it draws in full; the nearest <see cref="MaxBodies"/> are kept for this frame.</summary>
    public static void OfferContact(Construct c, in Matrix4x4 model, float dist2)
    {
        if (!ContactOn || ContactForcedOff || c.PartCount == 0)
            return;
        var k = _offerCount;
        if (k == MaxBodies)
        {
            var far = 0;
            for (var i = 1; i < k; i++)
                if (Offers[i].Dist2 > Offers[far].Dist2)
                    far = i;
            if (Offers[far].Dist2 <= dist2)
                return;
            k = far;
        }
        else
            _offerCount++;
        Offers[k] = new Offer { Body = c, Model = model, Dist2 = dist2 };
    }

    private const string ContactFs = """
        #version 410 core
        in vec2 vUv;
        uniform sampler2D uDepth;
        uniform sampler2D uTex2;   // the footprint: r cover (blurred), g the lowest box bottom over the cell (body frame)
        uniform vec4 uProj;        // M33, M43, 1 / M11, 1 / M22
        uniform mat4 uToBody;      // view space to the body's frame
        uniform vec4 uRect;        // the plan: min x, min z, 1 / size x, 1 / size z
        uniform vec3 uShade;       // reach, darkness, the pass's own: 1
        out vec4 FragColor;
        void main()
        {
            float d0 = texture(uDepth, vUv).r;
        #if REVZ
            if (d0 <= 1e-9)
                discard;
            float nd = d0;
        #else
            if (d0 >= 0.99999)
                discard;
            float nd = d0 * 2.0 - 1.0;
        #endif
            float l = uProj.y / (nd + uProj.x);
            vec3 v = vec3((vUv * 2.0 - 1.0) * uProj.zw * l, -l);
            vec3 p = (uToBody * vec4(v, 1.0)).xyz;
            vec2 uv = (p.xz - uRect.xy) * uRect.zw;
            if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0)
                discard;
            vec2 f = texture(uTex2, uv).rg;
            float gap = f.g - p.y;
            if (f.r < 0.004 || gap < 0.04) // (nothing over it, or the hull itself)
                discard;
            float s = f.r * (1.0 - smoothstep(0.0, uShade.x, gap)) * uShade.y * smoothstep(0.04, 0.2, gap);
            FragColor = vec4(vec3(1.0 - s), 1.0);
        }
        """;

    /// <summary>The contact shadows of the bodies offered this frame, over the opaque world (after ambient occlusion, before the effects).</summary>
    public static void ApplyContact(Renderer gfx, int w, int h, in Matrix4x4 proj)
    {
        var n = _offerCount;
        _offerCount = 0;
        LastContactBodies = 0;
        _contactFrame++;
        if ((_contactFrame & 255) == 0)
            DropOldFeet();
        if (n == 0 || !ContactOn || ContactForcedOff || _contactBroken || !Init())
        {
            ClearOffers(n);
            return;
        }
        if (_contact.Id == 0)
        {
            try
            {
                _contact = Link(QuadVs, ContactFs, "PostFx.contact");
                _lCtProj = Loc(_contact, "uProj\0"u8);
                _lCtToBody = Loc(_contact, "uToBody\0"u8);
                _lCtRect = Loc(_contact, "uRect\0"u8);
                _lCtShade = Loc(_contact, "uShade\0"u8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[postfx] contact shadows off: {ex.Message}");
                _contactBroken = true;
                ClearOffers(n);
                return;
            }
        }
        if (!Matrix4x4.Invert(gfx.ViewRelative, out var viewInv))
        {
            ClearOffers(n);
            return;
        }
        var viewProj = gfx.ViewRelative * proj;
        Resize(w, h);
        var fresh = _depthFresh;
        _depthFresh = false;
        if (!fresh && !CopyDepth(w, h))
        {
            Console.WriteLine("[postfx] contact shadows off: the depth buffer could not be read");
            _contactBroken = true;
            End(gfx, w, h);
            ClearOffers(n);
            return;
        }
        Begin();
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, Renderer.MainFbo);
        Viewport(0, 0, w, h);
        Native.glEnable(GlCapability.Blend);
        Native.glBlendFunc(GL_ZERO, GL_SRC_COLOR);
        Native.glColorMask(true, true, true, false);
        Native.glEnable(GlCapability.ScissorTest);
        _contact.Use();
        Native.glUniform4f(_lCtProj, proj.M33, proj.M43, 1f / proj.M11, 1f / proj.M22);
        Native.glUniform3f(_lCtShade, ContactReach, ContactDark, 1f);
        Bind(Unit0, _depthTex);
        for (var k = 0; k < n; k++)
        {
            ref var o = ref Offers[k];
            var foot = FootOf(o.Body);
            if (foot.Empty || !Matrix4x4.Invert(o.Model, out var modelInv))
                continue;
            if (!ScreenRect(foot, o.Model * viewProj, w, h, out var x0, out var y0, out var x1, out var y1))
                continue;
            Native.glScissor(x0, y0, x1 - x0, y1 - y0);
            var toBody = viewInv * modelInv;
            Native.glUniformMatrix4fv(_lCtToBody, 1, false, &toBody.M11);
            Native.glUniform4f(_lCtRect, foot.Rect.X, foot.Rect.Y, foot.Rect.Z, foot.Rect.W);
            Bind(Unit1, foot.Tex);
            Native.glDrawArrays(GL_TRIANGLES, 0, 3);
            LastContactBodies++;
        }
        Native.glDisable(GlCapability.ScissorTest);
        End(gfx, w, h);
        ClearOffers(n);
    }

    private static void ClearOffers(int n)
    {
        for (var k = 0; k < n; k++)
            Offers[k].Body = null!;
    }

    /// <summary>The window pixels the footprint box covers; false when it is behind the eye or off screen. A box that reaches behind the eye takes the whole window.</summary>
    private static bool ScreenRect(Foot f, in Matrix4x4 toClip, int w, int h, out int x0, out int y0, out int x1, out int y1)
    {
        float lx = 1, ly = 1, hx = -1, hy = -1;
        var behind = false;
        for (var i = 0; i < 8; i++)
        {
            var p = new Vector3((i & 1) == 0 ? f.Min.X : f.Max.X, (i & 2) == 0 ? f.Min.Y : f.Max.Y, (i & 4) == 0 ? f.Min.Z : f.Max.Z);
            var c = Vector4.Transform(new Vector4(p, 1f), toClip);
            if (c.W <= 0.05f)
            {
                behind = true;
                break;
            }
            float sx = c.X / c.W, sy = c.Y / c.W;
            lx = MathF.Min(lx, sx);
            ly = MathF.Min(ly, sy);
            hx = MathF.Max(hx, sx);
            hy = MathF.Max(hy, sy);
        }
        if (behind)
        {
            (lx, ly, hx, hy) = (-1, -1, 1, 1);
        }
        x0 = Math.Clamp((int)MathF.Floor((lx * 0.5f + 0.5f) * w), 0, w);
        y0 = Math.Clamp((int)MathF.Floor((ly * 0.5f + 0.5f) * h), 0, h);
        x1 = Math.Clamp((int)MathF.Ceiling((hx * 0.5f + 0.5f) * w), 0, w);
        y1 = Math.Clamp((int)MathF.Ceiling((hy * 0.5f + 0.5f) * h), 0, h);
        return x1 > x0 && y1 > y0;
    }

    /// <summary>The body's footprint, made again when its boxes change (ConstructShapeVersion), not on every rebuild.</summary>
    private static Foot FootOf(Construct c)
    {
        if (!Feet.TryGetValue(c.Id, out var f))
            Feet[c.Id] = f = new Foot();
        f.Used = _contactFrame;
        if (f.Shape != ConstructShapeVersion.Both(c))
            MakeFoot(c, f);
        return f;
    }

    // The plan of the body's boxes in its own frame (rest poses): each cell's cover and the lowest bottom over it, then the cover
    // blurred about half a metre and the bottoms spread into the cells round them, so the shadow's edge is soft and its height is
    // known a little past the hull.
    private static void MakeFoot(Construct c, Foot f)
    {
        f.Shape = ConstructShapeVersion.Both(c);
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Growing(i))
                continue; // (a piece growing on a die: out of the footprint, which keeps while it grows: Construct.GrowStamp)
            ref var part = ref c.Parts[i];
            var def = c.DefOf(i);
            foreach (var b in def.Boxes)
            {
                if (b.Hidden)
                    continue;
                var centre = part.Pos + Rot24.Apply(part.Rot, b.Center);
                var half = Rot24.ApplyHalf(part.Rot, b.Half);
                lo = Vector3.Min(lo, centre - half);
                hi = Vector3.Max(hi, centre + half);
            }
        }
        if (lo.X > hi.X)
        {
            f.Empty = true;
            return;
        }
        f.Empty = false;
        var margin = 0.8f;
        float x0 = lo.X - margin, z0 = lo.Z - margin, sx = hi.X - lo.X + margin * 2, sz = hi.Z - lo.Z + margin * 2;
        f.Rect = new Vector4(x0, z0, 1f / sx, 1f / sz);
        f.Min = new Vector3(x0, lo.Y - ContactReach - 0.5f, z0);
        f.Max = new Vector3(x0 + sx, hi.Y, z0 + sz);
        const int N = FootCells;
        float cx = sx / N, cz = sz / N;
        Array.Clear(FootCover);
        Array.Fill(FootLow, float.MaxValue);
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Growing(i))
                continue; // (a piece growing on a die: out of the footprint, which keeps while it grows: Construct.GrowStamp)
            ref var part = ref c.Parts[i];
            var def = c.DefOf(i);
            foreach (var b in def.Boxes)
            {
                if (b.Hidden)
                    continue;
                var centre = part.Pos + Rot24.Apply(part.Rot, b.Center);
                var half = Rot24.ApplyHalf(part.Rot, b.Half);
                var bottom = centre.Y - half.Y;
                int ia = Math.Clamp((int)((centre.X - half.X - x0) / cx), 0, N - 1), ib = Math.Clamp((int)((centre.X + half.X - x0) / cx), 0, N - 1);
                int ja = Math.Clamp((int)((centre.Z - half.Z - z0) / cz), 0, N - 1), jb = Math.Clamp((int)((centre.Z + half.Z - z0) / cz), 0, N - 1);
                // A box thinner than a cell covers its share of it (a rail, a leg), so thin things cast a lighter shadow.
                var share = MathF.Min(1f, half.X * 2 / cx) * MathF.Min(1f, half.Z * 2 / cz);
                for (var j = ja; j <= jb; j++)
                    for (var ii = ia; ii <= ib; ii++)
                    {
                        var at = j * N + ii;
                        FootCover[at] = MathF.Min(1f, FootCover[at] + share);
                        if (bottom < FootLow[at])
                            FootLow[at] = bottom;
                    }
            }
        }
        // Blur the cover (a box filter twice, about 0.5 m) and spread the bottoms out to the same reach.
        var rx = Math.Clamp((int)MathF.Round(0.5f / cx), 1, 6);
        var rz = Math.Clamp((int)MathF.Round(0.5f / cz), 1, 6);
        for (var pass = 0; pass < 2; pass++)
        {
            BoxBlur(FootCover, FootTmp, rx, true);
            BoxBlur(FootTmp, FootCover, rz, false);
        }
        for (var pass = 0; pass < 2; pass++)
        {
            SpreadLow(FootLow, FootTmp, rx, true);
            SpreadLow(FootTmp, FootLow, rz, false);
        }
        for (var k = 0; k < N * N; k++)
        {
            FootData[k * 2] = FootCover[k];
            FootData[k * 2 + 1] = FootLow[k] == float.MaxValue ? lo.Y : FootLow[k];
        }
        if (f.Tex == 0)
        {
            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            f.Tex = tex.Id;
        }
        Native.glActiveTexture(GL_TEXTURE0 + Unit1);
        Native.glBindTexture(GL_TEXTURE_2D, f.Tex);
        fixed (float* d = FootData)
            Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RG32F, N, N, 0, GL_RG, GL_FLOAT, d);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glActiveTexture(GL_TEXTURE0);
    }

    private static void BoxBlur(float[] src, float[] dst, int r, bool alongX)
    {
        const int N = FootCells;
        for (var j = 0; j < N; j++)
            for (var i = 0; i < N; i++)
            {
                float sum = 0;
                for (var d = -r; d <= r; d++)
                {
                    int a = alongX ? i + d : i, b = alongX ? j : j + d;
                    if (a >= 0 && a < N && b >= 0 && b < N)
                        sum += src[b * N + a];
                }
                dst[j * N + i] = sum / (2 * r + 1);
            }
    }

    private static void SpreadLow(float[] src, float[] dst, int r, bool alongX)
    {
        const int N = FootCells;
        for (var j = 0; j < N; j++)
            for (var i = 0; i < N; i++)
            {
                var low = src[j * N + i];
                if (low == float.MaxValue)
                {
                    float sum = 0;
                    var count = 0;
                    for (var d = -r; d <= r; d++)
                    {
                        int a = alongX ? i + d : i, b = alongX ? j : j + d;
                        if (a >= 0 && a < N && b >= 0 && b < N && src[b * N + a] != float.MaxValue)
                        {
                            sum += src[b * N + a];
                            count++;
                        }
                    }
                    if (count > 0)
                        low = sum / count;
                }
                dst[j * N + i] = low;
            }
    }

    /// <summary>Footprints of bodies not offered for a while (gone, far or out of view) are freed.</summary>
    private static void DropOldFeet()
    {
        FeetGone.Clear();
        foreach (var (id, f) in Feet)
            if (_contactFrame - f.Used > 600)
            {
                if (f.Tex != 0)
                {
                    var t = new GlTexture(f.Tex);
                    Native.glDeleteTextures(1, &t);
                }
                FeetGone.Add(id);
            }
        foreach (var id in FeetGone)
            Feet.Remove(id);
    }
}
