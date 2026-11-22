using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A socket's own works moving a module (docs/sandbox/design/module-sockets.md 6.5, Sean 2026-11-21). Server only.
///
/// A guide holds a loose module body as the physics gun would (<see cref="Construct.GrabbedBy"/> <see cref="Holder"/>), on its socket's axis:
/// <list type="bullet">
/// <item><see cref="Kind.Out"/>: the eject button, a rise on the Eject wire, or F on a dug socket: it slides the module out of its well,
/// a little clear of the frame, holds it there a while for a physics gun to take, then lets it go.</item>
/// <item><see cref="Kind.In"/>: a rise on the Attach wire: the nearest loose module of the socket's size within the hand's pull is drawn
/// over the socket's mouth, then down onto its seat, and fitted.</item>
/// <item><see cref="Kind.Pull"/>: the physics gun pulling a fitted module off. The player pulls on it (<see cref="PullStart"/>) for
/// <see cref="PullSeconds"/> before it unseats; then it moves out slowly for its first <see cref="PullSlowFor"/> m and quicker after,
/// only while they keep pulling, and slides back and seats again if they let go before it is clear. Clear, the gun has it.</item>
/// </list>
/// And the gun's pull-in (<see cref="PullIn"/>): a module held near a free socket that takes it is drawn over the mouth and down onto
/// its seat unless the player pulls it away (aims well off it); seated, the gun lets go and it is fitted.
///
/// The plate's wires (<see cref="WireStep"/>): Attach, Eject and Lock in after its Data ports; their last levels in the plate's
/// <see cref="CPart.Mem"/> bits. Nothing allocated in a steady tick.
/// </summary>
public static class ModuleGuide
{
    public const byte Holder = 254; // GrabbedBy while a guide holds a module (no player has this id)
    public const int MemAttach = 1, MemEject = 2, MemLockHigh = 4; // a plate's Mem bits: its wires' last levels
    public const int MemSeen = 8; // the levels were read once since the body was made or loaded (Mem isn't saved: a wire high at a load is no rise)
    public const float EjectSpeed = 0.5f; // m/s out of a well
    public const float EjectClear = 0.35f; // m: an ejected module stops this far past the frame's face...
    public const float HoverSeconds = 8f; // ...and is held there this long for a physics gun to take
    public const float InSpeed = 0.6f; // m/s down onto the seat (an Attach wire's pull-in)
    public const float Approach = 0.25f; // m: the mouth's point a module comes down from, over the frame's face
    public const float PullSeconds = 0.6f; // s of pulling before a fitted module unseats (a quick tap does nothing)
    public const float PullBeam = 0.3f; // m: the beam's point this far out past the module's grip counts as pulling
    public const float PullSlow = 0.05f, PullSlowFor = 0.12f, PullFast = 0.5f, PullBack = 0.2f; // m/s out (the first PullSlowFor m slowly), m/s back
    public const float CatchRange = 1.5f; // m: a held module's plug this near a free socket's face is caught by its pull
    public const float CatchSeconds = 0.9f, LoseSeconds = 0.35f; // the pull's capture builds over this, and falls over this while the player pulls away
    public const float CatchLag = 0.15f; // m: the pull moves on only while the module is this near where it wants it

    public enum Kind : byte { Out, In, Pull }

    private sealed class Guide
    {
        public ushort Ship, Body;
        public ModuleFitters.PartRef Plate;
        public Kind Kind;
        public float D, Goal, Held;
        public byte Player;
        public bool Want; // a pull: the player pulls on it this tick
        public bool Over; // an attach's pull-in: over the mouth, coming down
        public Vector3 At; // an attach's pull-in: where it wants the module's middle now (world), moving to the mouth
    }

    private struct Pull
    {
        public byte Phase; // 0 none, 1 straining, 2 guided out
        public bool Active; // the player still holds Fire
        public ushort Ship, Body;
        public ModuleFitters.PartRef Plate;
        public Vector3 Local, BodyLocal; // the beam's point on the ship, then on the module's body
        public float Dist, Strain;
        public bool Told;
        public bool Pulling; // the player pulls past it this tick (the shake everyone sees: ShowPull)
        public Vector3 Eye; // the player's eye and yaw this tick (the gun's hand-over)
        public float Yaw;
    }

    private sealed class WorldGuides
    {
        public readonly List<Guide> Guides = new(8);
        public readonly Pull[] Pulls = new Pull[256];
        public int AnyPull;
        public long Ticks;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ConstructWorld, WorldGuides> ByWorld = new();

