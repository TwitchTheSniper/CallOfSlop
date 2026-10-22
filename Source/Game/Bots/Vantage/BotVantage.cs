using System.Numerics;

namespace CallOfSlop;

/// <summary>Where a long-range bot is with its vantage point (BotVantage.cs).</summary>
public enum VantageState : byte
{
    None,
    Travel, // on the way to the spot (holding fire at long range unless threatened)
    Hold, // at the spot: scanning, peeking, shooting
}

/// <summary>The vantage side of a bot's brain (BotVantage.cs).</summary>
public sealed partial class BotBrain
{
    public VantageState VState;

    /// <summary>The spot being travelled to or held (index into <see cref="VantageMap.Spots"/> of <see cref="VantageOf"/>), or -1.</summary>
    public int VantageSpot = -1;

    public VantageMap? VantageOf;

    /// <summary>Rolled each life from personality, gun and difficulty: does this bot look for vantage points at all.</summary>
    public bool VantageMinded;

    /// <summary>Self-test: always vantage-minded with a long gun (skips the per-life roll).</summary>
    public bool ForceVantage;

    /// <summary>
    /// True while the bot deliberately stands still at a spot: holding a vantage point (also while shooting from it), a
    /// marksman's or anchor's hold, or a squad Hold order. Stuck detection must leave such a bot alone. Set every tick in BotSteer.
    /// </summary>
    public bool IsHoldingPosition { get; internal set; }

    internal uint VantageLife = uint.MaxValue;
    internal double VantageSince, VantageHoldUntil, VantageNextPick;
    internal int VantageKills; // kills since the hold began
    internal int VantageLast = -1; // the spot just left (not picked again for a while)
    internal double VantageLastUntil;
    internal double VantageNextLook;
    internal float VantageLookYaw;
    internal bool VantagePeek; // standing up to look (else ducked behind the spot's cover)
    internal double VantagePeekUntil;
    internal double VantageStanceAt = -100; // last crouch/prone line-of-sight check while shooting from the spot
    internal byte VantageStance; // 0 stand, 1 crouch, 2 prone
    internal uint VantageRolls; // counter for this bot's own deterministic rolls

    // ---- Sidearm ----
    internal bool OnSidearm;
    internal uint SidearmLife = uint.MaxValue;
    internal int PrimaryAmmo, SidearmAmmo = -1;
    internal double SidearmSince, SidearmQuietSince;
}

/// <summary>
/// Long-range bots and vantage points. A bot with a sniper rifle or a DMR (an LMG partly; a launcher only while hunting an
/// enemy aircraft) that rolled "vantage-minded" this life (marksmen nearly always, rushers sometimes, Easy bots rarely)
/// heads for a precomputed vantage point (<see cref="VantageMap"/>) overlooking the action (the hill, the flag it's after,
/// the last contact, the nearest enemy) that no teammate holds and that wasn't compromised lately. On the way it holds its
/// fire at long range unless it's shot at or the enemy is close. At the spot it holds: ducks behind the spot's cover and
/// stands to peek, scans the spot's sectors, shoots from there (crouched or prone when that still sees the target). It
/// relocates after a couple of kills, when shot from a flank (the spot is marked compromised), when the area goes quiet, or
/// when its time is up; with an enemy inside 8 m it gives the spot up and fights close, a bolt-action shooter switching to its
/// sidearm. Insane squads give their long-range member an overwatch spot instead (SquadOverwatch). Zombies co-op bots never
/// get here (the mode runs its own bots).
/// </summary>
public sealed partial class Game
{
    /// <summary>An enemy this close (metres) makes a long-range bot give up its spot and fight up close (sidearm for bolt actions).</summary>
    public const float VantageCloseRange = 8f;

    /// <summary>Spot picks allowed per server tick, shared by all bots (each pick scores every spot and casts up to 4 rays).</summary>
    private const int VantagePicksPerTick = 1;

    private int _vantagePickBudget;
    private uint _vantageBudgetTick = uint.MaxValue;
    private VantageMap? _vantageBadOf;
    private double[] _vantageBad = []; // per spot: compromised until then

    /// <summary>Statistics for the self-test.</summary>
    public int VantagePicks, VantageRelocations, VantageArrivals;

