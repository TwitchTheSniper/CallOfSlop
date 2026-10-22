using System.Numerics;

namespace CallOfSlop;

/// <summary>What a bot's sight line runs into (Game.SightLine).</summary>
public enum BotSightLine : byte
{
    Clear, // nothing in the way: it sees
    Blocked, // a wall, a box, the terrain or smoke
    Covered, // only cover that hides but doesn't stop bullets: foliage, a cornfield (Map.Concealed)
}

/// <summary>The blind-fire side of a bot's brain (see Game.BotBlindPlan).</summary>
public sealed partial class BotBrain
{
    /// <summary>The target's velocity when it was last seen (to guess where it went on from <see cref="LastSeenAt"/>).</summary>
    public Vector3 LastSeenVel;

    /// <summary>This tick: the bot is firing blind into cover at <see cref="BlindAim"/> (BotAim turns there).</summary>
    public bool BlindOn;

    /// <summary>Where the current blind burst goes: the estimate plus its error. Never the target's real position.</summary>
    public Vector3 BlindAim;

    /// <summary>The estimate the current burst was picked around (feet), and how far off it may be (metres).</summary>
    public Vector3 BlindAt;

    public float BlindErr;
    public int BlindBurst; // rounds left in this burst
    public double BlindNextBurst;
    internal double BlindLineAt = -100; // when the line to the estimate was last checked
    internal Vector3 BlindLineFor;
    internal BotSightLine BlindLine;

    /// <summary>Self-test: blind shots this bot has fired, and the point the last one was aimed at.</summary>
    public int BlindShots;

    public Vector3 LastBlindAim;
}

/// <summary>
/// Bots and foliage. A bot can't see through foliage or a cornfield (Map.Concealed: grass, bushes, shrubs, tree canopies,
/// concealing props), but bullets go through it, so when it has fresh information that someone is in there (it saw them
/// go in, heard them shoot or run, or a squadmate called the spot) it may fire blind into the cover at its estimate:
/// short, slower bursts with a cone several times its normal spread, around a guess that gets worse the longer it's been
/// since it last knew anything. Blind fire never reads the target's real position, never aims for the head, and stops a
/// few seconds after the information goes stale. Human players aren't affected (they see the plants on screen).
/// </summary>
public sealed partial class Game
{
    /// <summary>Blind fire stops this long after the bot last saw, heard or was told anything (seconds).</summary>
    public const double BlindMemory = 3.0;

    /// <summary>How much wider a blind shot's cone is than the bot's normal one.</summary>
    public const float BlindSpreadMult = 5f;

    /// <summary>The narrowest cone a blind shot gets (radians), so a sniper rifle's tiny spread doesn't make it a laser.</summary>
    public const float BlindMinSpread = 0.035f;

    /// <summary>How fast the estimate's error grows while the bot learns nothing new (metres per second: about a walk).</summary>
    public const float BlindErrGrowth = 1.5f;

    /// <summary>Blind fire runs at this fraction of the gun's fire rate (an interval this many times longer).</summary>
    public const float BlindIntervalMult = 1.8f;

    /// <summary>A hidden enemy running (faster than this, m/s) within <see cref="FootstepHearRange"/> gives its spot away, roughly.</summary>
    public const float FootstepSpeed = 3.5f;

    public const float FootstepHearRange = 14f;

    /// <summary>What's on the line from <paramref name="from"/> to <paramref name="to"/> for a bot's eyes (no allocation).</summary>
    private BotSightLine SightLine(Vector3 from, Vector3 to)
    {
        var d = to - from;
        var dist = d.Length();
        if (dist < 0.01f)
            return BotSightLine.Clear;
        if (World.Query.Raycast(from, d / dist, dist, QueryFilter.All) < dist - 0.05f)
            return BotSightLine.Blocked;
        if (SmokeBlocks(from, to, Brutal.GlfwApi.Glfw.Time)) // bots can't see through smoke either
            return BotSightLine.Blocked;
        return World.Map.Concealed(from, to) ? BotSightLine.Covered : BotSightLine.Clear; // ... or foliage and cornfields (MapConcealment.cs)
    }

