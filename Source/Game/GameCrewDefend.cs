using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Boarded crews defend themselves (Karl, 2026-11-18: "when boarded, NPCs should take up arms and try to defend themselves"), on top of
/// Twitch's boarding acts (GameCrewBoard.cs, bot-brain.md section 8). Server only. A crew body carries only its sidearm at its station; its
/// long gun is in the ship's lockers. When hostile players come aboard and the crew doesn't give up (any act but Surrender and Abandon):
/// <list type="bullet">
/// <item>Who: the off-duty crew (couches, no station) always; under Repel the work hands too (loaders, spotters, crane and dock hands,
/// builders), the engineer when its faction never scuttles, and the gunners once the boarders are at least half the crew left. The pilot
/// keeps the helm.</item>
/// <item>Arming: each walks (CrewNav) to the nearest arms point aboard (a Gun Rack or a kit locker, not a suit or a medicine locker) and
/// takes its long gun there in <see cref="ArmSeconds"/>. A ship with no locker it can reach: it fights with the sidearm.</item>
/// <item>Posts: about half hold the bridge (they stop by the helm), the rest the breach (where the first boarder came aboard): they stop
/// <see cref="BreachHold"/> m short of it, inside. Held, they crouch behind what is there and come up to shoot; reloading or hurt, they
/// duck. A breach guard who has seen nobody for <see cref="HuntAfter"/> s goes looking (it walks to the nearest boarder).</item>
/// <item>A walker that sees a boarder stops and fights (one going for its gun only when the boarder is close).</item>
/// </list>
/// Morale still falls with the boarders against the crew left, so a beaten crew still surrenders by its faction's rules, and the
/// scuttle is still the engineer's. Crew shoot only at boarders (the ship's foes aboard), never grenades or launchers inside their own ship.
/// Fixed per-body state (<see cref="_guards"/>, by player id): nothing allocated in a tick.
/// </summary>
public sealed partial class Game
{
    /// <summary>Seconds at the locker to take a long gun; m short of the breach a guard holds; quiet seconds before a breach guard hunts.</summary>
    public const float ArmSeconds = 1.5f, BreachHold = 7, BridgeHold = 3, HuntAfter = 12;

    /// <summary>Seconds a new fighter waits for its ship's walk graph (CrewNav builds it in slices) before it fights where it stands.</summary>
    public const double NavWait = 6;

    /// <summary>How far (m) from a fighter's feet its walk may start on the graph (a work hand stands beside its machine, off the walked spots).</summary>
    public const float FromAcross = 2.5f;

    /// <summary>A fighter opens a shut door between it and a boarder from this close (m).</summary>
    public const float GuardDoorReach = 2.5f;

    /// <summary>How far (m) a walking guard notices a boarder in sight and stops to fight; one going for its gun stops only this close.</summary>
    public const float GuardSees = 30, GuardSeesArming = 8;

    private enum GuardStage : byte
    {
        Start,
        ToLocker,
        Arming,
        ToPost,
        Hold,
        Hunt,
    }

    private struct CrewGuard
    {
        public uint Uid; // the member it was set up for (a body's id is reused)
        public GuardStage Stage;
        public bool Bridge; // its post: the bridge, else the breach
        public ushort Locker; // the arms point's tag
        public double Until, SeenAt, NextScan, DuckUntil;
        public byte Target, Tries; // (Tries: walks for the locker begun again after a stop)
        public int LastHealth;
    }

    private readonly CrewGuard[] _guards = new CrewGuard[256];

    /// <summary>A new crew body's arms: a plain sidearm out (no launcher), its long gun one fit for a ship's corridors (no sniper rifle), left in the lockers.</summary>
    private static void CrewArmsOnSpawn(Player p)
    {
        if (p.DefFor(p.Secondary) is var sec && (sec.IsLauncher || sec.Class != WeaponClass.Pistol))
            p.Secondary = Weapons.DefaultSecondary.Id;
        if (p.DefFor(p.Primary) is var pri && (pri.IsLauncher || pri.Class is WeaponClass.Sniper))
            p.Primary = Weapons.DefaultPrimary.Id;
        p.Weapon = p.Secondary;
        p.Grenade = ThrowableKind.None;
        p.GrenadesLeft = 0;
        if (p.Bot is { } b)
        {
            b.Ammo = p.DefFor(p.Weapon).Mag;
            b.ReloadUntil = 0;
        }
    }