    /// <summary>The band a bot plays with its current gun, or null when vantage logic doesn't apply to it.</summary>
    private VantageBand? VantageBandFor(Player p, BotBrain b, out Vector3? airTarget)
    {
        airTarget = null;
        var def = p.DefFor(p.Primary);
        switch (def.Class)
        {
            case WeaponClass.Sniper:
                return VantageBand.Long;
            case WeaponClass.DMR:
                return VantageBand.MediumLong;
            case WeaponClass.LMG:
                return VantageBand.Medium;
        }
        // A launcher: only while there's an enemy aircraft to hunt (somewhere with open sky over the action).
        if (!p.DefFor(p.Secondary).IsLauncher)
            return null;
        foreach (var e in StreakEntities.Values)
            if (StreakMachineHostile(e, p) && LauncherTargets.IsAirborne(e))
            {
                airTarget = e.Pos;
                return VantageBand.Medium;
            }
        return null;
    }

    /// <summary>How much the bot likes vantage play this life (0..1): personality, gun and difficulty.</summary>
    private static float VantagePreference(BotBrain b, Player p)
    {
        var personality = b.Personality switch
        {
            BotPersonality.Marksman => 0.95f,
            BotPersonality.Anchor => 0.6f,
            BotPersonality.Tactician => 0.55f,
            BotPersonality.Rusher => 0.45f, // a rusher with a sniper still wants a spot (an aggressive one, close to the action)
            BotPersonality.Flanker => 0.25f,
            _ => 0.35f,
        };
        var gun = p.DefFor(p.Primary).Class switch
        {
            WeaponClass.Sniper => 1f,
            WeaponClass.DMR => 0.85f,
            WeaponClass.LMG => 0.45f,
            _ => 0.6f, // launcher hunting
        };
        var skill = b.Skill switch { BotDifficulty.Easy => 0.15f, BotDifficulty.Normal => 0.75f, _ => 1f };
        return personality * gun * skill;
    }

    /// <summary>A per-bot roll in [0, 1) that leaves the match's shared random sequence alone.</summary>
    private static double VantageRoll(Player p, BotBrain b, uint salt) => TacticalGear.Roll((uint)b.Seed ^ (uint)p.Id, p.SpawnTick + ++b.VantageRolls * 7919u, salt);

    private bool TakeVantagePick()
    {
        if (_vantageBudgetTick != ServerTick)
        {
            _vantageBudgetTick = ServerTick;
            _vantagePickBudget = VantagePicksPerTick;
        }
        if (_vantagePickBudget <= 0)
            return false;
        _vantagePickBudget--;
        return true;
    }

    private double[] VantageBad(VantageMap vm)
    {
        if (!ReferenceEquals(_vantageBadOf, vm))
        {
            _vantageBadOf = vm;
            _vantageBad = new double[vm.Spots.Length]; // once per map
        }
        return _vantageBad;
    }

    private void VantageRelease(BotBrain b, double now, bool compromised)
    {
        if (b.VantageSpot >= 0 && b.VantageOf != null)
        {
            if (compromised)
                VantageBad(b.VantageOf)[b.VantageSpot] = now + 40;
            b.VantageLast = b.VantageSpot;
            b.VantageLastUntil = now + 30;
        }
        b.VState = VantageState.None;
        b.VantageSpot = -1;
        if (b.Mode == BotMode.Overwatch)
            b.Mode = BotMode.Roam;
    }

