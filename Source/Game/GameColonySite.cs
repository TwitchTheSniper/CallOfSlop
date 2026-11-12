using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The penal colony's site (docs/sandbox/constructs/buildings.md "The site"): what the buildings stand on and what joins
/// them. On a fresh story world the ground under the whole colony is graded to one level before anything but the yard is
/// built: a gravel bench in the colony's own plane (cut where the snow stands over it, filled where it falls away, as a
/// camp on frozen ground is really founded), with a ramp down to the land at each end of the street. Every building then
/// stands at one height, and the site's own pieces (the street's slabs, the muster square, the covered way, the heating
/// mains from the boiler house, the gate piers) are laid in that plane at fixed spots: rows of
/// Story/Opening/ColonyBuildings.json marked "site". The grade is ground ops like a player's Flatten (Game.Dig), so it
/// saves and reaches a joiner with the rest of the dug ground. A world from before the site keeps its ground and gets none
/// of the site's pieces: its buildings stand at their own heights.
/// </summary>
public sealed partial class Game
{
    private bool _csBench; // this world's colony stands on a graded bench (a fresh world; not kept: only its building uses it)
    private float _csLift; // the bench's top over the colony's pads' undersides (m): just over the highest land under it

    /// <summary>A spot of the site plan in the world: x and z in the colony's frame, <paramref name="up"/> m over the bench.</summary>
    private Vector3 ColonySiteSpot(Construct colony, float x, float z, float up = 0) => colony.ToWorld(new Vector3(x, colony.LocalMin.Y + _csLift + up, z));

    /// <summary>Server, a fresh story world, the colony just built and nothing round it yet: the bench graded and its ramps laid.
    /// False (and nothing changed) when the file has no bench, the ground can't be changed here, or the land under the site is
    /// too rough to level (more than the file's "most" m over or under the colony).</summary>
    private bool ColonySiteGrade()
    {
        var script = ColonyBuildingsScript.Current;
        if (script.Problem.Length > 0 || script.BenchMax.X <= script.BenchMin.X || Constructs.Get(_opColony) is not { } colony || DigGround == null)
            return false;
        Vector2 lo = script.BenchMin, hi = script.BenchMax;
        _csLift = 0;
        // How the land lies under it: the most it stands over or under the bench, on a 4 m grid.
        float over = 0, under = 0;
        for (var x = lo.X; x <= hi.X; x += 4)
            for (var z = lo.Y; z <= hi.Y; z += 4)
            {
                var at = ColonySiteSpot(colony, x, z);
                var up = GravityFrames.UpAt(at);
                var d = Vector3.Dot(ConstructWorld.Ground(World.Query, at.X, at.Z) - at, up);
                over = MathF.Max(over, d);
                under = MathF.Max(under, -d);
            }
        if (over > script.BenchMost || under > script.BenchMost)
        {
            Log($"[site] the land stands {over:0.0} m over and {under:0.0} m under the colony's level: too rough to level, no site");
            return false;
        }
        // The bench's top stands just over the highest land under it (a quarter of a metre over the colony's own ground at
        // most), so every column is made ground and the whole bench looks one surface, not patches of snow and fill.
        _csLift = MathF.Min(over, 0.25f) + 0.05f;
        // Strips along x, each a level Flatten stroke as wide as a grade may be, overlapping their neighbours by a quarter.
        const float R = GroundShape.MaxRadius, Step = R * 1.5f, Run = GroundShape.MaxLength - 2f;
        var kind = GroundShape.Grade(GroundKind.Stone);
        var ops = 0;
        for (var z = lo.Y + R; z < hi.Y - R + Step; z += Step)
        {
            var zz = MathF.Min(z, hi.Y - R);
            for (var x = lo.X + R; x < hi.X - R; x += Run)
            {
                var x1 = MathF.Min(x + Run, hi.X - R);
                Dig(new DigOp(kind, ColonySiteSpot(colony, x, zz), R, To: ColonySiteSpot(colony, x1, zz)));
                ops++;
            }
        }
        // The ramps: from the bench's edge down (or up) to the land as it lies.
        foreach (var (from, to) in script.Ramps)
        {
            var a = ColonySiteSpot(colony, from.X, from.Y);
            var b = ColonySiteSpot(colony, to.X, to.Y);
            b = ConstructWorld.Ground(World.Query, b.X, b.Z);
            Dig(new DigOp(kind, a, 3f, To: b));
            ops++;
        }
        Log($"[site] the bench is graded: {hi.X - lo.X:0} by {hi.Y - lo.Y:0} m, the land was up to {over:0.0} m over and {under:0.0} m under it, {ops} ground ops");
        return true;
    }

