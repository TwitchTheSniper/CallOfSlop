using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Construct glass" (board dev-sandbox-glass-transparent-gt01; docs/sandbox/CONSTRUCTS.md "Glass"): every glass part
    /// has a see-through pane box; the opaque pass leaves panes out and the glass pass draws them blended without depth
    /// writes, farthest first, with their edge strips (headless, through the draw recorder); the inventory tile and the part
    /// in your hands draw the pane blended after the rest; a ghost's pane is fainter; bad "glass" values are refused; the
    /// glass pass allocates nothing; the shatter event round-trips the wire; and in a real Sandbox game a shot through a
    /// standing pane hits the player behind it and damages the pane (until it shatters into shards, not a falling wreck),
    /// where a steel plate in the same place stops the round.
    /// </summary>
    private static void CheckConstructGlass()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"construct glass: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        GlassParts();
        GlassDrawing();
        GlassInGame(yard);
    }

    /// <summary>A free construct of one part stood up on edge (its +Z turned to straight down), pinned, at <paramref name="at"/>.</summary>
    private static Construct? StandUp(ConstructWorld w, string part, Vector3 at, float yaw = 0)
    {
        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        var c = ConstructBuild.PlaceNew(w, ConstructParts.Need(part), new FreePlacement { Pos = at, Rot = Quaternion.Normalize(rot), Anchor = true });
        if (c == null)
            Fail($"construct glass: a {part} couldn't be placed");
        return c;
    }

    private static void GlassParts()
    {
        var glassParts = ConstructParts.All.Where(d => d.Material == "glass").ToList();
        var without = glassParts.Where(d => !d.HasGlass).Select(d => d.Name).ToList();
        var pane = ConstructParts.Need("Glass Pane 1 m");
        var paneBoxes = pane.Boxes.Count(b => b.IsGlass);
        var solidPane = pane.Boxes.AsSpan(0, pane.SolidBoxes).ToArray().Any(b => b.IsGlass);
        var notGlass = ConstructParts.All.Count(d => d.HasGlass && d.Material != "glass");
        // Bad values are refused with a reason.
        string? Refused(string boxJson)
        {
            try
            {
                ConstructParts.Parse($$"""{"format": "{{ConstructParts.Format}}", "name": "Test Glass", "role": "panel", "tool": "wrench", "mass": 1, "health": 5, "material": "glass", "boxes": [{"size": [0.5, 0.03, 0.5]}, {{boxJson}}], "sockets": [{"name": "back", "at": [0, -0.015, 0], "dir": "-y"}]}""", "test.json");
                return null;
            }
            catch (ConstructPartException e)
            {
                return e.Message;
            }
        }
        var tooMuch = Refused("""{"size": [0.4, 0.01, 0.4], "glass": 2}""");
        var hiddenGlass = Refused("""{"size": [0.4, 0.01, 0.4], "glass": true, "hidden": true}""");
        var fine = Refused("""{"size": [0.4, 0.01, 0.4], "glass": 0.5}""");
        Line($"glass parts: {glassParts.Count} parts of glass ({string.Join(", ", glassParts.Select(d => d.Name))}), {without.Count} without a pane box; the 1 m pane: {paneBoxes} glass box (solid {solidPane}, drawn {pane.Boxes.First(b => b.IsGlass).Glass:0.00}); \"glass\": 2 refused: \"{tooMuch}\"; hidden glass refused: \"{hiddenGlass}\"; 0.5 {(fine == null ? "loads" : $"refused: {fine}")}");
        if (glassParts.Count < 4 || without.Count > 0)
            Fail($"construct glass: every glass part needs a see-through pane box (\"glass\": true); without: {string.Join(", ", without)}");
        if (paneBoxes != 1 || !solidPane)
            Fail("construct glass: the 1 m pane should have one solid glass box (it collides)");
        if (tooMuch == null || hiddenGlass == null || fine != null)
            Fail("construct glass: the \"glass\" key's checks are wrong (2 and hidden must be refused, 0.5 must load)");
        if (notGlass > 0)
            Line($"glass parts: {notGlass} parts of other materials have glass panes (windows in steel)");
    }

    private static void GlassDrawing()
    {
        var w = new ConstructWorld();
        var ground = ConstructWorld.Ground(World.Query, 60, 60);
        var near = StandUp(w, "Glass Pane 1 m", ground + new Vector3(0, 1.9f, 0));
        var far = StandUp(w, "Glass Pane 1 m", ground + new Vector3(0, 1.9f, -3f));
        var steel = StandUp(w, "Plate Steel 1 m", ground + new Vector3(0, 1.9f, -5f));
        if (near == null || far == null || steel == null)
            return;
        var pane = ConstructParts.Need("Glass Pane 1 m");
        var paneColour = pane.Boxes.First(b => b.IsGlass).Color;
        var eye = ConstructRender.PartCentre(near, 0) + new Vector3(0.2f, 0.1f, 4f);
        var rec = new DrawRecorder();
        var g = new Renderer { Recorder = rec };
        ConstructRender.ClearAim();
        ConstructRender.Draw(g, w, 1f, false);
        var opaque = rec.Count;
        var opaqueGlass = 0;
        var opaqueBlended = 0;
        for (var i = 0; i < rec.Count; i++)
        {
            if ((rec.Items[i].Color - paneColour).Length() < 1e-3f)
                opaqueGlass++;
            if (rec.Items[i].Blend || rec.Items[i].Alpha < 1f)
                opaqueBlended++;
        }
        var queued = ConstructRender.GlassCount;
        rec.Reset();
        ConstructRender.DrawGlass(g, eye);
        var glassDraws = rec.Count;
        var panes = 0;
        var allBlended = true;
        var firstPaneZ = float.NaN;
        var maxAlpha = 0f;
        for (var i = 0; i < rec.Count; i++)
        {
            ref var it = ref rec.Items[i];
            allBlended &= it.Blend && !it.DepthWrite && it.Alpha < 1f && it.Mode == 0;
            maxAlpha = MathF.Max(maxAlpha, it.Alpha);
            if (IsPaneDraw(it.M))
            {
                panes++; // (a pane: two sides about 0.94 m; its edge strips are long one way only)
                if (float.IsNaN(firstPaneZ))
                    firstPaneZ = it.M.M43;
            }
        }
        var farFirst = MathF.Abs(firstPaneZ - ConstructRender.PartCentre(far, 0).Z) < 0.3f;
        Line($"glass drawing: the opaque pass drew {opaque} boxes ({opaqueGlass} of the pane's colour, {opaqueBlended} blended) and kept {queued} panes; the glass pass drew {glassDraws} ({panes} panes and their edge strips), every one blended without depth writes, lit: {allBlended} (alpha up to {maxAlpha:0.00}), the farther pane first: {farFirst}");
        if (opaqueGlass != 0 || opaqueBlended != 0 || queued != 2)
            Fail("construct glass: the opaque pass drew a glass pane (or didn't keep both for the glass pass)");
        if (!allBlended || panes != 2 || glassDraws != ConstructRender.LastGlass || glassDraws < 6)
            Fail("construct glass: the glass pass didn't draw both panes blended, without depth writes, with their edges");
        if (!farFirst)
            Fail("construct glass: the glass pass should draw the farther pane first (back to front)");

        // At a grazing angle a pane reflects more (drawn more opaque) than face on.
        rec.Reset();
        ConstructRender.Draw(g, w, 1f, false);
        rec.Reset();
        var centre = ConstructRender.PartCentre(near, 0);
        ConstructRender.DrawGlass(g, centre + new Vector3(0, 0, 3f));
        var faceOn = PaneAlpha(rec);
        rec.Reset();
        ConstructRender.DrawGlass(g, centre + new Vector3(3f, 0, 0.15f));
        var grazing = PaneAlpha(rec);
        Line($"glass sheen: a pane face on is drawn {faceOn:0.00} opaque, at a grazing angle {grazing:0.00}");
        if (!(grazing > faceOn + 0.05f) || faceOn > 0.5f)
            Fail("construct glass: a pane should be faint face on and reflect more at a grazing angle");

        // The glass pass allocates nothing.
        rec.Keep = false;
        ConstructRender.Draw(g, w, 1f, false);
        ConstructRender.DrawGlass(g, eye);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 50; k++)
        {
            ConstructRender.Draw(g, w, 1f, false);
            ConstructRender.DrawGlass(g, eye);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        rec.Keep = true;
        Line($"glass drawing: 50 frames of the constructs and their glass allocated {bytes} bytes");
        if (bytes > 0)
            Fail($"construct glass: drawing glass allocated {bytes} bytes in 50 frames (want 0)");

        // The inventory's tile: the pane is a glass box, drawn blended after the frame.
        var tile = new List<TileBox>();
        SandboxTiles.CollectBoxes(pane.Id, tile);
        var tileGlass = tile.Count(b => b.Glass > 0);
        rec.Reset();
        SandboxTiles.DrawTile(g, tile);
        var tileLast = rec.Count > 0 ? rec.Items[rec.Count - 1] : default;
        var tileOk = tileGlass == 1 && tileLast.Blend && !tileLast.DepthWrite && tileLast.Alpha < 1f && !rec.Blend && rec.DepthWrite;
        // The part in your hands: the same (and the state put back for the rest of the view model).
        rec.Reset();
        var o = new ToolGfxOut(g);
        ToolRender.Emit(ref o, new ToolFrame { Held = pane, HeldPose = Matrix4x4.Identity });
        var heldLast = rec.Count > 0 ? rec.Items[rec.Count - 1] : default;
        var heldOpaque = 0;
        for (var i = 0; i < rec.Count - 1; i++)
            heldOpaque += rec.Items[i].Alpha >= 1f && !rec.Items[i].Blend ? 1 : 0;
        var heldOk = heldLast.Blend && !heldLast.DepthWrite && heldLast.Alpha < 1f && heldOpaque == rec.Count - 1 && !rec.Blend && rec.DepthWrite;
        var ghost = ConstructRender.GhostGlass(pane.Boxes.First(b => b.IsGlass));
        Line($"glass previews: the inventory tile has {tileGlass} glass box, drawn last blended ({tileOk}); the pane in your hands is drawn last blended over its {heldOpaque} frame boxes ({heldOk}); a ghost's pane at {ghost:0.00} of the ghost's opacity");
        if (!tileOk || !heldOk || ghost >= 1f)
            Fail("construct glass: the inventory tile, the held part or the ghost doesn't show the pane see-through");

        // The shatter event on the wire.
        w.FireCount = 0;
        w.Fires[w.FireCount++] = new CFireEvent { Kind = CFireKind.Shatter, At = new Vector3(1.5f, 2.25f, -3f), Half = new Vector3(0.47f, 0.47f, 0.015f) };
        var writer = new PacketWriter(1024);
        ConstructNet.WriteFires(writer, w);
        var r = new PacketReader(writer.Span[1..]);
        var got = new CFireEvent[4];
        var n = ConstructNet.ReadFires(ref r, got);
        var wireOk = n == 1 && got[0].Kind == CFireKind.Shatter && Vector3.Distance(got[0].At, w.Fires[0].At) < 1e-3f && Vector3.Distance(got[0].Half, w.Fires[0].Half) < 0.006f;
        w.FireCount = 0;
        Line($"glass on the wire: a shatter event reads back {(n == 1 ? $"{got[0].Kind} at {got[0].At}, half {got[0].Half}" : $"{n} events")} ({wireOk})");
        if (!wireOk)
            Fail("construct glass: the shatter event didn't round-trip Msg.CFire");
    }

    /// <summary>The largest alpha among a recording's panes (draws over half a metre both ways).</summary>
    private static float PaneAlpha(DrawRecorder rec)
    {
        var a = 0f;
        for (var i = 0; i < rec.Count; i++)
            if (IsPaneDraw(rec.Items[i].M))
                a = MathF.Max(a, rec.Items[i].Alpha);
        return a;
    }

    /// <summary>A draw over half a metre two ways (a 1 m pane, not one of its edge strips).</summary>
    private static bool IsPaneDraw(in Matrix4x4 m)
    {
        var x = new Vector3(m.M11, m.M12, m.M13).Length();
        var y = new Vector3(m.M21, m.M22, m.M23).Length();
        var z = new Vector3(m.M31, m.M32, m.M33).Length();
        return (x > 0.5f ? 1 : 0) + (y > 0.5f ? 1 : 0) + (z > 0.5f ? 1 : 0) >= 2;
    }

    private static void GlassInGame(Map yard)
    {
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("construct glass: no local player");
            return;
        }
        var victim = g.SelfTestAddHuman();
        g.SelfTestRespawn(victim, now);
        // An open spot: well away from the yard's constructs.
        var spot = Vector3.Zero;
        var bestRoom = -1f;
        for (var x = -24; x <= 24; x += 6)
            for (var z = -24; z <= 24; z += 6)
            {
                var room = float.MaxValue;
                foreach (var b in g.Constructs.Bodies)
                    room = MathF.Min(room, Vector2.Distance(new Vector2(x, z), new Vector2(b.WorldCom.X, b.WorldCom.Z)) - b.Radius);
                var at = ConstructWorld.Ground(World.Query, x, z);
                // (the shot's line, shooter to victim, clear of the map, and the floor level along it)
                var from = at + new Vector3(0.4f, 1.6f, 3.2f);
                var to = at + new Vector3(0.4f, 1.1f, -2.2f);
                var clear = World.Query.Raycast(from, Vector3.Normalize(to - from), Vector3.Distance(from, to), QueryFilter.Shots) >= Vector3.Distance(from, to) - 0.01f;
                var level = MathF.Abs(ConstructWorld.Ground(World.Query, x, z + 3f).Y - at.Y) < 0.05f && MathF.Abs(ConstructWorld.Ground(World.Query, x, z - 2f).Y - at.Y) < 0.05f;
                if (room > bestRoom && clear && level && MathF.Abs(at.Y - me.Position.Y) < 4f)
                {
                    bestRoom = room;
                    spot = at;
                }
            }
        Line($"glass in a game: the open spot at {spot} ({bestRoom:0.0} m from the nearest construct)");
        if (bestRoom < 0)
        {
            Fail("construct glass: no open spot on the yard to shoot through glass");
            return;
        }
        (int Hurt, float PaneHealth, bool Gone, int Shards) Shoot(string part, int shots)
        {
            if (StandUp(g.Constructs, part, spot + new Vector3(0, 1.9f, 0)) is not { } wall)
                return (-1, -1, false, 0);
            var centre = ConstructRender.PartCentre(wall, 0);
            var id = wall.Id;
            var health0 = wall.Parts[0].Health;
            var shards0 = g.ConstructShattersSeen;
            var bodies0 = g.Constructs.Bodies.Count;
            var hurt = 0;
            for (var k = 0; k < shots; k++)
            {
                g.SelfTestPlace(me, new Vector3(centre.X, spot.Y, centre.Z + 3f), 0);
                g.SelfTestPlace(victim, new Vector3(centre.X, spot.Y, centre.Z - 2f), MathF.PI);
                victim.ImmuneUntil = 0;
                var hp = victim.Health;
                var aim = centre with { Y = MathF.Min(centre.Y, victim.Chest.Y + 0.15f) } - new Vector3(0, 0, 2f);
                g.SelfTestShoot(me, Vector3.Normalize(aim - me.Eye), now + k * 0.5);
                Run(g, ref now, World.TickDt * 2);
                hurt += Math.Max(0, hp - victim.Health);
            }
            var left = g.Constructs.Get(id) is { } c && c.PartCount > 0 ? c.Parts[0].Health : 0f;
            var gone = g.Constructs.Get(id) == null;
            var shards = g.ConstructShattersSeen - shards0;
            var wrecks = g.Constructs.Bodies.Count - (bodies0 - (gone ? 1 : 0));
            if (g.Constructs.Get(id) is { } still)
                g.Constructs.Remove(still);
            Line($"glass in a game: {shots} shot(s) through a standing {part}: the player behind it lost {hurt} health; the part went from {health0:0} to {left:0} health{(gone ? $", shattered ({shards} shard bursts, {wrecks} wreck pieces)" : "")}");
            if (gone && wrecks != 0)
                Fail($"construct glass: a shattered {part} left {wrecks} wreck pieces (it should burst into shards)");
            return (hurt, left / MathF.Max(1, health0), gone, shards);
        }
        var first = Shoot("Glass Pane 1 m", 1);
        if (first.Hurt <= 0 || first.PaneHealth >= 1f)
            Fail("construct glass: a shot through a glass pane should hit the player behind it and damage the pane");
        var many = Shoot("Glass Pane 1 m", 8);
        if (!many.Gone || many.Shards < 1)
            Fail("construct glass: a pane shot again and again should shatter (a shard burst on the host)");
        var plate = Shoot("Plate Steel 1 m", 1);
        if (plate.Hurt != 0)
            Fail("construct glass: a steel plate in the same place should stop the round (the player behind it was hurt)");
    }
}
