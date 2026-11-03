using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What a running modular engine looks like (ConstructEngine.cs): pistons pumping, the crank and flywheel turning, a damaged
/// cylinder stuttering, a big engine shaking, and smoke: grey puffs at idle, a black column from the stacks under load.
/// Drawn from the rpm and load every peer has, so nothing here touches the simulation. Puffs live in a fixed pool and are
/// drawn in the blended pass; nothing allocates.
/// </summary>
public static class EngineFx
{
    public const int MaxPuffs = 256;

    private struct Puff
    {
        public Vector3 At, Vel;
        public float Age, Life, Size, Dark;
    }

    private static readonly Puff[] Puffs = new Puff[MaxPuffs];
    private static readonly int[] Order = new int[MaxPuffs];
    private static readonly float[] Keys = new float[MaxPuffs];
    private static int _next, _live;
    private static double _lastAge;
    private static ushort _ball;
    private static uint _seed = 12345;

    /// <summary>Puffs alive (the self-test).</summary>
    public static int LivePuffs => _live;

    /// <summary>Clears the smoke (the self-test, a new match).</summary>
    public static void Clear()
    {
        Array.Clear(Puffs);
        _live = 0;
        _next = 0;
    }

    private static float Rand()
    {
        _seed = _seed * 1664525u + 1013904223u;
        return (_seed >> 8) / 16777216f;
    }

    /// <summary>The crank's turns a second as drawn: you can watch the pistons, faster with the rpm, slower the bigger the engine.</summary>
    public static float DrawnHz(Construct c, ModEngine e)
    {
        var max = c.EngineDef.MaxRpm;
        var size = 1f + e.Size * 0.6f;
        return (1.2f + 5f * Math.Clamp(c.Rpm / MathF.Max(1, max), 0, 1)) / size;
    }

    /// <summary>One construct, each frame before it is drawn: the crank turns, the engine shakes, the smoke leaves.</summary>
    public static void Frame(Construct c, in Matrix4x4 body, double now, float alpha)
    {
        if (c.ActiveEngine is not { } e || c.Engine >= c.PartCount || !c.EngineRunning)
        {
            c.EngineShake = Vector3.Zero;
            c.EngineDrawn = now;
            return;
        }
        var dt = (float)Math.Clamp(now - c.EngineDrawn, 0, 0.1);
        c.EngineDrawn = now;
        var hz = DrawnHz(c, e);
        c.EngineAngle = (c.EngineAngle + hz * MathF.Tau * dt) % (MathF.Tau * 1024);
        // The shake: a big engine rocks at idle and smooths out as it revs; a misfiring one lurches.
        var scale = EngineRules.Current.Scale[e.Size];
        var rough = 1f - Math.Clamp(c.Rpm / MathF.Max(1, c.EngineDef.PeakRpm), 0, 1) * 0.7f;
        var amount = 0.0035f * scale * (0.5f + 0.25f * MathF.Min(e.Cylinders, 8) / 4f) * rough * (e.Misfiring + e.Wrecked > 0 ? 1.6f : 1f);
        c.EngineShake = new Vector3(MathF.Sin(c.EngineAngle * 2.3f), MathF.Sin(c.EngineAngle * 3f + 1f), MathF.Sin(c.EngineAngle * 1.7f + 2f)) * amount;
        if (dt <= 0)
            return;
        // The smoke: more and blacker with the load; a hurt engine smokes more.
        var sick = (float)(e.Misfiring + e.Wrecked) / MathF.Max(1, e.Cylinders);
        var load = MathF.Max(c.EngineLoad, sick * 0.6f);
        var rate = (1.2f + 16f * load) * (1f + e.Size * 0.5f);
        for (var k = 0; k < e.EmitCount; k++)
        {
            var part = e.Emit[k];
            if (part >= c.PartCount || c.Parts[part].Broken)
                continue;
            ref var p = ref c.Parts[part];
            var d = p.D;
            var stack = d.Piece == EnginePiece.Stack;
            var want = (stack ? rate : rate * 0.4f) * dt;
            while (want > 0)
            {
                if (want < 1 && Rand() > want)
                    break;
                want -= 1;
                c.SocketAt(part, d.SmokeSocket, out var at, out var dir);
                var up = ConstructParts.DirVector(dir);
                var world = Vector3.Transform(at, body);
                var along = Vector3.TransformNormal(up, body);
                ref var f = ref Puffs[_next];
                if (f.Life <= 0)
                    _live++;
                _next = (_next + 1) % MaxPuffs;
                var size = 0.28f * scale * (stack ? 1f : 0.6f);
                f.At = world + along * (0.05f * scale);
                f.Vel = (along * (1.6f + 3.2f * load) + new Vector3(Rand() - 0.5f, 0.4f, Rand() - 0.5f) * 0.7f) * MathF.Sqrt(scale);
                f.Age = 0;
                f.Life = 2.2f + 2.6f * load + Rand();
                f.Size = size * (1f + 0.5f * load);
                f.Dark = load;
            }
        }
    }