    private static float Dt => (float)World.TickDt;

    /// <summary>Is a guide holding <paramref name="c"/> where a physics gun may take it (an ejected module hovering clear)?</summary>
    public static bool Takeable(ConstructWorld world, Construct c)
    {
        if (c.GrabbedBy != Holder || !ByWorld.TryGetValue(world, out var wg))
            return false;
        foreach (var g in wg.Guides)
            if (g.Body == c.Id)
                return g.Kind == Kind.Out && g.D >= g.Goal - 0.01f;
        return false;
    }

    /// <summary>Starts a guide on module body <paramref name="body"/> for the socket on plate <paramref name="plate"/> of <paramref name="ship"/>.</summary>
    public static void Start(ConstructWorld world, Construct ship, ModuleFitters.PartRef plate, Construct body, Kind kind, byte player, float d = 0)
    {
        var wg = ByWorld.GetOrCreateValue(world);
        wg.Guides.RemoveAll(x => x.Body == body.Id); // (an ejected module drawn back in: one guide a body)
        if (ship.Dirty)
            ship.Rebuild();
        var si = plate.Find(ship) ? ModuleSockets.SocketOfPlate(ship, plate.Part) : -1;
        var goal = si >= 0 ? ship.ModSockets[si].Seat + EjectClear : EjectClear;
        wg.Guides.Add(new Guide { Ship = ship.Id, Body = body.Id, Plate = plate, Kind = kind, D = d, Goal = goal, Player = player, At = body.ToWorld(body.Com), Want = kind == Kind.Pull }); // (a pull's first step moves it out: the player's tick that unseated it set no Want)
        body.GrabbedBy = Holder;
        body.GrabBoost = 1f;
        body.GrabLocal = body.Com;
        body.GrabTarget = body.ToWorld(body.Com);
        body.GrabRot = body.Rot;
        body.GrabAccel = ConstructGrab.AccelFor(body.Mass, true);
        body.HeldNear = ship.Id; // (no contact with the ship while its socket moves it: it slides along its well's liner)
        body.Vel = ship.Vel;
        body.AngVel = ship.AngVel;
        body.Wake();
    }

    private static void Let(Construct? body, Construct? ship)
    {
        if (body == null)
            return;
        if (body.GrabbedBy == Holder)
            body.GrabbedBy = 255;
        if (ship == null || body.HeldNear == ship.Id)
            body.HeldNear = 0;
        body.GrabBoost = 1f;
        body.Wake();
    }

    /// <summary>
    /// The pose module body <paramref name="body"/> takes seated on socket <paramref name="si"/>, and the socket's normal (world): null, or why it has none.
    /// </summary>
    private static string? Seated(Construct ship, int si, Construct body, int plug, out Quaternion rot, out Vector3 pos, out Vector3 n)
    {
        n = default;
        var why = ConstructEdits.SeatPose(ship, ship.ModSockets[si], body, plug, out _, out _, out rot, out pos);
        if (why != null)
            return why;
        ModuleSockets.PlateRest(ship, ship.ModSockets[si].Plate, out var plateRot, out _, out _);
        n = Vector3.Normalize(Vector3.Transform(Rot24.Apply(plateRot, Vector3.UnitY), ship.Rot));
        return null;
    }

    /// <summary>The ship's own motion where the module is (the guide moves it on top of that).</summary>
    private static void Frame(Construct ship, Construct body, Vector3 at)
    {
        body.GrabFrameVel = ship.Vel + Vector3.Cross(ship.AngVel, at - ship.WorldCom);
        body.GrabFrameSpin = ship.AngVel;
    }

    /// <summary>Server, each tick after the players: the guides move their modules; the plates' wires are read.</summary>
    public static void Step(ConstructWorld world, ConstructEdits hands)
    {
        var wg = ByWorld.GetOrCreateValue(world);
        wg.Ticks++;
        if ((wg.Ticks & 1) == 0)
            WireStep(world, hands);
        for (var k = wg.Guides.Count - 1; k >= 0; k--)
            if (StepGuide(world, hands, wg, wg.Guides[k]))
                wg.Guides.RemoveAt(k);
    }

