using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The command view's missiles layer (docs/space/COMMAND.md "The missiles layer"; Karl, 2026-11-17: "Missiles show up on command view.
/// Add a tickbox to show missiles, their target and their projected trajectory/flight path"). The MISSILES tickbox turns it on and off.
/// Every missile in flight (a launcher's or a designed one off an Ordnance Bench: torpedoes, MIRV warheads) is a small dart along its
/// way, with a line to what it steers for (a body, a flare that drew it, or a point) and a dashed path ahead: its own flight stepped
/// forward against the target moving on as it moves now. Everything comes from the shell pool every PC already flies (the launch
/// event and ShellSteer give a client the push, turn, burn, target and aim), so nothing new goes on the wire. Off, it costs one
/// bool check a frame; on, at most <see cref="CmdMaxMissiles"/> missiles, each path <see cref="CmdMissileSteps"/> steps, nothing allocated
/// but a label's text, made at most four times a second when it changes.
/// </summary>
public sealed partial class Ui
{
    /// <summary>Is the missiles layer shown (kept while the view is closed and opened again).</summary>
    public bool CommandMissilesOn;

    public const int CmdMaxMissiles = 96, CmdMissileSteps = 40;
    public const float CmdMissileMinAhead = 3f, CmdMissileMaxAhead = 600f; // s: how far ahead a path looks

    private static readonly Vector3 CmdOrange = new(1f, 0.55f, 0.18f);

    /// <summary>A label per pool slot, kept from frame to frame (keyed by the shell's id, its target and the numbers it shows).</summary>
    private readonly CmdLabel[] _cmdMslLabels = new CmdLabel[CmdMaxMissiles];
    private readonly ushort[] _cmdMslLabelFor = new ushort[CmdMaxMissiles];
    private readonly float2[] _cmdMslPts = new float2[CmdMissileSteps + 1];
    private int _cmdMslShown, _cmdMslHoming;
    private CmdLabel _cmdMslNote;

    /// <summary>What the layer drew last frame (the window tool's <c>missiles</c>): missiles, and those homing on something.</summary>
    public (int Shown, int Homing) CommandMissileCounts => (_cmdMslShown, _cmdMslHoming);

    private static bool CmdIsMissile(in CShell s) => !s.Landed && s.Kind == ShellKind.Missile;

