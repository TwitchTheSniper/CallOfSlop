using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Harpoon lines in the game (docs/sandbox/CONSTRUCTS.md, "Flame and harpoon"): the hand gun's shot, ends on players and what a
/// line does to them. A line is a rope in <see cref="ConstructWorld.Connectors"/>; an end on a player is a world end that this
/// PC keeps on them (<see cref="ConnectorEnd.Player"/>). The rope can't move a player through the solver, so each PC does it
/// in the shared movement step (<see cref="TetherPlayer"/>): past the rope's length plus a give they are drawn back along it,
/// and that give's stretch is what pulls the body at the other end (ConstructPhysics, ConnectorRows), so a crate follows a man
/// and a parked truck stops him. Nothing here allocates.
/// </summary>
public sealed partial class Game
{
    private const float TetherPullRate = 9f; // m/s of pull for each metre past the give
    private const float TetherPullMax = 14f; // m/s
    private const int HarpoonTearDamage = 10; // what tearing a head out costs

    private bool _hpReel, _hpFiredStroke;
    private int _hpLineStamp = -1;
    private string _hpHeld = "", _hpIn = "", _hpSeat = "";

    /// <summary>Server: a hand harpoon's round reached <paramref name="dist"/>: ties a rope from the shooter to what it struck. A round at the end of its reach ties nothing.</summary>
    private void SandboxHarpoonShot(Player shooter, Vector3 o, Vector3 d, float dist, float reach, ConstructHit? construct, Player? target)
    {
        var w = Constructs;
        var old = w.LineHeldBy(shooter.Id);
        if (old >= 0)
            w.CutLine(old);
        var at = o + d * dist;
        var from = new ConnectorEnd { Player = (byte)(shooter.Id + 1), Local = shooter.Chest };
        ConnectorEnd head;
        if (target != null)
            head = new ConnectorEnd { Player = (byte)(target.Id + 1), Local = target.Chest };
        else if (construct is { } ch && ch.Distance <= dist + 1e-3f && ch.Part < ch.Body.PartCount)
            head = new ConnectorEnd { Body = ch.Body.Id, Part = (ushort)ch.Part, Local = ConstructWorld.PartLocal(ch.Body, ch.Part, at) };
        else if (dist < reach - 1e-3f)
            head = new ConnectorEnd { Local = at };
        else
            return;
        w.TieLine(World.Query, 0, from, head, ConstructWeaponParts.Current.HarpoonHandReel);
    }

    /// <summary>Server, before the constructs' step: every end on a player goes where they are; a line whose player left or died goes, and so does a line whose holder put the launcher away.</summary>
    private void SandboxHarpoonTick()
    {
        var w = Constructs;
        for (var k = w.ConnectorCount - 1; k >= 0; k--)
        {
            ref var cn = ref w.Connectors[k];
            if (cn.A.Player != 0)
            {
                if (!Players.TryGetValue((byte)(cn.A.Player - 1), out var p) || !p.Alive || !p.WeaponDef.Harpoon)
                {
                    w.CutLine(k);
                    continue;
                }
                cn.A.Local = p.Chest;
            }
            if (cn.B.Player != 0)
            {
                if (!Players.TryGetValue((byte)(cn.B.Player - 1), out var p) || !p.Alive)
                {
                    w.CutLine(k);
                    continue;
                }
                cn.B.Local = p.Chest;
            }
        }
    }

    /// <summary>Every PC, each frame: ends on players follow them as drawn; the local launcher's from its muzzle.</summary>
    private void HarpoonEndsFrame(double now)
    {
        var w = Constructs;
        for (var k = 0; k < w.ConnectorCount; k++)
        {
            ref var cn = ref w.Connectors[k];
            if (cn.A.Player != 0 && Players.TryGetValue((byte)(cn.A.Player - 1), out var a))
                cn.A.Local = a.WeaponDef.Harpoon ? TracerStart(a, a.WeaponDef, 0, now) : a.RenderEye;
            if (cn.B.Player != 0 && Players.TryGetValue((byte)(cn.B.Player - 1), out var b))
                cn.B.Local = b.RenderEye - new Vector3(0, 0.4f, 0);
        }
    }

    /// <summary>
    /// The movement step's end, on the server and in the client's prediction: a player past a line's length plus the give is
    /// drawn back along it, as a velocity so the mover keeps them out of walls, and staggered so the ground's grip doesn't undo
    /// it. Riders are left alone (the frame they stand in moves them): a line held from a moving deck tears when it runs out.
    /// </summary>
    private void TetherPlayer(byte id, ref MoveState s)
    {
        if (s.RideBody != 0 || s.RideUp != Vector3.Zero || s.Noclip || s.MantleTicks > 0)
            return;
        var w = Constructs;
        var me = id + 1;
        var chest = GravityFrames.Above(s.Position, World.HeightOf(s.Stance) * 0.65f);
        for (var k = 0; k < w.ConnectorCount; k++)
        {
            ref var cn = ref w.Connectors[k];
            if (cn.A.Player != me && cn.B.Player != me)
                continue;
            if (!w.EndPoint(cn.A.Player == me ? cn.B : cn.A, out var other))
                continue;
            var to = other - chest;
            var dist = to.Length();
            var past = dist - cn.Length - ConstructWorld.PlayerEndGive;
            if (past <= 0 || dist < 1e-3f)
                continue;
            var along = to / dist;
            var want = MathF.Min(past * TetherPullRate, TetherPullMax);
            var speed = Vector3.Dot(s.Velocity, along);
            if (speed >= want)
                continue;
            s.Velocity += along * (want - speed);
            s.KnockTicks = Math.Max(s.KnockTicks, (byte)6);
            if (along.Y > 0.35f)
                s.Grounded = false;
        }
    }

