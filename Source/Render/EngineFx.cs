using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What a running modular engine looks like (ConstructEngine.cs): pistons pumping, the crank and flywheel turning, a damaged
/// cylinder stuttering, a big engine shaking, and smoke: grey puffs at idle, a black column from the stacks under load.
/// Drawn from the rpm and load every peer has, so nothing here touches the simulation. The smoke and flames are particles
/// (Source/Render/Fx/Fx.cs); nothing allocates.
/// </summary>
public static class EngineFx
{
    private static uint _seed = 12345;

    /// <summary>Puffs asked for so far, and where the last one left and how dark it was (the self-test).</summary>
    public static long Emitted;
    public static Vector3 LastAt;
    public static float LastDark;

    /// <summary>Starts the count again (the self-test, a new match).</summary>
    public static void Clear() => Emitted = 0;

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
        var scale = e.Scale;
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
            if (part >= c.PartCount || c.Parts[part].Broken || c.Parts[part].D.SmokeSocket < 0)
                continue; // (the list is from the last rebuild: a part taken off this frame may have shifted it)
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
                var size = 0.28f * scale * (stack ? 1f : 0.6f);
                var from = world + along * (0.05f * scale);
                var vel = (along * (1.6f + 3.2f * load) + new Vector3(Rand() - 0.5f, 0.4f, Rand() - 0.5f) * 0.7f) * MathF.Sqrt(scale);
                Emitted++;
                LastAt = from;
                LastDark = load;
                if (Rand() < Fx.Density)
                    Fx.EngineSmoke(from, vel + c.Vel * 0.5f, size * (1f + 0.5f * load), 2.2f + 2.6f * load + Rand(), load); // (the pool has them from here on: Fx.cs)
            }
            // Under hard load a flame licks from the stack's top.
            if (stack && c.EngineLoad >= 0.85f)
            {
                c.SocketAt(part, d.SmokeSocket, out var at, out var dir);
                Fx.Flame(FxKind.Flame, Vector3.Transform(at, body), Vector3.TransformNormal(ConstructParts.DirVector(dir), body), c.Vel, 0.09f * scale, 0.5f * scale, c.EngineLoad, dt);
            }
        }
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