    /// <summary>
    /// BotDecide, after the squad: true when vantage play takes over this tick (mode <see cref="BotMode.Overwatch"/>).
    /// False leaves the normal decisions to run (fighting up close, cover, chasing), with the spot remembered where it makes sense.
    /// </summary>
    private bool VantageDecide(Player p, BotBrain b, Player? target, bool visible, double now)
    {
        if (b.Squad != null)
            return SquadOverwatchDecide(p, b, target, visible, now);
        if (!DamageEnabled)
            return false; // the countdown (frozen) or the results: nothing to set up yet
        if (Module.CoOp || b.Dummy || b.ForcedGoal != null)
        {
            if (b.VState != VantageState.None)
                VantageRelease(b, now, false);
            return false; // (self-tests walking a bot somewhere: no detours)
        }
        var band = VantageBandFor(p, b, out var airTarget);
        if (b.VantageLife != p.SpawnTick)
        {
            b.VantageLife = p.SpawnTick;
            b.VState = VantageState.None;
            b.VantageSpot = -1;
            b.VantageNextPick = now;
            b.VantageMinded = b.ForceVantage || VantageRoll(p, b, 0x7A0) < VantagePreference(b, p);
        }
        var nav = b.PathGraph;
        var vm = nav != null ? VantageMap.For(nav) : null;
        var objectiveFirst = b.Objective != null && b.ObjectiveMinded && b.Personality != BotPersonality.Marksman && !b.ForceVantage;
        if (band is not { } bnd || !b.VantageMinded || vm == null || vm.Spots.Length == 0 || objectiveFirst || CarriedFlagOf(p) >= 0)
        {
            if (b.VState != VantageState.None)
                VantageRelease(b, now, false);
            return false;
        }
        if (!ReferenceEquals(b.VantageOf, vm))
        {
            b.VantageOf = vm;
            b.VState = VantageState.None;
            b.VantageSpot = -1;
            b.VantageLast = -1;
        }
        if (b.Mode == BotMode.Cover && now < b.CoverUntil)
            return false;

        var dist = target != null ? Flat(target.Position - p.Position) : float.MaxValue;
        var hurtNow = b.HurtTime == now;
        // Up close: the spot is no good any more, fight (the sidearm comes out in BotSidearm).
        if (visible && dist < VantageCloseRange)
        {
            if (b.VState != VantageState.None)
                VantageRelease(b, now, b.VState == VantageState.Hold);
            return false;
        }
        if (visible && p.Health <= b.Profile.RetreatHealth)
        {
            if (b.VState == VantageState.Hold)
                VantageRelease(b, now, true); // they've found us: get out
            return false;
        }

        if (b.VState == VantageState.Hold)
        {
            var spot = vm.Spots[b.VantageSpot];
            var killsToMove = b.Personality is BotPersonality.Marksman or BotPersonality.Rusher ? 2 : 3;
            var quietFor = b.Personality switch { BotPersonality.Marksman => 18.0, BotPersonality.Rusher => 8.0, _ => 12.0 };
            var lastAction = Math.Max(Math.Max(b.LastSeenTime, b.HeardTime), b.VantageSince);
            // Shot from a direction the spot doesn't look over (a flank), or without seeing who: compromised, move.
            var flanked = hurtNow && (!visible || !SectorCovers(spot.Sectors, target!.Position - p.Position));
            var relocate = flanked || b.VantageKills >= killsToMove || (!visible && (now - lastAction > quietFor || now >= b.VantageHoldUntil));
            if (relocate)
            {
                var from = b.VantageSpot;
                var compromised = flanked || b.VantageKills >= killsToMove;
                VantageRelease(b, now, compromised);
                VantageRelocations++;
                b.VantageNextPick = now;
                // A new spot away from the shooter (where it was heard or seen), right away if this tick's pick is free.
                var threat = flanked ? (visible ? target!.Position : now - b.HeardTime < 2 ? b.HeardAt : p.Position) : (Vector3?)null;
                if (TakeVantagePick() && VantagePick(p, b, vm, bnd, VantageFocus(p, b, airTarget, now), threat, from, now) is { } next)
                {
                    VantageBegin(p, b, next, now);
                    b.Mode = BotMode.Overwatch;
                    return true;
                }
                return false;
            }
            b.Mode = BotMode.Overwatch;
            return true;
        }

        if (b.VState == VantageState.Travel)
        {
            var spot = vm.Spots[b.VantageSpot];
            var far = Flat(spot.Pos - p.Position);
            if (far < 1.2f && MathF.Abs(spot.Pos.Y - p.Position.Y) < 1.5f)
            {
                b.VState = VantageState.Hold;
                b.VantageSince = now;
                b.VantageKills = 0;
                b.VantageNextLook = now;
                b.VantagePeek = false;
                b.VantagePeekUntil = now + 1.5;
                var hold = b.Personality switch { BotPersonality.Marksman => 30.0, BotPersonality.Rusher => 12.0, _ => 20.0 };
                b.VantageHoldUntil = now + hold + VantageRoll(p, b, 0x7A1) * hold * 0.5;
                VantageArrivals++;
                b.Mode = BotMode.Overwatch;
                return true;
            }
            // Too long on the way (blocked, a long detour): try somewhere else.
            if (now - b.VantageSince > 20 + Flat(spot.Pos - p.Position) / 3)
            {
                VantageRelease(b, now, false);
                b.VantageNextPick = now + 1;
                return false;
            }
            if (visible)
            {
                // Moving in the open: don't start a long-range duel. Shot at, or targeted: fight properly now (the spot is
                // still the plan afterwards). Merely close: shoot on the move and keep going. Far: hold fire.
                if (now - b.HurtTime < 2.5 || ShootingAt(target!, p, now))
                    return false;
                b.HoldFire = dist >= VantageHoldFireRange(bnd);
            }
            b.Mode = BotMode.Overwatch;
            return true;
        }

        // No spot yet: pick one (rationed across all bots), unless already in a real fight. An enemy far off in sight
        // doesn't count: hold fire, find a spot first.
        if (visible && (now - b.HurtTime < 2.5 || dist < VantageHoldFireRange(bnd) || ShootingAt(target!, p, now)))
            return false;
        if (now < b.VantageNextPick)
        {
            b.HoldFire = visible && b.VantageNextPick - now < 1.5; // about to pick again: don't open up meanwhile
            return false; // (no spot to be had lately: play normally)
        }
        if (!TakeVantagePick())
        {
            b.HoldFire = visible; // this tick's pick went to another bot: wait a tick
            return false;
        }
        b.VantageNextPick = now + 3;
        if (VantagePick(p, b, vm, bnd, VantageFocus(p, b, airTarget, now), null, -1, now) is not { } pick)
        {
            b.VantageNextPick = now + 8;
            return false;
        }
        VantageBegin(p, b, pick, now);
        b.HoldFire = visible;
        b.Mode = BotMode.Overwatch;
        return true;
    }