    /// <summary>Client, each frame with the trigger: with a line out the hand harpoon's trigger reels it in and fires nothing. The pull that fired it reels nothing until it is let go.</summary>
    private bool HarpoonTrigger(bool held)
    {
        if (!SandboxOn || !Active.Def.Harpoon)
        {
            HarpoonReel(false);
            return false;
        }
        if (Constructs.LineHeldBy(LocalId) < 0)
        {
            HarpoonReel(false);
            _hpFiredStroke = held;
            return false;
        }
        _hpFiredStroke &= held;
        HarpoonReel(held && !_hpFiredStroke);
        return true;
    }

    private void HarpoonReel(bool on)
    {
        if (on == _hpReel)
            return;
        _hpReel = on;
        SandboxSendHarpoon(on ? (byte)1 : (byte)2);
    }

    /// <summary>Client: R with a line out cuts it. True when it did (the reload waits).</summary>
    public bool HarpoonCut()
    {
        if (!SandboxOn || !Active.Def.Harpoon || Constructs.LineHeldBy(LocalId) < 0)
            return false;
        SandboxSendHarpoon(0);
        return true;
    }

    /// <summary>Client: F with a head stuck in you tears it out. True when it did.</summary>
    public bool HarpoonTearOut()
    {
        if (Constructs.PlayerEndCount == 0 || Constructs.LineIn(LocalId) < 0)
            return false;
        SandboxSendHarpoon(3);
        return true;
    }

    private void SandboxSendHarpoon(byte op)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Harpoon).U8(op);
        SandboxSendBuilt(w);
    }

    /// <summary>Server: a player works a line. Null, or why not.</summary>
    private string? SandboxHarpoonAct(Player p, byte op)
    {
        var w = Constructs;
        if (op == 3)
        {
            var stuck = w.LineIn(p.Id);
            if (stuck < 0)
                return null;
            w.CutLine(stuck);
            if (p.Health > 1)
                DamagePlayer(p, p, Math.Min(HarpoonTearDamage, p.Health - 1), false, p.Weapon, SandboxNow, DamageKind.Bullet);
            return null;
        }
        var k = w.LineHeldBy(p.Id);
        if (k < 0)
            return null;
        if (op == 0)
            w.CutLine(k);
        else
            w.Connectors[k].Reeling = op == 1;
        return null;
    }

    /// <summary>The line about a harpoon for the HUD ("" for none): a head stuck in you, your line, or a gun's in your seat's hands. Made once per bind change.</summary>
    public string HarpoonHudLine(Player me)
    {
        var w = Constructs;
        if (w.ConnectorCount == 0)
            return "";
        if (_hpLineStamp != SandboxKeys.Stamp || _hpHeld.Length == 0)
        {
            _hpLineStamp = SandboxKeys.Stamp;
            _hpHeld = SandboxKeys.Line("Harpoon", "line out", null, $"{SandboxKeys.Fire}: reel in - {SandboxKeys.Reload}: cut the line");
            _hpIn = SandboxKeys.Line("Harpooned", "held by the line", null, $"{SandboxKeys.Use}: tear it out");
            _hpSeat = SandboxKeys.Line("Harpoon", "line out", null, $"Fire: reel in - {SandboxKeys.Reload}: cut the line");
        }
        if (w.PlayerEndCount > 0 && w.LineIn(me.Id) >= 0)
            return _hpIn;
        if (w.PlayerEndCount > 0 && Active.Def.Harpoon && w.LineHeldBy(me.Id) >= 0)
            return _hpHeld;
        ref var h = ref SandboxHands.Hand(me.Id);
        if (h.Seated && w.Get(h.SeatBody) is { GunCount: > 0 } c)
            for (var k = 0; k < c.GunCount; k++)
                if ((c.Guns[k].Effects & GunEffect.Harpoon) != 0 && c.Guns[k].Seat == h.SeatPart && GunLineOut(w, c.Id, c.Guns[k].Part))
                    return _hpSeat;
        return "";
    }

    /// <summary>A client has no <see cref="CConnector.Line"/>: a rope from this gun's part is its line.</summary>
    private static bool GunLineOut(ConstructWorld w, ushort body, ushort part)
    {
        for (var k = 0; k < w.ConnectorCount; k++)
            if (w.Connectors[k].A.Body == body && w.Connectors[k].A.Part == part)
                return true;
        return false;
    }
}
