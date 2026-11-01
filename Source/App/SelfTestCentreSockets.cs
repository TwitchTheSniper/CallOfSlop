using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Centre sockets" (board item dev-centre-sockets-cs02): a structural part (frame, panel, foundation) gets a structural
    /// socket at the centre of each large face (both sides at least <see cref="ConstructParts.CentreFaceMin"/>) unless one
    /// faces out of it within <see cref="ConstructParts.CentreNear"/>, appended after the file's own sockets so every index in
    /// old links, blueprints and saves is unchanged; "centreSockets": false opts out. Checked over every part file, then a
    /// frame block mounted at a deck plate's centre through the held-part path.
    /// </summary>
    private static void CheckCentreSockets()
    {
        var gained = 0;
        var added = 0;
        var folder = ConstructParts.Folder;
        foreach (var d in ConstructParts.All.Where(x => !x.Generated))
        {
            var path = Directory.GetFiles(folder, d.File, SearchOption.AllDirectories).FirstOrDefault();
            if (path == null)
                continue;
            var text = File.ReadAllText(path);
            ConstructPartDef plain;
            try { plain = ConstructParts.Parse(text, d.File); }
            catch (Exception ex) { Fail($"centre sockets: {d.Name} won't parse: {ex.Message}"); continue; }
            if (plain.Sockets.Length > d.Sockets.Length)
            {
                Fail($"centre sockets: {d.Name} lost sockets");
                continue;
            }
            for (var i = 0; i < plain.Sockets.Length; i++)
                if (plain.Sockets[i] != d.Sockets[i])
                {
                    Fail($"centre sockets: {d.Name}: socket {i} ({plain.Sockets[i].Name}) moved or changed: old blueprints and saves would link to the wrong socket");
                    break;
                }
            var extra = d.Sockets.Skip(plain.Sockets.Length).ToList();
            var structural = d.Role is PartRole.Frame or PartRole.Panel or PartRole.Foundation;
            if (extra.Count > 0 && !structural)
                Fail($"centre sockets: {d.Name} ({d.Role}) isn't structural but gained {extra.Count}");
            if (extra.Any(s => s.Kind != SocketKind.Struct || !s.Name.StartsWith("centre ")))
                Fail($"centre sockets: {d.Name}: an added socket isn't a structural \"centre\" one");
            // Every large face has a socket near its centre; an added one has none of the file's own near it.
            var lattice = d.Folder.StartsWith("Structure") || d.Folder.StartsWith("Building") || d.Stage || d.DamagedName.Length > 0 || d.CentreOptOut;
            if (extra.Count > 0 && d.CentreOptOut)
                Fail($"centre sockets: {d.Name} opted out but gained sockets");
            if (structural && !lattice)
                for (byte dir = 0; dir < 6; dir++)
                {
                    var axis = dir >> 1;
                    var a1 = (axis + 1) % 3;
                    var a2 = (axis + 2) % 3;
                    if (d.BoundsMax[a1] - d.BoundsMin[a1] < ConstructParts.CentreFaceMin || d.BoundsMax[a2] - d.BoundsMin[a2] < ConstructParts.CentreFaceMin)
                        continue;
                    var c = (d.BoundsMin + d.BoundsMax) / 2;
                    c[axis] = (dir & 1) == 0 ? d.BoundsMax[axis] : d.BoundsMin[axis];
                    var any = d.Sockets.Any(s => s.Dir == dir && Vector3.Distance(s.At, c) < ConstructParts.CentreNear);
                    var own = plain.Sockets.Any(s => s.Dir == dir && Vector3.Distance(s.At, c) < ConstructParts.CentreNear);
                    var solid = false;
                    for (var b = 0; b < d.SolidBoxes && !solid; b++)
                    {
                        var bx = d.Boxes[b];
                        solid = MathF.Abs(bx.Center[axis] + ((dir & 1) == 0 ? 1 : -1) * bx.Half[axis] - c[axis]) < 0.005f
                            && MathF.Abs(c[a1] - bx.Center[a1]) <= bx.Half[a1] + 0.005f && MathF.Abs(c[a2] - bx.Center[a2]) <= bx.Half[a2] + 0.005f;
                    }
                    if (solid && !any)
                        Fail($"centre sockets: {d.Name} has a large {ConstructParts.DirName(dir)} face with no socket at its centre");
                    if (own && extra.Any(s => s.Dir == dir))
                        Fail($"centre sockets: {d.Name} got a centre socket on {ConstructParts.DirName(dir)} though the file has one there");
                }
            if (extra.Count > 0)
            {
                gained++;
                added += extra.Count;
            }
        }
        Line($"centre sockets: {gained} parts gained {added} sockets: " + string.Join(", ", ConstructParts.All.Where(x => x.Sockets.Any(s => s.Name.StartsWith("centre "))).Select(x => x.Name + " " + x.Sockets.Count(s => s.Name.StartsWith("centre ")))));
        if (gained == 0)
            Fail("centre sockets: no part gained a centre socket");

        var deck = ConstructParts.Need("Deck Plate");
        if (!deck.Sockets.Any(s => s.Name == "centre +y" && s.Kind == SocketKind.Struct && Vector3.Distance(s.At, new Vector3(0, 0.05f, 0)) < 0.01f))
            Fail("centre sockets: the Deck Plate has no centre socket on its top");
        if (deck.Sockets.Any(s => s.Name == "centre -y"))
            Fail("centre sockets: the Deck Plate got a second socket under it, where its own is at the centre");
        if (deck.Sockets.Any(s => s.Name is "centre +x" or "centre -x" or "centre +z" or "centre -z"))
            Fail("centre sockets: a Deck Plate's thin edges got centre sockets");

        // A file's own centre socket stops one being added; the opt-out adds none.
        var src = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(folder, deck.File, SearchOption.AllDirectories)[0]))!.AsObject();
        ConstructPartDef Load(JsonObject o)
        {
            var p = ConstructParts.Parse(o.ToJsonString(), "DeckPlate.json");
            p.Folder = deck.Folder;
            p.Sockets = ConstructParts.WithCentreSockets(p);
            return p;
        }
        var own0 = Load(src).Sockets.Length;
        var list = src["sockets"]!.AsArray();
        list.Add(JsonNode.Parse("{\"name\":\"mine\",\"at\":[0.04,0.05,0],\"dir\":\"+y\"}"));
        var withOwn = Load(src);
        if (withOwn.Sockets.Any(s => s.Name == "centre +y"))
            Fail("centre sockets: a socket 4 cm from the centre didn't stop the automatic one");
        if (withOwn.Sockets.Length != own0)
            Fail($"centre sockets: own centre socket should replace the automatic one ({withOwn.Sockets.Length} vs {own0})");
        src["centreSockets"] = false;
        if (Load(src).Sockets.Any(s => s.Name.StartsWith("centre ")))
            Fail("centre sockets: \"centreSockets\": false still added sockets");

        CsHeldPath(deck);
    }

    private static void CsHeldPath(ConstructPartDef deck)
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail("centre sockets: the Construct Yard map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me || OpenSpot(g.Constructs) is not { } spot)
        {
            Fail("centre sockets: no live player or open ground on the yard");
            return;
        }
        var w = g.Constructs;
        var block = ConstructParts.Need("Frame Block");
        me.Teleport(spot + new Vector3(0, 0.05f, 2.5f));
        me.Yaw = 0;
        Run(g, ref now, 0.3);
        g.SandboxSend(CActKind.Tool, part: (byte)deck.Tool);
        PlaceHold(g, me.Id, deck);
        var n0 = w.Bodies.Count;
        g.SandboxSend(CActKind.PlaceNew, at: spot, yaw: 0, normal: Vector3.UnitY, variant: 0, flags: ConstructPlace.FlagGrid);
        Run(g, ref now, deck.AttachSeconds + 0.4);
        if (w.Bodies.Count <= n0)
        {
            Fail($"centre sockets: the deck plate wasn't set down (\"{g.SandboxNotice}\")");
            return;
        }
        var c = w.Bodies[^1];
        var si = Array.FindIndex(c.DefOf(0).Sockets, s => s.Name == "centre +y");
        c.SocketAt(0, si, out var sat, out _);
        c.SegPose(c.Parts[0].Seg, out var q, out var t);
        var target = c.ToWorld(t + Vector3.Transform(sat, q));
        PlaceHold(g, me.Id, block);
        Run(g, ref now, 0.2);
        StandBy(me, c.WorldCom, 3.0f);
        Run(g, ref now, 0.3);
        var eye = me.RenderEye;
        var dir = Vector3.Normalize(target - eye);
        var vp = Matrix4x4.CreateLookAt(eye, eye + dir, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 0.1f, 100f);
        var look = Program.SandboxHeldFrame(g, eye, dir, new Vector2(1920, 1080), vp, false, now, out var sel, out var valid);
        if (sel < 0 || !valid)
        {
            Fail($"centre sockets: crosshair on the deck plate's centre shows no ghost (selected {sel}, valid {valid}, HUD \"{look}\")");
            return;
        }
        var before = c.PartCount;
        Program.SandboxHeldFrame(g, eye, dir, new Vector2(1920, 1080), vp, true, now + 0.02, out _, out _);
        Run(g, ref now, block.AttachSeconds + 0.4);
        if (c.PartCount != before + 1)
        {
            Fail($"centre sockets: the click at the deck plate's centre attached nothing (\"{g.SandboxNotice}\")");
            return;
        }
        var linked = false;
        for (var k = 0; k < c.LinkCount; k++)
            if (c.Links[k].A == 0 && c.Links[k].SA == si || c.Links[k].B == 0 && c.Links[k].SB == si)
                linked = true;
        if (!linked)
            Fail("centre sockets: the new block isn't linked to the centre socket");
    }
}
