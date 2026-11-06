using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Bombs and missiles in the air drawn as themselves (ShellLooks.cs, docs/sandbox/CONSTRUCTS.md "Modular ordnance: in the air"): the model
/// they had on the rack at their real size, turned as the shell's tick turn (nose along its way), with what a designed one's flight
/// knows shown on it: spent stages gone, pop-out wings folded until they deploy, a chute or ballute open behind it while it retards,
/// a flame at the nozzle while a stage burns. One model a part type, made the first time it is seen (never per frame); plain boxes go
/// into the frame's instanced batch, contoured ones are a draw each, so far away (or past a frame's budget) a shell is one batched box.
/// </summary>
public static partial class ConstructRender
{
    private const float ShellDetail = 0.015f; // a shell's length over its distance below which it is one box (a 2 m bomb at 130 m)
    private const int ShellShapedBudget = 400; // (a few dozen near ones in full: each contoured box is a draw call) // contoured draws a frame for shells in full (the rest are one box each)
    private const float ShellFlameReach = 600f; // m: no nozzle flame past this (the tracer still burns)
    private const int ShellFlamesMax = 64; // nozzle flames a frame (a salvo of rockets shares the particles with everything else)
    private const float WingOpenTicks = 12f, ChuteOpenTicks = 10f;

    /// <summary>Shells drawn in full and as one box last frame, and the draws they took (the F9 window, hand checks).</summary>
    public static int LastShellsFull { get; private set; }
    public static int LastShellsFar { get; private set; }
    public static int LastShellDraws { get; private set; }

    private static readonly Dictionary<ConstructPartDef, ShellModel> _shellModels = new(ReferenceEqualityComparer.Instance);
    private static double _shellFxLast;
    private static float _shellDetail = ShellDetail; // this frame's: raised while the near shells want more than the budget, so the nearest keep their model (not the first in the pool)
    private static int _shellWanted, _shellFlames;

    /// <summary>After a frame's shells: the detail threshold follows how many contoured draws the shells wanted.</summary>
    private static void ShellDetailNext()
    {
        if (_shellWanted > ShellShapedBudget)
            _shellDetail *= 1.25f;
        else if (_shellWanted < ShellShapedBudget * 0.6f && _shellDetail > ShellDetail)
            _shellDetail = MathF.Max(ShellDetail, _shellDetail * 0.92f);
        _shellWanted = _shellFlames = 0;
    }

    /// <summary>A part type as a shell draws it, relative to its bounds' centre (where the shell is).</summary>
    private sealed class ShellModel
    {
        public int Count, Shaped;
        public Matrix4x4[] Local = [];
        public Vector3[] Colour = [], Centre = [], Half = [];
        public ushort[] Shape = [];
        public float[] Surface = [], Fold = [];
        public byte[] Mode = [];
        public short[] Piece = []; // the design's piece each box draws (0 for a part file's)
        public int[] Cut = [int.MaxValue]; // by stages dropped: the pieces still flying (those before it)
        public float[] TailZ = [0]; // ...the tail of what is left (+Z is aft)
        public Vector3[] FarMin = [Vector3.Zero], FarMax = [Vector3.Zero];
        public bool[] Flame = []; // by stage burning: its motors show a flame
        public Vector3 FarColour;
        public float Radius;
        public int ChutePiece = -1;
        public bool Ballute;
        public ushort ChuteShape;
        public Vector3 ChuteColour;
    }

    private static ShellModel ShellModelOf(ConstructPartDef d)
    {
        if (_shellModels.TryGetValue(d, out var m))
            return m;
        m = BuildShellModel(d);
        _shellModels[d] = m;
        return m;
    }

