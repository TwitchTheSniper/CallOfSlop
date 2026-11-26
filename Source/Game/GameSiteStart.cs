using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A start on the ground in front of a big pre-built (docs/story/STARTS.md "A site start"; an opening's <c>"site"</c>): the
/// blueprint is built whole on the flattest dry ground near the map's spawn, and everyone wakes <c>"siteGap"</c> m out past its
/// front (its blueprint's low-z end: the Zero Day works' assembly yard), looking at it. The Zero Day works start
/// (Story/Starts/ZeroDayWorks.json; Sean, 2026-11-25: "spawns you in front of an idle, full Zero Day factory, for quicker
/// testing"). With <c>"siteIdle"</c> every Line Console on it is built stopped, so nothing is ordered until someone starts one.
/// Server only; nothing new on the wire or in the save (the wake spot is the Opening section's crash site).
/// </summary>
public sealed partial class Game
{
    private const float SiteSearch = 1500f; // m round the spawn the flat ground is looked for in
    private const float SiteRing = 48f; // m between the rings of spots tried
    private const float SiteSample = 24f; // m between the ground samples under a spot
    private const float SiteLevel = 2.5f; // m: ground that varies less than this under the whole footprint is taken at once

    /// <summary>Server, a fresh world's site start: the site built near <paramref name="from"/> and the wake spot in front of it. False: it couldn't be built.</summary>
    private bool OpeningSite(OpeningScript script, Vector3 from)
    {
        if (ConstructBuild.FindBlueprint(script.Site) is not { } bp)
        {
            Log($"[opening] no blueprint named {script.Site}: no site");
            return false;
        }
        var g0 = ConstructWorld.Ground(World.Query, from);
        var c = ConstructBuild.BuildOnGround(Constructs, bp, g0, 0, out var why, beside: false); // (its bodies beside it once it stands where it stays, below)
        if (c == null)
        {
            Log($"[opening] the site {script.Site} didn't build: {why}");
            return false;
        }
        if (c.Dirty)
            c.Rebuild();
        var min = c.LocalMin;
        var max = c.LocalMax;
        var mid = new Vector3((min.X + max.X) * 0.5f, 0, (min.Z + max.Z) * 0.5f);
        var half = new Vector2(max.X - min.X, max.Z - min.Z) * 0.5f;
        var rot0 = ConstructBuild.GroundRot(g0, 0);
        var ex = Vector3.Transform(Vector3.UnitX, rot0);
        var ez = Vector3.Transform(Vector3.UnitZ, rot0);
        Constructs.Remove(c, quiet: true); // (measured: off again, so the ground samples below meet the ground and not its own floor)
        // The flattest dry spot in rings round the spawn: the first whose ground varies under SiteLevel, else the least varied.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Vector3 best = g0;
        float bestSpread = float.MaxValue, bestTop = Alt(g0);
        var tried = 0;
        for (var r = 0f; r <= SiteSearch && bestSpread >= SiteLevel; r += SiteRing)
        {
            var n = r == 0 ? 1 : 16;
            for (var k = 0; k < n && bestSpread >= SiteLevel; k++)
            {
                var a = k * MathF.Tau / n;
                var at = g0 + (ex * MathF.Cos(a) + ez * MathF.Sin(a)) * r;
                tried++;
                if (!SiteSpotClear(at, half.Length()) || SiteGround(at, ex, ez, half, bestSpread) is not { } s)
                    continue;
                (best, bestSpread, bestTop) = (at, s.Spread, s.Top);
            }
        }
        if (bestSpread == float.MaxValue)
        {
            Log($"[opening] no dry ground for the site {script.Site} within {SiteSearch:0} m of {from.X:0},{from.Z:0}: it stands at the spawn");
            bestSpread = 0;
        }
        // Built again there: upright on the ground at the spot, its footprint's middle on it, its floor on the highest ground under it.
        var gb = ConstructWorld.Ground(World.Query, best);
        Constructs.StockAll = script.SiteStocked;
        try { c = ConstructBuild.BuildOnGround(Constructs, bp, gb, 0, out why, beside: false); }
        finally { Constructs.StockAll = false; }
        if (c == null)
        {
            Log($"[opening] the site {script.Site} didn't build at {best.X:0},{best.Z:0}: {why}");
            return false;
        }
        var rot = ConstructBuild.GroundRot(gb, 0);
        var up = Vector3.Transform(Vector3.UnitY, rot);
        c.Rot = rot;
        c.Pos = gb + up * (bestTop - Alt(gb)) - Vector3.Transform(new Vector3(mid.X, min.Y - 0.02f, mid.Z), rot);
        c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
        c.NetHistReset();
        c.Rebuild();
        var beside = ConstructBuild.SpawnBeside(Constructs, c, bp); // (Zero Day Works II's gensets, each its own body on a heavy power cable)
        int stores = 0, full = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (ConstructStoreVolume.HasContents(c.Parts[i].D) && Constructs.StoreOf(c, i) is { } st)
            {
                stores++;
                if (st.Amount > 0)
                    full++;
            }
        Log($"[opening] the site {script.Site}: {full} of {stores} silo(s) and bunker(s) hold stock{(script.SiteStocked ? " (siteStocked)" : "")}");
        var stopped = script.SiteIdle ? SiteStopConsoles(c) : 0;
        // Everyone wakes past its front (the blueprint's low z), facing it.
        var front = c.Pos + Vector3.Transform(new Vector3(mid.X, 0, min.Z - script.SiteGap), rot);
        var wake = ConstructWorld.Ground(World.Query, front);
        _opMiddle = _opGate = _opWake = _opCrash = wake;
        _opWakeYaw = GravityFrames.YawOf(wake, Vector3.Transform(Vector3.UnitZ, rot));
        Constructs.Events.Add($"opening: the site ({script.Site}) as construct {c.Id}");
        Log($"[opening] the site {script.Site} is construct {c.Id}, {max.X - min.X:0} x {max.Z - min.Z:0} m, its middle {Vector3.Distance(best, g0):0} m from the spawn on ground varying {bestSpread:0.0} m "
            + $"({tried} spots tried, {sw.Elapsed.TotalMilliseconds:0} ms){(script.SiteIdle ? $", {stopped} Line Console(s) stopped" : "")}{(beside.Count > 0 ? $", {beside.Count} body(ies) beside it" : "")}; everyone wakes at {wake.X:0},{wake.Y:0},{wake.Z:0}");
        return true;
    }

    /// <summary>A ground point's height: over a planetoid's centre, else its y.</summary>
    private static float Alt(Vector3 g) => World.Map.Planetoid is { } pl ? (g - pl.Center).Length() : g.Y;

    /// <summary>
    /// The ground under a footprint of half size <paramref name="half"/> (along <paramref name="ex"/> and <paramref name="ez"/>)
    /// round <paramref name="at"/>: how much it varies and its highest point, or null when it is a sea's water or varies more
    /// than <paramref name="worst"/> (no better than a spot already found).
    /// </summary>
    private (float Spread, float Top)? SiteGround(Vector3 at, Vector3 ex, Vector3 ez, Vector2 half, float worst)
    {
        int nx = Math.Max(2, (int)MathF.Ceiling(half.X * 2 / SiteSample)), nz = Math.Max(2, (int)MathF.Ceiling(half.Y * 2 / SiteSample));
        float low = float.MaxValue, high = float.MinValue;
        var pl = World.Map.Planetoid;
        for (var i = 0; i <= nx; i++)
            for (var j = 0; j <= nz; j++)
            {
                var p = at + ex * (-half.X + 2 * half.X * i / nx) + ez * (-half.Y + 2 * half.Y * j / nz);
                var g = ConstructWorld.Ground(World.Query, p);
                var h = Alt(g);
                low = MathF.Min(low, h);
                high = MathF.Max(high, h);
                if (high - low >= worst)
                    return null;
                if (pl is { HasSeasCached: true } && (g - pl.Center) is var rel && rel.LengthSquared() > 1f)
                {
                    var d = Vector3.Normalize(rel);
                    if (pl.IsWater(d.X, d.Y, d.Z, pl.HeightAt(d.X, d.Y, d.Z)))
                        return null; // (the works on a sea's flat water)
                }
            }
        return (high - low, high);
    }

    /// <summary>Is a spot clear of every other body (the map's own constructs) by the site's reach?</summary>
    private bool SiteSpotClear(Vector3 at, float reach)
    {
        foreach (var b in Constructs.Bodies)
        {
            var near = reach + b.Radius + 2f;
            if (Vector3.DistanceSquared(b.WorldCom, at) < near * near)
                return false;
        }
        return true;
    }

    /// <summary>Every Line Console on the body stopped (its Run off): the site stands idle until someone starts one. How many.</summary>
    private static int SiteStopConsoles(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = p.D;
            if (p.Broken || !ProductionLines.IsConsole(d))
                continue;
            if (d.CfgSlot[(int)CfgKey.StartOn] is var slot and >= 0)
                ConstructConfig.Set(ref p, d, slot, 0); // (built stopped, and stays so through a save)
            p.State = (byte)(p.State & ~1);
            n++;
        }
        return n;
    }
}