    /// <summary>
    /// Can a bot with its eyes at <paramref name="eye"/> see <paramref name="o"/>? It looks at the chest; when only cover
    /// hides that (a standing player in knee-high grass, a crouched one behind a bush), it looks at the head too, so a head
    /// above the grass is seen while someone lying in it isn't.
    /// </summary>
    private BotSightLine BotSees(Vector3 eye, Player o)
    {
        var r = SightLine(eye, o.Chest);
        if (r != BotSightLine.Covered)
            return r;
        return SightLine(eye, o.Eye + new Vector3(0, 0.05f, 0)) == BotSightLine.Clear ? BotSightLine.Clear : BotSightLine.Covered;
    }

    /// <summary>
    /// A scan found <paramref name="o"/> hidden by cover only: running gives it away (footsteps, rustling), but only
    /// roughly: the heard spot is off by a metre or more. Dead Silence keeps it quiet. (Gunfire is heard in BotPerceive.)
    /// </summary>
    private void BotNoticeHidden(BotBrain b, Player o, float dist, double now)
    {
        var v = o.Sim.Velocity;
        if (v.X * v.X + v.Z * v.Z < FootstepSpeed * FootstepSpeed || dist > FootstepHearRange || o.Perks.Has(PerkId.DeadSilence) || now - b.HeardTime < 0.5)
            return;
        b.HeardAt = o.Position + Noise(1f + 0.08f * dist);
        b.HeardTime = now;
        BotStats.FootstepCues++;
    }

    /// <summary>
    /// Before BotAim: should this bot fire blind into cover, and where? It takes its newest information (the spot where
    /// it last saw the target, carried on a little along the way it was going; a heard shot or footsteps; a squadmate's
    /// sighting on the blackboard), and fires only when that spot is hidden from it by cover alone (if it could see the
    /// spot, it would see who's there; behind a wall there's nothing to shoot at).
    /// </summary>
    private void BotBlindPlan(Player p, BotBrain b, bool visible, WeaponDef def, double now)
    {
        b.BlindOn = false;
        if (visible || b.HoldFire || !DamageEnabled || def.IsLauncher || (b.Mode == BotMode.Squad && b.Order.Kind == SquadOrderKind.Suppress))
        {
            b.BlindBurst = 0;
            return;
        }
        // The newest thing it knows.
        var info = b.LastSeenTime;
        var at = b.LastSeenAt + new Vector3(b.LastSeenVel.X, 0, b.LastSeenVel.Z) * (float)Math.Clamp(now - b.LastSeenTime, 0, 0.8);
        var err = 0.4f;
        if (b.HeardTime > info)
        {
            info = b.HeardTime;
            at = b.HeardAt;
            err = 1f + 0.06f * Flat(b.HeardAt - p.Position); // a sound is harder to place than a sighting
        }
        if (b.Squad is { } s)
        {
            for (var i = 0; i < s.Board.Count; i++)
            {
                var c = s.Board[i];
                if (c.Seen && c.Time > info && c.Reporter != p.Id)
                {
                    info = c.Time;
                    at = c.Pos;
                    err = 0.6f; // a squadmate's call: close, not exact
                }
            }
        }
        var age = now - info;
        if (age > BlindMemory)
        {
            b.BlindBurst = 0;
            return;
        }
        var dist = Flat(at - p.Position);
        if (dist < Map.ConcealSeeRange + 0.5f || dist > MathF.Min(def.RangeM * 2.5f, 60f))
            return;
        if (now - b.BlindLineAt > 0.25 || Vector3.DistanceSquared(b.BlindLineFor, at) > 1f)
        {
            b.BlindLineAt = now;
            b.BlindLineFor = at;
            b.BlindLine = SightLine(p.Eye, at + new Vector3(0, 0.35f, 0)); // low: if it could see this, it'd see someone lying there
        }
        if (b.BlindLine != BotSightLine.Covered)
        {
            b.BlindBurst = 0;
            return;
        }
        b.BlindOn = true;
        if (b.BlindBurst <= 0 && now >= b.BlindNextBurst)
        {
            // A new burst: somewhere around the estimate, at body height (never a deliberate head shot).
            var r = err + BlindErrGrowth * (float)age;
            var a = (float)_rng.NextDouble() * MathF.Tau;
            var off = MathF.Sqrt((float)_rng.NextDouble()) * r;
            b.BlindAt = at;
            b.BlindErr = r;
            b.BlindAim = at + new Vector3(MathF.Cos(a) * off, 0.25f + (float)_rng.NextDouble() * 0.85f, MathF.Sin(a) * off);
            b.BlindBurst = 2 + _rng.Next(3);
            BotStats.BlindBursts++;
        }
    }