    /// <summary>One guide's tick: true when it is over.</summary>
    private static bool StepGuide(ConstructWorld world, ConstructEdits hands, WorldGuides wg, Guide g)
    {
        var ship = world.Get(g.Ship);
        var body = world.Get(g.Body);
        if (body == null || body.GrabbedBy != Holder || ship == null || ship.PartCount == 0)
        {
            if (body?.GrabbedBy == Holder)
                Let(body, ship);
            else if (body != null && ship != null && body.HeldNear == ship.Id)
                body.HeldNear = 0; // (a gun took it: contacts with the ship again)
            EndPull(wg, g);
            return true;
        }
        if (ship.Dirty)
            ship.Rebuild();
        var si = g.Plate.Find(ship) ? ModuleSockets.SocketOfPlate(ship, g.Plate.Part) : -1;
        var plug = ConstructEdits.RootPlug(body);
        if (si < 0 || plug < 0 || Seated(ship, si, body, plug, out var rot, out var pos, out var n) is not null)
        {
            Let(body, ship);
            EndPull(wg, g);
            return true;
        }
        var s = ship.ModSockets[si];
        var dt = Dt;
        switch (g.Kind)
        {
            case Kind.Out:
                if (g.D < g.Goal)
                    g.D = MathF.Min(g.Goal, g.D + EjectSpeed * dt);
                else if ((g.Held += dt) >= HoverSeconds)
                {
                    Let(body, ship);
                    return true;
                }
                break;
            case Kind.Pull:
                if (g.Want)
                    g.D += (g.D < PullSlowFor ? PullSlow : PullFast) * dt;
                else
                    g.D -= PullBack * dt;
                g.Want = false; // (the player's tick sets it again while they pull)
                if (g.D <= 0)
                {
                    g.D = 0;
                    if (s.Module >= 0)
                        Let(body, ship); // (another module took the socket while it slid back: it is let go there)
                    else if (hands.FitBody(world, g.Player, ship, si, body, plug) is { } why)
                    {
                        Console.WriteLine($"[modules] construct {ship.Id}: module {body.Id} couldn't seat again: {why}");
                        Let(body, ship);
                    }
                    EndPull(wg, g);
                    return true;
                }
                if (g.D >= s.Seat + PullSlowFor)
                {
                    // Clear of its frame: the gun has it.
                    var eye = wg.Pulls[g.Player].Eye;
                    var yaw = wg.Pulls[g.Player].Yaw;
                    EndPull(wg, g);
                    body.ModSkipShip = ship.Id; // (pulled off on purpose: its own socket doesn't catch it straight back)
                    body.ModSkipPlate = g.Plate.Part;
                    if (hands.HandOver(world, g.Player, eye, yaw, body) is { } no)
                    {
                        hands.Notices.Add((g.Player, $"the module came off, but the gun couldn't hold it: {no}"));
                        Let(body, ship);
                    }
                    else if (body.GrabbedBy == Holder)
                        Let(body, ship);
                    if (body.HeldNear == ship.Id)
                        body.HeldNear = 0;
                    return true;
                }
                break;
            case Kind.In:
            {
                var mouth = pos + Vector3.Transform(body.Com, rot) + n * (s.Seat + Approach);
                if (!g.Over)
                {
                    var to = mouth - g.At;
                    var step = InSpeed * dt;
                    g.At = to.Length() <= step ? mouth : g.At + Vector3.Normalize(to) * step;
                    body.GrabTarget = g.At;
                    body.GrabRot = Quaternion.Slerp(body.Rot, rot, 0.08f);
                    Frame(ship, body, g.At);
                    if (Vector3.Distance(body.ToWorld(body.Com), mouth) < CatchLag && MathF.Abs(Quaternion.Dot(body.Rot, rot)) > 0.999f)
                    {
                        g.Over = true;
                        g.D = s.Seat + Approach;
                    }
                    if ((g.Held += dt) > 12f)
                    {
                        Let(body, ship); // (it never got there: something is in the way)
                        return true;
                    }
                    return false;
                }
                if (Vector3.Distance(body.ToWorld(body.Com), pos + Vector3.Transform(body.Com, rot) + n * g.D) < CatchLag)
                    g.D = MathF.Max(0, g.D - InSpeed * dt);
                if (g.D <= 0 && Vector3.Distance(body.ToWorld(body.Com), pos + Vector3.Transform(body.Com, rot)) < 0.03f)
                {
                    if (hands.FitBody(world, g.Player, ship, si, body, plug) is { } why)
                    {
                        Console.WriteLine($"[modules] construct {ship.Id}: the Attach wire couldn't fit module {body.Id}: {why}");
                        Let(body, ship);
                    }
                    return true;
                }
                break;
            }
        }
        var at = pos + Vector3.Transform(body.Com, rot) + n * g.D;
        body.GrabLocal = body.Com;
        body.GrabTarget = at;
        body.GrabRot = rot;
        body.GrabAccel = ConstructGrab.AccelFor(body.Mass, true);
        Frame(ship, body, at);
        body.Wake();
        return false;
    }

