using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One Insane bot following its squad's orders: reporting what it sees and hears to the blackboard, deciding when the
/// order beats its own instincts, steering for the order, suppressive fire, and the squad's part of route searches.
/// Everything still goes through the normal bot pipeline (BotTick) and the same input commands as a person.
/// </summary>
public sealed partial class Game
{
    /// <summary>After BotPerceive: per-life setup, and what this member perceives goes on the squad's blackboard.</summary>
    private void SquadPerceive(Player p, BotBrain b, Player? target, bool visible, double now)
    {
        if (b.Squad is not { } s)
            return;
        if (b.SquadLifeTick != p.SpawnTick)
        {
            b.SquadLifeTick = p.SpawnTick;
            b.Order = default;
            b.HoldFire = false;
            if (b.Role == SquadRole.Support && p.Grenade != ThrowableKind.None)
                p.Grenade = ThrowableKind.Smoke; // supports carry smoke to cover a fall-back
            s.NextPlan = Math.Min(s.NextPlan, now + 0.05);
        }
        if (visible && target != null)
        {
            if (!s.Board.TryGet(target.Id, out var known) || now - known.Time > 2)
                s.NextPlan = Math.Min(s.NextPlan, now); // a new contact: plan at once (next tick)
            s.Board.Report(target.Id, target.Position, target.Yaw, now, p.Id, seen: true);
        }
        else if (b.HeardTime == now)
            s.Board.Report(byte.MaxValue, b.HeardAt, 0, now, p.Id, seen: false);
    }

    /// <summary>A flanker holds its fire until it's at least this far round (degrees off the suppressor's angle), unless hit or right on top of them.</summary>
    public const float FlankFireAngle = 80f;

    /// <summary>
    /// Start of BotDecide: true when the squad order takes over this tick (mode <see cref="BotMode.Squad"/>). With an
    /// enemy in sight the bot fights normally (bases of fire keep their distance), except a flanker that sees the
    /// assault's target from a bad angle while the target isn't fighting it (not hit, not being shot at from the front):
    /// it holds fire and keeps going round.
    /// </summary>
    private bool SquadDecide(Player p, BotBrain b, Player? target, bool visible, double now)
    {
        b.HoldFire = false;
        if (b.Squad is not { } s || b.Order.Kind == SquadOrderKind.None)
            return false;
        if (visible)
        {
            if (target != null && (b.Role == SquadRole.Flanker || PincerSupport(b))
                && (s.Tactic != SquadTactic.Assault || (target.Id == s.Target && b.Order.Kind is SquadOrderKind.Flank or SquadOrderKind.Hold)))
            {
                var dist = Flat(target.Position - p.Position);
                var hurt = now - b.HurtTime < 1.5;
                // Shooting this way (its muzzle flashes facing us): it's fighting us, fight back.
                var fightingMe = now - target.LastShotTime < 0.6
                                 && Vector3.Dot(World.FlatForward(target.Yaw), FlatDir(p.Position - target.Position, Vector3.UnitX)) > 0.9f;
                if (!hurt && !fightingMe && dist > 6f && FlankAngle(s, target.Position, p.Position) < FlankFireAngle)
                {
                    b.HoldFire = true;
                    if (s.Tactic != SquadTactic.Assault)
                        s.NextPlan = Math.Min(s.NextPlan, now);
                    b.Mode = BotMode.Squad;
                    SquadStats.HeldFireTicks++;
                    return true;
                }
            }
            return false;
        }
        if (b.Mode == BotMode.Cover && now < b.CoverUntil)
            return false;
        b.Mode = BotMode.Squad;
        return true;
    }

    /// <summary>
    /// Degrees between the directions from <paramref name="target"/> to the squad's base of fire and to
    /// <paramref name="from"/>: 0 = same side as the suppressor, 180 = straight behind the target from its view of the suppressor.
    /// </summary>
    public float FlankAngle(BotSquad s, Vector3 target, Vector3 from)
    {
        Player? baseOfFire = null;
        foreach (var m in s.Members)
            if (m.Alive && m.Bot!.Role == SquadRole.Suppressor)
                baseOfFire = m;
        baseOfFire ??= s.Acting is { Alive: true } a && a.Bot!.Role != SquadRole.Flanker ? a : null;
        if (baseOfFire == null)
            return 180f;
        var u = FlatDir(baseOfFire.Position - target, Vector3.UnitX);
        var v = FlatDir(from - target, Vector3.UnitX);
        return MathF.Acos(Math.Clamp(Vector3.Dot(u, v), -1f, 1f)) * 180f / MathF.PI;
    }