    /// <summary>Whether a part is an arms point: a Gun Rack or a kit locker (not a space suit's, not a medicine locker).</summary>
    private static bool ArmsPoint(ConstructPartDef d)
    {
        var n = d.Name;
        if (n.Contains("Gun Rack", StringComparison.OrdinalIgnoreCase))
            return true;
        return n.Contains("Locker", StringComparison.OrdinalIgnoreCase) && !n.Contains("Suit", StringComparison.OrdinalIgnoreCase)
            && !n.Contains("Med", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The nearest whole arms point to <paramref name="world"/> (-1: none).</summary>
    private static int NearestArms(Construct c, Vector3 world)
    {
        var best = -1;
        var bestD = float.MaxValue;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.Health <= 0 || p.Broken || !ArmsPoint(p.D))
                continue;
            var d = Vector3.DistanceSquared(ConstructRender.PartCentre(c, i), world);
            if (d < bestD)
                (best, bestD) = (i, d);
        }
        return best;
    }

    /// <summary>
    /// The nearest whole arms point a fighter standing at <paramref name="feet"/> can walk to on its ship's graph (the same connected part), else
    /// the nearest at all (-1: none). An arms point on another deck it can't reach is passed over.
    /// </summary>
    private static int ReachableArms(Construct c, CrewShip s, Vector3 feet)
    {
        if (s.Nav is not { } g || s.NavBuild != g.Build)
            return NearestArms(c, feet);
        var from = CrewNav.NearestNode(g, c.ToLocal(feet), FromAcross);
        if (from < 0)
            return NearestArms(c, feet);
        var best = -1;
        var bestD = float.MaxValue;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.Health <= 0 || p.Broken || !ArmsPoint(p.D))
                continue;
            var to = CrewNav.NearestNode(g, CrewWorld.PartFoot(c, i), 2.5f, 2.5f);
            if (to < 0 || g.Comp[to] != g.Comp[from])
                continue;
            var d = Vector3.DistanceSquared(ConstructRender.PartCentre(c, i), feet);
            if (d < bestD)
                (best, bestD) = (i, d);
        }
        return best >= 0 ? best : NearestArms(c, feet);
    }

    /// <summary>Whether the member should be up defending under act <paramref name="act"/> (see the class summary).</summary>
    private bool CrewDefends(CrewShip s, NpcFactionDef? def, int m, BoardAct act, float ratio)
    {
        if (Crew.Player[m] == 0)
            return false;
        var st = Crew.State[m];
        if (st == CrewState.Free)
            return Crew.Job[m] switch
            {
                CrewJob.Pilot => false, // (turned out of the helm: the fill puts it back once the seat is free)
                CrewJob.Engineer => act != BoardAct.Scuttle, // (the scuttle takes an engineer)
                _ => true,
            };
        if (st != CrewState.Station || Crew.Station[m] < 0 || Crew.Station[m] >= s.StCount)
            return false;
        var kind = s.StKind[Crew.Station[m]];
        if (kind == CrewStationKind.Rest)
            return true;
        if (act != BoardAct.Repel)
            return false;
        return kind switch
        {
            CrewStationKind.Magazine or CrewStationKind.Spot or CrewStationKind.Crane or CrewStationKind.Dock or CrewStationKind.Build => true,
            CrewStationKind.Works => def is { Scuttle: ScuttleWhen.Never },
            CrewStationKind.Gun => ratio >= 0.5f,
            _ => false, // (the helm is kept; a miner is out on the ground)
        };
    }

    /// <summary>
    /// Each look at a boarding with a crew that fights on: stands up those who defend (<see cref="CrewDefends"/>), then moves each fighter on
    /// through its stages (locker, post, hold, hunt). Replaces the old Repel stand-up and walk-to-the-boarder (now the hunt).
    /// </summary>
    private void CrewDefend(Construct c, CrewShip s, NpcFactionDef? def, byte first, float ratio)
    {
        if (Players.TryGetValue(first, out var fb) && fb.Alive && !s.BreachSet)
        {
            s.BreachAt = c.ToLocal(fb.Position); // (where the first boarder was seen aboard: the way in)
            s.BreachSet = true;
        }
        var stood = 0;
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            if (!CrewDefends(s, def, m, s.Act, ratio) || !Players.TryGetValue((byte)(Crew.Player[m] - 1), out var bp) || !bp.Alive)
                continue;
            Crew.Leave(m, CrewState.Fighting);
            _guards[bp.Id].Uid = 0; // (a new boarding starts its stages afresh)
            if (SandboxHands.Hand(bp.Id).Seated)
                SandboxStand(bp, teleport: true); // (up from the couch or the gun)
            stood++;
        }
        var fighters = 0;
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            if (Crew.State[m] != CrewState.Fighting || Crew.Player[m] == 0 || !Players.TryGetValue((byte)(Crew.Player[m] - 1), out var bp) || !bp.Alive)
                continue;
            ref var g = ref _guards[bp.Id];
            if (g.Uid != Crew.Uid[m])
            {
                g = default;
                g.Uid = Crew.Uid[m];
                g.Bridge = s.Helm >= 0 && (fighters & 1) == 1; // (every other one holds the bridge)
                g.SeenAt = SandboxNow;
                g.Until = SandboxNow + NavWait; // (the walk graph may still be building: it waits this long for it)
                g.LastHealth = bp.Health;
                g.Target = 255;
            }
            fighters++;
            GuardLook(c, s, m, bp, ref g, first);
        }
        if (stood > 0)
            Console.WriteLine($"[crew] construct {c.Id}: {stood} crew take up arms against the boarders ({fighters} fighting)");
    }

    /// <summary>One fighter's stage, once a look (its walks are searched here, not per tick).</summary>
    private void GuardLook(Construct c, CrewShip s, int m, Player bp, ref CrewGuard g, byte first)
    {
        var now = SandboxNow;
        var walking = Crew.Nav.WalkerOf(m) >= 0;
        switch (g.Stage)
        {
            case GuardStage.Start when !NavReady(s) && now < g.Until:
            case GuardStage.ToPost when !NavReady(s) && now < g.Until:
                break; // (its walk graph is still building)
            case GuardStage.Start:
            {
                var armed = bp.Weapon == bp.Primary;
                var locker = armed ? -1 : ReachableArms(c, s, bp.Position);
                if (locker >= 0 && Crew.Errand(c, s, m, CrewWorld.PartFoot(c, locker), this, 2.5f, FromAcross))
                {
                    g.Locker = c.Parts[locker].Tag;
                    g.Stage = GuardStage.ToLocker;
                }
                else if (locker >= 0 && now < g.Until)
                    break; // (no way found from here yet: a search busy, or the graph rebuilding; tried again at the next look)
                else if (locker >= 0)
                {
                    Console.WriteLine($"[crew] construct {c.Id}: {bp.Name} finds no way to a locker: it fights with its sidearm");
                    g.Stage = GuardStage.ToPost;
                }
                else
                    g.Stage = GuardStage.ToPost; // (no locker it can reach: the sidearm)
                break;
            }
            case GuardStage.ToLocker when !walking:
            {
                var at = CrewWorld.PartByTag(c, g.Locker);
                if (at >= 0 && Vector3.Distance(bp.Position, ConstructRender.PartCentre(c, at)) < 3.5f)
                {
                    g.Stage = GuardStage.Arming;
                    g.Until = now + ArmSeconds;
                }
                else if (at >= 0 && now - g.SeenAt > 2 && g.Tries++ < 2)
                {
                    g.Stage = GuardStage.Start; // (stopped to fight a boarder who has gone from sight: for the locker again)
                    g.Until = now;
                }
                else
                    g.Stage = GuardStage.ToPost; // (gave up on the way, the locker is gone, or a boarder is in its face: the sidearm)
                break;
            }
            case GuardStage.ToPost:
            {
                if (!GuardPost(c, s, g.Bridge, out var post, out var hold) || Vector3.Distance(c.ToLocal(bp.Position), post) < hold)
                {
                    Crew.Nav.End(Crew.Nav.WalkerOf(m));
                    g.Stage = GuardStage.Hold;
                    g.SeenAt = Math.Max(g.SeenAt, now - HuntAfter * 0.5); // (a fresh post: a while before it hunts)
                }
                else if (!walking && !Crew.Errand(c, s, m, post, this, 2.5f, FromAcross))
                    g.Stage = GuardStage.Hold; // (no way there: it holds where it is)
                break;
            }
            case GuardStage.Hold when !g.Bridge && now - g.SeenAt > HuntAfter:
                g.Stage = GuardStage.Hunt;
                Console.WriteLine($"[crew] construct {c.Id}: {bp.Name} goes looking for the boarders");
                break;
            case GuardStage.Hunt:
                if (Players.TryGetValue(GuardQuarry(c, s, bp), out var b) && b.Alive)
                {
                    var to = c.ToLocal(b.Position);
                    if (Vector3.Distance(c.ToLocal(bp.Position), to) < RepelClose && now - g.SeenAt < 1.5)
                        Crew.Nav.End(Crew.Nav.WalkerOf(m)); // (close and in sight: it fights from here)
                    else
                        Crew.Errand(c, s, m, to, this, 2.5f, FromAcross); // (on until it sees them: round the corner, through the door)
                }
                break;
        }
        if (g.Stage is GuardStage.Hold or GuardStage.Hunt && now - g.SeenAt > 1.5)
            GuardDoor(c, s, bp);
    }

    /// <summary>
    /// A fighter with a boarder close behind a shut door (the first thing on the line to them, within <see cref="GuardDoorReach"/> m of it) opens
    /// it, as F does: a crew fights through its own doors, not round them. A locked one stays shut.
    /// </summary>
    private void GuardDoor(Construct c, CrewShip s, Player bp)
    {
        if (c.DoorCount == 0 || !Players.TryGetValue(GuardQuarry(c, s, bp), out var q))
            return;
        var d = q.Chest - bp.Eye;
        var dist = d.Length();
        if (dist < 0.3f || dist > 15)
            return;
        var reach = dist - 0.3f;
        if (SandboxRay(bp.Eye, d / dist, ref reach) is { } hit && hit.Body == c && hit.Distance < GuardDoorReach && c.DoorOf(hit.Part) >= 0
            && !ConstructDoors.TargetOpen(c, hit.Part) && ConstructDoors.Use(Constructs, c, hit.Part) == "")
            Console.WriteLine($"[crew] construct {c.Id}: {bp.Name} opens the {c.DefOf(hit.Part).Name} on the boarder");
    }

    private static bool NavReady(CrewShip s) => s.Nav is { } g && s.NavBuild == g.Build;

    /// <summary>A fighter's post in the body's frame and how close it holds (false: none: it holds where it stands).</summary>
    private bool GuardPost(Construct c, CrewShip s, bool bridge, out Vector3 post, out float hold)
    {
        if (bridge && s.Helm >= 0 && s.Helm < s.StCount && s.StPart[s.Helm] is var hp and >= 0 && hp < c.PartCount)
        {
            post = CrewWorld.PartFoot(c, hp);
            hold = BridgeHold;
            return true;
        }
        post = s.BreachAt;
        hold = BreachHold;
        return s.BreachSet;
    }

    /// <summary>The boarder nearest the fighter (255: none).</summary>
    private byte GuardQuarry(Construct c, CrewShip s, Player bp)
    {
        byte best = 255;
        var bestD = float.MaxValue;
        foreach (var o in Players.Values)
        {
            if (!o.Alive || o.CrewSlot >= 0 || !s.Foe(o.Id) || !Aboard(o, c.Id))
                continue;
            var d = Vector3.DistanceSquared(o.Position, bp.Position);
            if (d < bestD)
                (best, bestD) = (o.Id, d);
        }
        return best;
    }

    /// <summary>A fighting crew body's tick (Game.cs's player loop): its walk, its arming, or holding and shooting at boarders it sees.</summary>
    private void CrewDefendTick(Player p)
    {
        if (!p.Alive || p.Bot is not { } b)
            return;
        var m = p.CrewSlot;
        var now = SandboxNow;
        ref var g = ref _guards[p.Id];
        if (g.Uid != Crew.Uid[m] || Crew.ShipOf(Crew.Body[m]) is not { } s || Constructs.Get(s.Body) is not { Wreck: false } c)
        {
            CrewBodyTick(p); // (not set up yet: the next look does it)
            return;
        }
        // Reload, sight.
        var def = p.WeaponDef;
        if (b.ReloadUntil > 0 && now >= b.ReloadUntil)
        {
            b.Ammo = def.Mag;
            b.ReloadUntil = 0;
        }
        var hurt = p.Health < g.LastHealth;
        g.LastHealth = p.Health;
        if (hurt)
            g.DuckUntil = now + 0.8;
        Player? target = g.Target != 255 && Players.TryGetValue(g.Target, out var t) && t.Alive && s.Foe(t.Id) ? t : null;
        if (now >= g.NextScan || hurt)
        {
            g.NextScan = now + 0.2 + (p.Id & 7) * 0.01;
            target = GuardSight(c, s, p);
            if (target != null && target.Id != g.Target)
            {
                b.TargetSince = now;
                b.AimErrYaw = ((float)_rng.NextDouble() - 0.5f) * 2 * b.AimError * 3;
                b.AimErrPitch = ((float)_rng.NextDouble() - 0.5f) * 2 * b.AimError * 2;
            }
            g.Target = target?.Id ?? 255;
        }
        var visible = target != null && CanSeeBoarder(p, target);
        if (visible)
        {
            g.SeenAt = now;
            b.LastSeenAt = target!.Position;
            b.LastSeenTime = now;
            b.Target = target.Id;
        }
        // Walking: on unless a boarder is in sight (close, for one going for its gun).
        var w = Crew.Nav.WalkerOf(m);
        if (w >= 0)
        {
            var near = visible ? Vector3.Distance(p.Position, target!.Position) : float.MaxValue;
            if (near < (g.Stage == GuardStage.ToLocker ? GuardSeesArming : GuardSees))
            {
                Crew.Nav.End(w);
                if (g.Stage != GuardStage.ToLocker)
                    g.Stage = GuardStage.Hold;
            }
            else if (CrewWalkTick(p))
                return;
        }
        if (g.Stage == GuardStage.Arming && now >= g.Until)
        {
            p.Weapon = p.Primary;
            b.Ammo = p.DefFor(p.Primary).Mag;
            b.ReloadUntil = 0;
            g.Stage = GuardStage.ToPost;
            Console.WriteLine($"[crew] construct {c.Id}: {p.Name} took a {p.DefFor(p.Primary).Name} from the {(CrewWorld.PartByTag(c, g.Locker) is var lk and >= 0 ? c.DefOf(lk).Name : "locker")}");
            def = p.WeaponDef;
        }
        // Held: look at the boarder (or the way they will come), shoot, keep low.
        var steer = new BotIntent { Stationary = true };
        if (!visible)
        {
            steer.HasLook = true;
            steer.Look = now - b.LastSeenTime < 6 ? b.LastSeenAt : g.Bridge || !s.BreachSet ? p.Eye + GravityFrames.FlatForward(p.Position, p.Yaw) : c.ToWorld(s.BreachAt) + UpOf(p) * 1.2f;
        }
        var aim = GuardAim(p, b, target, visible, def, steer);
        if (g.Stage != GuardStage.Arming)
        {
            b.HoldFire = visible && !FireLineClear(p, aim, target!); // (a wall or the deck on the line as it aims: no round into its own ship)
            BotShoot(p, b, target, visible, def, aim, now);
            b.HoldFire = false;
        }
        var buttons = Buttons.None;
        // Cover, holding a post: up and shooting while it sees a boarder, down while it reloads or just took a hit; with nobody in sight it
        // keeps low and comes up for a look every few seconds (else it would never see over what it hides behind). Hunting, it walks upright.
        var crouch = g.Stage == GuardStage.Arming || now < g.DuckUntil || b.ReloadUntil > 0;
        if (!crouch && g.Stage == GuardStage.Hold && !visible)
            crouch = (now + (p.Id & 3) * 0.7) % 3.0 < 1.6;
        if (crouch)
            buttons |= Buttons.Crouch;
        var cmd = new InputCmd { Seq = p.LastInput.Seq + 1, Buttons = buttons, Yaw = aim.Yaw, Pitch = Math.Clamp(aim.Pitch, -1.5f, 1.5f), Weapon = p.Weapon, SpeedPct = 100 };
        if (p.Sim.RideUp != Vector3.Zero && p.Sim.RideBody != 0 && Constructs.Get(p.Sim.RideBody) is { } rc && p.Sim.RideSeg <= rc.JointCount)
        {
            rc.WorldFrame(p.Sim.RideSeg, out var q, out _);
            ConstructRide.LookIn(ConstructRide.RiderRot(q, p.Sim.RideUp), GravityFrames.Forward(p.Position, aim.Yaw, aim.Pitch), out var ry, out var rp);
            cmd.RideBody = p.Sim.RideBody;
            cmd.RideYaw = ry;
            cmd.RidePitch = rp;
        }
        ApplyCmd(p, cmd);
        b.LastHealth = p.Health;
    }

    /// <summary>
    /// A fighter sees <paramref name="o"/>: the bots' own sight line (the world, smoke) and no ship's part in the way to where it would aim
    /// (inside a ship its walls, decks and fittings are what stop the rounds: <see cref="SandboxRay"/>; glass lets it see).
    /// </summary>
    private bool CanSeeBoarder(Player p, Player o)
    {
        if (BotSees(p.Eye, o) != BotSightLine.Clear)
            return false;
        var at = AimPoint(o); // (where it aims: GuardAim)
        var d = at - p.Eye;
        var dist = d.Length();
        if (dist < 0.05f)
            return true;
        var reach = dist - 0.3f;
        return SandboxRay(p.Eye, d / dist, ref reach) == null;
    }

    /// <summary>A person's up: their ship's, riding its decks (a ship in space, rolled or on its back), else the world's.</summary>
    private static Vector3 UpOf(Player o) => o.Sim.RideUp != Vector3.Zero ? o.Sim.RideUpW : GravityFrames.UpAt(o.Position);

    /// <summary>Where a fighter aims at <paramref name="o"/>: mid-chest along their own up (<see cref="BotAimHeight"/> of their height).</summary>
    private static Vector3 AimPoint(Player o) => o.Position + UpOf(o) * (World.HeightOf(o.Sim.Stance) * (o.Sim.Stance == Stance.Prone ? 0.5f : BotAimHeight));

    /// <summary>
    /// <see cref="BotAim"/> for a fighter aboard: the same error that settles, wobble and turn rate, but its aim point and look are worked in
    /// the ship's frame (the target's own up), not the planet's: a crew on a rolled ship, one on its back or one in space aims true.
    /// </summary>
    private BotView GuardAim(Player p, BotBrain b, Player? target, bool visible, WeaponDef def, in BotIntent steer)
    {
        var dt = World.TickDt;
        var aim = new BotView { Error = 10 };
        float wantYaw = p.Yaw, wantPitch = p.Pitch;
        if (visible)
        {
            var d = AimPoint(target!) + target!.Sim.Velocity * 0.08f - p.Eye;
            aim.Distance = d.Length();
            var (y, pt) = GravityFrames.Angles(p.Position, d / MathF.Max(aim.Distance, 1e-3f));
            b.AimErrYaw *= MathF.Exp(-b.Settle * dt);
            b.AimErrPitch *= MathF.Exp(-b.Settle * (b.AimErrPitch > 0 ? BotPullDown : 1f) * dt);
            wantYaw = y + b.AimErrYaw + ((float)_rng.NextDouble() - 0.5f) * b.AimError * 0.15f;
            wantPitch = pt + b.AimErrPitch;
        }
        else if (steer.HasLook && steer.Look - p.Eye is var l && l.LengthSquared() > 0.09f)
            (wantYaw, wantPitch) = GravityFrames.Angles(p.Position, Vector3.Normalize(l));
        var maxTurn = b.TurnSpeed * dt;
        aim.Yaw = World.WrapAngle(p.Yaw + Math.Clamp(World.WrapAngle(wantYaw - p.Yaw), -maxTurn, maxTurn));
        aim.Pitch = Math.Clamp(p.Pitch + Math.Clamp(wantPitch - p.Pitch, -maxTurn, maxTurn), -1.55f, 1.55f);
        if (visible)
            aim.Error = MathF.Abs(World.WrapAngle(wantYaw - aim.Yaw)) + MathF.Abs(wantPitch - aim.Pitch);
        aim.Ads = visible && aim.Distance > 14f && def.AdsZoom >= 1.4f;
        return aim;
    }

    /// <summary>The fighter's round, laid as it aims now, meets no part of a ship before <paramref name="o"/> (it never shoots its own deck or walls).</summary>
    private bool FireLineClear(Player p, in BotView aim, Player o)
    {
        var reach = MathF.Max(0.1f, Vector3.Distance(p.Eye, AimPoint(o)) - 0.3f);
        return SandboxRay(p.Eye, GravityFrames.Forward(p.Position, aim.Yaw, aim.Pitch), ref reach) == null;
    }

    /// <summary>The nearest boarder the fighter can see (its ship's foes aboard, within <see cref="GuardSees"/> x 2 m). Null: none.</summary>
    private Player? GuardSight(Construct c, CrewShip s, Player p)
    {
        Player? best = null;
        var bestD = (GuardSees * 2) * (GuardSees * 2);
        foreach (var o in Players.Values)
        {
            if (!o.Alive || o.CrewSlot >= 0 || !s.Foe(o.Id) || !Aboard(o, c.Id))
                continue;
            var d = Vector3.DistanceSquared(o.Position, p.Position);
            if (d >= bestD || !CanSeeBoarder(p, o))
                continue;
            (best, bestD) = (o, d);
        }
        return best;
    }

    /// <summary>A tool's line (allocates): each fighting crew body's stage, post, gun and target.</summary>
    public string CrewDefendLine()
    {
        var parts = new List<string>();
        foreach (var p in Players.Values)
            if (p.CrewSlot >= 0 && p.CrewSlot < Crew.High && Crew.State[p.CrewSlot] == CrewState.Fighting)
            {
                var g = _guards[p.Id];
                parts.Add($"{p.Name} {(g.Uid == Crew.Uid[p.CrewSlot] ? g.Stage.ToString() : "new")} {(g.Bridge ? "bridge" : "breach")} {p.WeaponDef.Name}{(p.Weapon == p.Primary ? " (long gun)" : "")}{(p.Alive ? "" : " DEAD")}{(g.Target != 255 ? $" at {g.Target}" : "")}");
            }
        return parts.Count == 0 ? "nobody fighting" : string.Join("; ", parts);
    }

    /// <summary>A tool: whether the fighting body <paramref name="id"/> holds its long gun, and its stage's name.</summary>
    public (bool Armed, string Stage) CrewGuardOf(byte id) =>
        Players.TryGetValue(id, out var p) && p.CrewSlot >= 0 ? (p.Weapon == p.Primary, _guards[id].Stage.ToString()) : (false, "");

    /// <summary>A tool: <paramref name="by"/> shoots the first living crew body aboard <paramref name="body"/> that isn't surrendered. Null: none.</summary>
    public string? CrewShootAny(Player by, ushort body)
    {
        foreach (var p in Players.Values)
            if (p.Alive && p.CrewSlot >= 0 && p.CrewSlot < Crew.High && Crew.Body[p.CrewSlot] == body && Crew.State[p.CrewSlot] is not (CrewState.Surrendered or CrewState.Dead))
            {
                DamagePlayer(by, p, 1000, false, 0, SandboxNow);
                return $"{p.Name} ({Crew.Job[p.CrewSlot]}, {Crew.State[p.CrewSlot]}) shot";
            }
        return null;
    }

    /// <summary>A tool: stands <paramref name="p"/> at <paramref name="rest"/> (the body's rest frame) on <paramref name="c"/>'s deck, riding it as a crew body does.</summary>
    public void StandAboard(Player p, Construct c, Vector3 rest) => CrewStandAt(p, c, rest);
}
