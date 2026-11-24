using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A drive's live looks (the "Ship engines with interior parts" design, "Running an engine"): a bell and the pieces of its engine that
/// have "work" boxes (EngineTrainParts) show the burn from the bell's level, which every peer has (the host its own, a client the flame
/// byte). On each peer the level is eased into heat (3 s up, 25 s down) and kept per part on the construct: the throat ring, the
/// chamber's coils and sight ports glow red to white, a dial's needle sweeps, the injector's ring lamps run round it with the burn
/// (steady green fed and idle, dark unfed) and its valves stroke, with one burst of sparks at light-off. Drawn only: nothing per tick
/// on the server, nothing on the wire.
/// </summary>
public sealed partial class Construct
{
    public float[] DriveHeat = [], DriveLast = []; // a drive part's eased heat and the level it last saw, by part index (drawn only: ConstructRender.DrivePrepare)
    public double[] DriveAt = []; // ...and when it was last stepped
}

public static partial class ConstructRender
{
    private const float HeatUp = 3f, HeatDown = 25f, LightOff = 0.02f;
    private static readonly Vector3 LampFed = new(0.25f, 0.95f, 0.4f), LampDark = new(0.05f, 0.09f, 0.06f);

    // The part being drawn's drive state (DrivePrepare), for its boxes.
    private static float _driveLevel, _driveHeat;
    private static bool _driveFed;

    /// <summary>Is this part a drive part whose boxes show the burn (a thruster or a drive piece with "work" boxes)?</summary>
    private static bool DriveLooks(ConstructPartDef d) => d.HasWork && (d.Role == PartRole.Thruster || ConstructWorks.IsDrivePiece(d));

    /// <summary>The bell's level, heat and feed for part <paramref name="i"/> (a bell, or a piece of its engine), stepped once a frame. False: no bell serves it.</summary>
    private static bool DrivePrepare(Renderer gfx, Construct c, int i, ConstructPartDef d, in Matrix4x4 onBody)
    {
        var head = d.Role == PartRole.Thruster ? i : ConstructWorks.HeadOfPiece(c, i);
        if (head < 0)
            return false;
        var t = -1;
        for (var k = 0; k < c.ThrusterCount && t < 0; k++)
            if (c.Thrusters[k].Part == head)
                t = k;
        if (t < 0)
            return false;
        ref var th = ref c.Thrusters[t];
        _driveLevel = _fxHost ? th.Level * th.Cap : c.ThrustHeard ? th.Shown : c.ThrustShow;
        _driveFed = th.Fed || (_fxHost ? th.Buffer > 0 : _driveLevel > LightOff); // (the buffer runs down on the host only: a client takes an unfed bell that still burns as on its buffer)
        if (c.DriveHeat.Length < c.PartCount)
        {
            Array.Resize(ref c.DriveHeat, c.PartCount + 8); // (once as a body grows: never in a steady frame; kept through a hit or an edit, so a burning engine stays hot)
            Array.Resize(ref c.DriveLast, c.PartCount + 8);
            Array.Resize(ref c.DriveAt, c.PartCount + 8);
        }
        var dt = (float)Math.Clamp(Now - c.DriveAt[i], 0, 120); // (time since this part was last stepped: far or culled, it catches up)
        if (c.DriveAt[i] == 0)
            dt = 0;
        if (dt > 0)
        {
            ref var h = ref c.DriveHeat[i];
            var tau = _driveLevel > h ? HeatUp : HeatDown;
            h += (_driveLevel - h) * (1 - MathF.Exp(-dt / tau));
            // Light-off: one burst of sparks at the injector (and an Igniter) as the level first passes 2%.
            if ((d.WorksKind == WorksKind.Injector || d.TrainFn == TrainFunction.Igniter) && c.DriveLast[i] <= LightOff && _driveLevel > LightOff && Fx.On)
                LightOffSparks(gfx, c, i, onBody);
            c.DriveLast[i] = _driveLevel;
        }
        if (dt > 0 || c.DriveAt[i] == 0)
            c.DriveAt[i] = Now;
        _driveHeat = c.DriveHeat[i];
        return true;
    }