    /// <summary>Beyond this range a travelling long-range bot holds its fire (unless shot at or targeted).</summary>
    private static float VantageHoldFireRange(VantageBand band) => band switch { VantageBand.Long => 28f, VantageBand.MediumLong => 22f, _ => 16f };

    /// <summary>Is <paramref name="shooter"/> firing roughly at <paramref name="at"/> right now?</summary>
    private static bool ShootingAt(Player shooter, Player at, double now) =>
        now - shooter.LastShotTime < 0.6 && Vector3.Dot(World.FlatForward(shooter.Yaw), FlatDir(at.Position - shooter.Position, Vector3.UnitX)) > 0.9f;

    private static bool SectorCovers(byte sectors, Vector3 dir)
    {
        if (sectors == 0)
            return false;
        var yaw = MathF.Atan2(dir.X, -dir.Z);
        var bin = ((int)MathF.Round(yaw / (MathF.Tau / 8)) % 8 + 8) % 8;
        // The sector itself or either neighbour (a sector is 45 degrees; a little slack either side).
        return (sectors & (1 << bin)) != 0 || (sectors & (1 << ((bin + 1) & 7))) != 0 || (sectors & (1 << ((bin + 7) & 7))) != 0;
    }

    private static void VantageBegin(Player p, BotBrain b, int spot, double now)
    {
        b.VantageSpot = spot;
        b.VState = VantageState.Travel;
        b.VantageSince = now;
        b.VantageKills = 0;
        b.ClearPath();
    }

    /// <summary>
    /// What the spot should overlook: the active hill, the bot's objective (a flag), an enemy aircraft to hunt, the last
    /// contact, else the nearest enemy (the action). Null: no preference.
    /// </summary>
    private Vector3? VantageFocus(Player p, BotBrain b, Vector3? airTarget, double now)
    {
        if (airTarget is { } air)
            return air;
        if (ActiveHill is { } hill)
            return hill.Center;
        if (b.Objective is { } goal)
            return goal;
        if (now - b.LastSeenTime < 20)
            return b.LastSeenAt;
        if (now - b.HeardTime < 15)
            return b.HeardAt;
        return NearestEnemy(p)?.Position;
    }

