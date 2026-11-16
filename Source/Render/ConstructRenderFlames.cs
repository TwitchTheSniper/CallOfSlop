using System.Numerics;

namespace CallOfSlop;

/// <summary>What a part's flame burns by ("source" in its "flame" block).</summary>
public enum FlameSource : byte
{
    Thrust, // a thruster's own level (the host's, CBodyKind.Thrust on a client); any other part, or before the host has said, the construct's (Construct.ThrustShow)
    Burner, // a lift cell's burner (its level on the host; clients read it through the pose's smoke share)
    Engine, // the engine's load (a modular engine's stack, a jet that burns with the engine)
    Signal, // the part's own output, 0..1 (a logic part, or whatever sets it)
    Always, // lit while the part is whole
}

/// <summary>
/// A flame on a part (<c>"flame"</c> in a part file; ConstructParts.ReadFlames): any part, in any role, gets one with a line of data and
/// no code. Thrusters and burners that give none get one from their own numbers.
/// </summary>
public sealed class FlameDef
{
    public Vector3 At; // where it starts (the part's frame)
    public byte Dir; // the way it leaves (a direction index)
    public float Radius = 0.1f, Length = 1f;
    public FxKind Kind = FxKind.Flame;
    public FlameSource Source;
    public uint Tint = Fx.White;
}

public sealed partial class Construct
{
    public int[] FlameParts = []; // the parts with a flame or a stack, by part index (made again when the construct rebuilds)
    public short[] FlameThruster = []; // ...and each one's place in Thrusters (-1: not a thruster)
    public int FlameCount;
    public int FlameStamp = -1;
}

public static partial class ConstructRender
{
    private static double _fxLast;
    private static float _fxDt;
    private static bool _fxHost; // this peer runs the simulation (a client's constructs come in poses)

    /// <summary>Called before the frame's constructs: the seconds their flames emit for.</summary>
    private static void FlamesBegin(bool host)
    {
        _fxHost = host;
        _fxDt = (float)Math.Clamp(Now - _fxLast, 0, 0.1);
        _fxLast = Now;
    }