    /// <summary>The layer: each missile, its target line and its path ahead.</summary>
    private void CmdMissiles(ImDrawListPtr dl, Game g, float fade, double now)
    {
        _cmdMslShown = _cmdMslHoming = 0;
        if (!CommandMissilesOn)
            return;
        var w = g.Constructs;
        var me = g.Local;
        var slot = 0;
        for (var k = 0; k < w.ShellCount && slot < CmdMaxMissiles; k++)
        {
            ref var s = ref w.Shells[k];
            if (!CmdIsMissile(s))
                continue;
            var n = slot++;
            _cmdMslShown++;

            // What it steers for, as the flight does (ConstructOrdnance.Fly, OrdnanceFlight.Fly).
            Construct? target = null;
            var aimKind = 0; // 0 nothing (it flies on), 1 a body, 2 a flare, 3 a point
            var aimAt = Vector3.Zero;
            if (s.Decoy != 0 && w.ShellAt(s.Decoy) is var f and >= 0)
            {
                aimKind = 2;
                aimAt = w.Shells[f].Pos;
            }
            else if (s.Decoy == 0 && s.Target != 0 && w.Get(s.Target) is { } t)
            {
                aimKind = 1;
                target = t;
                aimAt = t.WorldCom;
            }
            else if (s.Decoy == 0 && (s.Flags & ShellFlags.HasAim) != 0)
            {
                aimKind = 3;
                aimAt = s.Aim;
            }
            if (aimKind != 0)
                _cmdMslHoming++;
            var mine = target != null && me != null && (target.Owner == me.Id || Factions.Shares(target.Owner, me.Id));
            var colour = mine ? CmdRed : CmdOrange;

            // Its path ahead.
            var speed = s.Vel.Length();
            var dist = aimKind != 0 ? Vector3.Distance(s.Pos, aimAt) : 0f;
            var closing = aimKind != 0 && dist > 1e-3f ? MathF.Max(speed, Vector3.Dot(s.Vel - (target?.Vel ?? Vector3.Zero), (aimAt - s.Pos) / dist)) : speed;
            var eta = aimKind != 0 && closing > 1f ? dist / closing : float.NaN;
            var life = ((s.Life > 0 ? s.Life : ConstructWorld.ShellMaxAge) - s.Age) / (float)World.TickRate;
            var ahead = Math.Clamp(float.IsFinite(eta) ? eta * 1.3f + 1f : (s.Burn + s.Wait) / (float)World.TickRate + 10f, CmdMissileMinAhead, CmdMissileMaxAhead);
            ahead = MathF.Max(0.5f, MathF.Min(ahead, life));
            var pts = CmdMissilePath(w, s, target, ahead, out var met);

            var at = CmdP(_cvPose.ToEcliptic(Vec3d.From(s.Pos)));
            for (var p = 0; p + 1 < pts; p++)
                CmdLine(dl, _cmdMslPts[p], _cmdMslPts[p + 1], colour, 0.8f * fade * (1f - 0.6f * p / CmdMissileSteps), 1.3f); // (fading as it looks further ahead)
            if (met && pts > 1)
            {
                var end = _cmdMslPts[pts - 1];
                var x = 4.5f * _scale;
                CmdLine(dl, end + new float2(-x, -x), end + new float2(x, x), colour, fade, 1.4f);
                CmdLine(dl, end + new float2(-x, x), end + new float2(x, -x), colour, fade, 1.4f);
            }

            // The line to its target, ending on the craft as the view draws it.
            if (aimKind != 0)
            {
                var to = CmdP(_cvPose.ToEcliptic(Vec3d.From(aimAt)));
                if (target != null && CmdFind(target.Id) is var ci and >= 0)
                    to = _cmdCraft[ci].At;
                var dash = Math.Min(400, Math.Max(1, (int)((to - at).Length() / (5 * _scale))));
                for (var d = 0; d < dash; d += 2)
                    CmdLine(dl, at + (to - at) * (d / (float)dash), at + (to - at) * Math.Min(1f, (d + 1) / (float)dash), CmdWhite, 0.3f * fade, 0.8f); // (a fine dotted line: the path is the solid one)
                var pulse = 0.55f + 0.45f * MathF.Sin((float)(now * 6) + s.Id);
                if (aimKind == 1)
                    CmdRing(dl, to, 11 * _scale, colour, pulse * fade, 1.3f, 4, 0.45f, (float)(now * 2));
                else
                    CmdRing(dl, to, 5 * _scale, colour, pulse * fade, 1f);
            }

            // The missile: a dart along its way on the screen.
            var tip = CmdP(_cvPose.ToEcliptic(Vec3d.From(s.Pos + (speed > 0.5f ? s.Vel / speed : Vector3.UnitY))));
            var dir = tip - at;
            var dl2 = dir.Length();
            dir = dl2 > 1e-3f ? dir / dl2 : new float2(0, -1);
            var side = new float2(-dir.Y, dir.X);
            var len = 7 * _scale;
            var nose = at + dir * len;
            var back = at - dir * (len * 0.6f);
            CmdGlow(dl, nose, back + side * (len * 0.55f), colour, fade, 1.3f);
            CmdGlow(dl, nose, back - side * (len * 0.55f), colour, fade, 1.3f);
            CmdLine(dl, back + side * (len * 0.55f), back - side * (len * 0.55f), colour, fade, 1.3f);

            // Its label, beside it: what it goes for, how far and how soon.
            if (_cmdMslLabelFor[n] != s.Id)
            {
                _cmdMslLabelFor[n] = s.Id;
                _cmdMslLabels[n] = default;
            }
            ref var label = ref _cmdMslLabels[n];
            if (label.Stale((long)(now * 4))) // (at most four times a second)
                label.Text = CmdMissileText(target, aimKind, mine, dist, eta);
            if (CmdOn(at))
                CmdText(dl, at + new float2(10 * _scale, -14 * _scale), 10.5f, colour, 0.95f * fade, label.Text!);
        }
    }

    private string CmdMissileText(Construct? target, int aimKind, bool mine, float dist, float eta)
    {
        var what = aimKind switch
        {
            1 => (mine ? "INCOMING > " : "MISSILE > ") + CmdName(target!),
            2 => "MISSILE > FLARE",
            3 => "MISSILE > POINT",
            _ => "MISSILE  NO LOCK",
        };
        if (aimKind == 0)
            return what;
        return what + "  " + CmdDistance(dist) + (float.IsFinite(eta) && eta < 3600 ? "  " + CmdTime(eta) : "");
    }

