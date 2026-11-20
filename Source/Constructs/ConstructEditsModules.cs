using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Module sockets by hand (docs/sandbox/design/module-sockets.md 6.1 and 6.4; ModuleSockets.cs): F on a Module Socket (or a
/// Module Stand, or the plug of a module fitted to one) takes its module off when one is fitted (it comes away whole as a
/// body of its own, lifted clear of the plate), and fits the nearest free module whose plug lies within the hand's pull
/// (2.5 m) of a free plate: the module is turned square onto the plate at the allowed turn nearest the way it lies, and
/// welded on by its plug (<see cref="ConstructWorld.Merge"/>). A module standing on another stand is taken off that first.
/// In Survival the hand's limits hold (up to 2x2 and the physics gun's lift). Server; the client sends the plain Use.
/// </summary>
public sealed partial class ConstructEdits
{
    public const float ModuleLift = 0.05f; // m: a module taken off is set this far off its plate, along the plate's normal

    /// <summary>
    /// For the window tool's <c>modsock fit PART</c> (host): the nearest loose module of plate <paramref name="plate"/>'s size is set still
    /// 0.6 m off its face, as a player's physics gun would hold it, then F on the plate runs (the hand's or a fitter's whole fit).
    /// </summary>
    public string? ModuleFitNear(ConstructWorld world, byte player, Construct c, int plate)
    {
        if (c.Dirty)
            c.Rebuild();
        var si = ModuleSockets.SocketOfPlate(c, plate);
        if (si < 0)
            return "not a tracked socket";
        var s = c.ModSockets[si];
        c.SocketAt(s.Plate, c.DefOf(s.Plate).ModSocket, out var faceAt, out _);
        var wface = c.ToWorld(faceAt);
        var wdir = Vector3.Transform(Rot24.Apply(c.Parts[s.Plate].Rot, Vector3.UnitY), c.Rot);
        Construct? best = null;
        var bestDist = 30f;
        foreach (var o in world.Bodies)
        {
            if (o == c)
                continue;
            if (o.Dirty)
                o.Rebuild();
            foreach (var m in o.Modules)
                if (m.Socket < 0 && o.DefOf(m.Plug).ModW == s.W && o.DefOf(m.Plug).ModL == s.L && Vector3.Distance(o.ToWorld(o.Com), wface) < bestDist)
                    (best, bestDist) = (o, Vector3.Distance(o.ToWorld(o.Com), wface));
        }
        if (best == null)
            return $"no loose {ModuleSocketTable.SizeText(s.W, s.L)} module within 30 m";
        best.SocketAt(best.Modules[0].Plug, best.DefOf(best.Modules[0].Plug).ModSocket, out var pat, out _);
        best.Pos += wface + wdir * 0.6f - best.ToWorld(pat);
        best.PrevPos = best.Pos;
        best.Vel = Vector3.Zero;
        best.SocketAt(best.Modules[0].Plug, best.DefOf(best.Modules[0].Plug).ModSocket, out pat, out _);
        var why = ModuleUseLogged(world, player, c, plate);
        return why == null ? null : $"{why} (body {best.Id}: its plug {Vector3.Distance(best.ToWorld(pat), wface):0.00} m from the face)";
    }

    /// <summary>For the window tool's <c>modsock use PART</c> (host): F on a plate or a fitted module's part, as a player's F.</summary>
    public string? ModuleUseNow(ConstructWorld world, byte player, Construct c, int part) => ModuleUseLogged(world, player, c, part);

    private string? ModuleUseLogged(ConstructWorld world, byte player, Construct c, int part)
    {
        var why = ModuleUse(world, player, c, part);
        if (why != null)
            Console.WriteLine($"[modules] player {player}: F on construct {c.Id} part {part}: {why}");
        return why;
    }

    /// <summary>F on a plate or a fitted plug of <paramref name="c"/>: null when done, else why not.</summary>
    private string? ModuleUse(ConstructWorld world, byte player, Construct c, int part)
    {
        if (c.Dirty)
            c.Rebuild();
        if (c.Parts[part].Broken)
            return $"the {c.DefOf(part).Name} is wrecked";
        if (Contested(c, player) is { } defended)
            return defended;
        if (SalvageRefusal(c, player) != null)
            return "that construct is someone else's";
        var d = c.DefOf(part);
        var si = -1;
        if (d.ModPart == ModPart.Socket)
            si = ModuleSockets.SocketOfPlate(c, part);
        else if (ModuleSockets.ModuleOfPart(c, part) is var mi0 and >= 0)
        {
            var plate = ModuleSockets.PlateOf(c, c.Modules[mi0].Plug);
            si = plate >= 0 ? ModuleSockets.SocketOfPlate(c, plate) : -1;
            if (si < 0)
                return null; // (a loose module's plug: nothing to do; the caller says nothing)
        }
        if (si < 0)
            return "that socket isn't tracked (too many sockets on one construct)";
        var s = c.ModSockets[si];
        if (ModuleFitters.Busy(world, c, s.Plate))
            return "a Module Fitter is at work on that socket";
        return s.Module >= 0 ? ModuleOff(world, player, c, si) : ModuleOn(world, player, c, si);
    }

    /// <summary>Takes the module fitted to socket <paramref name="si"/> off: its parts are cut out as a body of their own.</summary>
    private string? ModuleOff(ConstructWorld world, byte player, Construct c, int si)
    {
        var s = c.ModSockets[si];
        var m = c.Modules[s.Module];
        if (m.Bridged)
            return "that module is welded to more than its socket: take the extra welds off with the attach tool first";
        if ((!world.Creative || world.TimedMachines) && !s.Stand && (!ModuleSocketTable.Current.HandSize(s.W, s.L) || m.Mass > LiftOf(player, false)) && ModuleFitters.Reach(world, c) > 0)
        {
            if (ModuleFitters.TakeOff(world, c, si) is { } fitterWhy)
                return fitterWhy;
            Done.Add($"player {player}: a Module Fitter takes the module off socket part {s.Plate} of construct {c.Id}");
            return null;
        }
        if (!world.Creative && !s.Stand)
        {
            var t = ModuleSocketTable.Current;
            if (!t.HandSize(s.W, s.L))
                return $"a {ModuleSocketTable.SizeText(s.W, s.L)} module is taken off by a Module Fitter: by hand, up to {ModuleSocketTable.SizeText(t.HandUpToW, t.HandUpToL)}";
            if (m.Mass > LiftOf(player, false))
                return $"the module weighs {m.Mass / 1000f:0.#} t: by hand up to {LiftOf(player, false) / 1000f:0.#} t (a Module Fitter lifts more)";
        }
        var of = c.ModuleOf!;
        // The module and every submodule on it (a turret on a turret bay): modules whose plug sits on a plate of one taken.
        var take = new bool[c.Modules.Length];
        take[s.Module] = true;
        for (var more = true; more;)
        {
            more = false;
            for (var k = 0; k < c.Modules.Length; k++)
                if (!take[k] && ModuleSockets.PlateOf(c, c.Modules[k].Plug) is var pl and >= 0 && of[pl] >= 0 && take[of[pl]])
                    take[k] = more = true;
        }
        var marks = new bool[c.PartCount];
        var count = 0;
        for (var j = 0; j < c.PartCount; j++)
            if (of[j] >= 0 && take[of[j]])
            {
                if (c.DefOf(j).Role == PartRole.Seat && c.Parts[j].SitterPlus1 != 0)
                    return "someone is sitting in it: they get out first";
                marks[j] = true;
                count++;
            }
        if (count >= c.PartCount)
            return "that's the whole construct";
        var name = ModuleSockets.ModuleName(c, s.Module);
        var normal = Vector3.Transform(Rot24.Apply(c.Parts[s.Plate].Rot, Vector3.UnitY), c.Rot);
        if (world.CutOut(c, marks, player) is not { } piece)
            return "the world is full";
        piece.Pos += normal * ModuleLift;
        piece.PrevPos = piece.Pos;
        piece.Wake();
        Done.Add($"player {player}: took the {name} ({count} parts) off socket part {s.Plate} of construct {c.Id} -> {piece.Id}");
        return null;
    }

    /// <summary>Fits the nearest free module within the hand's pull onto socket <paramref name="si"/>.</summary>
    private string? ModuleOn(ConstructWorld world, byte player, Construct c, int si)
    {
        var s = c.ModSockets[si];
        var plateDef = c.DefOf(s.Plate);
        if (c.Parts[s.Plate].Seg != 0)
            return "a socket on a joint takes its module by the attach tool's socket weld";
        if (s.InTheWay >= 0)
            return $"in the way: the {c.DefOf(s.InTheWay).Name} (part {s.InTheWay}) is in the socket's room";
        var hand = ModuleSocketTable.Current.HandPull;
        var fitterReach = world.Creative && !world.TimedMachines ? 0 : ModuleFitters.Reach(world, c); // (a Module Fitter aboard: modules within its reach of the socket; it checks both ends)
        var pull = MathF.Max(hand, fitterReach * 2);
        c.SocketAt(s.Plate, plateDef.ModSocket, out var faceAt, out var faceDir);
        var wface = c.ToWorld(faceAt);
        Construct? best = null;
        var bestPlug = -1;
        var bestDist = pull;
        // A module stored on a stand of this same construct (a rack beside the socket) is a candidate too.
        foreach (var m in c.Modules)
        {
            if (!m.OnStand || c.Parts[m.Plug].Broken)
                continue;
            var pd = c.DefOf(m.Plug);
            if (pd.ModW != s.W || pd.ModL != s.L)
                continue;
            c.SocketAt(m.Plug, pd.ModSocket, out var pat, out _);
            var dist = Vector3.Distance(c.ToWorld(pat), wface);
            if (dist < bestDist)
                (best, bestPlug, bestDist) = (c, m.Plug, dist);
        }
        foreach (var o in world.Bodies)
        {
            if (o == c || o.Driver != 255 || o.CarriedBy != 0 || o.HeldBy != 0 || Vector3.Distance(o.ToWorld(o.Com), wface) > o.Radius + pull) // (Radius is from the centre of mass: a module cut off a ship keeps the ship's origin, metres away)
                continue;
            if (o.Dirty)
                o.Rebuild();
            foreach (var m in o.Modules)
            {
                if (m.Socket >= 0 || o.Parts[m.Plug].Seg != 0 || o.Parts[m.Plug].Broken)
                    continue;
                var pd = o.DefOf(m.Plug);
                if (pd.ModW != s.W || pd.ModL != s.L)
                    continue;
                o.SocketAt(m.Plug, pd.ModSocket, out var pat, out _);
                var dist = Vector3.Distance(o.ToWorld(pat), wface);
                if (dist < bestDist)
                    (best, bestPlug, bestDist) = (o, m.Plug, dist);
            }
        }
        if (best == null)
            return $"bring a {ModuleSocketTable.SizeText(s.W, s.L)} module within {pull:0.#} m of the socket first (the physics gun carries it)";
        // By hand when it may (Creative, or in the hand's size, lift and pull), else by a Module Fitter aboard.
        var bestMass = ModuleSockets.ModuleOfPart(best, bestPlug) is var bmi and >= 0 ? best.Modules[bmi].Mass : 0f;
        var byFitter = fitterReach > 0 && (bestDist > hand || !ModuleSocketTable.Current.HandSize(s.W, s.L) || bestMass > LiftOf(player, false));
        if (Contested(best, player) is { } theirs)
            return theirs;
        if (SalvageRefusal(best, player) != null)
            return "that module is someone else's";
        string? PoseOf(Construct body, int plug, out byte bodyTurn, out Vector3 rel, out Quaternion rot, out Vector3 pos) => SeatPose(c, s, body, plug, out bodyTurn, out rel, out rot, out pos);
        var mi = ModuleSockets.ModuleOfPart(best, bestPlug);
        // The fit's rules before anything moves (a module on a stand is judged by its own parts).
        if ((PoseOf(best, bestPlug, out _, out _, out var rot0, out var pos0) ?? ModuleSockets.FitRefusal(c, si, best, mi, rot0, pos0, byHand: !byFitter, world.Creative, moduleOnly: true, lift: LiftOf(player, world.Creative))) is { } early)
            return early;
        // Off a stand (or anything else it stands on) first: its parts as a body of their own.
        var of = best.ModuleOf!;
        var all = true;
        for (var j = 0; j < best.PartCount && all; j++)
            all = of[j] == mi;
        if (!all)
        {
            if (best.Modules[mi].Bridged)
                return "that module is welded to more than its plug: take the extra welds off first";
            var marks = new bool[best.PartCount];
            var before = 0; // (a stand on this construct: the plate's index after the cut)
            for (var j = 0; j < best.PartCount; j++)
            {
                marks[j] = of[j] == mi;
                if (marks[j] && j < s.Plate)
                    before++;
            }
            var own = best == c;
            if (world.CutOut(best, marks, player) is not { } lifted)
                return "the world is full";
            if (own)
            {
                c.Rebuild();
                si = ModuleSockets.SocketOfPlate(c, s.Plate - before);
                if (si < 0)
                    return "the socket moved";
                s = c.ModSockets[si];
                plateDef = c.DefOf(s.Plate);
                c.SocketAt(s.Plate, plateDef.ModSocket, out faceAt, out faceDir);
            }
            best = lifted;
            best.Rebuild();
            bestPlug = best.Modules.Length > 0 ? best.Modules[0].Plug : -1;
            mi = 0;
            if (bestPlug < 0)
                return "the module came apart";
        }
        if (PoseOf(best, bestPlug, out var bodyTurn, out var rel, out var rot, out var pos) is { } noTurn)
            return noTurn;
        var plugDef = best.DefOf(bestPlug);
        if (ModuleSockets.FitRefusal(c, si, best, mi, rot, pos, byHand: !byFitter, world.Creative, lift: LiftOf(player, world.Creative)) is { } why)
            return why;
        if (world.CanMerge(c, best) is { } merge)
            return merge;
        if (byFitter)
        {
            if (ModuleFitters.Fit(world, c, si, best, bestPlug, bodyTurn, rel, rot, pos) is { } fitterWhy)
                return fitterWhy;
            Done.Add($"player {player}: a Module Fitter fits module construct {best.Id} onto socket part {s.Plate} of construct {c.Id}");
            return null;
        }
        if (SocketWeld.Overlap(world, Query, best, c, pos, rot, Feet(world)) is { } blocked)
            return blocked;
        var from = best.Id;
        var parts = best.PartCount;
        best.Rot = best.PrevRot = rot;
        best.Pos = best.PrevPos = pos;
        best.Vel = best.AngVel = Vector3.Zero;
        var plan = new JoinPlan { Other = best, PartA = s.Plate, SockA = plateDef.ModSocket, PartB = bestPlug, SockB = plugDef.ModSocket, Turn = bodyTurn, Offset = rel };
        if (!world.Merge(c, plan))
            return "it can't be fitted";
        Done.Add($"player {player}: fitted module construct {from} ({parts} parts) onto socket part {s.Plate} of construct {c.Id}");
        return null;
    }

    /// <summary>
    /// Where module body <paramref name="body"/> goes when its plug <paramref name="plug"/> is fitted to socket <paramref name="s"/> of
    /// <paramref name="c"/>: the plug's face onto the plate's, at the allowed turn nearest the way it lies. The body's turn (Rot24) and
    /// offset in the ship's frame, and its world pose now; null, or why no turn fits.
    /// </summary>
    public static string? SeatPose(Construct c, in CModSocket s, Construct body, int plug, out byte bodyTurn, out Vector3 rel, out Quaternion rot, out Vector3 pos)
    {
        bodyTurn = 0;
        rel = pos = default;
        rot = Quaternion.Identity;
        var plateDef = c.DefOf(s.Plate);
        c.SocketAt(s.Plate, plateDef.ModSocket, out var faceAt, out var faceDir);
        var plugDef = body.DefOf(plug);
        var plugPartRot = body.Parts[plug].Rot;
        var inv = Quaternion.Conjugate(c.Rot);
        var lies = inv * body.Rot * Rot24.Quat(plugPartRot); // (the plug's turn in the ship's frame now)
        var want = (byte)(faceDir ^ 1);
        byte plugRot = 255;
        var bestMatch = -1f;
        for (byte r = 0; r < Rot24.Count; r++)
        {
            if (Rot24.ApplyDir(r, plugDef.Sockets[plugDef.ModSocket].Dir) != want || !ModuleSockets.TurnFits(plateDef, c.Parts[s.Plate].Rot, plugDef, r, s.Keyed))
                continue;
            var match = MathF.Abs(Quaternion.Dot(Rot24.Quat(r), lies));
            if (match > bestMatch)
                (plugRot, bestMatch) = (r, match);
        }
        if (plugRot == 255)
            return "no turn fits";
        for (byte b = 0; b < Rot24.Count; b++)
            if (Rot24.Compose(b, plugPartRot) == plugRot)
            {
                bodyTurn = b;
                break;
            }
        body.SocketAt(plug, plugDef.ModSocket, out var plugAt, out _);
        rel = ConstructWorld.Quantise(faceAt - Rot24.Apply(bodyTurn, plugAt));
        rot = Quaternion.Normalize(c.Rot * Rot24.Quat(bodyTurn));
        pos = c.ToWorld(rel);
        return null;
    }
}