    private static void EndPull(WorldGuides wg, Guide g)
    {
        if (g.Kind != Kind.Pull)
            return;
        ref var p = ref wg.Pulls[g.Player];
        if (p.Phase == 2 && p.Body == g.Body)
        {
            p = default;
            wg.AnyPull = Math.Max(0, wg.AnyPull - 1);
        }
    }

    // ---- The plate's wires --------------------------------------------------------------------------------------------

    /// <summary>
    /// Every other tick: the plates' Attach, Eject and Lock wires. A rise on Attach ejects a fitted module or draws one in (<see cref="AttachNear"/>);
    /// a rise on Eject ejects; Lock high locks. At most one act a body a step (an act rebuilds it).
    /// </summary>
    public static void WireStep(ConstructWorld world, ConstructEdits hands)
    {
        var bodies = world.Bodies;
        for (var b = 0; b < bodies.Count; b++)
        {
            var c = bodies[b];
            if (c.ModSockets.Length == 0 || c.WireCount == 0 || c.Dirty)
                continue;
            for (var si = 0; si < c.ModSockets.Length; si++)
            {
                var s = c.ModSockets[si];
                if (s.Stand || s.Plate >= c.PartCount)
                    continue;
                var d = c.DefOf(s.Plate);
                float attach = 0, eject = 0, lck = 0;
                var wired = false;
                for (var w = 0; w < c.WireCount; w++)
                {
                    ref var x = ref c.Wires[w];
                    if (x.To != s.Plate || x.ToPort < d.ModData || x.From >= c.PartCount)
                        continue;
                    wired = true;
                    var v = c.Parts[x.From].Broken ? 0f : x.FromPort == 0 ? c.Parts[x.From].Out : x.FromPort == 1 ? c.Parts[x.From].Out1 : ConstructModules.Output(c, x.From, x.FromPort);
                    switch (x.ToPort - d.ModData)
                    {
                        case ModuleSockets.AttachIn: attach += v; break;
                        case ModuleSockets.EjectIn: eject += v; break;
                        case ModuleSockets.LockIn: lck += v; break;
                    }
                }
                ref var p = ref c.Parts[s.Plate];
                var was = (int)p.Mem;
                if (!wired)
                {
                    if (was != 0)
                        p.Mem = 0;
                    continue;
                }
                var now = (attach >= 0.5f ? MemAttach : 0) | (eject >= 0.5f ? MemEject : 0) | (lck >= 0.5f ? MemLockHigh : 0) | MemSeen;
                if (now == was)
                    continue;
                p.Mem = now;
                if ((was & MemSeen) == 0)
                    continue; // (the first read since a load or a new wire: levels, no rise)
                var rise = now & ~was;
                string? why = null;
                if ((rise & MemEject) != 0 && s.Module >= 0)
                    why = hands.ModuleEject(world, 255, c, si);
                else if ((rise & MemAttach) != 0)
                    why = s.Module >= 0 ? hands.ModuleEject(world, 255, c, si) : AttachNear(world, c, si);
                else
                    continue;
                if (why != null)
                    Console.WriteLine($"[modules] construct {c.Id}: socket part {s.Plate}'s wire: {why}");
                break; // (an act changes the body: the rest next step)
            }
        }
    }