    /// <summary>
    /// Its flight stepped forward <paramref name="ahead"/> seconds into <see cref="_cmdMslPts"/>, on the screen: gravity, its push while
    /// it has burn left, a designed weapon's drag, and its turn towards its target's place then (moving on at its velocity now). Stages
    /// to come and wings aren't stepped: the path is what it would do as it is. The number of points; <paramref name="met"/> when it
    /// reaches what it steers for.
    /// </summary>
    private int CmdMissilePath(ConstructWorld w, in CShell s0, Construct? target, float ahead, out bool met)
    {
        met = false;
        var pos = s0.Pos;
        var vel = s0.Vel;
        var burn = s0.Burn;
        var wait = s0.Wait;
        var designed = (s0.Flags & ShellFlags.Designed) != 0;
        var flare = s0.Decoy != 0 && w.ShellAt(s0.Decoy) is var fi and >= 0 ? w.Shells[fi].Pos : (Vector3?)null;
        var tPos = target?.WorldCom ?? Vector3.Zero;
        var tVel = target?.Vel ?? Vector3.Zero;
        var hasAim = s0.Decoy == 0 && target == null && (s0.Flags & ShellFlags.HasAim) != 0;
        var reach = MathF.Max(s0.Prox, target != null ? MathF.Max(2f, target.Radius * 0.6f) : 2f);
        var dt = ahead / CmdMissileSteps;
        var ticks = Math.Max(1, (int)MathF.Round(dt * World.TickRate));
        var tickDt = 1f / World.TickRate;
        var age = s0.Age;
        _cmdMslPts[0] = CmdP(_cvPose.ToEcliptic(Vec3d.From(pos)));
        var n = 1;
        for (var step = 0; step < CmdMissileSteps; step++)
        {
            // Coarse steps of whole ticks: burn and wait count down as the flight's do.
            var h = ticks * tickDt;
            vel += ConstructShells.GravityAt(pos) * h;
            var speed = vel.Length();
            var air = designed ? MathF.Min(1f, ConstructAir.Density(pos)) : 0f;
            var burning = false;
            if (wait > 0)
                wait = Math.Max(0, wait - ticks);
            else if (burn > 0)
            {
                var share = Math.Min(burn, ticks) * tickDt;
                burn = Math.Max(0, burn - ticks);
                burning = true;
                var push = s0.Thrust + (designed && speed >= s0.MinSpeed ? s0.AirPush * air : 0);
                var along = speed > 0.5f ? vel / speed : Vector3.Normalize(-ConstructShells.GravityAt(pos) + new Vector3(0, 1e-3f, 0));
                vel += along * (push * share);
                speed = vel.Length();
            }
            if (designed && s0.Drag > 0 && air > 0 && age >= s0.Deploy)
            {
                vel -= vel * MathF.Min(1f, s0.Drag * air * speed / OrdFlightStats.DragSpeed * h);
                speed = vel.Length();
            }
            var rate = designed ? s0.Turn + s0.TurnAir * air * MathF.Min(1f, speed / OrdFlightStats.TurnSpeed) + (burning ? s0.TurnBurn : 0) : s0.Kind == ShellKind.Missile ? s0.Turn : 0;
            var t = (step + 1) * h;
            Vector3? aim = null;
            if (flare is { } fp)
                aim = fp;
            else if (target != null)
            {
                var tp = tPos + tVel * t;
                aim = tp + tVel * MathF.Min(Vector3.Distance(pos, tp) / MathF.Max(speed, 1f), 3f);
            }
            else if (hasAim)
                aim = s0.Aim;
            if (aim is { } a && rate > 0 && speed > 1f && (a - pos).LengthSquared() > 1e-2f)
                vel = RocketFlight.Turn(vel / speed, Vector3.Normalize(a - pos), rate * h) * speed;
            var was = pos;
            pos += vel * h;
            age += ticks;
            _cmdMslPts[n++] = CmdP(_cvPose.ToEcliptic(Vec3d.From(pos)));

            // Did this step pass what it goes for (the target where it will be, a flare, a point)?
            var goal = flare ?? (target != null ? tPos + tVel * t : hasAim ? s0.Aim : (Vector3?)null);
            if (goal is { } gl)
            {
                var seg = pos - was;
                var l2 = seg.LengthSquared();
                var u = l2 > 1e-6f ? Math.Clamp(Vector3.Dot(gl - was, seg) / l2, 0f, 1f) : 0f;
                if (Vector3.DistanceSquared(was + seg * u, gl) < reach * reach)
                {
                    _cmdMslPts[n - 1] = CmdP(_cvPose.ToEcliptic(Vec3d.From(was + seg * u)));
                    met = true;
                    break;
                }
            }
        }
        return n;
    }

    /// <summary>The MISSILES tickbox, beside LOGISTICS, and how many are in flight under it.</summary>
    private void CmdMissilesButton(ImDrawListPtr dl, float2 q0, float2 size, float fade)
    {
        if (CmdButton(dl, q0, q0 + size, "   MISSILES", CmdOrange, fade, CommandMissilesOn))
            CommandMissilesOn = !CommandMissilesOn;
        var box = 10 * _scale;
        var b0 = new float2(q0.X + 9 * _scale, q0.Y + (size.Y - box) * 0.5f);
        dl.AddRect(b0, b0 + new float2(box, box), Hud.Colour(CommandMissilesOn ? CmdWhite : CmdOrange, fade * 0.9f), 0, 0, _scale);
        if (CommandMissilesOn)
        {
            CmdLine(dl, b0 + new float2(box * 0.18f, box * 0.52f), b0 + new float2(box * 0.42f, box * 0.78f), CmdWhite, fade, 1.6f);
            CmdLine(dl, b0 + new float2(box * 0.42f, box * 0.78f), b0 + new float2(box * 0.85f, box * 0.2f), CmdWhite, fade, 1.6f);
            if (_cmdMslNote.Stale(_cmdMslShown))
                _cmdMslNote.Text = _cmdMslShown == 0 ? "NO MISSILES IN FLIGHT" : _cmdMslShown == 1 ? "1 MISSILE IN FLIGHT" : _cmdMslShown + " MISSILES IN FLIGHT";
            CmdText(dl, new float2(q0.X + size.X * 0.5f, q0.Y + size.Y + 7 * _scale), 10, CmdOrange, 0.8f * fade, _cmdMslNote.Text!, 1);
        }
    }
}