    private static void LightOffSparks(Renderer gfx, Construct c, int i, in Matrix4x4 onBody)
    {
        c.PartBounds(i, out var mn, out var mx);
        var at = Vector3.Transform((mn + mx) * 0.5f, onBody) + (gfx.Relative ? gfx.Origin : Vector3.Zero);
        _fxEmitted = true;
        for (var k = 0; k < 24; k++)
        {
            var dir = new Vector3(Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f) * 2f;
            Fx.Emit(FxKind.Spark, at, c.Vel + dir * (2f + 3f * Random.Shared.NextSingle()), 0.012f + 0.01f * Random.Shared.NextSingle(), 0.15f + 0.3f * Random.Shared.NextSingle());
        }
    }

    /// <summary>A heat's colour: a dull red at a tenth, orange at a half, near white at full (each box's own colour under it, at rest).</summary>
    private static Vector3 HeatColour(Vector3 rest, float heat)
    {
        if (heat < 0.08f)
            return rest;
        var h = Math.Clamp((heat - 0.08f) / 0.92f, 0, 1);
        var hot = h < 0.5f ? Vector3.Lerp(new Vector3(0.45f, 0.06f, 0.02f), new Vector3(1f, 0.45f, 0.1f), h * 2) : Vector3.Lerp(new Vector3(1f, 0.45f, 0.1f), new Vector3(1f, 0.92f, 0.75f), (h - 0.5f) * 2);
        return hot * (0.9f + 0.1f * MathF.Sin((float)Now * MathF.Tau * (0.8f + 3.2f * _driveLevel))); // (it throbs at 0.8 + 3.2 x level Hz)
    }

    /// <summary>The colour a drive's glowing box draws in now, or null when it draws as any box does.</summary>
    private static Vector3? DriveColour(Construct c, int i, in CPartBox box)
    {
        switch (box.Work)
        {
            case BoxWork.Glow:
                return _driveHeat < 0.08f ? null : HeatColour(box.Color, _driveHeat);
            case BoxWork.Chase:
                if (!_driveFed)
                    return LampDark;
                if (_driveLevel <= 0.03f)
                    return LampFed;
                // Round the ring: its place by its angle about the piece's axis (z), lit in turn at 2 + 18 x level steps a second.
                var a = MathF.Atan2(box.Center.Y, box.Center.X) / MathF.Tau;
                var at = (float)((Now * (2 + 18 * _driveLevel) / 8 - a) % 1.0);
                if (at < 0)
                    at += 1;
                return at < 0.25f ? new Vector3(0.75f, 1f, 0.55f) : LampFed * 0.35f;
            default:
                return null;
        }
    }

    /// <summary>A drive box's motion: a needle sweeps with the level, a valve strokes while it sprays, the chamber shakes up to 3 mm.</summary>
    private static void DriveMove(Construct c, in CPartBox box, in CPart p, ConstructPartDef d, Vector3 centre, ref Matrix4x4 model)
    {
        var axis = Rot24.Apply(p.Rot, ConstructParts.DirVector(box.WorkAxis));
        switch (box.Work)
        {
            case BoxWork.Spin:
                model = model * Matrix4x4.CreateTranslation(-centre) * Matrix4x4.CreateFromAxisAngle(axis, 2.4f * MathF.Min(1, _driveHeat * 1.2f) - 1.2f) * Matrix4x4.CreateTranslation(centre); // (a dial's needle: from its stop to full as the heat rises)
                break;
            case BoxWork.Stroke when _driveLevel > 0.03f:
                var f = (float)(Now * (4 + 16 * _driveLevel) % 1.0);
                model *= Matrix4x4.CreateTranslation(axis * (box.RigTravel * (f < 0.3f ? f / 0.3f : (1 - f) / 0.7f)));
                break;
        }
        if (d.WorksKind == WorksKind.Core && _driveLevel > 0.05f)
        {
            var s = 0.003f * _driveLevel;
            model *= Matrix4x4.CreateTranslation(new Vector3(MathF.Sin((float)Now * 61f), MathF.Sin((float)Now * 47f + 1.3f), MathF.Sin((float)Now * 53f + 2.1f)) * s);
        }
    }
}