    /// <summary>A rise on the Attach wire of an empty socket: the nearest loose module of its size within the hand's pull is drawn in. Null, or why not.</summary>
    public static string? AttachNear(ConstructWorld world, Construct c, int si)
    {
        var s = c.ModSockets[si];
        if (s.InTheWay >= 0)
            return $"in the way: the {c.DefOf(s.InTheWay).Name} is in the socket's room";
        if (!c.Rigid(c.Parts[s.Plate].Seg) || !ModuleSockets.PlateFrame(c, s.Plate, out _, out _))
            return "a socket on a moving joint can't draw a module in";
        if (ModuleFitters.Busy(world, c, s.Plate))
            return "a Module Fitter is at work on it";
        ModuleSockets.PlateRest(c, s.Plate, out _, out var faceAt, out _);
        var face = c.ToWorld(faceAt);
        var pull = ModuleSocketTable.Current.HandPull;
        Construct? best = null;
        var bestPlug = -1;
        var bestDist = pull;
        foreach (var o in world.Bodies)
        {
            if (o == c || o.GrabbedBy != 255 && !Takeable(world, o) || o.Driver != 255 || o.CarriedBy != 0 || o.HeldBy != 0 || o.Anchored || o.Pinned || o.NudgeBy != 255
                || Vector3.Distance(o.ToWorld(o.Com), face) > o.Radius + pull)
                continue;
            var plug = ConstructEdits.RootPlug(o);
            if (plug < 0 || !ModuleSockets.Matches(s, o.DefOf(plug)))
                continue;
            var all = true;
            for (var j = 0; j < o.PartCount && all; j++)
                all = ModuleSockets.ModuleOfPart(o, j) >= 0; // (a module standing on something else: F takes it off that first)
            if (!all)
                continue;
            o.SocketAt(plug, o.DefOf(plug).ModSocket, out var pat, out _);
            var dist = Vector3.Distance(o.ToWorld(pat), face);
            if (dist < bestDist)
                (best, bestPlug, bestDist) = (o, plug, dist);
        }
        if (best == null)
            return $"no loose {ModuleSocketTable.SizeText(s)} module within {pull:0.#} m";
        if (ConstructEdits.SeatPose(c, s, best, bestPlug, out _, out _, out var rot, out var pos) is { } noTurn)
            return noTurn;
        var mi = ModuleSockets.ModuleOfPart(best, bestPlug);
        if (ModuleSockets.FitRefusal(c, si, best, mi, rot, pos, byHand: false, creative: true) is { } why)
            return why;
        Start(world, c, ModuleFitters.PartRef.Of(c, s.Plate), best, Kind.In, 255);
        return null;
    }

    // ---- The physics gun ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The physics gun's beam on a fitted module's part: its pull begins (it unseats after <see cref="PullSeconds"/> of pulling).
    /// Null, or why it won't come (said at once, so a locked socket never seems to give).
    /// </summary>
    public static string? PullStart(ConstructWorld world, ConstructEdits hands, byte player, Construct c, int si, Vector3 eye, Vector3 local)
    {
        var s = c.ModSockets[si];
        if (s.Module < 0 || s.Stand)
            return "nothing to pull off";
        if (hands.Hand(player).Tool != ConstructTool.PhysicsGun || hands.Hand(player).Seated || hands.Hand(player).GrabBody != 0)
            return null;
        if (ModuleSockets.Locked(c, s.Plate))
            return "the socket is locked: slide its lock off to pull the module out";
        if (ModuleFitters.Busy(world, c, s.Plate))
            return "a Module Fitter is at work on that socket";
        var m = c.Modules[s.Module];
        if (m.Bridged)
            return "that module is welded to more than its socket";
        var creative = world.Creative && !world.TimedMachines;
        if (!creative && !ModuleSocketTable.Current.HandSize(s))
            return $"a {ModuleSocketTable.SizeText(s)} module is too big for the physics gun: a Module Fitter takes it off";
        if (m.Mass > hands.LiftOf(player, world.Creative))
            return $"the module weighs {m.Mass:0} kg: too heavy for the physics gun ({hands.LiftOf(player, world.Creative):0} kg)";
        var wg = ByWorld.GetOrCreateValue(world);
        ref var p = ref wg.Pulls[player];
        if (p.Phase != 0)
            return null;
        p = new Pull { Phase = 1, Active = true, Ship = c.Id, Plate = ModuleFitters.PartRef.Of(c, s.Plate), Local = local, Dist = MathF.Max(ConstructGrab.MinDist, Vector3.Distance(eye, c.ToWorld(local))) };
        wg.AnyPull++;
        return null;
    }

    /// <summary>The player let go of Fire: a strain ends; a module on its way out slides back and seats unless it is clear.</summary>
    public static void PullEnd(ConstructWorld world, byte player)
    {
        if (!ByWorld.TryGetValue(world, out var wg))
            return;
        ref var p = ref wg.Pulls[player];
        if (p.Phase == 1)
        {
            p = default;
            wg.AnyPull = Math.Max(0, wg.AnyPull - 1);
        }
        else
            p.Active = false;
    }