    /// <summary>After BotShoot: one blind shot when the burst is on, the view has come round to it and the gun is ready.</summary>
    private void BotBlindFire(Player p, BotBrain b, WeaponDef def, in BotView aim, double now)
    {
        if (!b.BlindOn || b.BlindBurst <= 0 || now < b.NextFire || b.Ammo <= 0 || b.ReloadUntil != 0)
            return;
        var d = b.BlindAim - p.Eye;
        var dist = d.Length();
        if (dist < 1f)
            return;
        var wantYaw = MathF.Atan2(d.X, -d.Z);
        var wantPitch = MathF.Asin(Math.Clamp(d.Y / dist, -1f, 1f));
        if (MathF.Abs(World.WrapAngle(wantYaw - aim.Yaw)) > 0.1f || MathF.Abs(wantPitch - aim.Pitch) > 0.1f)
            return;
        var spread = MathF.Max(BotShotSpread(p, def, false, b.Skill) * BlindSpreadMult, BlindMinSpread);
        ServerResolveShot(p, def, p.Eye, World.Forward(aim.Yaw, aim.Pitch), spread, (uint)_rng.Next(), ServerTick, now);
        b.Ammo--;
        b.BlindBurst--;
        b.BlindShots++;
        b.LastBlindAim = b.BlindAim;
        BotStats.BlindShots++;
        b.NextFire = now + def.FireInterval * BlindIntervalMult * ZombieFireIntervalMult(p) + (def.Mode == FireMode.Semi ? b.TriggerDelay * 2 : 0);
        if (b.BlindBurst == 0)
            b.BlindNextBurst = now + 0.5 + _rng.NextDouble() * 0.6;
    }

    /// <summary>
    /// Insane suppressive fire into cover (SquadMember.cs): the spot is a squadmate's call, so the shot isn't aimed either:
    /// the blind cone, around a spot off by the call's error (growing with its age).
    /// </summary>
    private Vector3 BlindSuppressDir(Player p, BotBrain b, WeaponDef def, in BotView aim, float dist, double now, out float spread)
    {
        spread = MathF.Max(BotShotSpread(p, def, aim.Ads, b.Skill) * BlindSpreadMult, BlindMinSpread);
        var age = b.Squad != null && b.Squad.Board.TryGet(b.Squad.Target, out var c) ? (float)(now - c.Time) : 1f;
        var r = 0.6f + BlindErrGrowth * MathF.Max(0f, age);
        var ang = MathF.Atan2(r, MathF.Max(dist, 1f));
        var yaw = aim.Yaw + ((float)_rng.NextDouble() - 0.5f) * 2f * ang;
        var pitch = aim.Pitch + ((float)_rng.NextDouble() - 0.5f) * ang;
        BotStats.BlindShots++;
        b.BlindShots++;
        return World.Forward(yaw, pitch);
    }

    /// <summary>Self-test: what's on the line from one point to another for a bot (<see cref="SightLine"/>).</summary>
    internal BotSightLine SelfTestSightLine(Vector3 from, Vector3 to) => SightLine(from, to);

    /// <summary>Self-test: can bot <paramref name="bot"/> see <paramref name="o"/> right now (<see cref="BotSees"/>)?</summary>
    internal BotSightLine SelfTestBotSees(Player bot, Player o) => BotSees(bot.Eye, o);
}
