using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Building Station's arm rovers (the rover docks, Sean 2026-11-24: the Building Station Lab's approved look; ConstructStationRovers.cs),
/// drawn on every peer from the trips as this peer heard them, so host and client draw the same. Each dock's rover: small (four wheels, a
/// plain arm), medium (six wheels, a truss lower arm, a solid forearm held by a crank and parallel link, a short counterweight) or large
/// (eight wheels, truss booms, the forearm's truss tapering to the wrist pin, a wrist link to the gripper, a deep counterweight behind the
/// shoulder pin); a telescoping mast all the way down on its pad and rising as it drives off; the arm folded away while parked (lower arm
/// up, forearm folded down beside it); the gripper scaled with the arm; the piece it carries hanging from the jaws, turned by the wrist on
/// its way to its place. The job's lamp on the Core's console. Looks only: nothing here collides or runs on the server. Drawn within
/// <see cref="DrawNear"/>. Allocates nothing.
/// </summary>
public static class StationRender
{
    public const float DrawNear = 160f;
    private static readonly Vector3 Yellow = new(0.94f, 0.72f, 0.1f), Dark = new(0.133f, 0.149f, 0.169f), Steel = new(0.6f, 0.63f, 0.66f),
        Out = new(0.94f, 0.47f, 0.09f), Hazard = new(0.95f, 0.69f, 0.12f), Rubber = new(0.07f, 0.075f, 0.08f), Black = new(0.11f, 0.125f, 0.14f),
        Lamp = new(1f, 0.91f, 0.63f), Plate1 = new(0.227f, 0.247f, 0.275f), Plate2 = new(0.169f, 0.184f, 0.208f);
    private static readonly float[] LargeWheels = [-3.78f, -1.26f, 1.26f, 3.78f];
    private static readonly ushort CylShape = PartShapes.For(new PartBox { Shape = "cylinder", Size = Vector3.One, Segments = 14 });
    private static readonly Matrix4x4 ToX = Matrix4x4.CreateRotationY(MathF.PI / 2), ToY = Matrix4x4.CreateRotationX(-MathF.PI / 2);

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var now = ConstructConveyor.Clock;
        Span<StationDock> docks = stackalloc StationDock[ConstructStation.MaxArms];
        foreach (var c in world.Bodies)
        {
            if (c.PieceRackCount == 0 || c.MachineAsmCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var k = 0; k < c.PieceRackCount; k++)
            {
                int t = c.PieceRacks[k];
                if (t >= c.PartCount || c.Parts[t].Broken || !c.DefOf(t).IsBuildStation || ConstructStation.BaySize(c, t) is not { X: > 0 })
                    continue;
                ConstructRender.PartDrawn(c, t, out var pos, out var rot);
                var floor = pos + Vector3.Transform(ConstructStation.BayFloor(c, t), rot);
                if (Vector3.DistanceSquared(eye, floor) > DrawNear * DrawNear)
                    continue;
                var j = c.Parts[t].Station;
                // The job's lamp in the console's hood: green building, amber costing, blinking red failed, white done, dark idle.
                var core = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
                Box(gfx, core, new Vector3(-2.74f, 2.53f, -0.36f), new Vector3(0.1f, 0.07f, 0.14f), LampColour(j, now));
                var bay = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(floor);
                var n = StationRovers.Docks(c, t, docks);
                for (var a = 0; a < n; a++)
                    DrawRover(gfx, bay, docks[a], j, a, now);
            }
        }
    }

    /// <summary>The state lamp's colour.</summary>
    private static Vector3 LampColour(StationJob? j, double now)
    {
        var blink = (now * 2.0 % 1.0) < 0.5;
        return j?.State switch
        {
            StationState.Building => new Vector3(0.25f, 1f, 0.4f),
            StationState.Costing => new Vector3(1f, 0.72f, 0.15f),
            StationState.Failed => blink ? new Vector3(1f, 0.2f, 0.15f) : new Vector3(0.25f, 0.06f, 0.05f),
            StationState.Done => new Vector3(0.9f, 0.95f, 1f),
            _ => new Vector3(0.12f, 0.13f, 0.12f),
        };
    }

    /// <summary>Rover <paramref name="a"/> where its trip has it now (or where it stands, or parked on its pad).</summary>
    private static void DrawRover(Renderer gfx, Matrix4x4 bay, in StationDock dk, StationJob? j, int a, double now)
    {
        var rc = StationRovers.Classes[dk.Cls];
        Vector2 at;
        float h, up = 0;
        Vector3 hook;
        ushort carried = ushort.MaxValue;
        var wrist = 0f;
        var moving = false;
        var turn = Quaternion.Identity;
        if (j != null && j.ArmTicks[a] > 0)
        {
            var p = j.Rov[a];
            var u = (float)Math.Min(now - j.ArmStart[a], p.Total);
            p.BaseAt(u, out var x, out var z, out h);
            at = new Vector2(x, z);
            hook = p.HookAt(u);
            up = p.Up;
            moving = u < p.Total;
            if (j.ArmDef[a] != ushort.MaxValue && !p.Home && u >= p.GripT && u < p.FitT)
            {
                carried = j.ArmDef[a];
                turn = p.Turn;
                wrist = Math.Clamp((u - p.GripT) / MathF.Max(0.5f, (p.FitT - p.GripT) * 0.7f), 0f, 1f);
            }
        }
        else if (j != null && j.RovSet[a])
            (at, h, hook) = (j.RovAt[a], j.RovH[a], j.RovHook[a]);
        else
        {
            at = dk.Mid;
            h = MathF.Atan2(dk.Dir.X, dk.Dir.Y);
            hook = new Vector3(dk.Dir.X * 1.5f, rc.Shoulder + 0.5f, dk.Dir.Y * 1.5f);
        }
        var fold = StationRovers.Fold(dk, at);
        var hookAt = new Vector3(at.X, 0, at.Y) + hook;
        if (up <= 0)
            up = hookAt.Y;
        Rover(gfx, bay, rc, dk.Cls, at, h, hookAt, up, fold, carried, turn, wrist, moving, now);
    }

    /// <summary>One rover: its chassis at (<paramref name="at"/>, heading <paramref name="h"/>), its mast, turret and arm to the hook, the piece under the jaws.</summary>
    private static void Rover(Renderer gfx, Matrix4x4 bay, in RoverClass rc, int cls, Vector2 at, float h, Vector3 hook, float up, float fold, ushort carried,
        Quaternion turn, float wrist, bool moving, double now)
    {
        var body = Matrix4x4.CreateRotationY(h) * Matrix4x4.CreateTranslation(at.X, 0, at.Y) * bay;
        Chassis(gfx, body, cls, moving, now);
        // The mast: three nested sections from the body's top to under the shoulder, telescoped by the hook's height (all the way down parked).
        var horiz = Vector2.Distance(at, new Vector2(hook.X, hook.Z));
        var sy = StationRovers.ShoulderAt(rc, horiz, hook.Y, up, fold);
        var k = cls == 3 ? Math.Clamp(rc.Reach / 9f, 1f, 3f) : cls == 2 ? 1.5f : 1f;
        float bot = rc.Shoulder - 0.5f, top = sy - 0.5f, span = top - bot;
        var mastW = Math.Clamp(rc.Reach / 9f, 1f, 2.4f);
        if (span > 0.3f)
            for (var s = 0; s < 3; s++)
            {
                float y0 = bot + span * s / 3 - 0.25f, y1 = bot + span * (s + 1) / 3 + 0.35f, w = (0.78f - s * 0.14f) * mastW;
                Box(gfx, body, new Vector3(0, (y0 + y1) * 0.5f, 0), new Vector3(w, y1 - y0, w), s % 2 == 1 ? Yellow : Dark);
            }
        // The turret turns to the hook.
        var shoulder = new Vector3(at.X, sy, at.Y);
        var wristPt = hook + new Vector3(0, 1f, 0);
        var flat = new Vector2(wristPt.X - shoulder.X, wristPt.Z - shoulder.Z);
        var yaw = flat.LengthSquared() > 0.0025f ? MathF.Atan2(flat.X, flat.Y) : h;
        var tf = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(shoulder) * bay; // (the turret's frame: +z towards the hook, y up)
        float a1 = rc.Reach * 0.52f, a2 = rc.Reach * 0.48f;
        var d = MathF.Max(0.3f, flat.Length());
        var wy = wristPt.Y - shoulder.Y;
        var D = Math.Clamp(MathF.Sqrt(d * d + wy * wy), 0.3f, a1 + a2 - 0.01f);
        var phi = MathF.Atan2(wy, d);
        var al = MathF.Acos(Math.Clamp((a1 * a1 + D * D - a2 * a2) / (2 * a1 * D), -1f, 1f));
        var th1 = phi + al;
        var E = new Vector3(0, a1 * MathF.Sin(th1), a1 * MathF.Cos(th1));
        var W = new Vector3(0, D * MathF.Sin(phi), D * MathF.Cos(phi));
        if (fold > 0)
        {
            // Parked: the lower arm stood up, the forearm folded back down beside it; it unfolds as the rover drives off.
            var e = StationRovers.Ease(fold);
            const float up1 = 1.45f;
            var Ef = new Vector3(0, a1 * MathF.Sin(up1), a1 * MathF.Cos(up1));
            var back = up1 + MathF.PI - 0.12f;
            var Wf = Ef + new Vector3(0, a2 * MathF.Sin(back), a2 * MathF.Cos(back));
            E = Vector3.Lerp(E, Ef, e);
            W = Vector3.Lerp(W, Wf, e);
        }
        Turret(gfx, tf, cls, k);
        var u1 = Vector3.Normalize(E);
        var u2 = Vector3.Normalize(W - E);
        if (cls == 1)
        {
            Beam(gfx, tf, Vector3.Zero, E, 0.42f, 0.5f, Yellow);
            Cyl(gfx, tf, 0, Vector3.Zero, 0.62f, 1.0f, Dark);
            Cyl(gfx, tf, 0, E, 0.5f, 0.8f, Dark);
            Beam(gfx, tf, E, W, 0.32f, 0.38f, Yellow);
        }
        else
        {
            // Truss lower arm (root block, lattice, end block), its pins, and the counterweight tail with its stack behind the shoulder pin.
            Beam(gfx, tf, Vector3.Zero, u1 * 0.8f * k, 0.7f * k, 0.9f * k, Yellow);
            Truss(gfx, tf, u1 * 0.8f * k, E - u1 * 0.6f * k, 0.7f * k, 0.85f * k);
            Beam(gfx, tf, E - u1 * 0.6f * k, E, 0.7f * k, 0.85f * k, Yellow);
            for (var s = -1; s <= 1; s += 2)
                Cyl(gfx, tf, 0, new Vector3(s * 0.42f * k, 0, 0), 0.8f * k, 0.12f * k, Black);
            Beam(gfx, tf, Vector3.Zero, -u1 * 1.4f * k, 0.55f * k, 0.6f * k, Yellow);
            var cwN = cls == 2 ? 3 : (int)MathF.Round(3 + k * 2);
            var cwS = cls == 2 ? 1.0f : 1.3f;
            for (var s = 0; s < cwN; s++)
                Beam(gfx, tf, -u1 * (1.4f * k + s * 0.2f * k), -u1 * (1.58f * k + s * 0.2f * k), cwS * k, cwS * k, s % 2 == 1 ? Plate1 : Plate2);
            // The forearm: a stub behind the elbow pin (the parallel link's lever), then solid (medium) or a truss tapering to the wrist pin (large).
            var stub = 0.55f * k;
            var B = E - u2 * stub;
            Beam(gfx, tf, B, E + u2 * stub, 0.5f * k, 0.6f * k, Yellow);
            for (var s = -1; s <= 1; s += 2)
                Cyl(gfx, tf, 0, E + new Vector3(s * 0.32f * k, 0, 0), 0.66f * k, 0.1f * k, Black);
            if (cls == 2)
            {
                Beam(gfx, tf, E, W, 0.4f * k, 0.5f * k, Yellow);
                Beam(gfx, tf, W - u2 * 0.4f * k, W, 0.5f * k, 0.6f * k, Yellow);
            }
            else
            {
                var tip = W - u2 * (1.8f * k);
                Truss(gfx, tf, E + u2 * stub * 0.6f, tip, 0.5f * k, 0.6f * k);
                Taper(gfx, tf, tip, W, 0.5f * k, 0.6f * k);
                Cyl(gfx, tf, 0, W, 0.4f * k, 0.45f * k, Dark);
            }
            Cyl(gfx, tf, 0, B, 0.26f * k, 0.56f * k, Black);
            // The crank and the parallel link rod: the crank from the shoulder pin parallel to the forearm's lever, the rod to the lever's end.
            var A = B - E;
            Beam(gfx, tf, Vector3.Zero, A, 0.18f * k, 0.18f * k, Yellow);
            Beam(gfx, tf, A, B, 0.16f * k, 0.16f * k, Yellow);
            Cyl(gfx, tf, 0, A, 0.3f * k, 0.24f * k, Black);
        }
        // The head hangs plumb under the wrist: its knuckle, the drop, the gripper's bar and jaws (wider with the arm); a wrist link on large.
        var headTop = W;
        if (cls == 3)
        {
            Cyl(gfx, tf, 0, W, 0.42f * k, 0.5f * k, Dark);
            headTop = W - new Vector3(0, 0.35f * k, 0);
            Beam(gfx, tf, W, headTop, 0.3f * k, 0.3f * k, Steel);
        }
        var hookL = W - new Vector3(0, 1f, 0); // (the jaws' line, a metre under the wrist; folded: wherever the wrist is)
        Cyl(gfx, tf, 0, headTop, 0.36f, 0.5f, Dark);
        Beam(gfx, tf, headTop, hookL + new Vector3(0, 0.22f, 0), 0.18f, 0.18f, Steel);
        Box(gfx, tf, hookL + new Vector3(0, 0.22f, 0), new Vector3(0.9f * k, 0.08f, 0.2f * k), Dark);
        var grip = carried == ushort.MaxValue ? 0.42f : 0.3f;
        for (var s = -1; s <= 1; s += 2)
            Box(gfx, tf, hookL + new Vector3(s * grip * k, 0.05f, 0), new Vector3(0.06f, 0.26f * k, 0.16f * k), Out);
        if (carried == ushort.MaxValue || ConstructParts.Get(carried) is not { } pd)
            return;
        // The piece: its top at the jaws, turned by the wrist from how it lay towards its turn where it goes.
        var q = Quaternion.Slerp(Quaternion.Identity, turn, StationRovers.Ease(wrist));
        var hv = (pd.BoundsMax - pd.BoundsMin) * 0.5f;
        var topY = MathF.Abs(Vector3.Transform(Vector3.UnitX, q).Y) * hv.X + MathF.Abs(Vector3.Transform(Vector3.UnitY, q).Y) * hv.Y + MathF.Abs(Vector3.Transform(Vector3.UnitZ, q).Y) * hv.Z;
        if (hv.Y > 20f)
            return;
        var jaws = Vector3.Transform(hookL, tf);
        var m = Matrix4x4.CreateTranslation(-(pd.BoundsMin + pd.BoundsMax) * 0.5f) * Matrix4x4.CreateFromQuaternion(q)
            * Matrix4x4.CreateTranslation(0, -topY - 0.004f, 0) * Matrix4x4.CreateTranslation(jaws);
        for (var bx = 0; bx < pd.Boxes.Length; bx++)
        {
            ref readonly var box = ref pd.Boxes[bx];
            if (!box.Hidden)
                gfx.Draw(box.Model(0, box.Center) * m, box.Color, shape: box.Shape);
        }
    }

    /// <summary>The chassis in its own frame (+z forward): body, hazard deck, wheels, a lamp at the nose, a beacon that turns while it moves.</summary>
    private static void Chassis(Renderer gfx, Matrix4x4 f, int cls, bool moving, double now)
    {
        var beacon = moving && (now * 3.0 % 1.0) < 0.5 ? Hazard : new Vector3(0.35f, 0.25f, 0.05f);
        switch (cls)
        {
            case 1:
                Box(gfx, f, new Vector3(0, 0.5f, 0), new Vector3(1.7f, 0.45f, 2.5f), Dark);
                Box(gfx, f, new Vector3(0, 0.74f, 0), new Vector3(1.5f, 0.04f, 2.3f), Hazard);
                for (var sx = -1; sx <= 1; sx += 2)
                    for (var sz = -1; sz <= 1; sz += 2)
                        Cyl(gfx, f, 0, new Vector3(sx * 0.92f, 0.32f, sz * 0.85f), 0.64f, 0.3f, Rubber);
                Box(gfx, f, new Vector3(0, 0.55f, 1.27f), new Vector3(0.8f, 0.2f, 0.06f), Lamp);
                Cyl(gfx, f, 1, new Vector3(0, 1.05f, 0), 0.8f, 0.6f, Dark);
                Box(gfx, f, new Vector3(0.55f, 0.82f, -0.9f), new Vector3(0.12f, 0.12f, 0.12f), beacon);
                break;
            case 2:
                Box(gfx, f, new Vector3(0, 0.62f, 0), new Vector3(2.6f, 0.6f, 3.8f), Dark);
                Box(gfx, f, new Vector3(0, 0.94f, 0), new Vector3(2.4f, 0.04f, 3.6f), Hazard);
                for (var sx = -1; sx <= 1; sx += 2)
                    for (var w = -1; w <= 1; w++)
                        Cyl(gfx, f, 0, new Vector3(sx * 1.35f, 0.4f, w * 1.3f), 0.8f, 0.36f, Rubber);
                Box(gfx, f, new Vector3(0, 0.7f, 1.95f), new Vector3(1.2f, 0.3f, 0.1f), Lamp);
                Cyl(gfx, f, 1, new Vector3(0, 1.35f, 0), 1.0f, 0.8f, Dark);
                Box(gfx, f, new Vector3(0.9f, 1.03f, -1.5f), new Vector3(0.16f, 0.16f, 0.16f), beacon);
                break;
            default:
                // (the lab's heavy chassis, 6.5 x 10 m on its 8 x 12 m pad: a power pack at the back, outriggers at the corners)
                Box(gfx, f, new Vector3(0, 1.19f, 0), new Vector3(6.48f, 1.26f, 10.08f), Dark);
                Box(gfx, f, new Vector3(0, 1.85f, 0), new Vector3(6.12f, 0.056f, 9.72f), Hazard);
                for (var sx = -1; sx <= 1; sx += 2)
                    foreach (var z in LargeWheels)
                        Cyl(gfx, f, 0, new Vector3(sx * 3.33f, 0.77f, z), 1.54f, 0.9f, Rubber);
                Box(gfx, f, new Vector3(0, 2.4f, -3.96f), new Vector3(4.68f, 1.12f, 1.8f), Steel);
                Box(gfx, f, new Vector3(0, 1.33f, 5.09f), new Vector3(2.88f, 0.49f, 0.14f), Lamp);
                for (var sx = -1; sx <= 1; sx += 2)
                    for (var sz = -1; sz <= 1; sz += 2)
                        Box(gfx, f, new Vector3(sx * 3.42f, 0.56f, sz * 4.68f), new Vector3(0.54f, 1.12f, 0.54f), Yellow);
                Cyl(gfx, f, 1, new Vector3(0, 2.6f, 0), 3.2f, 1.4f, Dark);
                Box(gfx, f, new Vector3(2.2f, 3.0f, -3.96f), new Vector3(0.24f, 0.24f, 0.24f), beacon);
                break;
        }
    }

    /// <summary>The slewing head on the mast's top, in the turret's frame (the shoulder pin at the origin).</summary>
    private static void Turret(Renderer gfx, Matrix4x4 tf, int cls, float k)
    {
        if (cls == 1)
        {
            Cyl(gfx, tf, 1, new Vector3(0, -0.45f, 0), 1.2f, 0.3f, Yellow);
            Box(gfx, tf, Vector3.Zero, new Vector3(0.9f, 0.9f, 0.9f), Yellow);
            Box(gfx, tf, new Vector3(0, 0, -0.7f), new Vector3(0.7f, 0.6f, 0.6f), Dark);
            return;
        }
        Cyl(gfx, tf, 1, new Vector3(0, -0.5f * k, 0), 1.5f * k, 0.25f * k, Black);
        Box(gfx, tf, new Vector3(0, -0.05f * k, 0), new Vector3(1.1f * k, 0.9f * k, 1.2f * k), Yellow);
        for (var s = -1; s <= 1; s += 2)
            Cyl(gfx, tf, 0, new Vector3(s * 0.62f * k, 0, 0), 1.0f * k, 0.14f * k, Yellow);
    }

    /// <summary>A lattice boom from a to b in the turret's frame (its sides upright): four chords, rungs at each bay, diagonals on both sides.</summary>
    private static void Truss(Renderer gfx, Matrix4x4 tf, Vector3 a, Vector3 b, float w, float hgt)
    {
        var len = Vector3.Distance(a, b);
        if (len < 0.2f)
            return;
        var z = (b - a) / len;
        var x = Vector3.UnitX; // (the turret frame's side: every boom swings in its y-z plane)
        var y = Vector3.Normalize(Vector3.Cross(z, x));
        var c = MathF.Max(0.1f, w * 0.16f);
        for (var sx = -1; sx <= 1; sx += 2)
            for (var sy = -1; sy <= 1; sy += 2)
            {
                var o = x * (sx * w * 0.5f) + y * (sy * hgt * 0.5f);
                Beam(gfx, tf, a + o, b + o, c, c, Yellow);
            }
        var bays = Math.Max(3, (int)MathF.Round(len / (hgt * 1.1f)));
        for (var i = 0; i <= bays; i++)
        {
            var p = a + z * (len * i / bays);
            for (var sy = -1; sy <= 1; sy += 2)
                Beam(gfx, tf, p + y * (sy * hgt * 0.5f) - x * (w * 0.5f), p + y * (sy * hgt * 0.5f) + x * (w * 0.5f), c * 0.7f, c * 0.7f, Yellow);
            for (var sx = -1; sx <= 1; sx += 2)
                Beam(gfx, tf, p + x * (sx * w * 0.5f) - y * (hgt * 0.5f), p + x * (sx * w * 0.5f) + y * (hgt * 0.5f), c * 0.7f, c * 0.7f, Yellow);
            if (i == bays)
                break;
            var q = a + z * (len * (i + 1) / bays);
            var s = (i & 1) == 0 ? 1 : -1;
            for (var sx = -1; sx <= 1; sx += 2)
                Beam(gfx, tf, p + x * (sx * w * 0.5f) - y * (s * hgt * 0.5f), q + x * (sx * w * 0.5f) + y * (s * hgt * 0.5f), c * 0.55f, c * 0.55f, Hazard);
        }
    }

    /// <summary>The large forearm's end: its four chords running in to the wrist pin.</summary>
    private static void Taper(Renderer gfx, Matrix4x4 tf, Vector3 a, Vector3 b, float w, float hgt)
    {
        var len = Vector3.Distance(a, b);
        if (len < 0.1f)
            return;
        var z = (b - a) / len;
        var x = Vector3.UnitX;
        var y = Vector3.Normalize(Vector3.Cross(z, x));
        var c = MathF.Max(0.1f, w * 0.16f);
        for (var sx = -1; sx <= 1; sx += 2)
            for (var sy = -1; sy <= 1; sy += 2)
                Beam(gfx, tf, a + x * (sx * w * 0.5f) + y * (sy * hgt * 0.5f), b, c, c, Yellow);
    }

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 at, Vector3 size, Vector3 colour) =>
        gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(at) * frame, colour);

    /// <summary>A cylinder along axis <paramref name="axis"/> (0 x, 1 y, 2 z).</summary>
    private static void Cyl(Renderer gfx, Matrix4x4 frame, int axis, Vector3 centre, float diameter, float length, Vector3 colour) =>
        gfx.Draw(Matrix4x4.CreateScale(diameter, diameter, length) * (axis == 0 ? ToX : axis == 1 ? ToY : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(centre) * frame, colour, shape: CylShape);

    /// <summary>A bar from a to b, <paramref name="w"/> across and <paramref name="hgt"/> deep, its sides kept upright where it can.</summary>
    private static void Beam(Renderer gfx, Matrix4x4 frame, Vector3 a, Vector3 b, float w, float hgt, Vector3 colour)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = MathF.Abs(z.X) < 0.98f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z) is var cx && cx.LengthSquared() > 1e-6f ? cx : Vector3.UnitX) : Vector3.UnitZ;
        if (MathF.Abs(z.Y) > 0.98f)
            x = Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(w, hgt, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f) * frame, colour);
    }
}