    private static void AgePuffs(double now)
    {
        var dt = (float)Math.Clamp(now - _lastAge, 0, 0.1);
        _lastAge = now;
        var live = 0;
        for (var k = 0; k < MaxPuffs; k++)
        {
            ref var f = ref Puffs[k];
            if (f.Life <= 0)
                continue;
            f.Age += dt;
            if (f.Age >= f.Life)
            {
                f.Life = 0;
                continue;
            }
            live++;
            f.At += f.Vel * dt;
            f.Vel *= MathF.Max(0, 1 - 0.5f * dt);
            f.Vel += new Vector3(0.25f, 0.5f, 0.1f) * (dt * MathF.Max(1, f.Size)); // (it drifts and keeps rising)
        }
        _live = live;
    }

    /// <summary>The smoke, back to front, in the blended pass.</summary>
    public static void DrawSmoke(Renderer gfx, Vector3 eye, double now)
    {
        AgePuffs(now);
        if (_live == 0)
            return;
        if (_ball == 0)
            _ball = PartShapes.For(new PartBox { Shape = "sphere", Size = Vector3.One, Segments = 8 });
        var n = 0;
        for (var k = 0; k < MaxPuffs; k++)
            if (Puffs[k].Life > 0)
            {
                Order[n] = k;
                Keys[n++] = -Vector3.DistanceSquared(Puffs[k].At, eye); // (farthest first)
            }
        Array.Sort(Keys, Order, 0, n);
        gfx.SetBlend(true);
        gfx.SetDepthWrite(false);
        for (var i = 0; i < n; i++)
        {
            ref var f = ref Puffs[Order[i]];
            var t = f.Age / f.Life;
            var size = f.Size * (1f + 4.5f * t);
            var grey = float.Lerp(0.42f, 0.05f, f.Dark) + 0.18f * t;
            var alpha = (float.Lerp(0.3f, 0.72f, f.Dark)) * (1 - t) * MathF.Min(1, t * 12f + 0.3f);
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(f.At), new Vector3(grey, grey * 0.96f, grey * 0.92f), alpha, mode: 0, shape: _ball);
        }
    }

    /// <summary>A flame licking from the stack's top under hard load: a few glowing boxes (the opaque pass).</summary>
    public static int DrawFlames(Renderer gfx, Construct c, ModEngine e, in Matrix4x4 body, double now)
    {
        if (c.EngineLoad < 0.85f || !c.EngineRunning)
            return 0;
        var drawn = 0;
        var scale = EngineRules.Current.Scale[e.Size];
        for (var k = 0; k < e.EmitCount; k++)
        {
            var part = e.Emit[k];
            if (part >= c.PartCount || c.Parts[part].D.Piece != EnginePiece.Stack)
                continue;
            c.SocketAt(part, c.Parts[part].D.SmokeSocket, out var at, out var dir);
            var up = ConstructParts.DirVector(dir);
            var flick = 0.6f + 0.4f * MathF.Sin((float)now * 31f + k * 2f);
            gfx.Draw(Matrix4x4.CreateScale(0.12f * scale, 0.32f * scale * flick, 0.12f * scale) * Matrix4x4.CreateTranslation(at + up * (0.16f * scale * flick)) * body, new Vector3(1f, 0.55f, 0.15f), 0.9f, mode: 2);
            drawn++;
        }
        return drawn;
    }

    /// <summary>A box's turn and travel as it runs. <paramref name="model"/> is in the body frame; the part is a modular engine's.</summary>
    public static void Animate(Construct c, int i, in CPartBox box, Vector3 centre, ref Matrix4x4 model)
    {
        ref var p = ref c.Parts[i];
        if (box.Spin)
        {
            var axis = Rot24.Apply(p.Rot, Vector3.UnitZ);
            model = model * Matrix4x4.CreateTranslation(-p.Pos) * Matrix4x4.CreateFromAxisAngle(axis, c.EngineAngle) * Matrix4x4.CreateTranslation(p.Pos);
        }
        else if (box.Pump != 0)
        {
            var travel = box.Pump * PumpAt(c, i);
            model *= Matrix4x4.CreateTranslation(Rot24.Apply(p.Rot, Vector3.UnitY) * travel);
        }
    }

    /// <summary>Where a piston is in its stroke (-1..1 x half a stroke): evenly spaced through the cylinders, stopped when wrecked, stuttering when it misfires.</summary>
    public static float PumpAt(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        if (p.Broken)
            return -0.5f;
        var count = c.EngineOf[i] > 0 && c.ModEngines != null ? Math.Max(1, c.ModEngines[c.EngineOf[i] - 1].Cylinders) : 1;
        var phase = c.EngineAngle + MathF.Tau * (c.EngineSlot[i] * 3 % count) / count;
        var s = 0.5f * MathF.Sin(phase);
        if (ConstructEngine.Share(c, i, EngineRules.Current) < 1 && MathF.Sin(phase * 0.37f + i) > 0.2f)
            s *= 0.25f; // (a misfiring cylinder drags)
        return s;
    }
}
