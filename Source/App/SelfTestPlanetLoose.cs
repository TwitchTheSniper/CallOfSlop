using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Planetoid loose items" self-test section (docs/planets/PLANETS.md, "Loose things"): what lies about on a planetoid
/// lands on its ground and stays there, falling towards the centre. Loose construct parts (a block and a deck plate,
/// upright and tumbling) at the pole, on a face seam and on a cube corner, and one in a crater; a ragdoll and a grenade; and
/// a real Sandbox game where a player on the equator (kilometres below Y 0) drops a part and it stays in the world.
/// </summary>
public static partial class SelfTest
{
    private static void CheckPlanetLoose()
    {
        var previous = World.Map;
        try
        {
            var map = Maps.Find("Planetoid") is { Planetoid: not null } dev ? dev : Maps.All.FirstOrDefault(m => m.Planetoid != null); // (the Dev map these checks were made on, not the first world of the Planetoids pack)
            if (map?.Planetoid is not { } pl)
            {
                Fail("planetoid loose items: no map with a planetoid (Maps/Dev/Planetoid.json)");
                return;
            }
            map.RestoreProps();
            World.SetMap(map);
            var spots = PdSpots(pl);
            var equator = PlanetoidDir(0, 90);
            var all = spots.Append(("the equator", pl.GroundUnder(pl.Center + equator * (float)(pl.Radius + 50)), equator)).ToArray();
            foreach (var (name, g, up) in all)
                PlLooseParts(pl, name, g, up, false);
            pl.Edits.Apply(new DigOp(DigKind.Crater, spots[1].Ground, 3f).Quantised);
            PlLooseParts(pl, "a crater on the seam", pl.GroundUnder(spots[1].Ground + spots[1].Up * 5f), spots[1].Up, true);
            map.RestoreProps();
            foreach (var (name, g, up) in all)
                PlLooseRagdollAndGrenade(pl, name, g, up);
            PlLooseDrop(map);
        }
        finally
        {
            World.Map.RestoreProps();
            World.SetMap(previous);
        }
    }