    /// <summary>Server, each tick for each living player, after their grab's tick: their pull on a fitted module, and a held module's pull-in.</summary>
    public static void PlayerTick(ConstructWorld world, ConstructEdits hands, byte player, Vector3 eye, Vector3 look, float yaw)
    {
        if (!ByWorld.TryGetValue(world, out var wg))
            wg = ByWorld.GetOrCreateValue(world);
        ref var h = ref hands.Hand(player);
        if (wg.AnyPull > 0 && wg.Pulls[player].Phase != 0)
            PullTick(world, hands, wg, player, eye, look, yaw);
        if (h.GrabBody != 0 && h.GrabSeg == 0 && h.Tool == ConstructTool.PhysicsGun && world.Get(h.GrabBody) is { } held && held.GrabbedBy == player)
            PullIn(world, hands, wg, player, held);
        ref var pull = ref wg.Pulls[player];
        if (pull.Phase != 0 && world.Get(pull.Ship) is { } pullShip && !pullShip.Dirty)
            pull.Plate.Find(pullShip); // (the cut rebuilt the ship: its plate's index may have moved; cheap when it didn't)
        ShowPull(ref h, in pull);
    }

    /// <summary>The pull as the hands send it to everyone (the beam to where the player clicked, the module's shake: ModulePullLook.cs).</summary>
    private static void ShowPull(ref SandboxHand h, in Pull p)
    {
        if (p.Phase == 0 || h.GrabBody != 0 || p.Plate.Part < 0)
        {
            h.PullShip = h.PullPlate = h.PullBody = 0;
            h.PullShake = 0;
            return;
        }
        h.PullShip = p.Ship;
        h.PullPlate = (ushort)p.Plate.Part;
        h.PullBody = p.Phase == 2 ? p.Body : (ushort)0;
        h.PullLocal = p.Phase == 2 ? p.BodyLocal : p.Local;
        h.PullShake = p.Phase == 2 ? (byte)(p.Pulling ? 255 : 60) : (byte)Math.Clamp(p.Strain / PullSeconds * 255f, 0f, 255f);
    }

    private static void PullTick(ConstructWorld world, ConstructEdits hands, WorldGuides wg, byte player, Vector3 eye, Vector3 look, float yaw)
    {
        ref var p = ref wg.Pulls[player];
        ref var h = ref hands.Hand(player);
        p.Eye = eye;
        p.Yaw = yaw;
        if (h.Tool != ConstructTool.PhysicsGun || h.Seated)
            p.Active = false;
        var ship = world.Get(p.Ship);
        if (ship == null)
        {
            p = default;
            wg.AnyPull = Math.Max(0, wg.AnyPull - 1);
            return;
        }
        var beam = eye + look * p.Dist;
        if (p.Phase == 1)
        {
            if (ship.Dirty)
                ship.Rebuild();
            var si = p.Active && p.Plate.Find(ship) ? ModuleSockets.SocketOfPlate(ship, p.Plate.Part) : -1;
            if (si < 0 || ship.ModSockets[si].Module < 0 || ModuleSockets.Locked(ship, p.Plate.Part))
            {
                p = default;
                wg.AnyPull = Math.Max(0, wg.AnyPull - 1);
                return;
            }
            ModuleSockets.PlateRest(ship, p.Plate.Part, out var plateRot, out _, out _);
            var n = Vector3.Normalize(Vector3.Transform(Rot24.Apply(plateRot, Vector3.UnitY), ship.Rot));
            var anchor = ship.ToWorld(p.Local);
            var pulling = Vector3.Dot(beam - anchor, n) > PullBeam;
            p.Strain = pulling ? p.Strain + Dt : MathF.Max(0, p.Strain - Dt);
            if (!p.Told && p.Strain > 0.15f)
            {
                hands.Notices.Add((player, "pulling the module out of its socket: keep pulling"));
                p.Told = true;
            }
            if (p.Strain < PullSeconds)
                return;
            var plateRef = ModuleFitters.PartRef.Of(ship, p.Plate.Part);
            if (hands.CutModule(world, player, ship, si, out var piece, out _) is { } why)
            {
                hands.Notices.Add((player, why));
                p = default;
                wg.AnyPull = Math.Max(0, wg.AnyPull - 1);
                return;
            }
            Start(world, ship, plateRef, piece!, Kind.Pull, player);
            p.Phase = 2;
            p.Body = piece!.Id;
            p.BodyLocal = piece.ToLocal(anchor);
            Console.WriteLine($"[modules] player {player}: unseated a module from socket part {plateRef.Part} of construct {ship.Id} -> {piece.Id} (pulling)");
            return;
        }
        // Out of its seat, on the socket's axis: it moves out only while the player pulls past it.
        if (world.Get(p.Body) is not { } body)
            return;
        foreach (var g in wg.Guides)
            if (g.Body == p.Body && g.Kind == Kind.Pull)
            {
                if (p.Active && body.GrabbedBy == Holder && p.Plate.Find(ship))
                {
                    ModuleSockets.PlateRest(ship, p.Plate.Part, out var plateRot, out _, out _);
                    var n = Vector3.Normalize(Vector3.Transform(Rot24.Apply(plateRot, Vector3.UnitY), ship.Rot));
                    g.Want = Vector3.Dot(beam - body.ToWorld(p.BodyLocal), n) > PullBeam * 0.5f;
                }
                p.Pulling = g.Want && p.Active;
                break;
            }
    }

