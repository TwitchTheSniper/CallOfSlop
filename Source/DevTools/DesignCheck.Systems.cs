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
        void Tick(int n)
        {
            for (var k = 0; k < n; k++)
                world.Step(World.Query, World.TickDt);
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
            c.Parts[sw].State ^= 1; // (as F on it: ConstructEdits.Use)
            Tick(3);
            Say(!ConstructLamps.Lit(c, lamp), "the switch thrown puts it out");
            c.Parts[sw].State ^= 1;
            Tick(3);
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
        c.Parts[helm].Keys = CPart.Occupied;
        c.Wake();
        Tick(5);
        if (grapple >= 0)
        {
            var before = c.Parts[grapple].Out;
            c.Parts[helm].Keys = CPart.Occupied | ConstructParts.KeyHook;
            Tick(3);
            c.Parts[helm].Keys = CPart.Occupied;
            Tick(3);
            Say(before < 0.5f && c.Parts[grapple].Out >= 0.5f && ConstructLogic.WiredIn(world, c, grapple), $"the helm's hook key works the grapple ({before:0} -> {c.Parts[grapple].Out:0})");
            c.Parts[helm].Keys = CPart.Occupied | ConstructParts.KeyHook; // (and again: it lets go)
            Tick(3);
            c.Parts[helm].Keys = CPart.Occupied;
            Tick(3);
            Say(c.Parts[grapple].Out < 0.5f, "a second press lets it go");
        }
        else
            r.Fail("--systems: no grapple");

        // 5. The guns: none fires while the helm only sits; on the fire key every gun with a game gun fires (after its first load).
        var fired = new HashSet<int>();
        void Count()
        {
            for (var k = 0; k < world.FireCount; k++)
                if (world.Fires[k].Kind == CFireKind.Shot && world.Fires[k].Body == c.Id)
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
        c.Parts[helm].Keys = CPart.Occupied | ConstructParts.KeyFire;
        var shots = 0;
        var client = new ConstructWorld();
        var cfires = new CFireEvent[256];
        var clientShots = 0;
        for (var k = 0; k < 9 * World.TickRate; k++)
        {
            Tick(1);
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
        c.Parts[helm].Keys = CPart.Occupied;
        var guns = Enumerable.Range(0, c.GunCount).Select(k => (int)c.Guns[k].Part).ToHashSet();
        Say(c.GunCount > 0 && fired.SetEquals(guns), $"the fire key fires {fired.Count} of {c.GunCount} guns ({shots} shot events in 9 s)");

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
    }
}
