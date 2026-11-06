using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Artillery in the game (docs/sandbox/CONSTRUCTS.md, "Artillery"): the launch and landing as each PC sees them, a client's copies
/// in flight, and the gunner's aid: a marker where the gun lands as it points now, and a line with the range, the flight time and the
/// elevation. The shells themselves are in Source/Constructs/ConstructShells.cs.
/// </summary>
public sealed partial class Game
{
    public int ShellsLandedSeen; // (hand checks: landing events drawn here)

    private int _arcKey = -1;
    private string _arcLine = "";

    /// <summary>Client, each tick: its shells fly (the server's landing event ends them).</summary>
    private void ClientShellsTick()
    {
        Constructs.FlyShells(World.TickDt);
        Constructs.StepRounds(World.Query, World.TickDt, server: false);
        ShellTrails();
    }

    /// <summary>Flying rounds near this PC's eye, as a streak every few ticks: the same arc as the shot's, on from where its tracer ended. A busy sky draws no more than it can see.</summary>
    private void RoundTrails()
    {
        var w = Constructs;
        if (Local is not { } me)
            return;
        var eye = me.RenderEye;
        for (var k = 0; k < w.RoundCount; k++)
        {
            ref var r = ref w.Rounds[k];
            if (r.Age % 4 == 0 && Vector3.DistanceSquared(eye, r.Pos) < 400f * 400f)
                Tracers.Add(new Tracer { From = r.Pos - r.Vel * (4 * World.TickDt), To = r.Pos, Color = new Vector3(1f, 0.85f, 0.5f), Width = 0.03f, Born = Brutal.GlfwApi.Glfw.Time, Life = 0.12f });
        }
    }

    private void ShellTrails()
    {
        RoundTrails();
        var w = Constructs;
        for (var k = 0; k < w.ShellCount; k++)
        {
            ref var s = ref w.Shells[k];
            if (s.Landed)
                continue;
            if (s.Age % 4 == 0)
                Tracers.Add(new Tracer { From = s.Prev - s.Vel * (4 * World.TickDt), To = s.Pos, Color = new Vector3(0.85f, 0.8f, 0.7f), Width = 0.06f, Born = ConstructRender.Now, Life = 0.35f, Speed = 0 });
            if (Local is { } me && s.Age % 12 == 0 && s.Vel.Y < 0 && Vector3.DistanceSquared(me.RenderEye, s.Pos + s.Vel) < 900f)
                Audio.Effect3D(Sfx.Whizz, s.Pos, 0.9f);
        }
    }

    private void ShellOutFx(in CFireEvent e, double now)
    {
        if (!IsServer)
            Constructs.ClientAddShell(e.Shell, (ShellKind)e.Muzzle, e.At, e.To);
        ConstructShotsSeen++;
        var dir = e.To.LengthSquared() > 1e-4f ? Vector3.Normalize(e.To) : Vector3.UnitY;
        ConstructRender.AddFlash(e.At, dir, now, GunFx.Cannon);
        if (Constructs.Get(e.Body) is { } c && c.GunOf(e.Part) is var k and >= 0 && c.Guns[k].Def is { } def)
        {
            Audio.Shot(def, e.At, mergeN: c.Guns[k].MergeN);
            if (Local is { } me && (c.Guns[k].Seat >= 0 && c.Guns[k].Seat < c.PartCount && c.Parts[c.Guns[k].Seat].SitterPlus1 == me.Id + 1 || c.Driver == me.Id))
                Shake = MathF.Max(Shake, ConstructWeapons.GunnerShake(ConstructWeapons.PullKick(c.Guns[k])));
        }
        else
            Audio.Effect3D(Sfx.Explosion, e.At, 0.5f); // (a gun this PC hasn't got: a far report)
    }

    private void ShellLandFx(in CFireEvent e, double now)
    {
        if (!IsServer)
            Constructs.ClientDropShell(e.Shell);
        ShellsLandedSeen++;
        ConstructBlastsSeen++;
        var kind = (ShellKind)e.Muzzle;
        var r = MathF.Max(1f, e.Radius);
        if (kind == ShellKind.Smoke)
            for (var i = 0; i < 3; i++)
            {
                var a = i * MathF.Tau / 3f;
                Smokes.Add(new SmokeCloud(e.At + new Vector3(MathF.Cos(a) * 3f, 0.5f, MathF.Sin(a) * 3f), now, now + Throwables.SmokeDuration));
            }
        Blasts.Add(new Blast(e.At, now, r, ThrowableKind.Frag, kind == ShellKind.Incendiary ? new Vector3(1f, 0.45f, 0.1f) : default));
        if (Blasts.Count > MaxBlasts)
            Blasts.RemoveAt(0);
        Audio.Effect3D(Sfx.Explosion, e.At, 1f);
        if (Local is { } me)
            Shake = MathF.Max(Shake, Math.Clamp(r / 8f - Vector3.Distance(me.RenderEye, e.At) / (r * 6f), 0f, 1f));
    }

    /// <summary>The shell gun a seated player works, or -1.</summary>
    private int ArtilleryGun(Player me, out Construct? c)
    {
        c = null;
        ref var h = ref SandboxHands.Hand(me.Id);
        if (!h.Seated || Constructs.Get(h.SeatBody) is not { GunCount: > 0 } body)
            return -1;
        for (var k = 0; k < body.GunCount; k++)
            if (body.Guns[k].RoundV > 0 && body.Guns[k].Seat == h.SeatPart && body.Guns[k].Def != null)
            {
                c = body;
                return k;
            }
        return -1;
    }

    /// <summary>Each frame for the HUD: a gunner's line ("" for none), and the marker where their gun lands as it points now. The line is made again only when a number in it changes.</summary>
    public string ArtilleryHudLine(Player me)
    {
        ConstructRender.GunnerMarked = false;
        var k = ArtilleryGun(me, out var c);
        if (k < 0)
            return "";
        var lands = ConstructShells.Predict(c!, k, World.Query, out var land, out var secs);
        ConstructRender.GunnerMarked = lands;
        ConstructRender.GunnerMark = land;
        ConstructWeapons.MuzzleWorld(c!, k, out var muzzle, out var fwd);
        var up = GravityFrames.UpAt(muzzle);
        var elev = (int)MathF.Round(MathF.Asin(Math.Clamp(Vector3.Dot(fwd, up), -1, 1)) * 180f / MathF.PI);
        var flat = land - muzzle;
        flat -= up * Vector3.Dot(flat, up);
        var range = (int)MathF.Round(flat.Length());
        var tenths = (int)(secs * 10);
        var key = (range * 1000 + tenths) * 200 + elev + 100 + (lands ? 0 : 1 << 30) + SandboxKeys.Stamp * 7;
        if (key != _arcKey)
        {
            _arcKey = key;
            _arcLine = SandboxKeys.Line(c!.DefOf(c.Guns[k].Part).Name, lands ? $"lands {range} m out, {tenths / 10f:0.0} s, elevation {elev}" : $"elevation {elev}", lands ? null : "lands past the map", (c.Guns[k].Effects & GunEffect.Shell) != 0 ? $"{SandboxKeys.Aim}: high arc" : null);
        }
        return _arcLine;
    }
}