    /// <summary>
    /// A module held by the gun near a free socket that takes it: the socket's pull draws it over the mouth and down onto its seat, blended
    /// with where the player holds it, unless they pull it away. Seated, the gun lets go and it is fitted.
    /// </summary>
    private static void PullIn(ConstructWorld world, ConstructEdits hands, WorldGuides wg, byte player, Construct held)
    {
        var dt = Dt;
        Construct? ship = held.ModCatchShip != 0 ? world.Get(held.ModCatchShip) : null;
        if (ship == null)
        {
            if (wg.Ticks < held.ModCatchLook)
                return;
            held.ModCatchLook = wg.Ticks + World.TickRate / 4; // (a look four times a second while nothing is caught)
            if (!FindSocket(world, hands, player, held, out ship, out var plate))
                return;
            held.ModCatchShip = ship!.Id;
            held.ModCatchPlate = plate;
            held.ModCatch = 0;
        }
        if (ship!.Dirty)
            ship.Rebuild();
        var si = (uint)held.ModCatchPlate < (uint)ship.PartCount ? ModuleSockets.SocketOfPlate(ship, held.ModCatchPlate) : -1;
        var plug = ConstructEdits.RootPlug(held);
        if (si < 0 || plug < 0 || ship.ModSockets[si].Module >= 0 || Seated(ship, si, held, plug, out var rot, out var pos, out var n) is not null)
        {
            Uncatch(held, ship);
            return;
        }
        var s = ship.ModSockets[si];
        var seat = pos + Vector3.Transform(held.GrabLocal, rot);
        var mouth = seat + n * (s.Seat + Approach);
        var aim = held.GrabTarget; // (the player's: GrabTick set it this tick)
        var aimRot = held.GrabRot;
        var resisting = Vector3.Distance(aim, seat) > CatchRange + 0.5f;
        var now = held.ToWorld(held.GrabLocal);
        Vector3 want;
        Quaternion wantRot;
        float k;
        if (held.ModCatch < 0.6f)
        {
            k = Ease(held.ModCatch / 0.6f);
            want = Vector3.Lerp(aim, mouth, k);
            wantRot = Quaternion.Slerp(aimRot, rot, k);
        }
        else
        {
            k = (held.ModCatch - 0.6f) / 0.4f;
            want = Vector3.Lerp(mouth, seat, k);
            wantRot = rot;
        }
        var near = Vector3.Distance(now, want) < CatchLag + (held.ModCatch < 0.6f ? 0.25f : 0);
        if (resisting)
            held.ModCatch -= dt / LoseSeconds;
        else if (near || held.ModCatch < 0.55f)
            held.ModCatch = MathF.Min(1, held.ModCatch + dt / CatchSeconds);
        if (held.ModCatch <= 0)
        {
            Uncatch(held, ship);
            held.ModCatchLook = wg.Ticks + World.TickRate; // (pulled away: not caught again for a second)
            return;
        }
        held.HeldNear = held.ModCatch >= 0.6f ? ship.Id : (ushort)0; // (going down its well: no contact with the frame or the liner)
        held.GrabTarget = want;
        held.GrabRot = wantRot;
        if (held.ModCatch >= 1 && Vector3.Distance(now, seat) < 0.03f && MathF.Abs(Quaternion.Dot(held.Rot, rot)) > 0.9995f)
        {
            hands.DropHeld(world, player);
            Uncatch(held, ship);
            if (hands.FitBody(world, player, ship, si, held, plug) is { } why)
                hands.Notices.Add((player, $"it won't seat: {why}"));
        }
    }

    private static float Ease(float t) => t * t * (3 - 2 * t);