    /// <summary>
    /// The best free spot for the band: its sightline score, how well it overlooks <paramref name="focus"/> (the objective's
    /// bit, or a ray from the best few), not too far to walk (rushers want it close to the action), away from
    /// <paramref name="threat"/>, not held by another bot on our side (anyone, in free-for-all), not compromised, not the one
    /// just left. At most 4 ray casts; no allocations. Null when nothing qualifies.
    /// </summary>
    private int? VantagePick(Player p, BotBrain b, VantageMap vm, VantageBand band, Vector3? focus, Vector3? threat, int exclude, double now)
    {
        VantagePicks++;
        var bad = VantageBad(vm);
        Span<int> best = stackalloc int[4];
        Span<float> bestScore = stackalloc float[4];
        best.Fill(-1);
        bestScore.Fill(float.MinValue);
        var rusher = b.Personality == BotPersonality.Rusher;
        var air = p.DefFor(p.Primary).Class is not (WeaponClass.Sniper or WeaponClass.DMR or WeaponClass.LMG); // launcher hunting: needs open sky
        var objBit = VantageObjectiveBit(focus);
        var max = VantageMap.BandMax(band);
        for (var i = 0; i < vm.Spots.Length; i++)
        {
            ref readonly var s = ref vm.Spots[i];
            var score = s.Score(band);
            if (score <= 0 || i == exclude || now < bad[i] || (i == b.VantageLast && now < b.VantageLastUntil) || (air && s.Roofed))
                continue;
            if (VantageClaimed(p, vm, i))
                continue;
            var walk = Flat(s.Pos - p.Position);
            score -= walk * (rusher ? 0.1f : 0.035f);
            if (focus is { } f)
            {
                var df = Vector3.Distance(s.Pos, f);
                if ((s.Objectives & objBit) != 0)
                    score += 4f;
                score += VantageMap.BandWeight(band, df) * 3f - (df > max ? 4f : 0f);
                if (rusher)
                    score -= df * 0.05f; // in the thick of it
            }
            if (threat is { } t)
            {
                var dt = Flat(s.Pos - t);
                if (dt < 25f)
                    score -= (25f - dt) * 0.3f;
            }
            score += (((uint)(b.Seed ^ i) * 2654435761u) >> 22) / 1024f; // a little per-bot variety (0..1)
            // Insert into the top 4.
            for (var k = 0; k < 4; k++)
            {
                if (score <= bestScore[k])
                    continue;
                for (var m = 3; m > k; m--)
                {
                    best[m] = best[m - 1];
                    bestScore[m] = bestScore[m - 1];
                }
                best[k] = i;
                bestScore[k] = score;
                break;
            }
        }
        if (best[0] < 0)
            return null;
        // The focus in plain view from the spot? (An objective bit already says so.)
        if (focus is { } fp)
            for (var k = 0; k < 4 && best[k] >= 0; k++)
            {
                ref readonly var s = ref vm.Spots[best[k]];
                if ((s.Objectives & objBit) != 0)
                    return best[k];
                var eye = s.Pos + new Vector3(0, World.EyeHeight, 0);
                var to = fp + new Vector3(0, 1.2f, 0) - eye;
                var len = to.Length();
                if (len < 0.5f || World.Query.Raycast(eye, to / len, len, QueryFilter.StaticOnly) >= len - 0.3f)
                    return best[k];
            }
        return best[0];
    }

    /// <summary>The objective bit (<see cref="VantageSpot.Objectives"/>) of the hill or flag stand at <paramref name="focus"/>, or 0.</summary>
    private ushort VantageObjectiveBit(Vector3? focus)
    {
        if (focus is not { } f || Phase != MatchPhase.Playing)
            return 0;
        var o = World.Map.Objectives;
        for (var i = 0; i < o.Hills.Count && i < 12; i++)
            if (Flat(o.Hills[i].Center - f) < 3f)
                return (ushort)(1 << i);
        for (var i = 0; i < o.Flags.Length && i < 2; i++)
            if (Flat(o.Flags[i] - f) < 3f)
                return (ushort)(1 << (12 + i));
        return 0;
    }

    /// <summary>Is spot <paramref name="i"/> taken by another living bot on our side (any bot in free-for-all)?</summary>
    private bool VantageClaimed(Player p, VantageMap vm, int i)
    {
        foreach (var o in Players.Values)
        {
            if (o == p || !o.Alive || o.Bot is not { } ob || ob.VantageSpot != i || !ReferenceEquals(ob.VantageOf, vm))
                continue;
            if (!IsTeamMode || o.Team == p.Team)
                return true;
        }
        return false;
    }

