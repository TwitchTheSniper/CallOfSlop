using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private const string MeSmall = "Small ";
    private const string MeCrank = "Small Crank Section", MeCyl = "Small Cylinder", MeCarb = "Small Carburettor", MeRail = "Small Injector Rail", MeTurbo = "Small Turbo", MeSuper = "Small Supercharger", MeWheel = "Small Flywheel";

    /// <summary>
    /// "Modular engine" (docs/sandbox/CONSTRUCTS.md, "Modular engines"): the pieces load and bad ones are refused; the player's
    /// whole chain (the menu, PlaceNew, the held part's ghost and click, the Attach message) makes a four-part engine, the look
    /// line saying what is missing at each step; inline, V, boxer and radial layouts read right and their power is the sum;
    /// wrong builds say why; a Medium engine by the welder; a same-power engine drives a buggy as far as the fixed one, on its
    /// gearbox and through a routed shaft from a freeform-welded flywheel; a shot cylinder loses the right power; fuel use;
    /// a 40-cylinder engine's rebuild allocates nothing and its tick does no per-cylinder work; freeform welds; an anchored
    /// base with no seat, run by a wire, driving a power take-off; the smoke follows the load and leaves the stack's top; the
    /// sound's choice. "Modular engine network" is the same engine over the real transport.
    /// </summary>
    private static void CheckModularEngine()
    {
        MeFiles();
        MeLayouts();
        MeWrongBuilds();
        MeDamage();
        MeFuelUse();
        MeBigEngine();
        MeFreeform();
        MeAnchored();
        MeDriven();
        MeEffects();
        MeChain();
    }

    private static int MeFit(ConstructWorld w, Construct c, int on, string socket, string part, string by)
    {
        var i = RigFit(w, c, on, socket, part, by);
        if (i >= 0)
            c.Rebuild();
        return i;
    }

    /// <summary>A pinned frame with a crank of <paramref name="cranks"/> sections on it, a flywheel on the end and a tank on the crank's back end. Parts 0 and 1 are the frame, 2 up the crank.</summary>
    private static Construct? MeBase(ConstructWorld w, Vector3 at, int cranks = 1, bool flywheel = true, string prefix = MeSmall)
    {
        // A bigger engine's sockets are of its size class: it goes on a small frame through the size adapter, as a player's does.
        var second = prefix == MeSmall
            ? """{ "part": "Frame Beam", "on": 0, "socket": "x+0", "by": "x-0" }"""
            : $$"""{ "part": "Adapter {{prefix.Trim()}} to Small", "on": 0, "socket": "x+0", "by": "front0.0" }""";
        var bp = ConstructBuild.ParseBlueprint($$"""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Engine Bed", "steps": [
              { "part": "Frame Beam" },
              {{second}}
            ] }
            """, "engine-bed.json");
        var c = ConstructBuild.Build(w, bp, at, Quaternion.Identity, out var why);
        if (c == null)
        {
            Fail($"modular engine: the bed doesn't build: {why}");
            return null;
        }
        c.Pinned = true;
        var crank = MeFit(w, c, 1, prefix == MeSmall ? "y+1" : "back", prefix + "Crank Section", "-y");
        for (var k = 1; k < cranks && crank >= 0; k++)
            crank = MeFit(w, c, crank, "front", prefix + "Crank Section", "back");
        if (flywheel && crank >= 0)
        {
            MeFit(w, c, crank, "front", prefix + "Flywheel", "crank");
            MeFit(w, c, 2, "back", prefix == MeSmall ? "Fuel Tank" : prefix + "Fuel Bunker", "+z");
        }
        c.Rebuild();
        return crank < 0 ? null : c;
    }

    private static ModEngine? MeOf(Construct c) => c.ActiveEngine ?? (c.ModCount > 0 ? c.ModEngines![0] : null);

    private static string MeRead(Construct c) => MeOf(c) is { } e ? ConstructEngine.Readout(e) : "(no engine)";

    private static void MeExpect(Construct c, string want, string what)
    {
        var got = MeRead(c);
        if (got != want)
            Fail($"modular engine ({what}): the readout says \"{got}\", not \"{want}\"");
    }

    /// <summary>The pieces' files, the rules, what mates what.</summary>
    private static void MeFiles()
    {
        var pieces = ConstructParts.All.Where(d => d.Piece != EnginePiece.None).ToList();
        var problem = EngineRules.Current.Problem;
        Line($"modular engine: {pieces.Count} pieces, rules \"{problem}\"");
        if (pieces.Count != 40)
            Fail($"modular engine: {pieces.Count} pieces load, not 40 (ten kinds in four sizes)");
        if (problem.Length > 0)
            Fail($"modular engine: the rules file is refused: {problem}");
        var bad = EngineRules.Parse("""{ "format": "call-of-slop-modular-engine/1", "sizes": [ { "name": "tiny" } ] }""");
        var bad2 = EngineRules.Parse("""{ "format": "call-of-slop-modular-engine/1", "thirstyAt": 99 }""");
        var bad3 = EngineRules.Parse("""{ "format": "call-of-slop-modular-engine/1", "sizes": [ { "name": "small", "idleRpm": 9000 } ] }""");
        if (!bad.Problem.Contains("name") || !bad2.Problem.Contains("thirstyAt") || !bad3.Problem.Contains("rpm"))
            Fail($"modular engine: bad rules weren't refused for their reasons ({bad.Problem} / {bad2.Problem} / {bad3.Problem})");
        const string head = """ "format": "call-of-slop-construct-part/1", "name": "Bad", "mass": 10, "health": 10, "boxes": [ { "size": [1, 1, 1] } ], "sockets": [ { "name": "base", "at": [0, -0.5, 0], "dir": "-y" }, { "name": "top", "at": [0, 0.5, 0], "dir": "+y" } ] """;
        foreach (var (body, word) in new[]
                 {
                     ("""{ "kind": "laser", "size": "small" }""", "kind"),
                     ("""{ "kind": "output", "size": "small" }""", "modular"),
                     ("""{ "kind": "fuel", "size": "huge" }""", "size"),
                     ("""{ "kind": "crank", "size": "small" }""", "crank"),
                     ("""{ "kind": "cylinder", "size": "small", "power": 2 }""", "power"),
                     ("""{ "kind": "stack", "size": "small", "spool": 99 }""", "spool"),
                 })
        {
            try
            {
                ConstructParts.Parse($$"""{ {{head}}, "role": "enginePiece", "enginePiece": {{body}} }""", "bad.json");
                Fail($"modular engine: a bad piece was accepted: {body}");
            }
            catch (ConstructPartException ex)
            {
                if (!ex.Message.Contains(word))
                    Fail($"modular engine: a bad piece was refused for the wrong reason: {ex.Message}");
            }
        }
        // A moving box must be a detail: it adds no collision.
        try
        {
            ConstructParts.Parse($$"""{ "format": "call-of-slop-construct-part/1", "name": "Bad", "mass": 10, "health": 10, "boxes": [ { "size": [1, 1, 1], "pump": 0.1 } ], "role": "frame" }""", "bad.json");
            Fail("modular engine: a solid box that pumps was accepted");
        }
        catch (ConstructPartException ex) when (ex.Message.Contains("detail"))
        {
        }
        // Every size has every kind, and only the small ones are items.
        foreach (var size in new[] { "Small", "Medium", "Large", "Capital" })
            foreach (var kind in new[] { "Crank Section", "Cylinder", "Carburettor", "Injector Rail", "Turbo", "Supercharger", "Flywheel", "Starter Motor", "Exhaust Outlet", "Smokestack" })
                if (ConstructParts.Find($"{size} {kind}") is not { } d)
                    Fail($"modular engine: no {size} {kind}");
                else if (size == "Small" == d.PhysicsOnly)
                    Fail($"modular engine: {d.Name}: physics-only is {d.PhysicsOnly}");
        // What mates what: a ghost only offers what a piece is for.
        var w = new ConstructWorld();
        if (MeBase(w, new Vector3(0, 2, 0)) is not { } c)
            return;
        var cyl = ConstructParts.Need(MeCyl);
        var carb = ConstructParts.Need(MeCarb);
        var big = ConstructParts.Need("Medium Cylinder");
        var side = Array.FindIndex(c.DefOf(2).Sockets, s => s.Name == "+y");
        var end = Array.FindIndex(c.DefOf(2).Sockets, s => s.Name == "back");
        var own = Array.FindIndex(cyl.Sockets, s => s.Name == "base");
        if (!SnapBuild.SinglePurpose(cyl) || SnapBuild.SinglePurpose(c.DefOf(2)) || !SnapBuild.CanMate(c, cyl, own, 2, side) || SnapBuild.CanMate(c, cyl, own, 2, end)
            || SnapBuild.CanMate(c, carb, Array.FindIndex(carb.Sockets, s => s.Name == "base"), 2, side) || SnapBuild.CanMate(c, big, own, 2, side))
            Fail("modular engine: the ghost rules are wrong (a cylinder takes a crank's side, not its end; a carburettor takes a head; sizes don't mix)");
        if (!SnapBuild.Fit(c, ConstructParts.Need("Small Starter Motor"), 2, side, -1, out _))
            Fail("modular engine: a starter doesn't fit a crank's side");
    }

    /// <summary>Inline, V, boxer, W and radial: what they read, and that power is the sum.</summary>
    private static void MeLayouts()
    {
        var r = EngineRules.Current;
        var kw = r.Kw[0];
        var w = new ConstructWorld();
        // An inline four: 4 sections up the top, one carburettor.
        if (MeBase(w, new Vector3(0, 2, 0), 4) is not { } i4)
            return;
        var first = 0;
        for (var k = 0; k < 4; k++)
        {
            var cyl = MeFit(w, i4, 2 + k, "+y", MeCyl, "base");
            first = k == 0 ? cyl : first;
        }
        MeFit(w, i4, first, "head", MeCarb, "base");
        MeExpect(i4, $"I4 {kw * 4:0} kW", "inline four");
        if (!i4.HasPowertrain && !MeOf(i4)!.Complete)
            Fail($"modular engine: the inline four doesn't run: {i4.DriveProblem}");
        // A V8: two banks at right angles.
        if (MeBase(w, new Vector3(20, 2, 0), 4) is not { } v8)
            return;
        var a = -1;
        var b = -1;
        for (var k = 0; k < 4; k++)
        {
            var ca = MeFit(w, v8, 2 + k, "+y", MeCyl, "base");
            var cb = MeFit(w, v8, 2 + k, "+x", MeCyl, "base");
            a = k == 0 ? ca : a;
            b = k == 0 ? cb : b;
        }
        MeFit(w, v8, a, "head", MeCarb, "base");
        MeExpect(v8, $"V8 {kw * 4:0} kW - no fuel system on bank 2", "a V8 with one bank fed");
        var half = MeOf(v8)!.Kw;
        MeFit(w, v8, b, "head", MeCarb, "base");
        MeExpect(v8, $"V8 {kw * 8:0} kW", "a V8");
        Line($"modular engine: a V8 fed on one bank {half:0} kW, on both {MeOf(v8)!.Kw:0} kW");
        // A boxer: opposite sides.
        if (MeBase(w, new Vector3(40, 2, 0), 2) is not { } b4)
            return;
        for (var k = 0; k < 2; k++)
        {
            var ca = MeFit(w, b4, 2 + k, "+x", MeCyl, "base");
            var cb = MeFit(w, b4, 2 + k, "-x", MeCyl, "base");
            if (k == 0)
            {
                MeFit(w, b4, ca, "head", MeRail, "base");
                MeFit(w, b4, cb, "head", MeRail, "base");
            }
        }
        MeExpect(b4, $"B4 {kw * 4 * 1.2f:0} kW, frugal", "a boxer on injector rails");
        // A radial: every side of one section (the bed takes the first section's underside: the next has all four).
        if (MeBase(w, new Vector3(60, 3, 0), 4) is not { } rad)
            return;
        const int nose = 5;
        foreach (var side in new[] { "+x", "-x", "+y", "-y" })
        {
            var cyl = MeFit(w, rad, nose, side, MeCyl, "base");
            var fuel = MeFit(w, rad, cyl, "head", MeCarb, "base");
            if (side == "+x")
                MeFit(w, rad, fuel, "top", MeTurbo, "base");
            else if (side == "-x")
                MeFit(w, rad, fuel, "top", MeSuper, "base");
        }
        MeExpect(rad, $"R4 {kw * (1.4f + 1.25f + 2f):0} kW", "a radial with a turbo and a supercharger");
        if (MathF.Abs(MeOf(rad)!.Spool - 1.5f) > 0.01f)
            Fail($"modular engine: a turbo's lag is {MeOf(rad)!.Spool} s, not 1.5");
        // The torque is the power at the peak rpm, and the drivetrain sees it.
        var d = i4.EngineDef;
        var engine = i4.Engine >= 0 ? d.EngineTorque * r.Peak[0] / ConstructDrive.RpmPerRadS / 1000f : 0;
        if (MathF.Abs(engine - kw * 4) > 0.5f || d.MaxRpm != r.Max[0])
            Fail($"modular engine: the inline four's numbers don't add up ({engine:0.0} kW at the peak, {d.MaxRpm} rpm limiter)");
    }

    /// <summary>"It must be impossible to build a useless engine without being told why".</summary>
    private static void MeWrongBuilds()
    {
        var w = new ConstructWorld();
        var x = 0f;
        Construct? Bed(int cranks = 1, bool fly = true, string prefix = MeSmall)
        {
            x += 20;
            return MeBase(w, new Vector3(x, 2, 0), cranks, fly, prefix);
        }
        if (Bed(1, false) is not { } crank)
            return;
        MeExpect(crank, "no cylinders on the crank", "a crank alone");
        var loose = w.Create(new Vector3(0, 2, 30), Quaternion.Identity);
        w.AddPart(loose!, ConstructParts.Need(MeCyl), Vector3.Zero, 0);
        loose!.Rebuild();
        MeExpect(loose, "no crank: a cylinder goes on a crank section", "a cylinder alone");
        if (loose.DriveProblem != "no crank: a cylinder goes on a crank section")
            Fail($"modular engine: the HUD's reason for a lone cylinder is \"{loose.DriveProblem}\"");
        var lone = w.Create(new Vector3(0, 2, 40), Quaternion.Identity);
        w.AddPart(lone!, ConstructParts.Need("Small Smokestack"), Vector3.Zero, 0);
        lone!.Rebuild();
        if (ConstructEngine.Look(lone, 0) != "Small Smokestack - stand it on an exhaust outlet, or pipe one into it")
            Fail($"modular engine: a lone stack's look line is \"{ConstructEngine.Look(lone, 0)}\"");
        if (Bed(1, false) is not { } bare)
            return;
        var cyl = MeFit(w, bare, 2, "+y", MeCyl, "base");
        MeExpect(bare, "I1 0 kW - no fuel system on bank 1", "no fuel system");
        MeFit(w, bare, cyl, "head", MeCarb, "base");
        MeExpect(bare, "I1 40 kW - no flywheel on the end of the crank", "no output");
        if (bare.DriveProblem != "no flywheel on the end of the crank")
            Fail($"modular engine: the HUD's reason for no output is \"{bare.DriveProblem}\"");
        var fly = MeFit(w, bare, 2, "front", MeWheel, "crank");
        MeExpect(bare, "I1 40 kW - no fuel: link a fuel tank", "no tank");
        if (bare.DriveProblem != "no fuel: link a fuel tank")
            Fail($"modular engine: the HUD's reason for no tank is \"{bare.DriveProblem}\"");
        MeFit(w, bare, fly, "+x", "Fuel Tank", "-x");
        MeExpect(bare, "I1 40 kW", "a working single");
        if (bare.Engine < 0 || bare.DriveProblem != "no transmission")
            Fail($"modular engine: a working single isn't the construct's engine ({bare.Engine}): \"{bare.DriveProblem}\"");
        // A cylinder bolted on the crank's end is told where it goes.
        if (Bed(1, false) is not { } end)
            return;
        MeFit(w, end, 2, "front", MeCyl, "base");
        MeExpect(end, "a cylinder goes on the crank's side, not its end", "a cylinder on the end");
        // Sizes don't mix: a medium socket refuses a small part, and forced on (a weld ignores classes) it says so.
        if (Bed(1, true, "Medium ") is not { } mixed)
            return;
        if (FitTurned(w, mixed, 2, "+y", MeCyl, "base", _ => true) >= 0)
            Fail("modular engine: a small cylinder fits a medium crank's socket");
        var smallDef = ConstructParts.Need(MeCyl);
        var small = w.AddPart(mixed, smallDef, mixed.Parts[2].Pos + new Vector3(0, 1.5f, 0), 0);
        w.AddLink(mixed, 2, Array.FindIndex(mixed.DefOf(2).Sockets, k => k.Name == "+y"), small, Array.FindIndex(smallDef.Sockets, k => k.Name == "base"));
        mixed.Rebuild();
        MeExpect(mixed, "mixed sizes: use one size", "a small cylinder on a medium crank");
        // A Large engine needs a starter and, with it, a battery.
        if (Bed(1, false, "Large ") is not { } large)
            return;
        var lc = MeFit(w, large, 2, "+y", "Large Cylinder", "base");
        var lf = MeFit(w, large, lc, "head", "Large Carburettor", "base");
        var lw = MeFit(w, large, 2, "front", "Large Flywheel", "crank");
        MeExpect(large, "I1 16 MW - needs a starter motor", "a Large engine with no starter");
        var st = MeFit(w, large, lw, "-x", "Large Starter Motor", "base");
        MeFit(w, large, lf, "top", "Large Exhaust Outlet", "base");
        MeFit(w, large, 2, "back", "Large Fuel Bunker", "+z");
        var t = MeRead(large);
        Line($"modular engine: a Large single with a starter reads \"{t}\" (starter {st})");
        if (t != "I1 16 MW" || large.Engine < 0 || !large.EngineDef.NeedsStarter)
            Fail($"modular engine: a Large single with a starter and a tank reads \"{t}\" (engine {large.Engine})");
        else if (!ConstructEngine.Look(large, lw).Contains("no battery for the starter"))
            Fail($"modular engine: the look line doesn't say the starter needs a battery: \"{ConstructEngine.Look(large, lw)}\"");
    }

    /// <summary>A shot cylinder misfires and loses the right power; a wrecked one gives nothing; the rest keep running.</summary>
    private static void MeDamage()
    {
        var w = new ConstructWorld();
        if (MeBase(w, new Vector3(0, 2, 0), 4) is not { } c)
            return;
        var cyl = new int[4];
        for (var k = 0; k < 4; k++)
            cyl[k] = MeFit(w, c, 2 + k, "+y", MeCyl, "base");
        MeFit(w, c, cyl[0], "head", MeCarb, "base");
        var def = c.DefOf(cyl[1]);
        MeExpect(c, "I4 160 kW", "before the shot");
        w.DamagePart(c, cyl[1], def.Health * 0.3f + def.Armour, out _);
        if (c.Dirty)
            Fail("modular engine: a scratch on a cylinder asked for a rebuild");
        w.DamagePart(c, cyl[1], def.Health * 0.3f + def.Armour, out _);
        if (!c.Dirty)
            Fail("modular engine: a cylinder under half health didn't change the sums");
        c.Rebuild();
        MeExpect(c, "I4 140 kW, 1 misfiring", "a shot cylinder misfires");
        w.DamagePart(c, cyl[1], def.Health, out _);
        c.Rebuild();
        MeExpect(c, "I4 120 kW, 1 wrecked", "a wrecked cylinder");
        if (c.Engine < 0)
            Fail("modular engine: losing a cylinder killed the engine");
        var look = ConstructEngine.Look(c, cyl[1]);
        if (!look.Contains("wrecked,") || !look.Contains("1 wrecked"))
            Fail($"modular engine: a wrecked cylinder's look line is \"{look}\"");
        foreach (var k in new[] { 0, 2, 3 })
            w.DamagePart(c, cyl[k], def.Health * 2, out _);
        c.Rebuild();
        if (c.Engine >= 0 || c.DriveProblem != "every cylinder is wrecked")
            Fail($"modular engine: with every cylinder wrecked: engine {c.Engine}, \"{c.DriveProblem}\" ({MeRead(c)})");
        Line($"modular engine: damage: {MeRead(c)}");
    }

    /// <summary>Fuel use follows the sums, in the tank: an injector rail's engine burns a fifth less a kW.</summary>
    private static void MeFuelUse()
    {
        float Burned(string fuel, out float kw, out float rate)
        {
            var w = new ConstructWorld();
            var q = new DriveWorld();
            (kw, rate) = (0, 0);
            if (MeBase(w, new Vector3(0, 2, 0), 2) is not { } c)
                return 0;
            var a = MeFit(w, c, 2, "+y", MeCyl, "base");
            MeFit(w, c, 3, "+y", MeCyl, "base");
            MeFit(w, c, a, "head", fuel, "base");
            MeFit(w, c, c.Engine >= 0 ? c.Engine : PartNamed(c, MeWheel), "+z", "Transmission", "-z");
            var sw = SpFitAnywhere(w, c, "Lever");
            SpWire(w, c, sw, c.Engine, 0);
            c.Parts[sw].State = 1;
            kw = MeOf(c)!.Kw;
            rate = MeOf(c)!.Burn;
            SpRun(w, q, 30);
            var f0 = c.Fuel;
            SpRun(w, q, 120);
            return f0 - c.Fuel;
        }
        var carb = Burned(MeCarb, out var ck, out var cr);
        var rail = Burned(MeRail, out var rk, out var rr);
        var burn = EngineRules.Current.BurnPerKw;
        Line($"modular engine: fuel in 2 s: carburettor {carb:0.###} l ({ck:0} kW), injector rail {rail:0.###} l ({rk:0} kW)");
        if (MathF.Abs(cr - ck * burn) > 1e-5f || MathF.Abs(rr - rk * 0.8f * burn) > 1e-5f)
            Fail($"modular engine: burn rates {cr} and {rr} aren't the sums");
        if (carb <= 0 || MathF.Abs(rail / carb - rr / cr) > 0.05f)
            Fail($"modular engine: the fuel burned ({carb} vs {rail} l) doesn't follow the burn rates ({cr}, {rr})");
    }

    /// <summary>40 cylinders: built once, allocating nothing, and one engine in the tick.</summary>
    private static void MeBigEngine()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        if (MeBase(w, new Vector3(0, 4, 0), 13) is not { } c)
            return;
        // (the first sections sit over the bed: ten sections past them carry all four sides)
        var fed = false;
        foreach (var side in new[] { "+x", "-x", "+y", "-y" })
        {
            fed = false;
            for (var k = 3; k < 13; k++)
            {
                var cyl = MeFit(w, c, 2 + k, side, MeCyl, "base");
                if (!fed)
                    MeFit(w, c, cyl, "head", MeRail, "base");
                fed = true;
            }
        }
        var want = 40 * EngineRules.Current.Kw[0] * 1.2f;
        var read = MeRead(c);
        Line($"modular engine: 40 cylinders: \"{read}\", {c.PartCount} parts");
        if (!read.StartsWith("R40") || MathF.Abs(MeOf(c)!.Kw - want) > 1f)
            Fail($"modular engine: the 40-cylinder engine reads \"{read}\", not {want:0} kW");
        MeFit(w, c, c.Engine, "+z", "Transmission", "-z");
        var sw = SpFitAnywhere(w, c, "Lever");
        SpWire(w, c, sw, c.Engine, 0);
        c.Parts[sw].State = 1;
        var clock = Stopwatch.StartNew();
        const int rounds = 50;
        ConstructEngine.Build(c);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < rounds; k++)
            ConstructEngine.Build(c);
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        var ms = clock.Elapsed.TotalMilliseconds / (rounds + 1);
        c.Rebuild();
        SpRun(w, q, 30);
        var tickBytes = GC.GetAllocatedBytesForCurrentThread();
        SpRun(w, q, 120);
        tickBytes = GC.GetAllocatedBytesForCurrentThread() - tickBytes;
        Line($"modular engine: working out 40 cylinders {ms:0.000} ms, {bytes} bytes; 120 running ticks {tickBytes} bytes; {c.Rpm:0} rpm");
        if (bytes != 0 || tickBytes != 0)
            Fail($"modular engine: a 40-cylinder engine allocates ({bytes} bytes working it out, {tickBytes} in 120 ticks)");
        if (ms > 5)
            Fail($"modular engine: working out 40 cylinders takes {ms:0.00} ms");
        if (!c.EngineRunning || c.Rpm <= 0)
            Fail("modular engine: the 40-cylinder engine doesn't run on a wire");
        // The Capital size scales the same sums.
        var big = new ConstructWorld();
        if (MeBase(big, new Vector3(0, 40, 0), 1, false, "Capital ") is { } cap)
        {
            var cc = MeFit(big, cap, 2, "+y", "Capital Cylinder", "base");
            MeFit(big, cap, cc, "head", "Capital Carburettor", "base");
            MeExpect(cap, "I1 320 MW - no flywheel on the end of the crank", "a Capital single");
        }
    }

    /// <summary>A real weld with the welder's own call: a Medium cylinder (too heavy for the hands) and pieces welded off the grid count like bolted ones.</summary>
    private static void MeFreeform()
    {
        var w = new ConstructWorld();
        if (MeBase(w, new Vector3(0, 3, 0), 1, true, "Medium ") is not { } c)
            return;
        var body = (string name, Vector3 at, Quaternion rot) =>
        {
            var n = w.Create(at, rot)!;
            w.AddPart(n, ConstructParts.Need(name), Vector3.Zero, 0);
            n.Rebuild();
            return n;
        };
        var crankAt = c.ToWorld(c.Parts[2].Pos);
        var cyl = ConstructParts.Need("Medium Cylinder");
        var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.2f);
        var mover = body("Medium Cylinder", crankAt + Vector3.Transform(new Vector3(0, 1f - cyl.BoundsMin.Y + 0.01f, 0), tilt), tilt);
        var joint = w.FreeformWeld(c, 2, mover, 0);
        c.Rebuild();
        if (joint < 0 || w.Get(mover.Id) != null)
        {
            Fail($"modular engine: the Medium cylinder wouldn't freeform-weld (joint {joint})");
            return;
        }
        var ci = c.PartCount - 1;
        var carbDef = ConstructParts.Need("Medium Carburettor");
        var cp = c.Parts[ci];
        c.SegPose(cp.Seg, out var sq, out var st);
        var carbAt = c.ToWorld(st + Vector3.Transform(cp.Pos + new Vector3(0, 1.5f - carbDef.BoundsMin.Y + 0.008f, 0), sq));
        var fuel = body("Medium Carburettor", carbAt, tilt);
        var j2 = w.FreeformWeld(c, ci, fuel, 0);
        c.Rebuild();
        var read = MeRead(c);
        Line($"modular engine: freeform: joints {joint}, {j2}, rigid {c.Rigid(c.Parts[ci].Seg)}, same piece {c.MoveSeg(c.Parts[ci].Seg) == c.MoveSeg(c.Parts[2].Seg)}, \"{read}\", engine {c.Engine}");
        if (j2 < 0 || read != "I1 800 kW" || c.Engine < 0)
            Fail($"modular engine: pieces welded off the grid don't make an engine: \"{read}\", engine {c.Engine}: {c.DriveProblem}");
        // A cylinder on a moving joint is no part of the engine: only one rigid piece runs as one.
        if (MeBase(w, new Vector3(30, 3, 0), 1, true) is not { } m)
            return;
        var tt = MeFit(w, m, 2, "+y", "Turntable", "base");
        var moving = MeFit(w, m, tt, "top", MeCyl, "base");
        if (moving >= 0 && m.EngineOf[moving] == m.EngineOf[2] && m.EngineOf[2] != 0)
            Fail("modular engine: a cylinder on a turntable counted as bolted to the crank");
    }

    /// <summary>Anchored, no seat: a switch on the flywheel's run port, a lever on its throttle, a power take-off turning a turntable.</summary>
    private static void MeAnchored()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        if (MeBase(w, new Vector3(0, 2, 0), 2) is not { } c)
            return;
        var a = MeFit(w, c, 2, "+y", MeCyl, "base");
        MeFit(w, c, 3, "+y", MeCyl, "base");
        MeFit(w, c, a, "head", MeCarb, "base");
        var fly = PartNamed(c, MeWheel);
        var trans = MeFit(w, c, fly, "+z", "Transmission", "-z");
        var pto = RigFit(w, c, RigFit(w, c, trans, "+z", "Drive Shaft 1 m", "end-"), "end+", "Power Take-Off", "-z");
        var tt = RigFit(w, c, pto, "+y", "Turntable", "base");
        RigFit(w, c, tt, "top", "Frame Block", "-y");
        var sw = SpFitAnywhere(w, c, "Switch");
        var lever = SpFitAnywhere(w, c, "Lever");
        c.Rebuild();
        if (tt < 0 || sw < 0 || lever < 0 || !c.Anchored || c.DriveSeat >= 0 || !c.HasPowertrain || !c.PtoDriven[tt])
        {
            Fail($"modular engine: the anchored rig didn't build (anchored {c.Anchored}, seat {c.DriveSeat}, powertrain {c.HasPowertrain}: {c.DriveProblem})");
            return;
        }
        SpWire(w, c, sw, c.Engine, 0);
        SpWire(w, c, lever, c.Engine, 1);
        c.Parts[lever].State = 1;
        var j = c.JointOf(tt);
        SpRun(w, q, 60);
        var off = MathF.Abs(c.Joints[j].Angle);
        var rpmOff = c.Rpm;
        c.Parts[sw].State = 1;
        var a0 = c.Joints[j].Angle;
        SpRun(w, q, 120);
        var on = MathF.Abs(c.Joints[j].Angle - a0);
        var rpm = c.Rpm;
        var load = c.EngineLoad;
        c.Parts[sw].State = 0;
        var stopped = SpUntil(w, q, 240, () => !c.EngineRunning && c.Rpm <= 0 && c.Asleep);
        Line($"modular engine: anchored, no seat: off {off:0.00} rad ({rpmOff:0} rpm), on {on:0.00} rad in 2 s ({rpm:0} rpm, load {load:0.00}), asleep {stopped} ticks after");
        if (off > 0.01f || rpmOff > 0 || on < 0.5f || rpm <= 0 || load < 0.5f)
            Fail("modular engine: an anchored seatless engine should run on its wire and turn its power take-off, and only then");
        if (stopped < 0)
            Fail("modular engine: the anchored engine didn't go back to sleep");
        SpClientAgrees(c, "modular engine base");
    }

    /// <summary>A buggy with a modular engine of the fixed Engine's numbers goes as far: on the gearbox, and through a routed shaft from a freeform-welded flywheel.</summary>
    private static void MeDriven()
    {
        if (ConstructBuild.FindBlueprint("Buggy") is not { } bp)
        {
            Fail("modular engine: no Buggy blueprint");
            return;
        }
        var q = new DriveWorld();
        var refWorld = new ConstructWorld();
        if (DriveSpawn(refWorld, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } refCar)
            return;
        var stock = ConstructParts.Need("Engine");
        var reference = FdRun(refWorld, q, refCar, "fixed engine");
        if (reference == null)
            return;
        var rules = new EngineRules();
        rules.Idle[0] = stock.IdleRpm;
        rules.Peak[0] = stock.PeakRpm;
        rules.Max[0] = stock.MaxRpm;
        rules.Kw[0] = stock.EngineTorque * (stock.PeakRpm / ConstructDrive.RpmPerRadS) / 1000f / 3f;
        rules.BurnPerKw = stock.BurnRate / (rules.Kw[0] * 3f);
        EngineRules.Use(rules);
        try
        {
            // On the gearbox: a flywheel where the engine was, a crank and three cylinders round it.
            var ep = refCar.Parts[19];
            for (var pass = 0; pass < 2; pass++)
            {
                var shaft = pass == 1;
                var v = FdCopy(bp);
                v.Links.Clear();
                // (the crank needs the room: the tank moves forward on the deck)
                v.Steps[19] = (MeWheel, shaft ? 16 : 18, shaft ? "-z" : "+y", "+z", 0);
                v.Steps[20] = ("Fuel Tank", 16, "front30", "-y", 0);
                if (shaft)
                {
                    var outDir = Rot24.Apply(ep.Rot, Vector3.UnitZ);
                    v.StepWeld[19] = (ep.Pos - outDir * 0.613f, Rot24.Quat(ep.Rot));
                }
                void Add(string part, int on, string socket, string own)
                {
                    v.Steps.Add((part, on, socket, own, 0));
                    v.StepSettings.Add(-1);
                    v.StepConfig.Add(null);
                    v.StepPaint.Add(0);
                    v.StepWeld.Add(null);
                }
                var n = v.Steps.Count;
                Add(MeCrank, 19, "crank", "front");
                Add(MeCyl, n, "+y", "base");
                Add(MeCyl, n, "+x", "base");
                Add(MeCyl, n, "-x", "base");
                Add(MeCarb, n + 1, "head", "base");
                Add(MeCarb, n + 2, "head", "base");
                Add(MeCarb, n + 3, "head", "base");
                var w = new ConstructWorld();
                var car = ConstructBuild.BuildOnGround(w, v, Vector3.Zero, MathF.PI / 2, out var why);
                if (car == null)
                {
                    Fail($"modular engine: the modular buggy doesn't build{(shaft ? " (shaft)" : "")}: {why}");
                    return;
                }
                var nodes = ReadOnlySpan<CRouteNode>.Empty;
                var fly = PartNamed(car, MeWheel);
                var fd = car.DefOf(fly);
                if (shaft)
                {
                    var outPort = (byte)Array.FindIndex(fd.Sockets, s => s.Name == "+z");
                    var inPort = (byte)Array.FindIndex(car.DefOf(18).Sockets, s => s.Name == "-z");
                    var bad = ConstructRoutes.Check(w, car, RouteType.Shaft, fly, outPort, 255, 0, Vector3.Zero, nodes, 18, inPort);
                    if (bad != null || ConstructRoutes.Add(w, car, RouteType.Shaft, fly, outPort, 255, 0, Vector3.Zero, nodes, 18, inPort) < 0)
                    {
                        Fail($"modular engine: a shaft from the flywheel to the gearbox is refused: {bad}");
                        return;
                    }
                }
                // The tank's pipe, into the fuel system's own port.
                var tank = PartNamed(car, "Fuel Tank");
                var carb = PartNamed(car, MeCarb);
                const byte fuelIn = 255; // (a part's first pipe port)
                var pipeBad = ConstructRoutes.Check(w, car, RouteType.Pipe, tank, 255, 255, 0, Vector3.Zero, nodes, carb, fuelIn);
                if (pipeBad != null || ConstructRoutes.Add(w, car, RouteType.Pipe, tank, 255, 255, 0, Vector3.Zero, nodes, carb, fuelIn) < 0)
                {
                    Fail($"modular engine: the fuel pipe into the carburettor is refused: {pipeBad}");
                    return;
                }
                car.Rebuild();
                Line($"modular engine: {(shaft ? "through a shaft" : "on the gearbox")}: \"{MeRead(car)}\", {car.Mass:0} kg, can drive {car.CanDrive} {car.DriveProblem}");
                if (FdRun(w, q, car, shaft ? "modular, shaft" : "modular") is not { } r)
                    return;
                var rel = MathF.Abs(r.Fwd - reference.Value.Fwd) / MathF.Max(reference.Value.Fwd, 0.1f);
                if (rel > 0.2f || r.Driven != reference.Value.Driven || r.Back > -0.5f)
                    Fail($"modular engine: the same power doesn't drive the same ({r.Fwd:0.00} m against the fixed engine's {reference.Value.Fwd:0.00} m{(shaft ? ", through a shaft" : "")}, reverse {r.Back:0.0})");
                if (r.Bytes > 0)
                    Fail($"modular engine: {r.Bytes} bytes allocated driving");
            }
        }
        finally
        {
            EngineRules.Use(null);
        }
    }

    /// <summary>The smoke leaves the top of the stack and thickens with the load; the sound picks the nearest engines.</summary>
    private static void MeEffects()
    {
        var w = new ConstructWorld();
        if (MeBase(w, new Vector3(0, 2, 0)) is not { } c)
            return;
        var cyl = MeFit(w, c, 2, "+y", MeCyl, "base");
        var fuel = MeFit(w, c, cyl, "head", MeCarb, "base");
        var outlet = MeFit(w, c, fuel, "top", "Small Exhaust Outlet", "base");
        var s1 = MeFit(w, c, outlet, "top", "Small Smokestack", "base");
        var s2 = MeFit(w, c, s1, "top", "Small Smokestack", "base");
        var e = MeOf(c);
        if (e == null || e.EmitCount != 1 || e.Emit[0] != s2)
            Fail($"modular engine: the smoke should leave the top stack (part {s2}), not {(e == null ? "nothing" : string.Join(", ", e.Emit.Take(e.EmitCount)))}");
        if (c.Engine < 0)
        {
            Fail($"modular engine: the stacked engine doesn't run: {c.DriveProblem}");
            return;
        }
        c.EngineRunning = true;
        c.Rpm = 3000;
        var body = Matrix4x4.CreateFromQuaternion(c.Rot) * Matrix4x4.CreateTranslation(c.Pos);
        long Puffs(float load)
        {
            EngineFx.Clear();
            c.EngineLoad = load;
            var n0 = EngineFx.Emitted;
            c.EngineDrawn = 0;
            for (var f = 1; f <= 120; f++)
                EngineFx.Frame(c, body, f / 60.0, 1f);
            return EngineFx.Emitted - n0;
        }
        var idle = Puffs(0);
        var hard = Puffs(1);
        var dark = EngineFx.LastDark;
        c.SocketAt(s2, c.DefOf(s2).SmokeSocket, out var top, out _);
        var off = Vector3.Distance(EngineFx.LastAt, Vector3.Transform(top, body));
        Line($"modular engine: smoke in 2 s: {idle} puffs idle, {hard} at full load (dark {dark:0.0}), {off:0.00} m from the stack's top");
        if (idle < 1 || hard < idle * 5 || dark < 0.9f || off > 0.2f)
            Fail("modular engine: the smoke should pour harder and blacker from the top of the stack as the load rises");
        // A stack across the yard, fed by a pipe from the outlet, still smokes.
        if (MeBase(w, new Vector3(20, 2, 0)) is { } p)
        {
            var pc = MeFit(w, p, 2, "+y", MeCyl, "base");
            var pf = MeFit(w, p, pc, "head", MeCarb, "base");
            var po = MeFit(w, p, pf, "top", "Small Exhaust Outlet", "base");
            var stack = SpFitAnywhere(w, p, "Small Smokestack", "base");
            if (stack >= 0)
            {
                var nodes = ReadOnlySpan<CRouteNode>.Empty;
                const byte si = 255, so = 255; // (each part's first pipe port)
                var bad = ConstructRoutes.Check(w, p, RouteType.Pipe, po, so, 255, 0, Vector3.Zero, nodes, stack, si);
                if (bad == null && ConstructRoutes.Add(w, p, RouteType.Pipe, po, so, 255, 0, Vector3.Zero, nodes, stack, si) >= 0)
                {
                    p.Rebuild();
                    var pe = MeOf(p);
                    if (pe == null || pe.EmitCount < 1 || pe.Emit[0] != stack)
                        Fail($"modular engine: a stack on a pipe from the outlet doesn't smoke (emit {(pe == null ? -1 : pe.EmitCount > 0 ? pe.Emit[0] : -1)}, stack {stack})");
                }
                else
                    Fail($"modular engine: an exhaust pipe into a stack is refused: {bad}");
            }
        }
        // The sound: the loop per cylinder count, pitched by rpm, the nearest engines only.
        var sounds = new EngineSounds();
        sounds.Pick(w, c.WorldCom);
        var engines = w.Bodies.Count(b => b.ActiveEngine != null && b.EngineRunning);
        Line($"modular engine: sound: {sounds.Count} voices for {engines} running, variant {sounds.Variant[0]}, pitch {sounds.Pitch[0]:0.00}");
        if (sounds.Count != 1 || sounds.Body[0] != c.Id || MathF.Abs(sounds.Pitch[0] - 3000f / Synth.EngineLoopRpm) > 0.01f)
            Fail("modular engine: the sound didn't pick the running engine at its rpm");
        if (Synth.EngineLoopVariant(8, false) == Synth.EngineLoopVariant(4, false) || Synth.EngineLoopVariant(8, true) == Synth.EngineLoopVariant(8, false) || Synth.EngineLoopVariant(40, false) != Synth.EngineLoopVariant(16, false))
            Fail("modular engine: the loop doesn't change with the cylinders or a misfire");
        EngineFx.Clear();
    }

    /// <summary>The player's whole chain, with the readout at each step: the menu, PlaceNew, the held part's ghost, the click, the Attach message.</summary>
    private static void MeChain()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"modular engine: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        if (SpawnLocalSafely(g, ref now) is { } no)
        {
            Fail($"modular engine: {no}");
            return;
        }
        var me = g.Local!;
        var w = g.Constructs;
        if (OpenSpot(w) is not { } spot)
        {
            Fail("modular engine: no open level ground on the yard");
            return;
        }
        void Take(ConstructPartDef d)
        {
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
            g.SandboxSend(CActKind.Tool, part: (byte)d.Tool);
            g.SandboxTakePart(d.Id);
            Run(g, ref now, 0.2);
        }
        var crank = ConstructParts.Need(MeCrank);
        Take(crank);
        var bodies = w.Bodies.Count;
        StandBy(me, spot, 2.5f);
        Run(g, ref now, 0.1);
        g.SandboxSend(CActKind.PlaceNew, at: spot, yaw: 0, normal: Vector3.UnitY, variant: 0, flags: ConstructPlace.FlagGrid | ConstructPlace.FlagAnchor);
        Run(g, ref now, crank.AttachSeconds + 0.4);
        if (w.Bodies.Count <= bodies)
        {
            Fail($"modular engine: the crank wasn't set down (\"{g.SandboxNotice}\")");
            return;
        }
        var c = w.Bodies[^1];
        if (ConstructEngine.Look(c, 0) != "Small Crank Section - no cylinders on the crank")
            Fail($"modular engine: the first piece's look line is \"{ConstructEngine.Look(c, 0)}\"");
        // Each piece from the menu, the crosshair on what it goes on: a ghost, the click, the readout.
        string Click(string part, int on, string then)
        {
            var d = ConstructParts.Need(part);
            Take(d);
            StandBy(me, c.WorldCom, 2.6f);
            Run(g, ref now, 0.05);
            var eye = me.RenderEye;
            var target = c.ToWorld(c.Parts[on].Pos);
            var dir = Vector3.Normalize(target - eye);
            if (!w.Raycast(eye, dir, ConstructBuild.Reach + 1, out var hit) || hit.Body != c)
                return $"the crosshair on the {c.DefOf(on).Name} misses it";
            var vp = Matrix4x4.CreateLookAt(eye, eye + dir, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 0.1f, 100f);
            var look = Program.SandboxHeldFrame(g, eye, dir, new Vector2(1920, 1080), vp, false, now, out var sel, out var valid);
            if (sel < 0 || !valid)
                return $"{part}: no ghost (selected {sel}, valid {valid}): HUD \"{look}\"";
            var before = c.PartCount;
            g.SandboxSay("");
            Program.SandboxHeldFrame(g, eye, dir, new Vector2(1920, 1080), vp, true, now + 0.02, out _, out _);
            Run(g, ref now, d.AttachSeconds + 0.4);
            if (c.PartCount != before + 1)
                return $"{part}: the click attached nothing (\"{g.SandboxNotice}\")";
            var read = ConstructEngine.Readout(MeOf(c)!);
            Line($"modular engine: chain: {part} on the {c.DefOf(on).Name}: \"{read}\"");
            return read == then ? "" : $"{part}: the readout is \"{read}\", not \"{then}\"";
        }
        var steps = new (string Part, Func<int> On, string Then)[]
        {
            (MeCyl, () => 0, "I1 0 kW - no fuel system on bank 1"),
            (MeCarb, () => 1, "I1 40 kW - no flywheel on the end of the crank"),
            (MeWheel, () => 0, "I1 40 kW - no fuel: link a fuel tank"),
        };
        foreach (var (part, on, then) in steps)
            if (Click(part, on(), then) is { Length: > 0 } why)
            {
                Fail($"modular engine: chain: {why}");
                return;
            }
        var fly = PartNamed(c, MeWheel);
        var tank = ConstructParts.Need("Fuel Tank");
        MeFit(w, c, fly, "+x", tank.Name, "-x");
        var ok = ConstructEngine.Readout(MeOf(c)!);
        Line($"modular engine: chain: four parts and a tank: \"{ok}\", {c.PartCount} parts");
        if (ok != "I1 40 kW" || c.PartCount != 5 || c.Engine != fly)
            Fail($"modular engine: the four-part engine reads \"{ok}\" ({c.PartCount} parts, engine {c.Engine}): {c.DriveProblem}");
        // The ghost refuses what doesn't go there, with the HUD's reason: a cylinder aimed at the flywheel finds the crank instead or says why.
        var cyl = ConstructParts.Need(MeCyl);
        var fs = Array.FindIndex(c.DefOf(fly).Sockets, s => s.Name == "+y");
        if (SnapBuild.CanMate(c, cyl, 0, fly, fs))
            Fail("modular engine: a cylinder's ghost shows on a flywheel");
    }

    /// <summary>"Modular engine network": a client building on the host's engine, and reading it: the same sums, the rpm and load in the pose, a shot cylinder's misfire.</summary>
    private static void CheckModularEngineNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"modular engine network: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26391;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("modular engine network: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var w = server.Constructs;
        var at = ConstructWorld.Ground(World.Query, sp.Position.X + 3f, sp.Position.Z) + new Vector3(0, 1.2f, 0);
        if (MeBase(w, at, 2) is not { } c)
            return;
        c.Owner = sp.Id;
        var cyl = MeFit(w, c, 2, "+y", MeCyl, "base");
        var second = MeFit(w, c, 3, "+y", MeCyl, "base");
        MeFit(w, c, cyl, "head", MeCarb, "base");
        var fly = PartNamed(c, MeWheel);
        MeFit(w, c, fly, "+z", "Transmission", "-z");
        var sw = SpFitAnywhere(w, c, "Switch");
        var lever = SpFitAnywhere(w, c, "Lever");
        SpWire(w, c, sw, c.Engine, 0);
        SpWire(w, c, lever, c.Engine, 1);
        c.Parts[lever].State = 1;
        c.Parts[sw].State = 1;
        c.Wake();
        Pump(server, [a], 1.2, ref now, clock);
        var copy = a.Constructs.Get(c.Id);
        if (copy == null)
        {
            Fail("modular engine network: the client has no copy of the engine");
            return;
        }
        var host = MeRead(c);
        var theirs = MeRead(copy);
        Line($"modular engine network: host \"{host}\" {c.Rpm:0} rpm load {c.EngineLoad:0.00}; client \"{theirs}\" {copy.Rpm:0} rpm load {copy.EngineLoad:0.00}, running {copy.EngineRunning}");
        if (host != theirs || copy.ActiveEngine == null || MathF.Abs(copy.EngineDef.EngineTorque - c.EngineDef.EngineTorque) > 0.01f)
            Fail($"modular engine network: the client's sums differ (\"{theirs}\", host \"{host}\")");
        if (!copy.EngineRunning || copy.Rpm <= 0 || copy.EngineLoad < 0.4f)
            Fail($"modular engine network: the client doesn't see it running (running {copy.EngineRunning}, {copy.Rpm} rpm, load {copy.EngineLoad})");
        // A client's Attach: a third cylinder's section is bare, a client fits a fuel rail: the sums follow on both.
        var hurt = c.DefOf(second);
        w.DamagePart(c, second, hurt.Health * 0.6f + hurt.Armour, out _);
        Pump(server, [a], 0.6, ref now, clock);
        var hostHurt = MeRead(c);
        var clientHurt = MeRead(copy);
        Line($"modular engine network: shot: host \"{hostHurt}\", client \"{clientHurt}\"");
        if (!hostHurt.Contains("1 misfiring") || hostHurt != clientHurt)
            Fail($"modular engine network: a shot cylinder's misfire doesn't reach the client (host \"{hostHurt}\", client \"{clientHurt}\")");
        w.DamagePart(c, second, hurt.Health * 2f, out _);
        Pump(server, [a], 0.6, ref now, clock);
        if (MeRead(c) != MeRead(copy) || !MeRead(copy).Contains("1 wrecked"))
            Fail($"modular engine network: a wrecked cylinder: host \"{MeRead(c)}\", client \"{MeRead(copy)}\"");
        // The client's Attach message: a smokestack piece on the outlet it fits.
        var outlet = ConstructParts.Need("Small Exhaust Outlet");
        var head = Array.FindIndex(c.DefOf(cyl).Sockets, s => s.Name == "+x");
        a.SandboxSend(CActKind.Tool, part: (byte)outlet.Tool);
        a.SandboxTakePart(outlet.Id);
        Pump(server, [a], 0.3, ref now, clock);
        if (!SnapBuild.Fit(copy, outlet, cyl, head, -1, out var ghost))
        {
            Fail("modular engine network: the client's ghost for an outlet on a head doesn't fit");
            return;
        }
        var parts = c.PartCount;
        StandBy(sp, c.WorldCom, 2.5f);
        Pump(server, [a], 0.2, ref now, clock);
        a.SandboxSend(CActKind.Attach, body: c.Id, part: (byte)cyl, socket: (byte)head, variant: (byte)ghost.Variant);
        Pump(server, [a], outlet.AttachSeconds + 0.6, ref now, clock);
        Line($"modular engine network: the client's Attach: {parts} to {c.PartCount} parts (client {copy.PartCount}), emits {MeOf(c)!.EmitCount}/{MeOf(copy)!.EmitCount}: \"{a.SandboxNotice}\"");
        if (c.PartCount != parts + 1 || copy.PartCount != c.PartCount || MeOf(copy)!.Emit[0] != MeOf(c)!.Emit[0])
            Fail($"modular engine network: a client's Attach didn't reach both sums ({c.PartCount}, {copy.PartCount}): \"{a.SandboxNotice}\"");
    }
}