    private static ShellModel BuildShellModel(ConstructPartDef d)
    {
        var m = new ShellModel();
        var offset = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var st = d.OrdStats;
        var design = d.OrdDesign;
        var n = 0;
        foreach (var b in d.Boxes)
            n += b.Hidden ? 0 : 1;
        m.Local = new Matrix4x4[n];
        m.Colour = new Vector3[n];
        m.Centre = new Vector3[n];
        m.Half = new Vector3[n];
        m.Shape = new ushort[n];
        m.Surface = new float[n];
        m.Fold = new float[n];
        m.Mode = new byte[n];
        m.Piece = new short[n];
        var biggest = -1f;
        for (var i = 0; i < d.Boxes.Length; i++)
        {
            var b = d.Boxes[i];
            if (b.Hidden)
                continue;
            var k = m.Count++;
            m.Local[k] = b.Model(0, b.Center - offset);
            m.Colour[k] = b.Color;
            m.Centre[k] = b.Center - offset;
            m.Half[k] = b.Half;
            m.Shape[k] = b.Shape;
            m.Surface[k] = Surfaces.Code(b.Surface, 0f);
            m.Mode[k] = b.Glow ? (byte)2 : (byte)0;
            m.Piece[k] = st != null && i < st.BoxPiece.Length ? Math.Max((short)0, st.BoxPiece[i]) : (short)0;
            m.Fold[k] = st != null && i < st.BoxFold.Length ? st.BoxFold[i] : 0f;
            if (b.Shape != 0)
                m.Shaped++;
            var volume = b.Half.X * b.Half.Y * b.Half.Z;
            if (volume > biggest)
                (biggest, m.FarColour) = (volume, b.Color);
        }
        // Stages: what is left after each drop (Stages are tail first; a dropped stage takes the separator ahead of it).
        if (design != null && st != null)
        {
            var seps = new List<int>();
            for (var p = 0; p < design.Pieces.Count; p++)
                if (design.Pieces[p].Section.Ord!.Slot == "stage")
                    seps.Add(p);
            m.Cut = new int[seps.Count + 1];
            m.Cut[0] = design.Pieces.Count;
            for (var k = 1; k <= seps.Count; k++)
                m.Cut[k] = seps[seps.Count - k];
            var stages = st.Flight.Stages;
            m.Flame = new bool[stages.Count];
            for (var k = 0; k < stages.Count; k++)
                m.Flame[k] = stages[k].Thrust > 0 && stages[k].Fuels != "cold gas"; // (a bottle of gas pushes without a flame)
            m.Radius = st.Diameter * 0.5f;
            for (var p = 0; p < design.Pieces.Count; p++)
            {
                var o = design.Pieces[p].Section.Ord!;
                if (m.ChutePiece >= 0 || o.Num("deploy") <= 0 || o.Num("drag") <= 0 || o.Num("lift") > 0)
                    continue;
                m.ChutePiece = p; // (a retard chute or a ballute: its pack's look is the canopy's)
                m.Ballute = o.Num("drag") < 1f;
                for (var k = m.Count - 1; k >= 0; k--)
                    if (m.Piece[k] == p)
                    {
                        (m.ChuteShape, m.ChuteColour) = (m.Shape[k], m.Colour[k]);
                        break;
                    }
            }
        }
        var cuts = m.Cut.Length;
        m.TailZ = new float[cuts];
        m.FarMin = new Vector3[cuts];
        m.FarMax = new Vector3[cuts];
        for (var c = 0; c < cuts; c++)
        {
            Vector3 mn = new(float.MaxValue), mx = new(float.MinValue);
            for (var k = 0; k < m.Count; k++)
            {
                if (m.Piece[k] >= m.Cut[c])
                    continue;
                var h = m.Fold[k] > 0 ? m.Half[k] with { X = m.Fold[k] } : m.Half[k];
                mn = Vector3.Min(mn, m.Centre[k] - h);
                mx = Vector3.Max(mx, m.Centre[k] + h);
            }
            if (mn.X > mx.X)
                (mn, mx) = (d.BoundsMin - offset, d.BoundsMax - offset);
            (m.FarMin[c], m.FarMax[c], m.TailZ[c]) = (mn, mx, mx.Z);
        }
        if (m.Radius <= 0)
            m.Radius = MathF.Min(m.FarMax[0].X - m.FarMin[0].X, m.FarMax[0].Y - m.FarMin[0].Y) * 0.5f;
        return m;
    }

    /// <summary>A shell with a look: its model at this frame's pose. Returns the draws.</summary>
    private static int DrawShellLook(Renderer gfx, ref CShell s, ConstructPartDef look, float alpha, float fxDt, ref int shapedLeft, ref int full, ref int far, bool cull)
    {
        var m = ShellModelOf(look);
        var at = s.Landed ? s.Pos : Vector3.Lerp(s.Prev, s.Pos, alpha);
        var rot = s.Landed ? s.Rot : Quaternion.Normalize(Quaternion.Slerp(s.PrevRot, s.Rot, alpha));
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(at);
        var cut = Math.Min((int)s.Stage, m.Cut.Length - 1);
        var keep = m.Cut[cut];
        var age = s.Age + (s.Landed ? 0 : alpha);
        var deployed = s.Deploy <= 0 ? 1f : Math.Clamp((age - s.Deploy) / WingOpenTicks, 0f, 1f);
        var length = m.FarMax[cut].Z - m.FarMin[cut].Z;
        var dist = Vector3.Distance(gfx.CameraPosition, at);
        var drawn = 0;
        if (cull && !InView(at, length * 0.5f + m.Radius * 8f + 0.5f)) // (out of view: nothing drawn, its flame still burns)
            drawn = -1;
        else if (length > dist * _shellDetail && (_shellWanted += m.Shaped) > 0 && shapedLeft >= m.Shaped)
        {
            full++;
            shapedLeft -= m.Shaped;
            for (var k = 0; k < m.Count; k++)
            {
                if (m.Piece[k] >= keep)
                    continue;
                if (m.Fold[k] > 0 && deployed < 1f)
                {
                    // A pop-out wing: folded inside the body until it deploys, then out over a few ticks.
                    var t = deployed * deployed * (3f - 2f * deployed);
                    var h = m.Half[k] with { X = float.Lerp(m.Fold[k], m.Half[k].X, t) };
                    gfx.Draw(Matrix4x4.CreateScale(h * 2) * Matrix4x4.CreateTranslation(m.Centre[k]) * frame, m.Colour[k], 1f, mode: m.Mode[k], surface: m.Surface[k]);
                }
                else
                    gfx.Draw(m.Local[k] * frame, m.Colour[k], 1f, mode: m.Mode[k], shape: m.Shape[k], surface: m.Surface[k]);
                drawn++;
            }
            if (m.ChutePiece >= 0 && m.ChutePiece < keep && !s.Landed && s.Deploy > 0 && age >= s.Deploy)
                drawn += DrawChute(gfx, m, m.TailZ[cut], Math.Clamp((age - s.Deploy) / ChuteOpenTicks, 0.05f, 1f), frame);
        }
        else
        {
            far++;
            var mn = m.FarMin[cut];
            var mx = m.FarMax[cut];
            gfx.Draw(Matrix4x4.CreateScale(Vector3.Max(mx - mn, new Vector3(0.02f))) * Matrix4x4.CreateTranslation((mn + mx) * 0.5f) * frame, m.FarColour);
            drawn++;
        }
        // A stage burning: the thrusters' flame at its nozzle (Fx particles, as a construct's thruster).
        var stage = Math.Min((int)s.Stage, m.Flame.Length - 1);
        if (fxDt > 0 && _shellFlames < ShellFlamesMax && !s.Landed && stage >= 0 && m.Flame[stage] && s.Burn > 0 && s.Wait == 0 && dist < ShellFlameReach)
        {
            var aft = Vector3.Transform(Vector3.UnitZ, rot);
            var r = MathF.Max(0.03f, m.Radius * 0.7f);
            Fx.Flame(FxKind.Flame, at + aft * m.TailZ[cut], aft, s.Vel, r, MathF.Max(0.6f, r * 10f), 1f, fxDt);
            _shellFlames++;
        }
        return Math.Max(0, drawn);
    }