    /// <summary>BotSteer for <see cref="BotMode.Overwatch"/>: walk to the spot, then hold it, scanning its sectors.</summary>
    private void VantageSteer(Player p, BotBrain b, NavGraph nav, bool visible, double now, ref BotIntent s)
    {
        if (b.VantageOf is not { } vm || b.VantageSpot < 0 || b.VantageSpot >= vm.Spots.Length)
        {
            if (b.Squad != null && b.Order.Overwatch)
                s.Stationary = true; // a squad's overwatch: the order's spot (SquadSteer brought it here)
            return;
        }
        ref readonly var spot = ref vm.Spots[b.VantageSpot];
        var far = Flat(spot.Pos - p.Position);
        if (b.VState == VantageState.Travel || far > 0.9f)
        {
            s.Wish = BotFollow(p, b, nav, spot.Pos, now, ref s);
            s.Sprint = far > 6f && !visible && b.VState == VantageState.Travel;
            s.Stationary = s.Wish == Vector3.Zero;
            return;
        }
        s.Stationary = true;
        if (visible)
            return; // BotAim faces the target
        // Scan: the spot's sectors in turn, now and then the way the action is.
        if (now >= b.VantageNextLook)
        {
            b.VantageNextLook = now + 2.2 + VantageRoll(p, b, 0x7A2) * 2.0;
            var sectors = spot.Sectors;
            var pick = (int)(VantageRoll(p, b, 0x7A3) * 8);
            var yaw = p.Yaw;
            for (var k = 0; k < 8 && sectors != 0; k++)
            {
                var bin = (pick + k) & 7;
                if ((sectors & (1 << bin)) == 0)
                    continue;
                yaw = bin * (MathF.Tau / 8) + ((float)VantageRoll(p, b, 0x7A4) - 0.5f) * 0.6f;
                break;
            }
            if (now - b.HeardTime < 4)
                yaw = MathF.Atan2(b.HeardAt.X - p.Position.X, -(b.HeardAt.Z - p.Position.Z));
            b.VantageLookYaw = yaw;
        }
        s.HasLook = true;
        // Out over the area at the height of the ground below the spot (looking down from a roof or a hill).
        s.Look = p.Eye + World.FlatForward(b.VantageLookYaw) * 25f - new Vector3(0, MathF.Max(0, spot.Height) + World.EyeHeight - 1.2f, 0);
    }

    /// <summary>BotButtons, last: stance at a held spot (duck behind cover and stand to peek; crouch or prone to shoot when that still sees the target).</summary>
    private void VantageButtons(Player p, BotBrain b, bool visible, in BotIntent steer, ref Buttons buttons, double now)
    {
        if (b.Mode != BotMode.Overwatch || !steer.Stationary || (b.VState != VantageState.Hold && !(b.Squad != null && b.Order.Overwatch)))
            return;
        var target = Players.GetValueOrDefault(b.Target);
        buttons &= ~(Buttons.Sprint | Buttons.Jump | Buttons.Crouch | Buttons.Prone);
        if (visible && target != null)
        {
            if (b.ReloadUntil > 0)
            {
                buttons |= Buttons.Crouch; // reload behind the spot's cover
                return;
            }
            if (now - b.VantageStanceAt > 0.35)
            {
                b.VantageStanceAt = now;
                b.VantageStance = 0;
                if (b.Skill != BotDifficulty.Easy && HasLineOfSight(p.Position + new Vector3(0, World.EyeOf(Stance.Crouch), 0), target.Chest))
                {
                    b.VantageStance = 1;
                    if (b.Profile.GoesProne && Flat(target.Position - p.Position) > 22f
                        && HasLineOfSight(p.Position + new Vector3(0, World.EyeOf(Stance.Prone) + 0.1f, 0), target.Chest))
                        b.VantageStance = 2;
                }
            }
            if (b.VantageStance == 2)
                buttons |= Buttons.Prone;
            else if (b.VantageStance == 1)
                buttons |= Buttons.Crouch;
            return;
        }
        b.VantageStanceAt = -100;
        // Nobody in sight: duck a few seconds, stand and look a few seconds.
        if (now >= b.VantagePeekUntil)
        {
            b.VantagePeek = !b.VantagePeek;
            b.VantagePeekUntil = now + (b.VantagePeek ? 2.5 + VantageRoll(p, b, 0x7A5) * 1.5 : 1.5 + VantageRoll(p, b, 0x7A6) * 2.0);
        }
        if (!b.VantagePeek)
            buttons |= Buttons.Crouch;
    }

    /// <summary>End of BotSteer: whether the bot is deliberately holding a position this tick (<see cref="BotBrain.IsHoldingPosition"/>).</summary>
    private static bool BotHoldingPosition(BotBrain b, in BotIntent s, double now) =>
        s.Stationary && ((b.Mode == BotMode.Overwatch && (b.VState == VantageState.Hold || b.Order.Overwatch))
                         || (b.HoldUntil > now && b.Mode == BotMode.Roam)
                         || (b.Mode == BotMode.Squad && b.Order.Kind == SquadOrderKind.Hold));

    /// <summary>BotNoteKill: counts kills from the spot (a couple and it's time to move).</summary>
    private static void VantageNoteKill(BotBrain b)
    {
        if (b.VState == VantageState.Hold)
            b.VantageKills++;
    }