    private static void FindFlames(Construct c)
    {
        c.FlameStamp = c.RebuildStamp;
        c.FlameCount = 0;
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D is { } d && (d.Flames.Length > 0 || d.SmokeSize > 0))
                n++;
        if (n == 0)
            return;
        if (c.FlameParts.Length < n)
        {
            c.FlameParts = new int[n];
            c.FlameThruster = new short[n];
        }
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D is { } d && (d.Flames.Length > 0 || d.SmokeSize > 0))
            {
                var t = -1;
                for (var k = 0; k < c.ThrusterCount && t < 0; k++)
                    if (c.Thrusters[k].Part == i)
                        t = k;
                c.FlameThruster[c.FlameCount] = (short)t;
                c.FlameParts[c.FlameCount++] = i;
            }
    }

    /// <summary>How hard a flame burns now, from what this peer has.</summary>
    private static float FlameLevel(Construct c, int i, int thruster, FlameDef f)
    {
        ref var p = ref c.Parts[i];
        switch (f.Source)
        {
            case FlameSource.Thrust:
                if (thruster >= 0 && thruster < c.ThrusterCount && c.Thrusters[thruster].Part == i)
                {
                    if (_fxHost)
                        return MathF.Max(c.Thrusters[thruster].Level * c.Thrusters[thruster].Cap, c.Thrusters[thruster].Ab); // (a drive short of its works burns low, as the clients are told)
                    if (c.ThrustHeard)
                        return c.Thrusters[thruster].Shown;
                }
                return c.ThrustShow;
            case FlameSource.Burner:
                for (var k = 0; k < c.BurnerCount; k++)
                    if (c.Burners[k].Part == i && c.Burners[k].Level > 0)
                        return c.Burners[k].Level;
                return _fxHost ? 0f : c.SmokeLevel; // (a client doesn't run the burners: the pose's smoke share carries the hottest, ConstructNet)
            case FlameSource.Engine:
                return c.EngineRunning ? Math.Clamp(c.EngineLoad, 0.15f, 1f) : 0f;
            case FlameSource.Signal:
                return Math.Clamp(p.Out, 0, 1);
            default:
                return 1f;
        }
    }

    /// <summary>One construct's flames, stack smoke and wheel dust this frame: particles from what every peer has, so host and clients see the same. Nothing when it has none.</summary>
    private static void EmitFlames(Construct c, in Matrix4x4 worldBody, Vector3 pos, Quaternion rot, Vector3 eye, float alpha)
    {
        if (!Fx.On)
            return;
        var dt = _fxDt;
        if (dt <= 0)
            return;
        if (c.FlameStamp != c.RebuildStamp)
            FindFlames(c);
        // Flames ride the body as drawn: each frame they move with it (Fx.Carrier), so at any speed they stay on their nozzles.
        var carrier = Fx.CarrierKept(c.Id, pos, rot); // (-1: none yet, taken at the first lit flame, so a body whose flames are out holds no slot; one just gone out keeps its slot until let go, so its last flames move with it)
        for (var n = 0; n < c.FlameCount; n++)
        {
            var i = c.FlameParts[n];
            if (i >= c.PartCount)
                continue;
            ref var p = ref c.Parts[i];
            var d = p.D;
            if (p.Broken)
                continue;
            var frame = p.Seg > 0 && p.Seg <= c.JointCount ? SegMatrix(c, p.Seg, alpha) * worldBody : worldBody;
            foreach (var f in d.Flames)
            {
                var level = FlameLevel(c, i, c.FlameThruster[n], f);
                if (level <= 0.005f)
                    continue;
                var at = Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, f.At), frame);
                var dir = Vector3.TransformNormal(ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, f.Dir)), frame);
                if (carrier < 0)
                    carrier = Fx.Carrier(c.Id, pos, rot);
                var carry = carrier != 0 ? Vector3.Zero : c.Vel + Vector3.Cross(c.AngVel, at - pos); // (no slot free: the body's speed)
                Fx.Flame(f.Kind, at, dir, carry, f.Radius, f.Length, level, dt, f.Tint, carrier);
                var tip = at + dir * (f.Length * 0.5f);
                if (Vector3.DistanceSquared(tip, eye) < 200f * 200f)
                    PostFx.AddHaze(tip + dir * (f.Length * 0.4f * level), MathF.Min(f.Radius * 3f + f.Length * 0.7f * level, 30f), 0.3f + 0.55f * level);
            }
            if (d.SmokeSize > 0 && c.SmokeLevel > 0 && c.SmokeFed.Length > i && c.SmokeFed[i])
            {
                var top = Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, d.SmokeAt), frame);
                var level = c.SmokeLevel;
                for (var k = Fx.Share((3f + 16f * level) * MathF.Max(1f, d.SmokeSize * 2f) * dt); k > 0; k--)
                    Fx.EngineSmoke(top, c.Vel * 0.6f + new Vector3(0.2f, d.SmokeSize * (2.5f + 4f * level), 0.1f), d.SmokeSize * 1.4f, 2.4f + 2.4f * level, 0.2f + 0.75f * level);
            }
        }
        // Dust off the wheels that are down, at speed, near the eye.
        if (c.WheelCount > 0)
        {
            var speed = c.Vel.Length();
            if (speed > 4f && Vector3.DistanceSquared(pos, eye) < 140f * 140f)
                for (var k = 0; k < c.WheelCount; k++)
                {
                    ref var w = ref c.Wheels[k];
                    if (!w.Touching)
                        continue;
                    for (var m = Fx.Share(MathF.Min(speed, 30f) * 1.1f * dt); m > 0; m--)
                    {
                        var at = Vector3.Transform(w.Centre - new Vector3(0, w.Radius + w.Compression, 0), worldBody);
                        Fx.Emit(FxKind.Dust, at, c.Vel * 0.15f + new Vector3(0, 0.9f, 0), 0.25f + 0.1f * w.Radius, 0.9f + 0.002f * speed);
                    }
                }
        }
    }
}
