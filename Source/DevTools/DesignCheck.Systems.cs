using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// The design check's systems (DesignCheck.cs): <c>--systems</c>. The design with a floodlight, a tow hitch, a docking clamp, a magnetic lock
/// and a hull grapple added where it has none (on its first body's top and keel: a test ship, never written anywhere), built on the ground
/// in a world of its own, then each system worked through the game's own code: the floods lit from the helm's switch (and put out), a tow
/// eye coupled to the hitch, a loose beam caught by the clamp, the grapple's hook key toggled, and the guns fired on the helm's fire key
/// (none before it). Last a joined client's copy (the full body, its poses, the couplings and the shots, through the wire) agrees with
/// the host's. Short: about 12 s of world steps.
/// </summary>
public static partial class DesignCheck
{
    private static void Systems(Run r)
    {
        var d = ShipDesign.FromJson(r.Text);
        var top = d.Hulls.Count > 0 ? d.Hulls[0].Id : -1;
        var added = new List<string>();
        void Add(string kind, double z, double t, Action<Piece>? set = null)
        {
            if (top < 0 || d.Pieces.Any(p => p.Kind == kind))
                return;
            var p = new Piece { Id = d.Pieces.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1, Kind = kind, Hull = top, Z = z, T = t, Side = 1, Pair = false };
            set?.Invoke(p);
            d.Pieces.Add(p);
            added.Add(kind);
        }
        var (front, len) = d.Hulls.Count > 0 ? (d.Hulls[0].Front, d.Hulls[0].Back - d.Hulls[0].Front) : (0, 40);
        Add("Floodlight M", front + len * 0.62, 1, p => p.Pitch = 45);
        Add("Docking clamp S", front + len * 0.68, 1);
        Add("Magnetic lock", front + len * 0.12, 1);
        Add("Hull grapple", front + len * 0.56, 1);
        Add("Tow hitch", front + len * 0.95, 0);
        Console.WriteLine($"[design] --systems: {(added.Count > 0 ? "added " + string.Join(", ", added) : "the design's own systems")}");
        if (DesignParts.Canon(d.ToJson()) is not { } text || DesignParts.Register(text) is not { } hash)
        {
            r.Fail($"--systems: the test ship isn't a design: {DesignParts.LastRefusal}");
            return;
        }
        var world = new ConstructWorld();
        var job = new ConstructBuild.GroundJob(world, DesignParts.BlueprintOf(hash, d.Name + " (systems)"));
        while (!job.Step(VehicleSpawner.SliceMs))
        {
        }
        if (job.Body == null || job.Join(r.Ground + new Vector3(0, 0, 0), 0, out var why) is not { } c)
        {
            r.Fail($"--systems: the test ship wasn't built: {job.Why}");
            return;
        }
        var ok = 0;
        void Say(bool pass, string what)
        {
            if (pass)
            {
                ok++;
                Console.WriteLine("[design]   ok " + what);
            }
            else
                r.Fail("--systems: " + what);
        }
        int held = -1, keySeat = -1; // (the helm's keys, held each tick as a sitter's are: the logic tick clears them)
        void Tick(int n)
        {
            for (var k = 0; k < n; k++)
            {
                if (keySeat >= 0 && held >= 0 && c.Parts[keySeat].Keys != (ushort)held)
                {
                    c.Parts[keySeat].Keys = (ushort)held;
                    c.Wake(); // (as a sitter's keys reaching the seat: ConstructEdits)
                }
                world.Step(World.Query, World.TickDt);
            }
        }
        int Find(Func<ConstructPartDef, bool> f)
        {
            for (var i = 0; i < c.PartCount; i++)
                if (f(c.DefOf(i)))
                    return i;
            return -1;
        }
        string V(Vector3 v) => $"({v.X:0.00} {v.Y:0.00} {v.Z:0.00})";
        Tick(30);
        Console.WriteLine($"[design] --systems ship: {c.PartCount} parts, {c.RouteCount} wires, {c.GunCount} guns, {c.Mass / 1000:0.0} t");
        c.WorldFrame(0, out var q, out var t0);

        // 1. The floodlights: lit from the spawn by the helm's switch, aimed as drawn, out when it is thrown.
        var lamp = Find(x => DesignParts.IsDesign(x) && x.Logic == CLogic.Lamp && x.LightRange > 0 && x.Breach == BreachKind.None);
        var sw = Find(x => DesignParts.IsDesign(x) && x.Logic == CLogic.Switch);
        if (lamp >= 0 && sw >= 0)
        {
            var ld = c.DefOf(lamp);
            var beam = Vector3.Transform(Rot24.Apply(c.Parts[lamp].Rot, ld.LightDir), q);
            Say(ConstructLamps.Lit(c, lamp), $"the {ld.Name.Split(' ').Last()}'s floodlight is lit from the helm's switch: {ld.LightRange:0} m, a {ld.LightCone:0} degree cone, beam {V(beam)} (up {Vector3.Dot(beam, Vector3.UnitY):0.00}), colour #{(int)ConstructConfig.Get(c.Parts[lamp], ld, CfgKey.Colour, 0):x6}");
            c.Parts[sw].State ^= 1; c.Wake(); // (as F on it: ConstructEdits.Use)
            Tick(20);
            Say(!ConstructLamps.Lit(c, lamp), "the switch thrown puts it out");
            c.Parts[sw].State ^= 1; c.Wake();
            Tick(20);
        }
        else
            r.Fail($"--systems: no floodlight (lamp {lamp}) or no lights switch ({sw})");

        // 2. The tow hitch: a loose Tow Eye brought to its ball couples, and stays coupled.
        var hitch = Find(x => DesignParts.IsDesign(x) && x.Role == PartRole.Hitch);
        Construct? eye = null;
        if (hitch >= 0)
        {
            var ball = ConstructWorld.HitchPoint(c, hitch);
            var eyeDef = ConstructParts.Need("Tow Eye");
            eye = ConstructBuild.PlaceNew(world, eyeDef, new FreePlacement { Pos = ball - eyeDef.HitchAt, Rot = Quaternion.Identity });
            if (eye == null)
                r.Fail("--systems: no room for a tow eye");
            else
            {
                var no = world.CanCouple(c, hitch, eye, 0);
                if (no == null)
                    world.Couple(c, hitch, eye, 0);
                Tick(60);
                Say(no == null && world.Coupled(c.Id, eye.Id), $"a tow eye couples to the hitch at {V(ball)} ({no ?? "coupled"}), {Vector3.Distance(ConstructWorld.HitchPoint(c, hitch), ConstructWorld.HitchPoint(eye, 0)):0.00} m apart after 1 s");
            }
        }
        else
            r.Fail("--systems: no tow hitch");

        // 3. The docking clamp: turned on, it catches a beam let go in its box.
        var clamp = Find(x => DesignParts.IsDesign(x) && x.IsDock && x.Name.Length > 0 && x.DockChute);
        clamp = clamp >= 0 ? clamp : Find(x => DesignParts.IsDesign(x) && x.IsDock);
        Construct? caught = null;
        if (clamp >= 0)
        {
            ConstructCargo.Box(c, clamp, out var centre, out var half);
            var at = t0 + Vector3.Transform(centre, q);
            caught = ConstructBuild.PlaceNew(world, ConstructParts.Need("Frame Beam"), new FreePlacement { Pos = at, Rot = q });
            c.Parts[clamp].State ^= 1; // (as F on it)
            c.Wake();
            Tick(120);
            Say(caught != null && caught.CarriedBy == c.Id, $"the {c.DefOf(clamp).Name.Split(' ').Last()} clamp catches a beam in its box at {V(at)} (half {V(half)}): carried by {caught?.CarriedBy}");
        }
        else
            r.Fail("--systems: no docking clamp");
        Say(Find(x => DesignParts.IsDesign(x) && x.IsDock && !x.DockChute) >= 0, "the magnetic lock is a dock");

        // 4. The grapple: the helm's hook key toggles it.
        var helm = c.DriveSeat >= 0 ? c.DriveSeat : Find(x => DesignParts.IsDesign(x) && x.Role == PartRole.Seat && x.Helm);
        var grapple = Find(x => DesignParts.IsDesign(x) && x.Breach != BreachKind.None);
        if (helm < 0)
        {
            r.Fail("--systems: no helm");
            return;
        }
        c.Parts[helm].SitterPlus1 = 1;
        (c.Driver, c.DriverSeat) = (0, helm);
        (keySeat, held) = (helm, CPart.Occupied);
        c.Wake();
        Tick(5);
        if (grapple >= 0)
        {
            var before = c.Parts[grapple].Out;
            held = CPart.Occupied | ConstructParts.KeyHook;
            Tick(3);
            held = CPart.Occupied;
            Tick(20);
            Say(before < 0.5f && c.Parts[grapple].Out >= 0.5f && ConstructLogic.WiredIn(world, c, grapple), $"the helm's hook key works the grapple ({before:0} -> {c.Parts[grapple].Out:0})");
            held = CPart.Occupied | ConstructParts.KeyHook; // (and again: it lets go)
            Tick(3);
            held = CPart.Occupied;
            Tick(20);
            Say(c.Parts[grapple].Out < 0.5f, "a second press lets it go");
        }
        else
            r.Fail("--systems: no grapple");

        // 5. The guns: none fires while the helm only sits; on the fire key every gun with a game gun fires (after its first load).
        var fired = new HashSet<int>();
        void Count()
        {
            for (var k = 0; k < world.FireCount; k++)
                if (world.Fires[k].Kind is CFireKind.Shot or CFireKind.ShellOut && world.Fires[k].Body == c.Id)
                    fired.Add(world.Fires[k].Part);
        }
        for (var k = 0; k < 60; k++)
        {
            Tick(1);
            Count();
        }
        Say(fired.Count == 0, $"no gun fires with the fire key up ({fired.Count})");
        for (var k = 0; k < c.GunCount; k++)
        {
            ref var g = ref c.Guns[k];
            ConstructWeapons.MuzzleWorld(c, k, out var muzzle, out var fwd);
            Console.WriteLine($"[design]   gun {c.DefOf(g.Part).Name.Split(' ').Last()}: muzzle {V(muzzle)}, bore {V(fwd)}, director {g.Director}, boxes {g.BoxCount}, {ConstructWeapons.BoxRounds(c, k, world)} rounds, {(string.IsNullOrEmpty(g.Problem) ? "no problem" : g.Problem)}");
        }
        held = CPart.Occupied | ConstructParts.KeyFire;
        var (shots, worstFire, sumFire) = (0, 0.0, 0.0);
        var client = new ConstructWorld();
        var cfires = new CFireEvent[256];
        var clientShots = 0;
        for (var k = 0; k < 9 * World.TickRate; k++)
        {
            var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            Tick(1);
            worstFire = Math.Max(worstFire, Ms(t1));
            sumFire += Ms(t1);
            Count();
            shots += world.FireCount;
            if (world.FireCount > 0)
            {
                var fw = new PacketWriter(4096);
                ConstructNet.WriteFires(fw, world);
                var fr = new PacketReader(fw.Span[1..]);
                clientShots += ConstructNet.ReadFires(ref fr, cfires);
            }
        }
        held = CPart.Occupied;
        for (var k = 0; k < c.GunCount; k++)
        {
            ref var gp = ref c.Parts[c.Guns[k].Part];
            Console.WriteLine($"[design]   gun {c.DefOf(c.Guns[k].Part).Name.Split(' ').Last()} after: {(fired.Contains(c.Guns[k].Part) ? "fired" : "never fired")}, {gp.Rounds} rounds in, reload {gp.Reload}, cool {gp.Cool}, its input {gp.In:0.0} ({gp.InCount} wires), broken {gp.Broken}");
        }
        var guns = Enumerable.Range(0, c.GunCount).Select(k => (int)c.Guns[k].Part).ToHashSet();
        var fixedGuns = Enumerable.Range(0, c.GunCount).Where(k => c.Guns[k].AimJoints == 0).Select(k => (int)c.Guns[k].Part).ToHashSet();
        Say(c.GunCount > 0 && fixedGuns.IsSubsetOf(fired), $"the fire key fires {fired.Count} of {c.GunCount} guns, every fixed one of {fixedGuns.Count} ({shots} shot events in 9 s, a tick firing {sumFire / (9 * World.TickRate):0.00} ms on average, the worst {worstFire:0.00} ms; a turret laid across its own hull holds fire)");

        // 5b. The turrets: the helm's look swung 35 degrees off the bow to each side, level; each turret on that side lays its weapon on what
        // the look meets (its bearing and trunnion, ConstructWeapons.Aim through the director) and fires on the fire key.
        var turrets = Enumerable.Range(0, c.GunCount).Where(k => c.Guns[k].AimJoints > 0).ToList();
        if (turrets.Count > 0)
        {
            c.WorldFrame(0, out var bq, out _);
            var bow = Vector3.Transform(-Vector3.UnitZ, bq);
            var side = Vector3.Transform(Vector3.UnitX, bq);
            var laid = 0;
            foreach (var s in new[] { 1f, -1f })
            {
                var look = Vector3.Normalize(bow * MathF.Cos(35 * MathF.PI / 180) + side * (s * MathF.Sin(35 * MathF.PI / 180)));
                (c.Parts[helm].AimYaw, c.Parts[helm].AimPitch) = (MathF.Atan2(look.X, -look.Z), MathF.Asin(Math.Clamp(look.Y, -1, 1)));
                fired.Clear();
                held = CPart.Occupied;
                Tick(150); // (60 degrees a second: round and laid)
                held = CPart.Occupied | ConstructParts.KeyFire;
                for (var k = 0; k < 90; k++)
                {
                    Tick(1);
                    Count();
                }
                held = CPart.Occupied;
                var point = ConstructWeapons.GunnerPoint(world, c, helm, World.Query, ConstructWeapons.DirectorReach);
                foreach (var k in turrets)
                {
                    ConstructWeapons.MuzzleWorld(c, k, out var muzzle, out var fwd);
                    if (MathF.Sign(Vector3.Dot(c.ToLocal(muzzle), Vector3.UnitX)) != s)
                        continue; // (the other side's: across the hull)
                    var want = Vector3.Normalize(ConstructWeapons.AimWay(c, k, muzzle, point)); // (the way it lays for that point: a round's drop allowed for)
                    var off = MathF.Acos(Math.Clamp(Vector3.Dot(fwd, want), -1, 1)) * 180 / MathF.PI;
                    ref var g = ref c.Guns[k];
                    ref var el = ref c.Joints[g.Seg - 1];
                    ref var br = ref c.Joints[Math.Max(0, el.ParentSeg - 1)];
                    var ok1 = off < 3 && fired.Contains(g.Part);
                    laid += ok1 ? 1 : 0;
                    Say(ok1, $"turret {c.DefOf(g.Part).Name.Split(' ').Last()} (side {s:+0;-0}): laid {off:0.0} degrees off its aim, bore {V(fwd)}, bearing {br.Angle * 180 / MathF.PI:0} degrees, trunnion {el.Angle * 180 / MathF.PI:0} (err {el.AimErr * 180 / MathF.PI:0.0} aimed {el.Aimed} load {el.Load:0} of {el.Strength:0}, limits {el.Min * 180 / MathF.PI:0} to {el.Max * 180 / MathF.PI:0}, axis {V(el.Axis)}), {(fired.Contains(g.Part) ? "fired" : "never fired")}");
                }
            }
            (c.Parts[helm].AimYaw, c.Parts[helm].AimPitch) = (0, 0);
        }

        // 6. A joined client: the body, its poses, the couplings and the shots, through the wire.
        var pw = new PacketWriter(1 << 20);
        ConstructNet.WriteFull(pw, c);
        var rd = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref rd, client, 0);
        foreach (var b in new[] { eye, caught })
            if (b != null)
            {
                pw = new PacketWriter(1 << 16);
                ConstructNet.WriteFull(pw, b);
                rd = new PacketReader(pw.Span[1..]);
                ConstructNet.Read(ref rd, client, 0);
            }
        pw = new PacketWriter(1 << 16);
        ConstructNet.WriteLinks(pw, world);
        rd = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref rd, client, 0);
        var cursor = 0;
        uint last = 0;
        foreach (var b in world.Bodies)
            b.Asleep = false;
        pw = new PacketWriter(1 << 20);
        ConstructNet.WritePoses(pw, world, 100, ref cursor);
        rd = new PacketReader(pw.Span[1..]);
        ConstructNet.ReadPoses(ref rd, client, 1.0, ref last);
        if (client.Get(c.Id) is not { } copy)
        {
            r.Fail("--systems: the client has no copy of the ship");
            return;
        }
        if (copy.Dirty)
            copy.Rebuild();
        Say(copy.PartCount == c.PartCount && copy.RouteCount == c.RouteCount && copy.GunCount == c.GunCount, $"the client's copy: {copy.PartCount} parts, {copy.RouteCount} wires, {copy.GunCount} guns (the host's {c.PartCount}, {c.RouteCount}, {c.GunCount})");
        if (turrets.Count > 0)
        {
            var worst = 0f;
            foreach (var k in turrets)
                for (int s = c.Guns[k].Seg, guard = 0; s > 0 && s <= c.JointCount && guard < 4; guard++, s = c.Joints[s - 1].ParentSeg)
                    worst = MathF.Max(worst, MathF.Abs(copy.Joints[s - 1].Angle - c.Joints[s - 1].Angle));
            Say(copy.GunCount == c.GunCount && worst < 0.02f, $"the client's turrets stand as the host's (the worst joint {worst * 180 / MathF.PI:0.00} degrees apart)");
        }
        if (lamp >= 0)
            Say(ConstructLamps.Lit(copy, lamp) == ConstructLamps.Lit(c, lamp), $"the client sees the floodlight {(ConstructLamps.Lit(copy, lamp) ? "lit" : "out")}, as the host");
        if (eye != null)
            Say(client.Coupled(c.Id, eye.Id), "the client has the tow coupling");
        if (caught != null)
            Say(client.Get(caught.Id) is { } cc && cc.CarriedBy == c.Id, "the client sees the beam held by the clamp");
        if (grapple >= 0 && clamp >= 0)
            Say(copy.Parts[clamp].Out == c.Parts[clamp].Out && copy.Parts[grapple].Out == c.Parts[grapple].Out, "the client's clamp and grapple are as the host's");
        Say(clientShots == shots, $"the client reads every shot ({clientShots} of {shots})");
        Console.WriteLine($"[design] --systems: {ok} ok");
        Hangars(r);
    }

    /// <summary>
    /// The hangars: the design's first body widened 3.5 times (its rooms and pieces left out: a test carrier, never written anywhere) with a
    /// hangar bay S on its back, a docking cradle in it, a lift platform and a ramp deck on its keel; held still 15 m over the ground (pinned).
    /// The bay's door opened (F), a small craft (the Shuttle, else a lighter shipped one the bay takes) set in the bay and held, then let go
    /// out of its mouth; the platform lowered and the ramp let down (F); last a joined client's copy agrees. About 20 s of world steps.
    /// </summary>
    private static void Hangars(Run r)
    {
        var o = System.Text.Json.Nodes.JsonNode.Parse(r.Text)!.AsObject();
        if (o["hulls"] is not System.Text.Json.Nodes.JsonArray { Count: > 0 } hulls)
        {
            r.Fail("--systems: no hull body for the hangar test");
            return;
        }
        const double widen = 6, stretch = 2; // (a Hangar bay M, 40 x 16 x 40 m: the Shuttle's 12 t is past an S's 6 t)
        var hull = hulls[0]!.DeepClone();
        foreach (var st in hull["stations"]!.AsArray())
        {
            st!["z"] = (double)st["z"]! * stretch;
            foreach (var pt in st["p"]!.AsArray())
                (pt![0], pt[1]) = ((double)pt[0]! * widen, (double)pt[1]! * widen);
        }
        o["hulls"] = new System.Text.Json.Nodes.JsonArray(hull);
        foreach (var list in new[] { "rooms", "doors", "risers", "windows", "passages", "joins", "pieces" })
            o[list] = new System.Text.Json.Nodes.JsonArray();
        o["name"] = (string?)o["name"] + " (hangar test)";
        var d = ShipDesign.FromJson(o.ToJsonString());
        var h = d.Hulls[0];
        var (front, len) = (h.Front, h.Back - h.Front);
        Piece Add(string kind, double z, double t)
        {
            var p = new Piece { Id = d.Pieces.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1, Kind = kind, Hull = h.Id, Z = z, T = t, Side = 1, Pair = false };
            d.Pieces.Add(p);
            DesignHangar.Init(d, p);
            return p;
        }
        var bayPiece = Add("Hangar bay M", front + len * 0.55, 1);
        Add("Docking cradle", front + len * 0.55, 1);
        Add("Lift platform", front + len * 0.3, 0);
        Add("Ramp deck", front + len * 0.8, 0);
        if (DesignParts.Canon(d.ToJson()) is not { } text || DesignParts.Register(text) is not { } hash)
        {
            r.Fail($"--systems: the hangar test ship isn't a design: {DesignParts.LastRefusal}");
            return;
        }
        var world = new ConstructWorld();
        var job = new ConstructBuild.GroundJob(world, DesignParts.BlueprintOf(hash, d.Name));
        while (!job.Step(VehicleSpawner.SliceMs))
        {
        }
        if (job.Body == null || job.Join(r.Ground, 0, out _) is not { } c)
        {
            r.Fail($"--systems: the hangar test ship wasn't built: {job.Why}");
            return;
        }
        // Held still over the ground, so its keel's platform and ramp have room to come down.
        c.Pos += new Vector3(0, 15, 0);
        c.PrevPos = c.Pos;
        c.Vel = c.AngVel = Vector3.Zero;
        c.Pinned = true;
        c.Rebuild();
        var ok = 0;
        void Say(bool pass, string what)
        {
            if (pass)
            {
                ok++;
                Console.WriteLine("[design]   ok " + what);
            }
            else
                r.Fail("--systems: " + what);
        }
        void Tick(int n)
        {
            for (var k = 0; k < n; k++)
                world.Step(World.Query, World.TickDt);
        }
        int Find(Func<ConstructPartDef, bool> f)
        {
            for (var i = 0; i < c.PartCount; i++)
                if (DesignParts.IsDesign(c.DefOf(i)) && f(c.DefOf(i)))
                    return i;
            return -1;
        }
        string V(Vector3 v) => $"({v.X:0.00} {v.Y:0.00} {v.Z:0.00})";
        Tick(10);
        Console.WriteLine($"[design] --systems hangar ship: {c.PartCount} parts, {c.JointCount} joints, {c.Mass / 1000:0.0} t, pinned {c.Anchored}");

        // 1. The bay: a dock that throws craft out of its mouth; its door opens on F.
        var bay = Find(x => x.IsDock && x.DockEject > 0);
        var door = Find(x => x.Door != null && x.Name.EndsWith($"p{d.Pieces.IndexOf(bayPiece)}.d0", StringComparison.Ordinal));
        if (bay < 0 || door < 0)
        {
            r.Fail($"--systems: the bay (part {bay}) or its door ({door}) wasn't made");
            return;
        }
        ConstructCargo.Box(c, bay, out var centre, out var half);
        c.WorldFrame(0, out var q, out var t0);
        var boxAt = t0 + Vector3.Transform(centre, q);
        var mouth = Vector3.Normalize(Vector3.Transform(Rot24.Apply(c.Parts[bay].Rot, -Vector3.UnitZ), q));
        Console.WriteLine($"[design]   bay: box at {V(boxAt)}, half {V(half)} (the part's, its side at most {ConstructCargo.SideLimit(c.DefOf(bay)):0} m), mouth {V(mouth)}, holding {ConstructCargo.IsFrozen(c.Parts[bay])}");
        var refused = ConstructDoors.Use(world, c, door);
        Tick((int)(c.DefOf(door).Door!.Seconds * World.TickRate) + 20);
        Say(string.IsNullOrEmpty(refused) && ConstructDoors.SettledOpen(c, door), $"F opens the bay's door ({(string.IsNullOrEmpty(refused) ? "opened" : refused)}; {(c.DefOf(door).Door!.Slide ? $"slid {c.DefOf(door).Door!.Travel:0.0} m" : "swung")})");

        // 2. A craft set in the bay settles and is held; let go, it leaves through the mouth.
        Construct? craft = null;
        foreach (var name in new[] { "Shuttle", "Limpet", "Scout Drone" })
        {
            if (ConstructBuild.FindBlueprint(name) is not { } bp)
                continue;
            var cj = new ConstructBuild.GroundJob(world, bp);
            while (!cj.Step(VehicleSpawner.SliceMs))
            {
            }
            if (cj.Body == null || cj.JoinAt(boxAt, q, 0, out _) is not { } b)
                continue;
            if (b.Mass > c.DefOf(bay).DockMaxMass)
            {
                Console.WriteLine($"[design]   the {name} ({b.Mass / 1000:0.0} t) is past the bay's {c.DefOf(bay).DockMaxMass / 1000:0.0} t: a lighter one");
                world.Remove(b, quiet: true);
                continue;
            }
            // Its middle on the box's middle, a little off its back wall.
            var shift = boxAt - b.WorldCom;
            b.Pos += shift;
            b.PrevPos = b.Pos;
            b.Vel = b.AngVel = Vector3.Zero;
            craft = b;
            Console.WriteLine($"[design]   craft: the {name}, {b.Mass / 1000:0.0} t, {b.PartCount} parts, set at {V(b.WorldCom)}");
            break;
        }
        if (craft == null)
        {
            r.Fail("--systems: no shipped craft could be set in the bay");
            return;
        }
        for (var k = 0; k < 8 * World.TickRate && craft.CarriedBy != c.Id; k++)
            Tick(1);
        Say(craft.CarriedBy == c.Id, $"the bay holds the craft (carried by {craft.CarriedBy}, at {V(craft.WorldCom)})");
        var heldAt = craft.WorldCom;
        Tick(60);
        Say(craft.CarriedBy == c.Id && Vector3.Distance(craft.WorldCom, heldAt) < 0.05f, $"and keeps it still ({Vector3.Distance(craft.WorldCom, heldAt):0.000} m in 1 s)");
        c.Parts[bay].State ^= 1; // (as F on it: the bay lets go)
        c.Wake();
        var (outV, outOf) = (0f, 0f);
        for (var k = 0; k < 90; k++)
        {
            Tick(1);
            outV = MathF.Max(outV, Vector3.Dot(craft.Vel, mouth));
            outOf = MathF.Max(outOf, Vector3.Dot(craft.WorldCom - boxAt, mouth));
        }
        Say(craft.CarriedBy == 0 && outV > 0.5f * c.DefOf(bay).DockEject && outOf > 1, $"let go, the craft is thrown out of the mouth at {outV:0.0} m/s (the bay's {c.DefOf(bay).DockEject:0}), at most {outOf:0.0} m on from the box's middle towards its mouth ({Vector3.Dot(Vector3.Abs(mouth), half):0.0}; in gravity a craft climbs on out on its own thrust)");

        // 3. The lift platform and the ramp deck: F lets each down (a lift's car to its next stop).
        foreach (var what in new[] { "Lift platform", "Ramp deck" })
        {
            var deck = -1;
            for (var i = 0; i < c.PartCount && deck < 0; i++)
                if (c.DefOf(i).Description.StartsWith(what, StringComparison.Ordinal) && c.DefOf(i).Name.EndsWith(".car", StringComparison.Ordinal))
                    deck = i;
            if (deck < 0)
            {
                r.Fail($"--systems: no {what.ToLowerInvariant()} was made");
                continue;
            }
            Span<int> slots = stackalloc int[Construct.MaxJoints];
            var n = DesignParts.LiftJoints(c, deck, slots);
            var why = DesignParts.LiftUse(c, deck, c.ToWorld(Vector3.Zero));
            Tick(8 * World.TickRate);
            var jt = n > 0 ? c.Joints[slots[0]] : default;
            Say(n == 1 && why == "" && MathF.Abs(jt.Angle - jt.Max) < 0.02f * MathF.Max(1, jt.Max), $"F lets the {what.ToLowerInvariant()} down: {(n == 1 ? $"its {(jt.Slides ? "piston" : "hinge")} at {jt.Angle:0.00} of {jt.Max:0.00}" : $"{n} joints")} ({(why == "" ? "moved" : why)})");
        }

        // 4. A joined client: the carrier, its door, its joints, and the craft let go, through the wire.
        var client = new ConstructWorld();
        foreach (var b in new[] { c, craft })
        {
            var pw = new PacketWriter(1 << 20);
            ConstructNet.WriteFull(pw, b);
            var rd = new PacketReader(pw.Span[1..]);
            ConstructNet.Read(ref rd, client, 0);
        }
        var cursor = 0;
        uint last = 0;
        foreach (var b in world.Bodies)
            b.Asleep = false;
        var pp = new PacketWriter(1 << 20);
        ConstructNet.WritePoses(pp, world, 100, ref cursor);
        var pr = new PacketReader(pp.Span[1..]);
        ConstructNet.ReadPoses(ref pr, client, 1.0, ref last);
        if (client.Get(c.Id) is not { } copy)
        {
            r.Fail("--systems: the client has no copy of the hangar ship");
            return;
        }
        if (copy.Dirty)
            copy.Rebuild();
        var worst = 0f;
        for (var j = 0; j < Math.Min(copy.JointCount, c.JointCount); j++)
            worst = MathF.Max(worst, MathF.Abs(copy.Joints[j].Angle - c.Joints[j].Angle));
        Say(copy.PartCount == c.PartCount && copy.JointCount == c.JointCount && worst < 0.02f, $"the client's copy: {copy.PartCount} parts, {copy.JointCount} joints (the host's {c.PartCount}, {c.JointCount}), the worst joint {worst:0.000} apart");
        Say(ConstructDoors.SettledOpen(copy, door) == ConstructDoors.SettledOpen(c, door), $"the client sees the bay's door {(ConstructDoors.SettledOpen(copy, door) ? "open" : "shut")}, as the host");
        Say(client.Get(craft.Id) is { } cc && cc.CarriedBy == craft.CarriedBy && Vector3.Distance(cc.WorldCom, craft.WorldCom) < 0.1f, $"the client has the craft as the host (carried by {client.Get(craft.Id)?.CarriedBy}, {(client.Get(craft.Id) is { } c2 ? Vector3.Distance(c2.WorldCom, craft.WorldCom) : -1):0.00} m apart)");
        Console.WriteLine($"[design] --systems hangars: {ok} ok");
    }
}