    /// <summary>Is a sphere in this pass's view (the frustum <see cref="CullPlanes"/> made)?</summary>
    private static bool InView(Vector3 centre, float r)
    {
        for (var k = 0; k < 6; k++)
        {
            var p = _planes[k];
            if (p.X * centre.X + p.Y * centre.Y + p.Z * centre.Z + p.W < -r)
                return false;
        }
        return true;
    }

    /// <summary>A retard chute (a wide canopy on lines, well behind) or a ballute (a balloon close behind), opening by <paramref name="open"/>. +Z is aft.</summary>
    private static int DrawChute(Renderer gfx, ShellModel m, float tail, float open, in Matrix4x4 frame)
    {
        var r = m.Radius;
        var across = (m.Ballute ? r * 4.4f : r * 7f) * open;
        var deep = m.Ballute ? across * 0.75f : across * 0.55f;
        var gap = m.Ballute ? r * 1.5f : across * 0.9f + r * 2f;
        var centre = new Vector3(0, 0, tail + gap + deep * 0.5f);
        var shape = m.Ballute ? Matrix4x4.Identity : Matrix4x4.CreateRotationX(MathF.PI); // (a chute's narrow end towards the bomb)
        gfx.Draw(Matrix4x4.CreateScale(across, across, deep) * shape * Matrix4x4.CreateTranslation(centre) * frame, m.ChuteColour, 1f, shape: m.ChuteShape);
        // Its lines: from the tail to the canopy's rim (one tether for a ballute).
        var from = new Vector3(0, 0, tail);
        var lines = m.Ballute ? 1 : 4;
        var rim = m.Ballute ? 0f : across * 0.42f;
        for (var k = 0; k < lines; k++)
        {
            var a = k * MathF.PI * 0.5f + MathF.PI * 0.25f;
            var to = new Vector3(MathF.Cos(a) * rim, MathF.Sin(a) * rim, tail + gap + (m.Ballute ? 0f : deep * 0.15f));
            var way = to - from;
            var len = way.Length();
            gfx.Draw(Matrix4x4.CreateScale(0.012f, 0.012f, len) * Basis(way) * Matrix4x4.CreateTranslation((from + to) * 0.5f) * frame, new Vector3(0.75f, 0.72f, 0.62f));
        }
        return 1 + lines;
    }

    /// <summary>The frame's seconds for the shells' flames (their own clock: a camera feed's pass emits none).</summary>
    private static float ShellFxDt()
    {
        if (!Fx.On || CameraFeeds.InPass)
            return 0f;
        var dt = (float)Math.Clamp(Now - _shellFxLast, 0, 0.1);
        _shellFxLast = Now;
        return dt;
    }

    /// <summary>A designed weapon's pop-out wing folded (on its rack, and in the air until it deploys): <paramref name="model"/> made again with the folded span.</summary>
    private static void FoldWing(ConstructPartDef d, int b, in CPartBox box, byte rot, Vector3 centre, ref Matrix4x4 model)
    {
        if (d.OrdStats is { BoxFold.Length: > 0 } st && b < st.BoxFold.Length && st.BoxFold[b] > 0)
            model = (box with { Half = box.Half with { X = st.BoxFold[b] } }).Model(rot, centre);
    }
}