    /// <summary>A caught module let go by its gun: the socket's pull ends and it has contacts with that ship again.</summary>
    public static void Uncatch(Construct held)
    {
        if (held.GrabbedBy != Holder && held.HeldNear == held.ModCatchShip)
            held.HeldNear = 0;
        held.ModCatchShip = 0;
        held.ModCatchPlate = -1;
        held.ModCatch = 0;
    }

    private static void Uncatch(Construct held, Construct? ship)
    {
        if (ship != null && held.HeldNear == ship.Id)
            held.HeldNear = 0;
        held.ModCatchShip = 0;
        held.ModCatchPlate = -1;
        held.ModCatch = 0;
    }

    /// <summary>A free socket of another body that takes <paramref name="held"/>'s module, its plug within <see cref="CatchRange"/> of the socket's face, and the fit's rules pass at its seat.</summary>
    private static bool FindSocket(ConstructWorld world, ConstructEdits hands, byte player, Construct held, out Construct? ship, out int plate)
    {
        ship = null;
        plate = -1;
        var plug = ConstructEdits.RootPlug(held);
        if (plug < 0)
            return false;
        for (var j = 0; j < held.PartCount; j++)
            if (ModuleSockets.ModuleOfPart(held, j) < 0)
                return false; // (it stands on something else: not a module alone)
        var pd = held.DefOf(plug);
        held.SocketAt(plug, pd.ModSocket, out var pat, out _);
        var plugAt = held.ToWorld(pat);
        var best = CatchRange;
        var mi = ModuleSockets.ModuleOfPart(held, plug);
        if (held.ModSkipShip != 0 && (world.Get(held.ModSkipShip) is not { } skip || (uint)held.ModSkipPlate >= (uint)skip.PartCount
            || Vector3.Distance(skip.SegToWorld(skip.Parts[held.ModSkipPlate].Seg, skip.Parts[held.ModSkipPlate].Pos), plugAt) > CatchRange + 0.25f))
        {
            held.ModSkipShip = 0; // (carried clear of the socket it came out of: that one may catch it again)
            held.ModSkipPlate = -1;
        }
        foreach (var c in world.Bodies)
        {
            if (c == held || c.ModSockets.Length == 0 || Vector3.Distance(c.ToWorld(c.Com), plugAt) > c.Radius + CatchRange)
                continue;
            for (var si = 0; si < c.ModSockets.Length; si++)
            {
                var s = c.ModSockets[si];
                if (s.Stand || s.Module >= 0 || s.InTheWay >= 0 || !ModuleSockets.Matches(s, pd) || !c.Rigid(c.Parts[s.Plate].Seg)
                    || c.Id == held.ModSkipShip && s.Plate == held.ModSkipPlate)
                    continue;
                ModuleSockets.PlateRest(c, s.Plate, out _, out var faceAt, out _);
                var dist = Vector3.Distance(c.ToWorld(faceAt), plugAt);
                if (dist >= best || ModuleFitters.Busy(world, c, s.Plate))
                    continue;
                if (ConstructEdits.SeatPose(c, s, held, plug, out _, out _, out var rot, out var pos) != null
                    || ModuleSockets.FitRefusal(c, si, held, mi, rot, pos, byHand: true, world.Creative, lift: hands.LiftOf(player, world.Creative)) != null)
                    continue;
                (ship, plate, best) = (c, s.Plate, dist);
            }
        }
        return ship != null;
    }

    /// <summary>Client and server: would a module held near here be caught (the client's prediction gives way to the server's pose)?</summary>
    public static bool NearFreeSocket(ConstructWorld world, Construct held)
    {
        if (held.Modules.Length == 0)
            return false;
        var plug = held.Modules[0].Plug;
        if ((uint)plug >= (uint)held.PartCount)
            return false;
        var pd = held.DefOf(plug);
        held.SocketAt(plug, pd.ModSocket, out var pat, out _);
        var plugAt = held.ToWorld(pat);
        foreach (var c in world.Bodies)
        {
            if (c == held || c.ModSockets.Length == 0 || Vector3.Distance(c.ToWorld(c.Com), plugAt) > c.Radius + CatchRange + 0.5f)
                continue;
            foreach (var s in c.ModSockets)
                if (!s.Stand && s.Module < 0 && ModuleSockets.Matches(s, pd)
                    && Vector3.Distance(c.SegToWorld(c.Parts[s.Plate].Seg, c.Parts[s.Plate].Pos), plugAt) < CatchRange + 0.5f + s.Wm + s.Lm)
                    return true;
        }
        return false;
    }
}