    /// <summary>A block dropped upright, a block dropped tumbling and a deck plate dropped flat, 2 m over the ground: each rests on it.</summary>
    private static void PlLooseParts(Planetoid pl, string name, Vector3 ground, Vector3 up, bool dug)
    {
        var w = new ConstructWorld();
        var q = World.Query;
        PdAxes(up, out var ta, out var tb);
        var stand = Quaternion.CreateFromRotationMatrix(GravityFrames.Basis(ground));
        var tumble = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(ta, 0.7f) * Quaternion.CreateFromAxisAngle(tb, 0.4f) * stand);
        var block = ConstructParts.Need("Frame Block");
        var plate = ConstructParts.Need("Deck Plate");
        var drops = new (string What, ConstructPartDef Def, Vector3 At, Quaternion Rot)[]
        {
            ("block", block, ground + up * 2f, stand),
            ("tumbling block", block, ground + up * 2.5f + ta * 1.6f, tumble),
            ("deck plate", plate, ground + up * 2f - ta * 1.8f + tb * 0.5f, stand),
        };
        var bodies = new List<(string What, Construct C, float Half)>();
        foreach (var (what, def, at, rot) in drops)
            if (ConstructEdits.DropLoose(w, def, at, rot) is { } c)
                bodies.Add((what, c, (def.BoundsMax - def.BoundsMin).Length() * 0.5f));
        if (bodies.Count != drops.Length)
        {
            Fail($"planetoid loose items at {name}: only {bodies.Count} of {drops.Length} parts could be dropped");
            return;
        }
        var lowest = new float[bodies.Count];
        Array.Fill(lowest, float.MaxValue);
        for (var t = 0; t < 4 * World.TickRate; t++)
        {
            w.Step(q, World.TickDt);
            for (var k = 0; k < bodies.Count; k++)
                if (w.Get(bodies[k].C.Id) != null)
                    lowest[k] = MathF.Min(lowest[k], pl.Altitude(bodies[k].C.WorldCom));
        }
        var report = new List<string>();
        for (var k = 0; k < bodies.Count; k++)
        {
            var (what, c, half) = bodies[k];
            var gone = w.Get(c.Id) == null;
            var alt = gone ? float.NaN : pl.Altitude(c.WorldCom);
            report.Add(gone ? $"{what} GONE" : $"{what} {alt:0.00} m up (lowest {lowest[k]:0.00}), {c.Vel.Length():0.00} m/s{(c.Asleep ? ", asleep" : "")}");
            // (resting: its middle over the ground by no more than its half diagonal, and not inside it; still)
            if (gone || !(alt > 0.02f) || alt > half + 0.1f || lowest[k] < -0.05f || !c.Asleep && c.Vel.Length() > 0.3f)
                Fail($"planetoid loose items at {name}: a dropped {what} didn't come to rest on the ground ({report[^1]}, half diagonal {half:0.00} m{(dug ? ", dug ground" : "")})");
        }
        Line($"planetoid loose items at {name}: {string.Join("; ", report)}");
    }

    /// <summary>A ragdoll falls over and settles on the ground; a grenade dropped from 3 m lands and stops on it.</summary>
    private static void PlLooseRagdollAndGrenade(Planetoid pl, string name, Vector3 ground, Vector3 up)
    {
        Ragdolls.Clear();
        var feet = ground + up * 0.02f;
        var body = Ragdolls.SpawnForTest(new RagdollDeath { Feet = feet, Yaw = 0.5f, Stance = Stance.Stand, Victim = 7, Killer = 3, Source = GravityFrames.Above(feet, 1.3f) + GravityFrames.FlatForward(feet, 0.5f) * 4, HasSource = true, GunPower = 3f },
            RagdollLook.Soldier, 0, new Vector3(0.5f));
        var under = 0f;
        var steps = 0;
        for (; steps < 600 && body.Live && !body.Sim.Broken && !body.Sim.Asleep; steps++)
        {
            Ragdolls.StepAll();
            for (var i = 0; i < RagdollRig.Count; i++)
                under = MathF.Min(under, pl.Altitude(body.Sim.Pos[i]) - RagdollRig.Radius[i]);
        }
        var pelvis = pl.Altitude(body.Sim.Pelvis);
        Ragdolls.Clear();

        var frag = new Projectile { Kind = ThrowableKind.Frag, Pos = ground + up * 3f, Vel = Vector3.Zero };
        var low = float.MaxValue;
        for (var t = 0; t < 3 * World.TickRate; t++)
        {
            Throwables.Step(frag, World.TickDt);
            low = MathF.Min(low, pl.Altitude(frag.Pos));
        }
        var rest = pl.Altitude(frag.Pos);
        Line($"planetoid loose items at {name}: ragdoll asleep {body.Sim.Asleep} after {steps * Ragdoll.Step:0.0} s, pelvis {pelvis:0.00} m up, deepest {under * 100:0.0} cm in; a grenade from 3 m rests {rest:0.00} m up (lowest {low:0.00}), {frag.Vel.Length():0.00} m/s");
        if (body.Sim.Broken || !body.Sim.Asleep || pelvis > 0.6f || pelvis < 0f || under < -0.08f)
            Fail($"planetoid loose items at {name}: a ragdoll didn't settle on the ground (broken {body.Sim.Broken}, asleep {body.Sim.Asleep} after {steps} steps, pelvis {pelvis:0.00} m up, deepest {under:0.000})");
        if (low < -0.02f || rest > 0.3f || frag.Vel.Length() > 0.5f)
            Fail($"planetoid loose items at {name}: a grenade dropped from 3 m didn't land and stop on the ground (rests {rest:0.00} m up, lowest {low:0.00}, {frag.Vel.Length():0.00} m/s)");
    }

    /// <summary>A real Sandbox game: a player on the equator (Y about -10 km) drops the part in their hands; it lands in front of them and stays in the world.</summary>
    private static void PlLooseDrop(Map map)
    {
        var pl = map.Planetoid!;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, map.Id);
        if (SpawnLocalSafely(g, ref now) is { } notReady)
        {
            Fail($"planetoid loose items: {notReady}");
            return;
        }
        var me = g.Local!;
        var up = PlanetoidDir(0, 90);
        var stand = pl.GroundUnder(pl.Center + up * (float)(pl.Radius + 50));
        me.Teleport(stand + up * 0.05f);
        Run(g, ref now, 0.5);
        var block = ConstructParts.Need("Frame Block");
        var hands = g.SandboxHands;
        hands.Hand(me.Id).Held = block.Id;
        var before = g.Constructs.Bodies.Count;
        var refused = hands.Drop(g.Constructs, me.Id, me.Position, me.Yaw);
        var dropped = g.Constructs.Bodies.Count > before ? g.Constructs.Bodies[^1] : null;
        var id = dropped?.Id ?? 0;
        var start = dropped == null ? float.NaN : pl.Altitude(dropped.WorldCom);
        Run(g, ref now, 3);
        var c = dropped == null ? null : g.Constructs.Get(id);
        var alt = c == null ? float.NaN : pl.Altitude(c.WorldCom);
        var ahead = c == null ? float.NaN : Vector3.Distance(c.WorldCom, me.Position);
        Line($"planetoid loose items: a part dropped on the equator (Y {me.Position.Y:0}) {(refused ?? "dropped")}, {start:0.00} m up at first, after 3 s {(c == null ? "GONE" : $"{alt:0.00} m up, {ahead:0.0} m from the player{(c.Asleep ? ", asleep" : "")}")}");
        if (refused != null || c == null || !(alt > 0.02f) || alt > 0.6f || ahead > 3f)
            Fail($"planetoid loose items: a part dropped on the equator didn't stay on the ground in front of the player ({refused ?? "dropped"}, {(c == null ? "gone from the world" : $"{alt:0.00} m up, {ahead:0.0} m away")})");
    }
}