    /// <summary>
    /// Server: one of the site's own pieces, laid in the colony's plane. Its footprint's middle goes on the row's spot, its
    /// underside "sink" m into the bench, square with the colony and turned by the row's yaw. No search and no clearance
    /// check: the site plan says where things are.
    /// </summary>
    private Construct? ColonySitePlace(Construct colony, ColonyBuildingsScript.Building b)
    {
        if (ConstructBuild.FindBlueprint(b.Blueprint) is not { } bp)
            return null;
        var spot = ColonySiteSpot(colony, b.At.X, b.At.Y, -b.Sink);
        var c = ConstructBuild.BuildOnGround(Constructs, bp, ConstructWorld.Ground(World.Query, spot.X, spot.Z), 0, out var why);
        if (c == null)
        {
            Log($"[site] the {b.Blueprint} doesn't build: {why}");
            return null;
        }
        c.Rot = Quaternion.Normalize(colony.Rot * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -b.Yaw * MathF.PI / 180f));
        if (c.Dirty)
            c.Rebuild();
        c.Pos += spot - c.ToWorld(new Vector3((c.LocalMin.X + c.LocalMax.X) * 0.5f, c.LocalMin.Y, (c.LocalMin.Z + c.LocalMax.Z) * 0.5f));
        c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
        c.PrevRot = c.Rot;
        c.NetHistReset();
        if (!c.Anchored)
            c.Frozen = true; // (a piece with no footing of its own stands as laid)
        c.Rebuild();
        return c;
    }

    /// <summary>Server, a fresh story world with its bench, after the buildings, the barracks and the Remote Works: the site's pieces.</summary>
    private void ColonySiteFurnish()
    {
        var script = ColonyBuildingsScript.Current;
        if (!_csBench || script.Problem.Length > 0 || Constructs.Get(_opColony) is not { } colony)
            return;
        var n = 0;
        foreach (var b in script.Buildings)
        {
            if (!b.Site || ColonySitePlace(colony, b) is not { } c)
                continue;
            _cbBuilt.Add((b.Blueprint, c.Id));
            n++;
        }
        Log($"[site] {n} site pieces laid round the colony");
    }

    /// <summary>
    /// Server: a building whose place the site plan fixes but another file builds (the Colony Barracks, the Remote Works):
    /// stood on its row's spot when the colony has a bench, or null (no bench, no row, or the spot is taken) and the caller
    /// looks for ground as it did before the site.
    /// </summary>
    private Construct? ColonySiteStand(string blueprint)
    {
        var script = ColonyBuildingsScript.Current;
        if (!_csBench || script.Problem.Length > 0 || Constructs.Get(_opColony) is not { } colony)
            return null;
        foreach (var b in script.Buildings)
            if (b.Elsewhere && b.Blueprint.Equals(blueprint, StringComparison.OrdinalIgnoreCase))
                return ColonyBuildingPlace(colony, b);
        return null;
    }

    /// <summary>
    /// Server, a survival world's start kit (GameSandboxSurvival.cs): a starter machine or rig stood in the site's works yard
    /// (the plan's "kit" rectangles, inside the fence), on the first spot of a 1.5 m grid where its footprint is clear of
    /// every other body's. Null with no bench or no room: the caller looks for ground round the colony as before, which
    /// with the fence up means outside it.
    /// </summary>
    private Construct? ColonySiteKit(string name)
    {
        var script = ColonyBuildingsScript.Current;
        if (!_csBench || script.Problem.Length > 0 || script.Kit.Length == 0 || Constructs.Get(_opColony) is not { } colony)
            return null;
        var first = ColonySiteSpot(colony, script.Kit[0].Min.X, script.Kit[0].Min.Y);
        var ground = ConstructWorld.Ground(World.Query, first.X, first.Z);
        var c = ConstructBuild.FindBlueprint(name) is { } bp ? ConstructBuild.BuildOnGround(Constructs, bp, ground, 0, out _)
            : ConstructParts.Find(name) is { } def ? ConstructBuild.PlaceNew(Constructs, def, ground, 0) : null;
        if (c == null)
            return null;
        if (c.Dirty)
            c.Rebuild();
        var (flo, fhi) = ColonyFootprint(c);
        var half = (fhi - flo) * 0.5f;
        var mid = (flo + fhi) * 0.5f;
        foreach (var (min, max) in script.Kit)
            for (var z = min.Y + half.Y; z <= max.Y - half.Y; z += 1.5f)
                for (var x = min.X + half.X; x <= max.X - half.X; x += 1.5f)
                {
                    var at = ColonySiteSpot(colony, x, z);
                    var move = new Vector3(at.X - mid.X, 0, at.Z - mid.Y);
                    if (!ColonyBuildingSpotClear(c, move, 1f))
                        continue;
                    var g = ConstructWorld.Ground(World.Query, at.X, at.Z);
                    c.Pos += move with { Y = g.Y - ground.Y };
                    c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
                    c.NetHistReset();
                    if (!c.Anchored)
                    {
                        c.Frozen = true; // (a rig stands as built until someone picks it up with the physics gun)
                        c.Rebuild();
                    }
                    return c;
                }
        Constructs.Remove(c);
        Log($"[site] no room in the works yard for the {name}: it stands outside the fence");
        return null;
    }

    /// <summary>Server: every body's footprint in the colony's own frame, one line each (the site plan is drawn against these).
    /// Once a new world.</summary>
    private void ColonySiteLog()
    {
        if (Constructs.Get(_opColony) is not { } colony)
            return;
        var inv = Quaternion.Conjugate(colony.Rot);
        foreach (var c in Constructs.Bodies)
        {
            var lo = new Vector3(float.MaxValue);
            var hi = new Vector3(float.MinValue);
            for (var k = 0; k < 8; k++)
            {
                var w = c.ToWorld(new Vector3((k & 1) == 0 ? c.LocalMin.X : c.LocalMax.X, (k & 4) == 0 ? c.LocalMin.Y : c.LocalMax.Y, (k & 2) == 0 ? c.LocalMin.Z : c.LocalMax.Z));
                var l = Vector3.Transform(w - colony.Pos, inv);
                lo = Vector3.Min(lo, l);
                hi = Vector3.Max(hi, l);
            }
            if (MathF.Abs(lo.X) < 150 && MathF.Abs(lo.Z) < 150)
                Log($"[site] {ColonySiteName(c)}: x {lo.X:0.00}..{hi.X:0.00}, z {lo.Z:0.00}..{hi.Z:0.00}, y {lo.Y:0.00}..{hi.Y:0.00}");
        }
    }

    private string ColonySiteName(Construct c)
    {
        foreach (var (name, body) in _cbBuilt)
            if (body == c.Id)
                return name;
        return c.Id == _opColony ? "Penal Colony" : c.Id == _opBarracks ? "Colony Barracks" : c.Id == _dpWorks ? "Remote Works" : $"construct {c.Id}";
    }
}