    /// <summary>BotSteer for <see cref="BotMode.Squad"/>: walk the order's route, hold, or stop where the suppression spot is in view.</summary>
    private void SquadSteer(Player p, BotBrain b, NavGraph nav, double now, ref BotIntent s)
    {
        var o = b.Order;
        b.FlankWidth = 0;
        if (o.FlankRoute)
        {
            b.FlankFrom = o.FlankFrom;
            b.FlankTo = o.FlankTo;
            b.FlankWidth = 12f;
        }
        var far = Flat(o.Goal - p.Position);
        switch (o.Kind)
        {
            case SquadOrderKind.Hold:
                if (far > 1.5f)
                    s.Wish = BotFollow(p, b, nav, o.Goal, now, ref s);
                else
                {
                    s.Stationary = true;
                    b.CrouchUntil = now + 0.2; // waiting low
                }
                break;
            case SquadOrderKind.Suppress:
                if (now - b.SuppressLosAt > 0.25)
                {
                    // A line to the spot at chest height, or else just over it (fire over the cover they ducked behind).
                    b.SuppressLosAt = now;
                    var inRange = Flat(o.Look - p.Position) < 45f;
                    var over = o.Look + new Vector3(0, 0.5f, 0);
                    b.SuppressAim = o.Look;
                    // Foliage or corn in the way doesn't stop the bullets: that's blind fire, wide (SuppressBlind, BotFoliage.cs).
                    var line = inRange ? SightLine(p.Eye, o.Look) : BotSightLine.Blocked;
                    b.SuppressLos = line != BotSightLine.Blocked;
                    b.SuppressBlind = line == BotSightLine.Covered;
                    if (inRange && line != BotSightLine.Clear)
                    {
                        var overLine = SightLine(p.Eye, over);
                        if (overLine == BotSightLine.Clear || (overLine == BotSightLine.Covered && !b.SuppressLos))
                        {
                            b.SuppressLos = true;
                            b.SuppressBlind = overLine == BotSightLine.Covered;
                            b.SuppressAim = over;
                        }
                    }
                    if (inRange && !b.SuppressLos && BotSuppressThroughWall(p, o))
                        b.SuppressLos = true; // a fresh last known spot behind a wall their gun goes through (GamePenetration.cs)
                }
                if (b.SuppressLos)
                    s.Stationary = true;
                else
                {
                    // Not in view of the spot yet: to the firing spot, or (there already) edge towards the spot itself.
                    var g = far > 1.2f ? o.Goal : o.Look - new Vector3(0, 1.1f, 0);
                    s.Wish = BotFollow(p, b, nav, g, now, ref s);
                    s.Sprint = Flat(g - p.Position) > 8f;
                }
                break;
            default:
                s.Wish = BotFollow(p, b, nav, o.Goal, now, ref s);
                s.Stationary = s.Wish == Vector3.Zero;
                s.Sprint = o.Sprint && far > 6f;
                break;
        }
        if (o.HasLook && !s.Mantle)
        {
            s.HasLook = true;
            s.Look = o.Kind == SquadOrderKind.Suppress && b.SuppressLos ? b.SuppressAim : o.Look;
        }
    }

    /// <summary>
    /// After BotShoot: a base of fire with a Suppress order puts rounds on the target's last known spot without seeing
    /// anyone (real shots, about a third of the gun's rate, a little wider), keeping a quarter of the magazine.
    /// </summary>
    private void SquadSuppress(Player p, BotBrain b, bool visible, WeaponDef def, in BotView aim, double now)
    {
        var o = b.Order;
        if (visible || b.Squad == null || b.Mode != BotMode.Squad || o.Kind != SquadOrderKind.Suppress)
            return;
        SquadStats.SuppressTicks++;
        if (!o.Fire)
            return;
        SquadStats.SuppressFireTicks++;
        if (!b.SuppressLos)
            return;
        SquadStats.SuppressLosTicks++;
        if (b.ReloadUntil != 0 || b.Ammo <= Math.Max(1, def.Mag / 4) || now < b.NextFire || !DamageEnabled)
            return;
        var d = b.SuppressAim - p.Eye;
        var dist = d.Length();
        if (dist < 1f || (dist > def.RangeM * 2.5f && def.Class != WeaponClass.Sniper))
            return;
        var wantYaw = MathF.Atan2(d.X, -d.Z);
        var wantPitch = MathF.Asin(Math.Clamp(d.Y / dist, -1f, 1f));
        if (MathF.Abs(World.WrapAngle(wantYaw - aim.Yaw)) > 0.08f || MathF.Abs(wantPitch - aim.Pitch) > 0.08f)
            return;
        var spread = def.HipSpreadRad * (aim.Ads ? def.AdsSpreadMult : 1f) + 0.025f;
        var shotDir = b.SuppressBlind ? BlindSuppressDir(p, b, def, aim, dist, now, out spread) : World.Forward(aim.Yaw, aim.Pitch); // into cover: not aimed
        ServerResolveShot(p, def, p.Eye, shotDir, spread, (uint)_rng.Next(), ServerTick, now);
        b.Ammo--;
        b.NextFire = now + def.FireInterval * 2.5 + (def.Mode == FireMode.Semi ? 0.15 : 0) + _rng.NextDouble() * 0.1;
        SquadStats.SuppressShots++;
    }

    /// <summary>Just before a member's A* search: ray budget for the exposure check, and whether it applies (assaults only).</summary>
    private static void SquadBeforeSearch(BotBrain b)
    {
        var s = b.Squad!;
        s.ExposureRays = SquadRaysPerSearch;
        s.ExposureOn = s.Tactic == SquadTactic.Assault;
    }

    private const int SquadRaysPerSearch = 48;

    /// <summary>Just after: the route's nodes are claimed for this member (the others' assault routes avoid them a little).</summary>
    private void SquadAfterSearch(BotBrain b)
    {
        var s = b.Squad!;
        SquadStats.ExposureRays += SquadRaysPerSearch - s.ExposureRays;
        if (s.ScratchGraph != b.SearchGraph || !s.ExposureOn)
            return;
        for (var i = 0; i < b.PathLen; i++)
        {
            var n = b.Path[i];
            s.ClaimStamp[n] = s.ClaimGen;
            s.ClaimBy[n] = b.SquadSlot;
        }
    }
}
