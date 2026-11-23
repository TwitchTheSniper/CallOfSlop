using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Building Station's crane (design page drawing 18; ConstructStation.cs "The fitting arms"): a runway rail along each end of the bay on
/// its corner masts' tops (on short legs at its corners when it has none), reaching on over the Core's hold; one bridge on the runway for
/// each fitting arm, with its trolley, hoist, cable, hook block and gripper; and a lamp on each mast's top that shows the job's state. An
/// arm's carry is posed from the job as this peer heard it, so host and client draw the same: the bridge runs to the hold, the hook drops
/// into it and comes up with the part, runs to the part's place, lowers it there and holds it while it goes on (the weld flash is
/// RouteRender's), then lifts clear. A station whose bay a real crane on its body reaches draws only its lamps (UnderRealCrane). Looks
/// only: nothing here collides or runs on the server. Drawn within <see cref="DrawNear"/>.
/// Allocates nothing.
/// </summary>
public static class StationRender
{
    public const float DrawNear = 160f;
    private static readonly Vector3 Yellow = new(0.94f, 0.76f, 0.1f), Dark = new(0.133f, 0.149f, 0.169f), Steel = new(0.6f, 0.63f, 0.66f),
        Out = new(0.94f, 0.47f, 0.09f), Hazard = new(0.95f, 0.69f, 0.12f), Cable = new(0.08f, 0.09f, 0.1f);
    private const float RailW = 0.32f; // the runway's I-beam's flange
    private const float LegEvery = 12f; // m: the most a runway spans between its supports (masts, legs); a longer bay gets legs along its sides

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var now = ConstructConveyor.Clock;
        foreach (var c in world.Bodies)
        {
            if (c.PieceRackCount == 0 || c.MachineAsmCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var k = 0; k < c.PieceRackCount; k++)
            {
                int t = c.PieceRacks[k];
                if (t >= c.PartCount || c.Parts[t].Broken || !c.DefOf(t).IsBuildStation || ConstructStation.BaySize(c, t) is not { X: > 0 } size)
                    continue;
                ConstructRender.PartDrawn(c, t, out var pos, out var rot);
                var floor = pos + Vector3.Transform(ConstructStation.BayFloor(c, t), rot);
                if (Vector3.DistanceSquared(eye, floor) > DrawNear * DrawNear)
                    continue;
                DrawStation(gfx, c, t, Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(floor), size, now,
                    UnderRealCrane(c, t));
            }
        }
    }

    /// <summary>
    /// Does a real Bridge or Hall Crane on the station's body reach a metre over its bay's middle? Then the station
    /// draws no crane of its own: its bridges stood parked in the way of the finished ship and clashed with the real crane (the Zero
    /// Day works' yard, Sean 2026-11-23). Reads the crane reach LifterRender keeps current; allocates nothing.
    /// </summary>
    private static bool UnderRealCrane(Construct c, int t)
    {
        if (c.CraneN == 0)
            return false;
        Freeform.PartWorld(c, t, out var sp, out var sr); // (the station in the cranes' own pose, the tick's, so a moving body doesn't flicker at a reach's edge)
        var at = sp + Vector3.Transform(ConstructStation.BayFloor(c, t) + Vector3.UnitY, sr);
        for (var k = 0; k < c.CraneN; k++)
        {
            int part = c.CraneDefs[k].Part;
            if (part >= c.PartCount || c.Parts[part].Broken || ConstructLifters.DefOf(c, part) is not { Bridge: true, RailWhy: 0 } ld)
                continue;
            ConstructLifters.Frame(c, part, ld, out _, out var rot, out var shoulder);
            if (ConstructLifters.InReach(ld, shoulder, rot, at, 0, out _))
                return true;
        }
        return false;
    }

    private static void DrawStation(Renderer gfx, Construct c, int t, Matrix4x4 bay, Vector3 size, double now, bool realCrane)
    {
        var d = c.DefOf(t);
        var cell = d.BlockCell;
        var masts = ConstructStation.BayCorner(c, t) > 0;
        float hx = size.X * 0.5f, hz = size.Z * 0.5f;
        // The Core's hold, in the bay's frame: where the hook picks a part up, and how far the runway reaches on over the Core.
        var floorLocal = ConstructStation.BayFloor(c, t);
        ConstructRackPack.Hold(d, out var hlo, out var hhi);
        var hold = (hlo + hhi) * 0.5f - floorLocal;
        var holdTop = hhi.Y - floorLocal.Y;
        // The runway: one rail along each end, over the end rows' masts (or on legs just outside the corners), from over the hold to the far
        // side, and on over the station's own pick pads past the bay (ConstructStationPads.cs).
        var top = size.Y; // (the masts' top: the rails sit on them)
        var railZ = masts ? hz - cell * 0.5f : hz + 0.25f;
        float xa = MathF.Min(-hx, hold.X) - 0.4f, xb = MathF.Max(hx, hold.X) + 0.4f; // (the runway's ends)
        if (MathF.Abs(hold.X) < hx)
            (xa, xb) = (-hx - 0.4f, hx + 0.4f);
        Span<ushort> pads = stackalloc ushort[ConstructStation.MaxOwnPads];
        var padN = ConstructStation.OwnPads(c, t, pads);
        float za = 0, zb = 0; // (how far the bridges reach out past the rails over a pick pad beside a long side: a cantilever)
        for (var p = 0; p < padN; p++)
            if (ConstructStation.PadInBay(c, t, pads[p], out var pm, out var ph))
            {
                if (MathF.Abs(pm.Z) - ph.Z >= hz - 0.05f)
                {
                    za = MathF.Min(za, pm.Z - ph.Z);
                    zb = MathF.Max(zb, pm.Z + ph.Z);
                    continue;
                }
                xa = MathF.Min(xa, pm.X - ph.X - 0.4f);
                xb = MathF.Max(xb, pm.X + ph.X + 0.4f);
            }
        // A long runway is a deeper girder on legs along the bay's sides (outside it, braced across to the rail), never more than
        // LegEvery between supports; a wide bay's bridge is a deeper truss (DrawBridge).
        var legs = Math.Max(0, (int)MathF.Ceiling((xb - xa) / LegEvery) - 1);
        var bayLegs = masts ? Math.Max(0, (int)MathF.Ceiling(size.X / LegEvery) - 1) : 0;
        var RailH = Math.Clamp(0.42f + (xb - xa) / (legs + 1) * 0.03f, 0.42f, 0.9f); // (its depth by the span it carries)
        var y = top + RailH * 0.5f + 0.004f;
        for (var s = -1; s <= 1 && !realCrane; s += 2) // (under a real crane: no runway of its own)
        {
            var z = s * railZ;
            Box(gfx, bay, new Vector3((xa + xb) * 0.5f, y, z), new Vector3(xb - xa, RailH - 0.08f, 0.06f), Dark); // (the web)
            Box(gfx, bay, new Vector3((xa + xb) * 0.5f, top + RailH - 0.02f, z), new Vector3(xb - xa + 0.004f, 0.04f, RailW), Steel); // (the top flange the wheels run on)
            Box(gfx, bay, new Vector3((xa + xb) * 0.5f, top + 0.024f, z), new Vector3(xb - xa + 0.008f, 0.04f, RailW + 0.006f), Dark); // (the bottom flange)
            for (var ei = 0; ei < 2; ei++)
            {
                var e = ei == 0 ? xa : xb;
                var ex = e + (e < 0 ? 0.08f : -0.08f);
                Box(gfx, bay, new Vector3(ex, top + RailH + 0.12f, z), new Vector3(0.16f, 0.22f, RailW + 0.04f), Hazard); // (the end stop)
            }
            // Where the runway leaves the bay over the Core: a knee brace down to the last mast (or leg), so the overhang reads as carried.
            var over = hold.X < 0 ? xa : xb;
            var at = hold.X < 0 ? -hx + cell * 0.5f : hx - cell * 0.5f;
            if (masts && MathF.Abs(over - at) > 0.6f)
                Beam(gfx, bay, new Vector3(at, top - 0.9f, z), new Vector3((over + at) * 0.5f, top + 0.02f, z), 0.12f, Dark);
            if (!masts)
                for (var e = -1; e <= 1; e += 2)
                {
                    var lx = e * (hx + 0.25f);
                    Box(gfx, bay, new Vector3(lx, top * 0.5f, z), new Vector3(0.2f, top, 0.2f), Dark); // (a leg at the corner)
                    Box(gfx, bay, new Vector3(lx, 0.6f, z), new Vector3(0.208f, 0.3f, 0.208f), Hazard);
                }
            // The legs between: spaced evenly along the runway, each outside the bay's side with a knee bracket in to the rail (on the
            // masts' bays the ones over the bay; past it, over a pick pad, the runway's own reach is carried the same way).
            for (var k = 1; k <= legs; k++)
            {
                var lx = xa + (xb - xa) * k / (legs + 1);
                if (masts && bayLegs == 0 && MathF.Abs(lx) < hx - cell * 0.5f)
                    continue; // (a short bay between its masts: its rail spans it)
                Leg(gfx, bay, lx, top, z, s * (hz + 0.45f), RailH);
            }
        }
        // The lamps on the masts' tops (on the runway's ends over the corners): the job's state.
        var j = c.Parts[t].Station;
        var lamp = LampColour(j, now);
        for (var sx = -1; sx <= 1; sx += 2)
            for (var sz = -1; sz <= 1; sz += 2)
            {
                var lx = sx * (masts ? hx - cell * 0.5f : hx + 0.25f);
                var lz = sz * railZ;
                var ly = top + (realCrane ? 0 : RailH) + 0.02f; // (no runway under a real crane: on the masts' tops)
                Box(gfx, bay, new Vector3(lx, ly + 0.04f, lz + sz * (RailW * 0.5f + 0.12f)), new Vector3(0.22f, 0.12f, 0.2f), Dark);
                Box(gfx, bay, new Vector3(lx, ly + 0.16f, lz + sz * (RailW * 0.5f + 0.12f)), new Vector3(0.16f, 0.14f, 0.14f), lamp);
            }
        // The bridges, one an arm.
        var n = realCrane ? 0 : ConstructStation.Arms(c, t); // (under a real crane: no bridges of its own either)
        var building = j is { State: StationState.Building };
        var hookUp = top - 0.7f;
        for (var a = 0; a < n; a++)
        {
            var park = new Vector3(-hx + (a + 0.5f) * size.X / n, hookUp, 0);
            var carry = 0;
            var from = j != null && j.ArmOffPad[a] ? j.ArmPick[a] : hold with { Y = holdTop - 0.05f }; // (off a pick pad: the piece's top on its deck)
            var at = j == null ? park : Pose(j, a, park, from, hookUp, building, now, out carry);
            at.X = Math.Clamp(at.X, xa + 0.6f, xb - 0.6f);
            at.Z = Math.Clamp(at.Z, MathF.Min(-railZ, za) + 0.7f, MathF.Max(railZ, zb) - 0.7f);
            DrawBridge(gfx, bay, at, top, railZ, MathF.Min(-railZ, za), MathF.Max(railZ, zb), RailH, carry > 0 && j != null ? j.ArmDef[a] : ushort.MaxValue, now, building && j != null && j.ArmTicks[a] > 0);
        }
    }

    /// <summary>The state lamp's colour: green while it builds, steady amber while it costs, red blinking when it stopped, white when done, dark when idle.</summary>
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

    /// <summary>
    /// Arm <paramref name="a"/>'s hook (x, height, z in the bay's frame) as its carry stands: from where it was to the hold, down and up with
    /// the part, to the part's place, down and held there until it goes on, then up. <paramref name="carry"/>: 1 while it holds the part.
    /// </summary>
    private static Vector3 Pose(StationJob j, int a, Vector3 park, Vector3 hold, float up, bool building, double now, out int carry)
    {
        carry = 0;
        if (!j.ArmDrawn[a])
        {
            (j.ArmLast[a], j.ArmFrom[a], j.ArmDrawn[a]) = (park, park, true);
            j.ArmSeen[a] = j.ArmSeq[a];
            j.ArmStart[a] = now - 60;
        }
        if (j.ArmSeen[a] != j.ArmSeq[a])
        {
            (j.ArmSeen[a], j.ArmStart[a], j.ArmFrom[a]) = (j.ArmSeq[a], now, j.ArmLast[a]);
            j.ArmFrom[a].Y = up;
        }
        var target = j.ArmAt[a];
        var dur = MathF.Max(1f, j.ArmTicks[a] / (float)World.TickRate);
        var u = (float)(now - j.ArmStart[a]) / dur;
        Vector3 at;
        if (!building || j.ArmTicks[a] == 0)
            at = j.ArmLast[a] with { Y = MathF.Min(up, j.ArmLast[a].Y + 0.02f) }; // (idle: where it was, the hook rising clear; an arm that never carried stays parked, not at its empty target, the bay's middle, where every idle bridge used to stack)
        else if (u < 0.2f)
            at = Vector3.Lerp(j.ArmFrom[a], hold with { Y = up }, Ease(u / 0.2f));
        else if (u < 0.3f)
            at = hold with { Y = Lerp(up, hold.Y, Ease((u - 0.2f) / 0.1f)) };
        else if (u < 0.4f)
        {
            at = hold with { Y = Lerp(hold.Y, up, Ease((u - 0.3f) / 0.1f)) };
            carry = 1;
        }
        else if (u < 0.75f)
        {
            at = Vector3.Lerp(hold with { Y = up }, target with { Y = up }, Ease((u - 0.4f) / 0.35f));
            carry = 1;
        }
        else if (u < 0.9f)
        {
            at = target with { Y = Lerp(up, MathF.Min(up, target.Y), Ease((u - 0.75f) / 0.15f)) };
            carry = 1;
        }
        else if (u < 1f)
        {
            at = target with { Y = MathF.Min(up, target.Y) };
            carry = 1;
        }
        else
            at = target with { Y = Lerp(MathF.Min(up, target.Y), up, Ease(MathF.Min(1f, (u - 1f) / 0.4f * dur / 2f))) }; // (it went on: the hook lifts clear in about a second)
        j.ArmLast[a] = at;
        return at;
    }

    private static float Ease(float x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>One bridge over the runway at its hook's x, its trolley over the hook's z, the hook at its height, the part it carries under the gripper.</summary>
    /// <summary>A runway leg beside the bay: a column from the floor at (<paramref name="x"/>, <paramref name="zLeg"/>) up to the rail's underside, a knee across to the rail at <paramref name="zRail"/>.</summary>
    private static void Leg(Renderer gfx, Matrix4x4 bay, float x, float top, float zRail, float zLeg, float railH)
    {
        Box(gfx, bay, new Vector3(x, top * 0.5f, zLeg), new Vector3(0.36f, top, 0.36f), Dark); // (the column)
        Box(gfx, bay, new Vector3(x, 0.03f, zLeg), new Vector3(0.7f, 0.06f, 0.7f), Steel); // (its base plate)
        Box(gfx, bay, new Vector3(x, 0.6f, zLeg), new Vector3(0.368f, 0.3f, 0.368f), Hazard);
        Box(gfx, bay, new Vector3(x, top - 0.15f, (zLeg + zRail) * 0.5f), new Vector3(0.3f, 0.3f, MathF.Abs(zLeg - zRail) + 0.3f), Dark); // (the cap beam under the rail)
        Beam(gfx, bay, new Vector3(x, top - 1.6f, zLeg), new Vector3(x, top - 0.3f, zRail), 0.14f, Dark); // (the knee)
        Box(gfx, bay, new Vector3(x, top + railH * 0.5f, zRail), new Vector3(0.03f, railH - 0.1f, RailW - 0.04f), Steel); // (a stiffener on the web over it)
    }

    private static void DrawBridge(Renderer gfx, Matrix4x4 bay, Vector3 hook, float top, float railZ, float zLo, float zHi, float RailH, ushort carried, double now, bool moving)
    {
        var x = hook.X;
        var rail = top + RailH;
        // The end trucks, on the rails' top flange (5 mm over it), with their wheels and a hazard face.
        for (var s = -1; s <= 1; s += 2)
        {
            var z = s * railZ;
            Box(gfx, bay, new Vector3(x, rail + 0.2f, z), new Vector3(1.6f, 0.3f, RailW + 0.18f), Dark);
            for (var w = -1; w <= 1; w += 2)
                Box(gfx, bay, new Vector3(x + w * 0.55f, rail + 0.1f, z), new Vector3(0.3f, 0.2f, RailW - 0.06f), Steel);
            Box(gfx, bay, new Vector3(x, rail + 0.2f, z + s * (RailW * 0.5f + 0.096f)), new Vector3(1.2f, 0.12f, 0.012f), Hazard);
        }
        // Two girders across, a walkway grating on one, the drive motor at one end. A wide bay's girders are deeper (a box girder to 12 m,
        // a truss past it: a bottom chord, posts and diagonals, Sean 2026-11-21: the Zero Day's 24 m bay).
        var span = zHi - zLo + RailW + 0.3f; // (rail to rail, or on out over a pick pad beside the bay: a cantilever)
        var zc = (zLo + zHi) * 0.5f;
        var Girder = Math.Clamp(0.5f + (span - 8f) * 0.05f, 0.5f, 1.4f);
        var gy = rail + 0.35f + Girder * 0.5f;
        var truss = span > 12f;
        var ribs = Math.Max(1, (int)(span / 2f)); // (a stiffener every 2 m or so, and a dark cap on each girder: no long plain yellow bar)
        for (var g = -1; g <= 1; g += 2)
        {
            var gx = x + g * 0.34f;
            if (!truss)
            {
                Box(gfx, bay, new Vector3(gx, gy, zc), new Vector3(0.26f, Girder, span), Yellow);
                for (var r = 1; r < ribs; r++)
                    Box(gfx, bay, new Vector3(gx, gy - 0.02f, zc - span * 0.5f + span * r / ribs), new Vector3(0.276f, Girder - 0.08f, 0.06f), Dark);
            }
            else
            {
                // The truss: a top chord (the trolley's rail), a bottom chord, a post every 2 m or so and a diagonal in each bay, all yellow.
                float yt = gy + Girder * 0.5f - 0.12f, yb = gy - Girder * 0.5f + 0.12f;
                Box(gfx, bay, new Vector3(gx, yt, zc), new Vector3(0.26f, 0.24f, span), Yellow);
                Box(gfx, bay, new Vector3(gx, yb, zc), new Vector3(0.22f, 0.24f, span - 0.6f), Yellow);
                for (var r = 0; r <= ribs; r++)
                {
                    var rz = zc - span * 0.5f + 0.3f + (span - 0.6f) * r / ribs;
                    Box(gfx, bay, new Vector3(gx, gy, rz), new Vector3(0.18f, Girder - 0.36f, 0.14f), Yellow);
                    if (r < ribs)
                    {
                        var nz = zc - span * 0.5f + 0.3f + (span - 0.6f) * (r + 1) / ribs;
                        var up = (r & 1) == 0;
                        Beam(gfx, bay, new Vector3(gx, up ? yb : yt, rz), new Vector3(gx, up ? yt : yb, nz), 0.12f, Yellow);
                    }
                }
                // Its ends drop to the end trucks: a solid plate over the last metre each side.
                for (var e = -1; e <= 1; e += 2)
                    Box(gfx, bay, new Vector3(gx, gy, zc + e * (span * 0.5f - 0.5f)), new Vector3(0.24f, Girder, 1.0f), Yellow);
            }
            Box(gfx, bay, new Vector3(gx, gy + Girder * 0.5f + 0.012f, zc), new Vector3(0.3f, 0.024f, span + 0.004f), Dark); // (the top flange)
        }
        Box(gfx, bay, new Vector3(x - 0.62f, gy + Girder * 0.5f - 0.03f, zc), new Vector3(0.5f, 0.04f, span - 0.4f), Dark); // (the walkway)
        Box(gfx, bay, new Vector3(x + 0.62f, rail + 0.55f, railZ - 0.2f), new Vector3(0.36f, 0.36f, 0.5f), Dark); // (the drive motor)
        Box(gfx, bay, new Vector3(x + 0.62f, rail + 0.55f, railZ - 0.47f), new Vector3(0.2f, 0.2f, 0.04f), Steel);
        // The trolley on the girders over the hook, its hoist drum and a beacon that turns while the arm moves (it never stops for anyone under it).
        var ty = gy + Girder * 0.5f + 0.2f;
        Box(gfx, bay, new Vector3(x, ty, hook.Z), new Vector3(1.0f, 0.4f, 1.1f), Dark);
        Box(gfx, bay, new Vector3(x + 0.12f, ty + 0.3f, hook.Z), new Vector3(0.4f, 0.22f, 0.7f), Steel);
        var beacon = moving && (now * 3.0 % 1.0) < 0.5 ? Hazard : new Vector3(0.35f, 0.25f, 0.05f);
        Box(gfx, bay, new Vector3(x - 0.3f, ty + 0.27f, hook.Z + 0.38f), new Vector3(0.14f, 0.14f, 0.14f), beacon);
        // The cable: two falls down between the girders to the hook block; the gripper's bar and jaws under it.
        var block = hook + new Vector3(0, 0.45f, 0);
        for (var s = -1; s <= 1; s += 2)
            Beam(gfx, bay, new Vector3(x, gy, hook.Z + s * 0.08f), block + new Vector3(0, 0.15f, s * 0.08f), 0.03f, Cable);
        Box(gfx, bay, block, new Vector3(0.34f, 0.34f, 0.36f), Out);
        Box(gfx, bay, block + new Vector3(0.172f, 0, 0), new Vector3(0.012f, 0.26f, 0.28f), Dark);
        Box(gfx, bay, hook + new Vector3(0, 0.2f, 0), new Vector3(0.12f, 0.16f, 0.12f), Steel); // (the swivel)
        Box(gfx, bay, hook + new Vector3(0, 0.1f, 0), new Vector3(0.9f, 0.06f, 0.16f), Dark); // (the gripper's bar)
        var grip = carried == ushort.MaxValue ? 0.42f : 0.3f; // (open while empty)
        for (var s = -1; s <= 1; s += 2)
            Box(gfx, bay, hook + new Vector3(s * grip, -0.02f, 0), new Vector3(0.05f, 0.22f, 0.14f), Out);
        if (carried == ushort.MaxValue || PartOf(carried) is not { } d)
            return;
        // The part it carries, its top at the hook, as it is drawn in its file (its own colours). A long piece (off a pick pad: up to 14 m)
        // also hangs on a two-leg bridle from the hook block to shackles near its ends, so it reads as carried level, not on one jaw.
        var size = d.BoundsMax - d.BoundsMin;
        var reach = MathF.Max(size.X, size.Z);
        if (reach > 3f)
        {
            var along = size.X >= size.Z ? Vector3.UnitX : Vector3.UnitZ;
            var half = MathF.Min(reach * 0.5f - 0.4f, 7f);
            for (var e = -1; e <= 1; e += 2)
            {
                var end = hook + along * (e * half) + new Vector3(0, 0.06f, 0);
                Box(gfx, bay, end, new Vector3(0.16f, 0.12f, 0.16f), Out); // (a shackle on the piece's top)
                Beam(gfx, bay, block, end + new Vector3(0, 0.05f, 0), 0.04f, Cable); // (its sling up to the hook block)
            }
        }
        var m = Matrix4x4.CreateTranslation(-(d.BoundsMin.X + d.BoundsMax.X) * 0.5f, -d.BoundsMax.Y, -(d.BoundsMin.Z + d.BoundsMax.Z) * 0.5f) * Matrix4x4.CreateTranslation(hook - new Vector3(0, 0.004f, 0)) * bay;
        if (size.Y > 40f)
            return;
        for (var bx = 0; bx < d.Boxes.Length; bx++)
        {
            ref readonly var box = ref d.Boxes[bx];
            if (!box.Hidden)
                gfx.Draw(box.Model(0, box.Center) * m, box.Color, shape: box.Shape);
        }
    }

    private static ConstructPartDef? PartOf(ushort id) => ConstructParts.Get(id);

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 at, Vector3 size, Vector3 colour) =>
        gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(at) * frame, colour);

    private static void Beam(Renderer gfx, Matrix4x4 frame, Vector3 a, Vector3 b, float thick, Vector3 colour)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = Vector3.Normalize(Vector3.Cross(MathF.Abs(z.Y) > 0.98f ? Vector3.UnitX : Vector3.UnitY, z));
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(thick, thick, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f) * frame, colour);
    }
}