    // ---- The sidearm ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Start of BotTick: a bot with a bolt-action or other slow-firing long gun switches to its sidearm (not a launcher) when
    /// an enemy it sees gets within <see cref="VantageCloseRange"/>, and back once nobody has been that close for 2.5 s. The two
    /// magazines are kept apart.
    /// </summary>
    private void BotSidearm(Player p, BotBrain b, double now)
    {
        if (b.SidearmLife != p.SpawnTick)
        {
            b.SidearmLife = p.SpawnTick;
            b.OnSidearm = false;
            b.SidearmAmmo = -1;
        }
        var primary = p.DefFor(p.Primary);
        var secondary = p.DefFor(p.Secondary);
        var slowGun = primary.Class == WeaponClass.Sniper || (primary.Class == WeaponClass.DMR && primary.FireInterval >= 0.45f);
        var can = slowGun && !secondary.IsLauncher && p.Secondary != p.Primary && !Module.CoOp && b.Skill != BotDifficulty.Easy;
        var target = Players.GetValueOrDefault(b.Target);
        var close = target is { Alive: true } && now - b.LastSeenTime < 0.4 && Flat(target.Position - p.Position) < (b.OnSidearm ? 14f : VantageCloseRange);
        if (close)
            b.SidearmQuietSince = now;
        if (!b.OnSidearm && can && close && p.Weapon == p.Primary)
            SwapSidearm(p, b, primary, secondary, true, now);
        else if (b.OnSidearm && (!can || now - b.SidearmQuietSince > 2.5 || p.Weapon != p.Secondary))
            SwapSidearm(p, b, primary, secondary, false, now);
    }

    private static void SwapSidearm(Player p, BotBrain b, WeaponDef primary, WeaponDef secondary, bool toSidearm, double now)
    {
        if (toSidearm)
        {
            b.PrimaryAmmo = b.ReloadUntil > 0 ? primary.Mag : b.Ammo; // a reload in progress finishes off-screen
            b.Ammo = b.SidearmAmmo >= 0 ? b.SidearmAmmo : secondary.Mag;
            p.Weapon = p.Secondary;
            b.SidearmSince = now;
        }
        else
        {
            if (p.Weapon == p.Secondary)
                b.SidearmAmmo = b.ReloadUntil > 0 ? secondary.Mag : b.Ammo;
            b.Ammo = b.PrimaryAmmo;
            if (p.Weapon == p.Secondary)
                p.Weapon = p.Primary;
        }
        b.OnSidearm = toSidearm;
        b.ReloadUntil = 0;
        b.BurstLeft = 0;
        b.CoverReload = false;
        b.NextFire = Math.Max(b.NextFire, now + (toSidearm ? secondary : primary).EquipTime);
    }

    // ---- Insane squads: overwatch --------------------------------------------------------------------------------

    /// <summary>
    /// PlanMove: the squad's long-range member (a sniper or DMR, else an LMG; never the one leading or carrying a flag, never
    /// the flanker) holds a vantage point overlooking the squad's goal instead of walking in the wedge. It keeps its spot while
    /// that still overlooks the goal and isn't compromised.
    /// </summary>
    private void SquadOverwatch(BotSquad s, NavGraph nav, double now)
    {
        var vm = VantageMap.For(nav);
        Player? pick = null;
        var pickRank = 0;
        foreach (var m in s.Members)
        {
            var mb = m.Bot!;
            if (!m.Alive || m == s.Acting || mb.Role == SquadRole.Flanker || CarriedFlagOf(m) >= 0)
                continue;
            var rank = m.DefFor(m.Primary).Class switch { WeaponClass.Sniper => 3, WeaponClass.DMR => 2, WeaponClass.LMG => 1, _ => 0 };
            if (rank > pickRank)
            {
                pickRank = rank;
                pick = m;
            }
        }
        foreach (var m in s.Members)
            if (m != pick && m.Bot!.VantageSpot >= 0 && m.Bot.Squad == s)
            {
                m.Bot.VantageSpot = -1;
                m.Bot.VState = VantageState.None;
            }
        if (pick == null || vm == null || vm.Spots.Length == 0 || Module.CoOp)
            return;
        var b = pick.Bot!;
        var band = pick.DefFor(pick.Primary).Class switch { WeaponClass.Sniper => VantageBand.Long, WeaponClass.DMR => VantageBand.MediumLong, _ => VantageBand.Medium };
        var bad = VantageBad(vm);
        var keep = ReferenceEquals(b.VantageOf, vm) && b.VantageSpot >= 0 && b.VantageSpot < vm.Spots.Length && now >= bad[b.VantageSpot]
                   && Vector3.Distance(vm.Spots[b.VantageSpot].Pos, s.Goal) < VantageMap.BandMax(band) && now - b.VantageSince < 60;
        if (!keep)
        {
            b.VantageOf = vm;
            b.VantageSpot = -1;
            if (VantagePick(pick, b, vm, band, s.Goal, null, -1, now) is not { } spot)
                return;
            b.VantageSpot = spot;
            b.VantageSince = now;
            b.VState = VantageState.Travel;
        }
        var at = vm.Spots[b.VantageSpot].Pos;
        if (Flat(at - pick.Position) < 1.5f)
            b.VState = VantageState.Hold;
        b.Order = new SquadOrder
        {
            Kind = SquadOrderKind.Hold, Goal = at, Look = s.Goal + new Vector3(0, 1.2f, 0), HasLook = true, Overwatch = true,
            Sprint = Flat(at - pick.Position) > 8f,
        };
        SquadStats.OverwatchOrders++;
    }

