using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Third person" (board item tp01): the Sandbox-only third-person camera (Source/App/ThirdPerson.cs). Checked: the
    /// Third person key shares T with Drop item without a clash (different modes: only one reads T in each), and the v15 to
    /// v16 keybind migration (T for both, a rebound Drop item key carried to the Sandbox drop); the view toggles on foot and
    /// in a seat separately; the on-foot camera that a wall pulls in stays outside the wall, and is free (not pulled) in
    /// the open; a shot in third person goes towards what the crosshair is on but stops at what the gun can see, never
    /// past a corner; a seat's orbit point is the centre of mass raised half the height, moves with its settings,
    /// survives a blueprint and the construct message to a client; the seat camera orbits that point at its distance; and
    /// the camera and the aim allocate nothing.
    /// </summary>
    private static void CheckThirdPerson()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"third person: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        TpKeys();
        TpToggle();
        TpFootCamera();
        TpSeatPoint();
    }

    private static void TpKeys()
    {
        var problems = new List<string>();
        var d = Keybinds.Defaults;
        if (Keybinds.DefaultsVersion != 16 || d.Length != (int)GameAction.COUNT)
            problems.Add($"binds version {Keybinds.DefaultsVersion}, {d.Length} defaults for {(int)GameAction.COUNT} actions");
        if (d[(int)GameAction.ThirdPerson] != Keybinds.Key(GlfwKey.T) || d[(int)GameAction.DropItem] != Keybinds.Key(GlfwKey.T))
            problems.Add("Third person and Drop item should both default to T");
        if (Keybinds.Overlap(GameAction.ThirdPerson, GameAction.DropItem) || Keybinds.ActiveIn(GameAction.DropItem, true) || !Keybinds.ActiveIn(GameAction.DropItem, false)
            || !Keybinds.ActiveIn(GameAction.ThirdPerson, true) || Keybinds.ActiveIn(GameAction.ThirdPerson, false))
            problems.Add("Third person (Sandbox only) and Drop item (every mode but Sandbox) must be mode-separate");
        // No two actions that can be live together share a default key.
        for (var i = 0; i < d.Length; i++)
            for (var j = i + 1; j < d.Length; j++)
                if (d[i] != Keybinds.Unbound && d[i] == d[j] && Keybinds.Overlap((GameAction)i, (GameAction)j))
                    problems.Add($"{(GameAction)i} and {(GameAction)j} clash on {Keybinds.Name(d[i])}");
        // T down: Sandbox reads Third person, the other modes Drop item.
        var down = new bool[(int)GameAction.COUNT];
        down[(int)GameAction.ThirdPerson] = down[(int)GameAction.DropItem] = true;
        Keybinds.Resolve(d, true, down);
        var sbThird = down[(int)GameAction.ThirdPerson];
        var sbDrop = down[(int)GameAction.DropItem];
        down[(int)GameAction.ThirdPerson] = down[(int)GameAction.DropItem] = true;
        Keybinds.Resolve(d, false, down);
        var zThird = down[(int)GameAction.ThirdPerson];
        var zDrop = down[(int)GameAction.DropItem];
        if (!sbThird || sbDrop || zThird || !zDrop)
            problems.Add($"T in Sandbox: third person {sbThird}, drop item {sbDrop}; elsewhere: third person {zThird}, drop item {zDrop}");
        // A v15 file: the new actions are not in it.
        int[] Old()
        {
            var b = (int[])d.Clone();
            b[(int)GameAction.ThirdPerson] = b[(int)GameAction.ThirdPersonSide] = b[(int)GameAction.SandboxDrop] = Keybinds.Unbound;
            return b;
        }
        var plain = Old();
        Keybinds.Migrate(plain, 15);
        if (plain[(int)GameAction.ThirdPerson] != Keybinds.Key(GlfwKey.T) || plain[(int)GameAction.DropItem] != Keybinds.Key(GlfwKey.T)
            || plain[(int)GameAction.SandboxDrop] != Keybinds.Key(GlfwKey.Delete) || plain[(int)GameAction.ThirdPersonSide] != Keybinds.Key(GlfwKey.N))
            problems.Add("a v15 file with the defaults should get Third person T, shoulder N, Sandbox drop Delete and keep Drop item T");
        var custom = Old();
        custom[(int)GameAction.DropItem] = Keybinds.Key(GlfwKey.U);
        Keybinds.Migrate(custom, 15);
        if (custom[(int)GameAction.DropItem] != Keybinds.Key(GlfwKey.U) || custom[(int)GameAction.SandboxDrop] != Keybinds.Key(GlfwKey.U) || custom[(int)GameAction.ThirdPerson] != Keybinds.Key(GlfwKey.T))
            problems.Add("a rebound Drop item key (U) should carry over to the Sandbox drop and leave T to Third person");
        // The player had put something else on T in Sandbox's context (a Sandbox action): Third person stays unbound, never a clash.
        var taken = Old();
        taken[(int)GameAction.Stow] = Keybinds.Key(GlfwKey.T);
        Keybinds.Migrate(taken, 15);
        if (taken[(int)GameAction.ThirdPerson] != Keybinds.Unbound || taken[(int)GameAction.Stow] != Keybinds.Key(GlfwKey.T))
            problems.Add("T already used by a Sandbox action: Third person should stay unbound");
        Line($"third person: keys: version {Keybinds.DefaultsVersion}, T in Sandbox reads third person {sbThird} / drop {sbDrop}, elsewhere third person {zThird} / drop {zDrop}; v15 migration: third person {Keybinds.Name(plain[(int)GameAction.ThirdPerson])}, "
             + $"sandbox drop {Keybinds.Name(plain[(int)GameAction.SandboxDrop])}; rebound drop carried {Keybinds.Name(custom[(int)GameAction.SandboxDrop])}; T taken: {Keybinds.Name(taken[(int)GameAction.ThirdPerson])}");
        if (problems.Count > 0)
            Fail($"third person: keys: {string.Join("; ", problems)}");
    }

    private static void TpToggle()
    {
        ThirdPerson.FootOn = ThirdPerson.SeatOn = false;
        ThirdPerson.Toggle(false);
        var a = ThirdPerson.FootOn && !ThirdPerson.SeatOn;
        ThirdPerson.Toggle(true);
        var b = ThirdPerson.FootOn && ThirdPerson.SeatOn;
        ThirdPerson.Toggle(false);
        var c = !ThirdPerson.FootOn && ThirdPerson.SeatOn; // (standing up leaves the seat's mode as it was)
        ThirdPerson.Toggle(true);
        var d = !ThirdPerson.FootOn && !ThirdPerson.SeatOn;
        ThirdPerson.FootOn = ThirdPerson.SeatOn = false;
        Line($"third person: toggle: on foot {a}, in a seat (each remembers its own) {b} {c} {d}");
        if (!a || !b || !c || !d)
            Fail("third person: T should toggle the on-foot view and the seat view separately");
    }

    /// <summary>Eyes on a grid over the yard, eight looks each: the camera pulled by walls, the aim not past corners.</summary>
    private static void TpFootCamera()
    {
        var q = World.Query;
        var up = Vector3.UnitY;
        int cases = 0, pulled = 0, free = 0, inside = 0, blocked = 0, past = 0, badFree = 0;
        float worstGap = 99;
        for (var x = -40f; x <= 40f; x += 2.5f)
            for (var z = -40f; z <= 40f; z += 2.5f)
            {
                var eye = ConstructWorld.Ground(q, x, z) + new Vector3(0, 1.6f, 0);
                if (q.PointInside(eye, 0.3f, QueryFilter.All))
                    continue;
                for (var k = 0; k < 8; k++)
                {
                    var yaw = k * MathF.PI / 4 + 0.3f;
                    var fwd = World.Forward(yaw, -0.05f);
                    var ideal = ThirdPerson.FootIdeal(eye, fwd, up, 3f, 1f);
                    var cam = ThirdPerson.FootCamera(q, eye, fwd, up, 3f, 1f);
                    cases++;
                    var want = Vector3.Distance(eye, ideal);
                    var got = Vector3.Distance(eye, cam);
                    if (got < want - 0.05f)
                        pulled++;
                    else if (q.Raycast(eye, Vector3.Normalize(ideal - eye), want + 0.3f, QueryFilter.All) >= want + 0.3f)
                    {
                        free++;
                        if (MathF.Abs(got - want) > 0.01f)
                            badFree++; // (nothing in the way, yet the camera isn't where it was wanted)
                    }
                    if (q.PointInside(cam, 0.05f, QueryFilter.All))
                        inside++;
                    // The aim: the shot goes towards what the camera ray meets, and never passes it.
                    var target = ThirdPerson.AimTarget(q, cam, fwd, eye, World.Range);
                    var dir = ThirdPerson.AimDir(q, cam, fwd, eye, World.Range);
                    var toTarget = Vector3.Distance(eye, target);
                    var shot = q.Raycast(eye, dir, World.Range, QueryFilter.Shots);
                    if (toTarget > 1f && toTarget < 150f)
                    {
                        if (shot > toTarget + 0.15f)
                            past++;
                        if (shot < toTarget - 0.5f)
                            blocked++; // (a corner: the camera sees the target, the gun can't; the shot stops at the wall)
                        worstGap = MathF.Min(worstGap, shot - toTarget);
                    }
                }
            }
        Line($"third person: camera over {cases} looks in the yard: {pulled} pulled in by a wall, {free} free, {inside} inside geometry; the aim: {blocked} looks where the gun can't see the target (stopped at the wall), {past} past the target");
        if (pulled < 20 || free < 20 || inside != 0 || badFree != 0)
            Fail($"third person: the camera should be pulled in by walls (got {pulled}), free in the open ({free}, {badFree} off its spot) and never inside geometry ({inside})");
        if (blocked < 3 || past != 0)
            Fail($"third person: a shot should stop at what the gun sees ({blocked} corner cases, expected some) and never pass the crosshair's target ({past})");

        // Nothing allocates.
        var e0 = ConstructWorld.Ground(q, 0, 0) + new Vector3(0, 1.6f, 0);
        var f0 = World.Forward(0.4f, -0.1f);
        for (var i = 0; i < 20; i++)
            ThirdPerson.AimDir(q, ThirdPerson.FootCamera(q, e0, f0, up, 3f, 1f), f0, e0, World.Range);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++)
            ThirdPerson.AimDir(q, ThirdPerson.FootCamera(q, e0, f0, up, 3f, 1f), f0, e0, World.Range);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"third person: 2000 camera and aim solves allocated {bytes} bytes");
        if (bytes != 0)
            Fail($"third person: the camera and the aim allocate ({bytes} bytes over 2000 solves)");
    }

    private static void TpSeatPoint()
    {
        const string json = """
            { "format": "call-of-slop-construct-blueprint/1", "name": "Orbit Test", "steps": [
              { "part": "Foundation" },
              { "part": "Seat", "on": 0, "socket": "top00", "by": "bottom", "config": { "orbitX": 1.5, "orbitY": 2, "orbitZ": 4, "orbitDist": 14, "zoomMin": 2, "zoomMax": 30, "followYaw": true } },
              { "part": "Seat", "on": 0, "socket": "top02", "by": "bottom" }
            ] }
            """;
        var q = World.Query;
        var bp = ConstructBuild.ParseBlueprint(json, "orbit.json");
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(q, 18, 10), 0.7f, out var why);
        if (c == null)
        {
            Fail($"third person: the orbit blueprint doesn't build: {why}");
            return;
        }
        var set = -1;
        var plain = -1;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Role == PartRole.Seat)
                (set < 0 ? ref set : ref plain) = i;
        if (set < 0 || plain < 0)
        {
            Fail("third person: the orbit blueprint has no two seats");
            return;
        }
        var pos = c.Pos;
        var rot = c.Rot;
        var def = ThirdPerson.SeatPoint(c, plain, pos, rot, out var dd, out var dz0, out var dz1, out var df);
        var expectDef = pos + Vector3.Transform(c.Com + new Vector3(0, (c.LocalMax.Y - c.LocalMin.Y) * 0.5f, 0), rot);
        var cfg = ThirdPerson.SeatPoint(c, set, pos, rot, out var cd, out var cz0, out var cz1, out var cf);
        var expectCfg = def + Vector3.Transform(new Vector3(1.5f, 2f, -4f), rot);
        var okDefault = Vector3.Distance(def, expectDef) < 1e-4f && dd == 10 && dz0 == 3 && dz1 == 40 && !df;
        var okConfig = Vector3.Distance(cfg, expectCfg) < 1e-3f && cd == 14 && cz0 == 2 && cz1 == 30 && cf;
        // The settings through the construct message to a client.
        var pw = new PacketWriter(8192);
        ConstructNet.WriteFull(pw, c);
        var client = new ConstructWorld();
        var r = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref r, client, 0);
        var copy = client.Get(c.Id);
        var onClient = copy != null && ThirdPerson.SeatPoint(copy, set, copy.Pos, copy.Rot, out var ld, out var lz0, out var lz1, out var lf) is var lp
                       && Vector3.Distance(lp, cfg) < 1e-3f && ld == 14 && lz0 == 2 && lz1 == 30 && lf;
        // Written back as a blueprint step, the same settings.
        var stepJson = ConstructConfig.ToJson(c.Parts[set], c.DefOf(set));
        var again = ConstructBuild.ParseBlueprint(json.Replace("{ \"orbitX\": 1.5, \"orbitY\": 2, \"orbitZ\": 4, \"orbitDist\": 14, \"zoomMin\": 2, \"zoomMax\": 30, \"followYaw\": true }", stepJson), "orbit2.json");
        var c2 = ConstructBuild.BuildOnGround(new ConstructWorld(), again, ConstructWorld.Ground(q, 18, 10), 0.7f, out _);
        var rebuilt = c2 != null && Vector3.Distance(ThirdPerson.SeatPoint(c2, set, c.Pos, c.Rot, out _, out _, out _, out _), cfg) < 1e-3f;
        // The camera orbits the point: the same distance whichever way it looks, in open air; a wall pulls it in.
        var air = new Vector3(0, 60, 0);
        var worstOff = 0f;
        for (var k = 0; k < 16; k++)
        {
            var f = World.Forward(k * MathF.Tau / 16, -0.3f);
            worstOff = MathF.Max(worstOff, MathF.Abs(Vector3.Distance(ThirdPerson.OrbitCamera(q, air, f, 14), air) - 14));
        }
        var low = ConstructWorld.Ground(q, 18, 10) + new Vector3(0, 1.5f, 0);
        var pulledIn = 0;
        for (var k = 0; k < 16; k++)
        {
            var f = World.Forward(k * MathF.Tau / 16, 0.05f);
            var cam = ThirdPerson.OrbitCamera(q, low, f, 30);
            if (Vector3.Distance(cam, low) < 29.5f)
                pulledIn++;
            if (q.PointInside(cam, 0.05f, QueryFilter.All))
                pulledIn -= 1000;
        }
        Line($"third person: seat orbit point: default at the centre of mass raised by half the height {okDefault}, configured (x 1.5, y 2, z 4 forward; distance 14, zoom 2 to 30, follow) {okConfig}, "
             + $"in a client's copy {onClient}, through a blueprint step {rebuilt}; the camera {worstOff:0.0000} m off its distance in open air; {pulledIn} of 16 low looks pulled in by walls, none inside");
        if (!okDefault || !okConfig || !onClient || !rebuilt || worstOff > 1e-3f || pulledIn < 1)
            Fail("third person: a seat's orbit point and camera settings should come from its settings, survive a blueprint and reach a client, and the camera should orbit at its distance and be pulled in by walls");
    }
}
