using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Space sky" (phase 4, docs/space/SPACE.md "Seeing space"): what the sky of a map in a star system draws besides the
    /// sun. From the Planetoid's pole in the Dev system: every other body but the star (the moonlet, the sister planetoid
    /// Bluestone, far Redrock) where its orbit puts it and as big as it looks, farthest first, in its file colour; the
    /// moonlet goes from new to full and through the Planetoid's shadow (eclipsed only on the night side), and keeps one
    /// face to the Planetoid while Bluestone's ground turns with its 9 h day; ships are points, the nearest 16, hidden
    /// behind a body's disc, dark in a shadow; the disc overlap behind eclipses; bad colours refused; the shader's arrays
    /// match; and a sky with 20 ships allocates nothing a frame.
    /// </summary>
    private static void CheckSpaceSky()
    {
        StarSystems.Reload();
        if (Maps.Find("Planetoid") is not { } map || !StarSystems.ForMap(map.Name, out var entry, out var home))
        {
            Fail("space sky: the Planetoid map or its star system is missing");
            return;
        }
        var sys = entry.System;
        var bubble = new SpaceBubble(sys, home);
        var t0 = sys.Start;
        var cam = new Vector3(0, 2, 0);
        int moon = sys.IndexOf("Moonlet"), blue = sys.IndexOf("Bluestone"), red = sys.IndexOf("Redrock");
        if (moon < 0 || blue < 0 || red < 0)
        {
            Fail("space sky: the Dev system should have Moonlet, Bluestone and Redrock");
            return;
        }

        // ---- Disc overlap (eclipses) -----------------------------------------------------------------------------------
        var lens = SpaceSkyView.DiscCover(1, 1, 1); // two equal discs a radius apart: (2 pi / 3 - sqrt 3 / 2) / pi
        if (SpaceSkyView.DiscCover(1, 1, 2.01) != 0 || SpaceSkyView.DiscCover(1, 2, 0.5) != 1 || Math.Abs(SpaceSkyView.DiscCover(1, 0.5, 0.2) - 0.25) > 1e-12
            || Math.Abs(lens - (2 * Math.PI / 3 - Math.Sqrt(3) / 2) / Math.PI) > 1e-9)
            Fail($"space sky: disc overlap is wrong (equal discs a radius apart cover {lens:0.0000}, want 0.3910)");

        // ---- The bodies ------------------------------------------------------------------------------------------------
        var view = new SpaceSkyView();
        view.Update(bubble, t0, cam);
        Line($"space sky: from the pole at the start: {string.Join(", ", Enumerable.Range(0, view.BodyCount).Select(i => $"{sys.Bodies[view.Bodies[i].Body].Name} {view.Bodies[i].Distance / 1000:0.#} km, {2 * view.Bodies[i].AngularRadius * 180 / Math.PI:0.#####} deg, phase {view.Bodies[i].Phase:0.00}, sun {view.Bodies[i].Light:0.00}"))}");
        if (view.BodyCount != sys.Count - 2)
            Fail($"space sky: the sky should show every body but the star and the Planetoid ({view.BodyCount} of {sys.Count - 2})");
        for (var i = 0; i < view.BodyCount; i++)
        {
            ref readonly var o = ref view.Bodies[i];
            if (o.Body is 0 || o.Body == home)
                Fail($"space sky: the sky shows {sys.Bodies[o.Body].Name} as a far body");
            if (i > 0 && view.Bodies[i - 1].Distance < o.Distance)
                Fail("space sky: far bodies should be farthest first (nearer ones are drawn over them)");
            if (!bubble.Look(o.Body, t0, cam, out var dir, out var dist, out var ar) || Vector3.Distance(dir, o.Dir) > 1e-5f
                || Math.Abs(dist - o.Distance) > 1e-3 * Math.Max(1, dist * 1e-9) || Math.Abs(ar - o.AngularRadius) > 1e-12)
                Fail($"space sky: {sys.Bodies[o.Body].Name} isn't where the bubble sees it ({o.Dir} vs {dir})");
            if (sys.Bodies[o.Body].Color is { } c && c != o.Color)
                Fail($"space sky: {sys.Bodies[o.Body].Name} isn't in its file colour");
            foreach (var axis in new[] { o.ToBodyX, o.ToBodyY, o.ToBodyZ })
                if (MathF.Abs(axis.Length() - 1) > 1e-5f)
                    Fail($"space sky: {sys.Bodies[o.Body].Name}'s turn into its own frame isn't a rotation");
        }
        var redO = Find(view, red);
        var blueO = Find(view, blue);
        if (redO.AngularRadius > 1e-6 || blueO.AngularRadius is < 1e-3 or > 3e-3)
            Fail($"space sky: Redrock should be a point (angular radius {redO.AngularRadius:g3}) and Bluestone a small disc ({blueO.AngularRadius:g3} rad)");

        // ---- The moonlet over two orbits: phases, the shadow, one face to the Planetoid; Bluestone turning ---------------
        double minPhase = 1, maxPhase = 0, minLight = 1;
        var eclipsedDaySide = 0;
        Vector3 face0 = default, blue0 = default;
        double faceDrift = 0, blueTurn = 0;
        var period = sys.Bodies[moon].Orbit.Period;
        for (var s = 0.0; s <= 2 * period; s += 15)
        {
            var t = t0 + (long)(s * SpaceTime.NsPerSecond);
            view.Update(bubble, t, cam);
            var m = Find(view, moon);
            minPhase = Math.Min(minPhase, m.Phase);
            maxPhase = Math.Max(maxPhase, m.Phase);
            minLight = Math.Min(minLight, m.Light);
            // Eclipsed only behind the Planetoid (its centre between the moonlet and the star).
            var toMoon = sys.PositionOf(moon, t) - sys.PositionOf(home, t);
            var toSun = sys.PositionOf(0, t) - sys.PositionOf(home, t);
            if (m.Light < 1 && Vec3d.Dot(toMoon, toSun) > 0)
                eclipsedDaySide++;
            // The way to the Planetoid's centre, in the moonlet's own frame.
            var toHome = (bubble.GameCentre - bubble.BodyInGame(moon, t)).Normalized().ToVector3();
            var face = Vector3.Normalize(m.ToBodyX * toHome.X + m.ToBodyY * toHome.Y + m.ToBodyZ * toHome.Z);
            var b = Find(view, blue);
            var bf = Vector3.Normalize(b.ToBodyX * toHome.X + b.ToBodyY * toHome.Y + b.ToBodyZ * toHome.Z);
            if (s == 0)
                (face0, blue0) = (face, bf);
            faceDrift = Math.Max(faceDrift, Math.Acos(Math.Clamp(Vector3.Dot(face, face0), -1, 1)) * 180 / Math.PI);
            if (s <= 4.5 * 3600)
                blueTurn = Math.Max(blueTurn, Math.Acos(Math.Clamp(Vector3.Dot(bf, blue0), -1, 1)) * 180 / Math.PI);
        }
        Line($"space sky: the moonlet over two orbits ({SpaceTime.FromSeconds(period)} each): phase {minPhase:0.00} to {maxPhase:0.00}, least sunlight {minLight:0.00}, its face drifts {faceDrift:0.0} deg; a fixed way turns {blueTurn:0} deg on Bluestone in 4.5 h");
        if (minPhase > 0.05 || maxPhase < 0.95)
            Fail($"space sky: the moonlet should go from new to full each orbit (phase {minPhase:0.00} to {maxPhase:0.00})");
        if (minLight > 0 || eclipsedDaySide > 0)
            Fail($"space sky: the moonlet should go dark in the Planetoid's shadow each orbit and only there (least sunlight {minLight:0.00}, {eclipsedDaySide} eclipses on the day side)");
        // Libration lets it nod a little: about 2e (5.7 degrees) in longitude from its eccentric orbit and up to its 5 degree
        // inclination in latitude (its spin axis is the ecliptic's), so about 11 degrees at most; a body that turned would drift 180.
        if (faceDrift > 15)
            Fail($"space sky: the locked moonlet should keep one face to the Planetoid (it drifts {faceDrift:0.0} degrees)");
        if (blueTurn < 90)
            Fail($"space sky: Bluestone's ground should turn with its 9 h day (a fixed way turns only {blueTurn:0} degrees in 4.5 h)");

        // ---- Ships -----------------------------------------------------------------------------------------------------
        view.Update(bubble, t0, cam);
        var pose = bubble.PoseAt(t0);
        var homePos = sys.PositionOf(home, t0);
        var sunWay = (sys.PositionOf(0, t0) - homePos).Normalized();
        // In the Planetoid's shadow, 20 km from its centre: dark. On its day side: lit.
        var shadowed = homePos + sunWay * -20000;
        var lit = homePos + sunWay * 20000;
        if (SpaceSkyView.SunVisible(sys, t0, shadowed, -1) != 0 || SpaceSkyView.SunVisible(sys, t0, lit, -1) != 1)
            Fail("space sky: a ship behind the Planetoid should be in its shadow, one on its day side in full sun");
        // A ship straight behind the moonlet, from the camera: hidden. One beside it: drawn where it is.
        var mo = Find(view, moon);
        var behind = pose.ToEcliptic(Vec3d.From(cam) + Vec3d.From(mo.Dir) * (mo.Distance + 5000));
        var beside = pose.ToEcliptic(new Vec3d(30000, 15000, 4000));
        if (view.AddShip(1, behind) || !view.AddShip(2, beside) || view.ShipCount != 1
            || Vector3.Distance(view.Ships[0].Dir, (pose.FromEcliptic(beside) - Vec3d.From(cam)).Normalized().ToVector3()) > 1e-5f)
            Fail($"space sky: a ship behind the moonlet should be hidden and one beside it drawn where it is ({view.ShipCount} ships)");

        // Through the game's sky: 20 ships round the Planetoid, the nearest 16 drawn, nearest first, no allocation.
        var flight = new SpaceFlight(sys, 32, t0);
        for (var i = 0; i < 20; i++)
        {
            var r = 15000 + 900 * i;
            var a = i * 0.7;
            flight.Ships.AddRelative(home, new Vec3d(r * Math.Cos(a), r * Math.Sin(a), 2000), new Vec3d(-Math.Sin(a), Math.Cos(a), 0) * Math.Sqrt(sys.Bodies[home].Mu / r));
        }
        var session = new SpaceSession(bubble, entry.Hash, map.Name, flight);
        if (map.Skybox == null)
            Fail("space sky: the Planetoid map has no sky");
        else
        {
            SpaceSky.Update(map, session, t0, cam);
            var v = SpaceSky.View;
            var sorted = true;
            for (var i = 1; i < v.ShipCount; i++)
                sorted &= v.Ships[i - 1].Distance <= v.Ships[i].Distance;
            if (v.ShipCount != SpaceSkyView.MaxShips || !sorted || v.BodyCount != sys.Count - 2)
                Fail($"space sky: the sky should hold the nearest {SpaceSkyView.MaxShips} of 20 ships, nearest first ({v.ShipCount}, sorted {sorted}) and every far body ({v.BodyCount})");
            var alloc = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 600; k++)
                SpaceSky.Update(map, session, t0 + k * SpaceTime.NsPerSecond, cam);
            alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
            if (alloc != 0)
                Fail($"space sky: the sky with 20 ships allocated {alloc} B in 600 frames");
            SpaceSky.Update(map, null, default, cam);
            if (SpaceSky.View.BodyCount + SpaceSky.View.ShipCount != 0)
                Fail("space sky: the far bodies should go when the bubble does");
        }

        // ---- Colours in system files -----------------------------------------------------------------------------------
        const string star = "{ \"name\": \"S\", \"gm\": 1e20, \"radius\": 1e8 }";
        foreach (var bad in new[] { "[2, 0, 0]", "\"red\"", "[0.5, 0.5]" })
            try
            {
                StarSystemFile.Load($"{{ \"bodies\": [{star}, {{ \"name\": \"P\", \"parent\": \"S\", \"radius\": 1000, \"gravity\": 1, \"color\": {bad}, \"orbit\": {{ \"au\": 1 }} }}] }}", "col.json");
                Fail($"space sky: a body with \"color\": {bad} should be refused");
            }
            catch (StarSystemException)
            {
            }

        // ---- The shader's arrays hold what the view keeps --------------------------------------------------------------
        var fs = SkyRenderer.FragmentSource;
        if (!fs.Contains($"uBodyPos[{SpaceSkyView.MaxBodies}]") || !fs.Contains($"uBodyRot[{SpaceSkyView.MaxBodies}]") || !fs.Contains($"uShips[{SpaceSkyView.MaxShips}]"))
            Fail("space sky: the sky shader's far-body arrays don't match SpaceSkyView.MaxBodies / MaxShips");
    }

    private static SkyObject Find(SpaceSkyView view, int body)
    {
        for (var i = 0; i < view.BodyCount; i++)
            if (view.Bodies[i].Body == body)
                return view.Bodies[i];
        Fail($"space sky: body {body} isn't in the sky");
        return default;
    }
}