    /// <summary>
    /// VantageDecide for a squad member (only reached with an enemy in sight: otherwise the squad's order runs): at its
    /// overwatch spot it shoots from there instead of strafing off it; shot from a flank there, the spot is compromised and
    /// the next plan picks another.
    /// </summary>
    private bool SquadOverwatchDecide(Player p, BotBrain b, Player? target, bool visible, double now)
    {
        if (!b.Order.Overwatch || !visible || target == null)
            return false;
        var at = Flat(b.Order.Goal - p.Position) < 1.5f;
        var dist = Flat(target.Position - p.Position);
        if (b.HurtTime == now && at && b.VantageOf is { } vm && b.VantageSpot >= 0 && b.VantageSpot < vm.Spots.Length
            && !SectorCovers(vm.Spots[b.VantageSpot].Sectors, target.Position - p.Position))
        {
            VantageBad(vm)[b.VantageSpot] = now + 40;
            b.Squad!.NextPlan = Math.Min(b.Squad.NextPlan, now);
            return false;
        }
        if (!at || dist < VantageCloseRange || p.Health <= b.Profile.RetreatHealth)
            return false;
        b.VState = VantageState.Hold;
        b.Mode = BotMode.Overwatch;
        return true;
    }

    // ---- Self-test hooks -----------------------------------------------------------------------------------------

    /// <summary>Self-test: puts the bot at spot <paramref name="spot"/> of the current map's vantage points, holding it.</summary>
    public void SelfTestVantageHold(Player p, int spot, double now)
    {
        var b = p.Bot!;
        var vm = VantageMap.For(NavGraph.Current)!;
        b.VantageLife = p.SpawnTick;
        b.VantageMinded = true;
        b.VantageOf = vm;
        b.VantageSpot = spot;
        b.VState = VantageState.Hold;
        b.VantageSince = now;
        b.VantageHoldUntil = now + 60;
        b.VantageKills = 0;
        b.Mode = BotMode.Overwatch;
        b.PathGraph = NavGraph.Current;
        b.ClearPath();
    }

    /// <summary>Self-test: is spot <paramref name="spot"/> marked compromised now?</summary>
    public bool SelfTestVantageCompromised(int spot, double now) =>
        VantageMap.For(NavGraph.Current) is { } vm && spot >= 0 && spot < vm.Spots.Length && now < VantageBad(vm)[spot];

    /// <summary>Self-test: one spot pick for a bot (no budget), as the decision code does it.</summary>
    public int SelfTestVantagePick(Player p, double now)
    {
        var b = p.Bot!;
        var vm = VantageMap.For(NavGraph.Current);
        if (vm == null || VantageBandFor(p, b, out var air) is not { } band)
            return -1;
        return VantagePick(p, b, vm, band, VantageFocus(p, b, air, now), null, -1, now) ?? -1;
    }

    /// <summary>Self-test: the vantage decision for one bot (as BotDecide runs it), without the rest of the tick.</summary>
    public bool SelfTestVantageDecide(Player p, double now)
    {
        var b = p.Bot!;
        var target = Players.GetValueOrDefault(b.Target);
        _vantageBudgetTick = uint.MaxValue; // a fresh pick budget each call
        return VantageDecide(p, b, target, target != null && now - b.LastSeenTime < 0.01, now);
    }
}
