using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Reads building files (call-of-slop-building/1) and furnishing rules (call-of-slop-furnishing/1). Unknown keys, numbers
/// out of range and broken references are errors naming the file and the place, so typos never fail silently.
/// </summary>
public static class BuildingFile
{
    public const string Format = "call-of-slop-building/1";
    public const string FurnishFormat = "call-of-slop-furnishing/1";

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // ---- Buildings ---------------------------------------------------------------------------------------------

    /// <summary>Parses one building.json. <paramref name="source"/> names it in errors.</summary>
    public static BuildingDef Load(string json, string source, string folder = "")
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, Options);
        }
        catch (JsonException ex)
        {
            throw new BuildingFileException($"{source}: not valid JSON ({ex.Message})");
        }
        using (doc)
        {
            var r = doc.RootElement;
            var w = source;
            Keys(r, w, "format", "name", "theme", "category", "description", "author", "seed", "wallThickness", "storeyHeight", "doorHeight",
                "exterior", "interior", "floor", "ceiling", "trim", "door", "palettes", "floors", "floorCount", "stairs", "roof", "extras", "anchors");
            if (Get(r, "format") is { } f && Text(f, w) != Format)
                throw new BuildingFileException($"{w}: \"format\" is \"{Format}\"");
            var d = new BuildingDef { Name = Ascii(Need(r, "name", w), $"{w}: name", 40), Folder = folder, SourceFile = source };
            w = $"{source} ({d.Name})";
            if (Get(r, "theme") is { } th) d.Theme = Ascii(th, $"{w}: theme", 200);
            if (Get(r, "category") is { } cat)
            {
                d.Category = Text(cat, w).ToLowerInvariant();
                if (d.Category is not ("residential" or "commercial" or "industrial" or "special"))
                    throw new BuildingFileException($"{w}: \"category\" is residential, commercial, industrial or special");
            }
            if (Get(r, "description") is { } de) d.Description = Ascii(de, $"{w}: description", 300);
            if (Get(r, "author") is { } au) d.Author = Ascii(au, $"{w}: author", 60);
            if (Get(r, "seed") is { } sd) d.Seed = Whole(sd, $"{w}: seed", 0, int.MaxValue);
            if (Get(r, "wallThickness") is { } wt) d.WallThickness = Range(Num(wt, w), 0.1f, 0.6f, $"{w}: wallThickness");
            if (Get(r, "storeyHeight") is { } sh) d.StoreyHeight = Range(Num(sh, w), 2.4f, 8f, $"{w}: storeyHeight");
            if (Get(r, "doorHeight") is { } dh) d.DoorHeight = Range(Num(dh, w), 1.9f, 4f, $"{w}: doorHeight");

            // Palettes first: colours elsewhere may name their keys ("$wall").
            if (Get(r, "palettes") is { } pals)
            {
                if (pals.ValueKind != JsonValueKind.Object)
                    throw new BuildingFileException($"{w}: \"palettes\" is an object of palettes: {{ \"brick\": {{ \"wall\": \"#9c4a3a\", ... }} }}");
                foreach (var p in pals.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object)
                        throw new BuildingFileException($"{w}: palette \"{p.Name}\" is an object of colours");
                    var colors = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in p.Value.EnumerateObject())
                        colors[c.Name] = LiteralColor(c.Value, $"{w}: palette \"{p.Name}\", \"{c.Name}\"");
                    d.Palettes.Add((p.Name, colors));
                }
                var keys = d.Palettes.SelectMany(p => p.Colors.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var p in d.Palettes)
                    foreach (var k in keys)
                        if (!p.Colors.ContainsKey(k))
                            throw new BuildingFileException($"{w}: palette \"{p.Name}\" has no \"{k}\" (every palette needs the same keys: {string.Join(", ", keys)})");
            }
            var ctx = new Ctx(d, w);
            if (Get(r, "exterior") is { } ex) d.ExteriorWall = ctx.Surface(ex, "exterior", d.ExteriorWall);
            if (Get(r, "interior") is { } inn) d.InteriorWall = ctx.Surface(inn, "interior", d.InteriorWall);
            if (Get(r, "floor") is { } fl) d.FloorSurface = ctx.Surface(fl, "floor", d.FloorSurface);
            if (Get(r, "ceiling") is { } ce) d.CeilingSurface = ctx.Surface(ce, "ceiling", d.CeilingSurface);
            if (Get(r, "trim") is { } tr) d.TrimSurface = ctx.Surface(tr, "trim", d.TrimSurface);
            if (Get(r, "door") is { } dc) d.DoorColor = ctx.Color(dc, $"{w}: door");

            if (Need(r, "floors", w) is not { ValueKind: JsonValueKind.Array } floors || floors.GetArrayLength() == 0)
                throw new BuildingFileException($"{w}: \"floors\" is a list of at least one floor");
            var fi = 0;
            foreach (var fe in floors.EnumerateArray())
                d.Floors.Add(ReadFloor(fe, ctx, $"{w}: floor {fi++}"));
            if (d.Floors.Count(x => x.Repeat) > 1)
                throw new BuildingFileException($"{w}: only one floor can be \"repeat\"");
            if (d.Floors.Count > 12)
                throw new BuildingFileException($"{w}: at most 12 floors");
            d.DefaultFloors = d.Floors.Count;
            if (Get(r, "floorCount") is { } fc)
            {
                d.DefaultFloors = Whole(fc, $"{w}: floorCount", d.MinFloors, d.MaxFloors);
                if (d.DefaultFloors != d.Floors.Count && !d.Floors.Any(x => x.Repeat))
                    throw new BuildingFileException($"{w}: \"floorCount\" differs from the floors listed, but no floor is \"repeat\"");
            }

            if (Get(r, "stairs") is { } st)
            {
                var si = 0;
                foreach (var se in List(st, $"{w}: stairs"))
                    d.Stairs.Add(ReadStair(se, ctx, $"{w}: stairs {si++}", d.Floors.Count));
            }
            if (Get(r, "roof") is { } ro) d.Roof = ReadRoof(ro, ctx, $"{w}: roof");
            if (Get(r, "extras") is { } exs)
            {
                var ei = 0;
                foreach (var ee in List(exs, $"{w}: extras"))
                    d.Extras.Add(ReadExtra(ee, ctx, $"{w}: extras {ei++}", d.Floors.Count));
            }
            if (Get(r, "anchors") is { } ans)
            {
                var ai = 0;
                foreach (var ae in List(ans, $"{w}: anchors"))
                {
                    var aw = $"{w}: anchors {ai++}";
                    Keys(ae, aw, "type", "name", "floor", "at");
                    var type = Text(Need(ae, "type", aw), aw).ToLowerInvariant();
                    if (type is not ("spawn" or "hill" or "flag" or "point"))
                        throw new BuildingFileException($"{aw}: \"type\" is spawn, hill, flag or point");
                    var at = Nums(Need(ae, "at", aw), 2, aw);
                    d.Anchors.Add(new BAnchor
                    {
                        Type = type, X = at[0], Z = at[1],
                        Name = Get(ae, "name") is { } an ? Ascii(an, aw, 40) : "",
                        Floor = Get(ae, "floor") is { } af ? Whole(af, aw, 0, d.Floors.Count - 1) : 0,
                    });
                }
            }
            return d;
        }
    }

    /// <summary>Colour and palette context for one file.</summary>
    private sealed class Ctx(BuildingDef def, string where)
    {
        public readonly string Where = where;

        public ColorRef Color(JsonElement e, string w)
        {
            if (e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 1 } s && s[0] == '$')
            {
                var key = s[1..];
                if (def.Palettes.Count == 0)
                    throw new BuildingFileException($"{w}: \"{s}\" names a palette colour, but the building has no \"palettes\"");
                if (!def.Palettes[0].Colors.TryGetValue(key, out var first))
                    throw new BuildingFileException($"{w}: no palette has \"{key}\" (keys: {string.Join(", ", def.Palettes[0].Colors.Keys)})");
                return new ColorRef(first, key);
            }
            return new ColorRef(LiteralColor(e, w));
        }

        /// <summary>{ "color": ..., "material": ... }, or just a colour.</summary>
        public BSurface Surface(JsonElement e, string name, BSurface? fallback)
        {
            var w = $"{Where}: {name}";
            if (e.ValueKind == JsonValueKind.Object)
            {
                Keys(e, w, "color", "material");
                var c = Get(e, "color") is { } ce ? Color(ce, w) : fallback?.Color ?? new ColorRef(new Vector3(0.6f));
                var layer = Get(e, "material") is { } me ? MaterialLayer(me, w) : fallback?.Layer ?? -1;
                return new BSurface(c, layer);
            }
            return new BSurface(Color(e, w), fallback?.Layer ?? -1);
        }
    }

    /// <summary>"brick", "concrete", "wood", "metal", "tiles", "dirt", "grid" or "none" (plain colour).</summary>
    private static int MaterialLayer(JsonElement e, string w) => Text(e, w).ToLowerInvariant() switch
    {
        "none" or "plain" or "plaster" or "paint" => -1,
        "concrete" => (int)Material.Concrete,
        "brick" => (int)Material.Brick,
        "wood" => (int)Material.Wood,
        "metal" => (int)Material.Metal,
        "tiles" => (int)Material.Tiles,
        "dirt" => (int)Material.Dirt,
        "grid" => (int)Material.Grid,
        var m => throw new BuildingFileException($"{w}: material \"{m}\" is brick, concrete, wood, metal, tiles, dirt, grid or none"),
    };

    private static BFloor ReadFloor(JsonElement e, Ctx ctx, string w)
    {
        Keys(e, w, "height", "rooms", "openings", "autoWindows", "repeat");
        var f = new BFloor { Height = Get(e, "height") is { } h ? Range(Num(h, w), 2.4f, 10f, $"{w}: height") : 0f };
        if (Get(e, "repeat") is { } rp) f.Repeat = Bool(rp, w);
        if (Get(e, "autoWindows") is { } aw) f.AutoWindows = ReadAutoWindows(aw, $"{w}: autoWindows");
        if (Need(e, "rooms", w) is not { ValueKind: JsonValueKind.Array } rooms || rooms.GetArrayLength() == 0)
            throw new BuildingFileException($"{w}: \"rooms\" is a list of at least one room");
        var ri = 0;
        foreach (var re in rooms.EnumerateArray())
        {
            var rw = $"{w}, room {ri++}";
            Keys(re, rw, "id", "type", "rect", "rects", "floor", "wall", "ceiling", "furnish", "props", "windows", "autoWindows", "openTo", "seed", "rules");
            var id = Ascii(Need(re, "id", rw), rw, 40);
            rw = $"{w}, room \"{id}\"";
            var rects = new List<BRect>();
            if (Get(re, "rect") is { } one)
                rects.Add(Rect(one, rw));
            if (Get(re, "rects") is { } many)
                foreach (var x in List(many, rw))
                    rects.Add(Rect(x, rw));
            if (rects.Count == 0)
                throw new BuildingFileException($"{rw}: needs \"rect\": [x0, z0, x1, z1] (or \"rects\": a list of them)");
            var room = new BRoom { Id = id, Type = Text(Need(re, "type", rw), rw).ToLowerInvariant(), Rects = rects };
            if (Get(re, "floor") is { } rf) room.Floor = ctx.Surface(rf, $"room \"{id}\" floor", null);
            if (Get(re, "wall") is { } rwl) room.Wall = ctx.Surface(rwl, $"room \"{id}\" wall", null);
            if (Get(re, "ceiling") is { } rc) room.Ceiling = ctx.Surface(rc, $"room \"{id}\" ceiling", null);
            if (Get(re, "seed") is { } rs) room.Seed = Whole(rs, rw, 0, int.MaxValue);
            if (Get(re, "rules") is { } ru) room.Rules = Text(ru, rw).ToLowerInvariant();
            if (Get(re, "windows") is { } rwin) room.Windows = Bool(rwin, rw);
            if (Get(re, "autoWindows") is { } raw) room.AutoWindows = ReadAutoWindows(raw, $"{rw}: autoWindows");
            if (Get(re, "openTo") is { } ot)
                foreach (var o in List(ot, rw))
                    room.OpenTo.Add(Text(o, rw));
            if (Get(re, "furnish") is { } fu)
            {
                var mode = Text(fu, rw).ToLowerInvariant();
                room.Furnish = mode switch
                {
                    "auto" => FurnishMode.Auto,
                    "none" => FurnishMode.None,
                    "explicit" or "props" => FurnishMode.Explicit,
                    _ => throw new BuildingFileException($"{rw}: \"furnish\" is \"auto\" (the room type's rules), \"none\" or \"explicit\" (only \"props\")"),
                };
            }
            if (Get(re, "props") is { } pl)
            {
                var pi = 0;
                foreach (var pe in List(pl, rw))
                {
                    var pw = $"{rw}, props {pi++}";
                    Keys(pe, pw, "name", "at", "wall", "along", "yaw", "y", "scale");
                    var p = new BPlacement { Prop = Text(Need(pe, "name", pw), pw) };
                    if (Get(pe, "at") is { } at)
                    {
                        var xz = Nums(at, 2, pw);
                        (p.X, p.Z) = (xz[0], xz[1]);
                    }
                    if (Get(pe, "wall") is { } wl)
                    {
                        var s = Text(wl, pw).ToUpperInvariant();
                        if (s is not ("N" or "S" or "E" or "W"))
                            throw new BuildingFileException($"{pw}: \"wall\" is N, S, E or W");
                        p.Wall = s[0];
                    }
                    if (Get(pe, "along") is { } al) p.Along = Num(al, pw);
                    if (Get(pe, "yaw") is { } yw) p.Yaw = Num(yw, pw);
                    if (Get(pe, "y") is { } py) p.Y = Range(Num(py, pw), 0f, 6f, pw);
                    if (Get(pe, "scale") is { } sc) p.Scale = Range(Num(sc, pw), 0.25f, 4f, pw);
                    room.Props.Add(p);
                }
            }
            f.Rooms.Add(room);
        }
        if (f.Rooms.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } dup)
            throw new BuildingFileException($"{w}: two rooms are called \"{dup.Key}\" (use \"rects\" for an L-shaped room)");
        if (Get(e, "openings") is { } ops)
        {
            var oi = 0;
            foreach (var oe in List(ops, $"{w}: openings"))
            {
                var ow = $"{w}, opening {oi++}";
                Keys(oe, ow, "type", "at", "width", "sill", "head", "material", "hinge", "swing", "open", "front", "health", "color");
                var kind = Text(Need(oe, "type", ow), ow).ToLowerInvariant() switch
                {
                    "door" => OpeningKind.Door,
                    "window" => OpeningKind.Window,
                    "arch" or "opening" or "doorway" => OpeningKind.Arch,
                    "hole" or "breach" => OpeningKind.Hole,
                    var k => throw new BuildingFileException($"{ow}: \"type\" is door, window, arch or hole (got \"{k}\")"),
                };
                var at = Nums(Need(oe, "at", ow), 2, ow);
                var o = new BOpening { Kind = kind, X = at[0], Z = at[1] };
                o.Width = Get(oe, "width") is { } wd ? Range(Num(wd, ow), 0.3f, 8f, $"{ow}: width") : kind == OpeningKind.Window ? 1.2f : 1f;
                o.Bottom = kind is OpeningKind.Window or OpeningKind.Hole ? 0.9f : 0f;
                o.Top = kind == OpeningKind.Window ? 2.2f : -1f; // doors: the building's door height (below); windows 1.3 m tall, so a broken one can be climbed through
                if (Get(oe, "sill") is { } si) o.Bottom = Range(Num(si, ow), 0f, 6f, $"{ow}: sill");
                if (Get(oe, "head") is { } he) o.Top = Range(Num(he, ow), 0.3f, 8f, $"{ow}: head");
                if (Get(oe, "material") is { } ma)
                    o.Material = Text(ma, ow).ToLowerInvariant() switch
                    {
                        "wood" => DoorMaterial.Wood,
                        "metal" or "steel" => DoorMaterial.Metal,
                        "glass" => DoorMaterial.Glass,
                        _ => throw new BuildingFileException($"{ow}: door \"material\" is wood, metal or glass"),
                    };
                if (Get(oe, "hinge") is { } hi)
                    o.HingeRight = Text(hi, ow).ToLowerInvariant() switch
                    {
                        "left" => false,
                        "right" => true,
                        _ => throw new BuildingFileException($"{ow}: \"hinge\" is left or right (seen from the side it opens away from)"),
                    };
                if (Get(oe, "swing") is { } sw)
                    o.SwingOut = Text(sw, ow).ToLowerInvariant() switch
                    {
                        "in" => false,
                        "out" => true,
                        _ => throw new BuildingFileException($"{ow}: \"swing\" is in or out"),
                    };
                if (Get(oe, "open") is { } op) o.StartOpen = Bool(op, ow);
                if (Get(oe, "front") is { } fr) o.Front = Bool(fr, ow);
                if (Get(oe, "health") is { } hp) o.Health = Whole(hp, ow, 1, 5000);
                if (Get(oe, "color") is { } co) o.Color = ctx.Color(co, ow);
                if (kind != OpeningKind.Door && (Get(oe, "hinge") != null || Get(oe, "swing") != null || Get(oe, "material") != null || Get(oe, "open") != null))
                    throw new BuildingFileException($"{ow}: \"hinge\", \"swing\", \"material\" and \"open\" are for doors");
                f.Openings.Add(o);
            }
        }
        return f;
    }

    private static BAutoWindows ReadAutoWindows(JsonElement e, string w)
    {
        Keys(e, w, "spacing", "width", "sill", "head", "health");
        var a = new BAutoWindows();
        if (Get(e, "spacing") is { } s) a.Spacing = Range(Num(s, w), 1.5f, 20f, $"{w}: spacing");
        if (Get(e, "width") is { } wd) a.Width = Range(Num(wd, w), 0.4f, 6f, $"{w}: width");
        if (Get(e, "sill") is { } si) a.Sill = Range(Num(si, w), 0f, 8f, $"{w}: sill");
        if (Get(e, "head") is { } he) a.Head = Range(Num(he, w), 0.5f, 9f, $"{w}: head");
        if (Get(e, "health") is { } hp) a.Health = Whole(hp, w, 1, 5000);
        if (a.Head <= a.Sill + 0.3f)
            throw new BuildingFileException($"{w}: \"head\" must be at least 0.3 m above \"sill\"");
        if (a.Width > a.Spacing - 0.3f)
            throw new BuildingFileException($"{w}: \"width\" must be at least 0.3 m less than \"spacing\"");
        return a;
    }

    private static BStair ReadStair(JsonElement e, Ctx ctx, string w, int floors)
    {
        Keys(e, w, "floor", "rect", "dir", "color", "railing", "alt");
        var s = new BStair { Floor = Whole(Need(e, "floor", w), w, 0, Math.Max(0, floors - 1)), Rect = Rect(Need(e, "rect", w), w) };
        (s.Dx, s.Dz) = Dir(Need(e, "dir", w), w);
        if (Get(e, "color") is { } c) s.Surface = ctx.Surface(c, "stairs color", null);
        if (Get(e, "railing") is { } ra) s.Railing = Bool(ra, w);
        if (Get(e, "alt") is { } alt)
        {
            Keys(alt, $"{w}: alt", "rect", "dir");
            s.AltRect = Rect(Need(alt, "rect", w), $"{w}: alt");
            (s.AltDx, s.AltDz) = Dir(Need(alt, "dir", w), $"{w}: alt");
        }
        return s;
    }

    private static (int, int) Dir(JsonElement e, string w) => Text(e, w).ToLowerInvariant() switch
    {
        "+x" or "x" or "e" or "east" => (1, 0),
        "-x" or "w" or "west" => (-1, 0),
        "+z" or "z" or "s" or "south" => (0, 1),
        "-z" or "n" or "north" => (0, -1),
        _ => throw new BuildingFileException($"{w}: \"dir\" is +x, -x, +z or -z (east, west, south, north)"),
    };

    private static BRoof ReadRoof(JsonElement e, Ctx ctx, string w)
    {
        Keys(e, w, "type", "ridge", "pitch", "overhang", "color", "parapet", "dir");
        var r = new BRoof();
        if (Get(e, "type") is { } t)
            r.Kind = Text(t, w).ToLowerInvariant() switch
            {
                "flat" => RoofKind.Flat,
                "gable" => RoofKind.Gable,
                "hip" => RoofKind.Hip,
                "shed" or "lean-to" => RoofKind.Shed,
                "none" => RoofKind.None,
                _ => throw new BuildingFileException($"{w}: \"type\" is flat, gable, hip, shed or none"),
            };
        if (Get(e, "ridge") is { } ri)
        {
            var s = Text(ri, w).ToLowerInvariant();
            if (s is not ("x" or "z"))
                throw new BuildingFileException($"{w}: \"ridge\" is x or z");
            r.Ridge = s[0];
        }
        if (Get(e, "pitch") is { } p) r.Pitch = Range(Num(p, w), 5f, 60f, $"{w}: pitch");
        if (Get(e, "overhang") is { } o) r.Overhang = Range(Num(o, w), 0f, 1.5f, $"{w}: overhang");
        if (Get(e, "parapet") is { } pa) r.Parapet = Range(Num(pa, w), 0f, 2f, $"{w}: parapet");
        if (Get(e, "color") is { } c) r.Surface = ctx.Surface(c, "roof color", null);
        if (Get(e, "dir") is { } d) (r.ShedDx, r.ShedDz) = Dir(d, w);
        return r;
    }

    private static BExtra ReadExtra(JsonElement e, Ctx ctx, string w, int floors)
    {
        var kind = Text(Need(e, "type", w), w).ToLowerInvariant() switch
        {
            "block" => ExtraKind.Block,
            "prop" => ExtraKind.Prop,
            "porch" => ExtraKind.Porch,
            "balcony" => ExtraKind.Balcony,
            "awning" => ExtraKind.Awning,
            "platform" or "mezzanine" => ExtraKind.Platform,
            var k => throw new BuildingFileException($"{w}: \"type\" is block, prop, porch, balcony, awning or platform (got \"{k}\")"),
        };
        var x = new BExtra { Kind = kind };
        switch (kind)
        {
            case ExtraKind.Block:
            {
                Keys(e, w, "type", "min", "max", "color");
                var a = Nums(Need(e, "min", w), 3, w);
                var b = Nums(Need(e, "max", w), 3, w);
                x.Min = new Vector3(MathF.Min(a[0], b[0]), MathF.Min(a[1], b[1]), MathF.Min(a[2], b[2]));
                x.Max = new Vector3(MathF.Max(a[0], b[0]), MathF.Max(a[1], b[1]), MathF.Max(a[2], b[2]));
                if (x.Max.X - x.Min.X < 0.01f || x.Max.Y - x.Min.Y < 0.01f || x.Max.Z - x.Min.Z < 0.01f)
                    throw new BuildingFileException($"{w}: a block needs some size in x, y and z");
                x.Surface = Get(e, "color") is { } c ? ctx.Surface(c, "block color", null) : null;
                break;
            }
            case ExtraKind.Prop:
            {
                Keys(e, w, "type", "name", "at", "yaw", "scale", "onRoof");
                x.Prop = Text(Need(e, "name", w), w);
                if (Get(e, "onRoof") is { } orf) x.OnRoof = Bool(orf, w);
                var at = Need(e, "at", w);
                var n = at.ValueKind == JsonValueKind.Array ? at.GetArrayLength() : 0;
                var v = Nums(at, n == 3 ? 3 : 2, w);
                x.At = n == 3 ? new Vector3(v[0], v[1], v[2]) : new Vector3(v[0], 0, v[1]);
                if (Get(e, "yaw") is { } yw) x.Yaw = Num(yw, w);
                if (Get(e, "scale") is { } sc) x.Scale = Range(Num(sc, w), 0.25f, 4f, w);
                break;
            }
            case ExtraKind.Porch:
            case ExtraKind.Awning:
                Keys(e, w, "type", "rect", "height", "roof", "posts", "color");
                x.Rect = Rect(Need(e, "rect", w), w);
                if (Get(e, "height") is { } h) x.Height = Range(Num(h, w), 2f, 8f, $"{w}: height");
                if (Get(e, "roof") is { } rf) x.Roof = Bool(rf, w);
                if (Get(e, "posts") is { } po) x.Posts = Bool(po, w);
                x.Surface = Get(e, "color") is { } pc ? ctx.Surface(pc, "porch color", null) : null;
                if (kind == ExtraKind.Awning)
                    x.Posts = Get(e, "posts") is { } ap && Bool(ap, w);
                break;
            case ExtraKind.Platform:
            {
                Keys(e, w, "type", "rect", "floor", "height", "railing", "color", "stairs");
                x.Rect = Rect(Need(e, "rect", w), w);
                x.Floor = Get(e, "floor") is { } pf ? Whole(pf, w, 0, Math.Max(0, floors - 1)) : 0;
                x.Height = Range(Num(Need(e, "height", w), w), 1f, 8f, $"{w}: height");
                if (Get(e, "railing") is { } pr) x.Railing = Range(Num(pr, w), 0f, 2f, $"{w}: railing");
                x.Surface = Get(e, "color") is { } plc ? ctx.Surface(plc, "platform color", null) : null;
                if (Get(e, "stairs") is { } ps)
                {
                    Keys(ps, $"{w}: stairs", "rect", "dir");
                    x.StairRect = Rect(Need(ps, "rect", w), $"{w}: stairs");
                    (x.StairDx, x.StairDz) = Dir(Need(ps, "dir", w), $"{w}: stairs");
                }
                break;
            }
            case ExtraKind.Balcony:
                Keys(e, w, "type", "rect", "floor", "railing", "color");
                x.Rect = Rect(Need(e, "rect", w), w);
                x.Floor = Whole(Need(e, "floor", w), w, 1, Math.Max(1, floors - 1));
                if (Get(e, "railing") is { } ra) x.Railing = Range(Num(ra, w), 0f, 2f, $"{w}: railing");
                x.Surface = Get(e, "color") is { } bc ? ctx.Surface(bc, "balcony color", null) : null;
                break;
        }
        return x;
    }

    // ---- Furnishing ------------------------------------------------------------------------------------------

    /// <summary>Parses one furnishing rule file. The room type is the "type" key (or the file name).</summary>
    public static FurnishRule LoadRule(string json, string source, string fallbackType)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, Options);
        }
        catch (JsonException ex)
        {
            throw new BuildingFileException($"{source}: not valid JSON ({ex.Message})");
        }
        using (doc)
        {
            var r = doc.RootElement;
            Keys(r, source, "format", "type", "description", "items");
            if (Get(r, "format") is { } f && Text(f, source) != FurnishFormat)
                throw new BuildingFileException($"{source}: \"format\" is \"{FurnishFormat}\"");
            var rule = new FurnishRule
            {
                Type = (Get(r, "type") is { } t ? Text(t, source) : fallbackType).ToLowerInvariant(),
                SourceFile = source,
            };
            if (Get(r, "description") is { } de) rule.Description = Ascii(de, source, 300);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var i = 0;
            foreach (var ie in List(Need(r, "items", source), $"{source}: items"))
            {
                var w = $"{source}: items {i++}";
                Keys(ie, w, "id", "props", "place", "count", "chance", "of", "y", "prefer", "gap", "distance", "area", "turn", "scale");
                var props = Get(ie, "props") is { } pe ? List(pe, w).Select(x => Text(x, w)).ToArray() : Array.Empty<string>();
                if (props.Length == 0)
                    throw new BuildingFileException($"{w}: \"props\" is a list of at least one prop name");
                var item = new FurnishItem
                {
                    Props = props,
                    Place = Text(Need(ie, "place", w), w).ToLowerInvariant() switch
                    {
                        "wall" => FurnishPlace.Wall,
                        "corner" => FurnishPlace.Corner,
                        "centre" or "center" => FurnishPlace.Centre,
                        "floor" => FurnishPlace.Floor,
                        "on" => FurnishPlace.On,
                        "beside" => FurnishPlace.Beside,
                        "front" => FurnishPlace.Front,
                        "around" => FurnishPlace.Around,
                        "ceiling" => FurnishPlace.Ceiling,
                        var p => throw new BuildingFileException($"{w}: \"place\" is wall, corner, centre, floor, on, beside, front, around or ceiling (got \"{p}\")"),
                    },
                };
                if (Get(ie, "id") is { } id)
                {
                    item.Id = Text(id, w);
                    if (!ids.Add(item.Id))
                        throw new BuildingFileException($"{w}: another item is already called \"{item.Id}\"");
                }
                if (Get(ie, "count") is { } c)
                {
                    if (c.ValueKind == JsonValueKind.Array)
                    {
                        var mm = Nums(c, 2, w);
                        (item.CountMin, item.CountMax) = ((int)mm[0], (int)mm[1]);
                    }
                    else
                        item.CountMin = item.CountMax = Whole(c, w, 0, 64);
                    if (item.CountMin < 0 || item.CountMax < item.CountMin || item.CountMax > 64)
                        throw new BuildingFileException($"{w}: \"count\" is a number or [min, max], 0 to 64");
                }
                if (Get(ie, "chance") is { } ch) item.Chance = Range(Num(ch, w), 0f, 1f, $"{w}: chance");
                if (Get(ie, "of") is { } of)
                {
                    item.Of = Text(of, w);
                    if (!ids.Contains(item.Of))
                        throw new BuildingFileException($"{w}: \"of\" names \"{item.Of}\", but no earlier item has that \"id\"");
                }
                if (item.Place is FurnishPlace.On or FurnishPlace.Beside or FurnishPlace.Front or FurnishPlace.Around && item.Of.Length == 0)
                    throw new BuildingFileException($"{w}: \"{item.Place.ToString().ToLowerInvariant()}\" needs \"of\": the id of an earlier item");
                if (Get(ie, "y") is { } y) item.Y = Range(Num(y, w), 0f, 4f, $"{w}: y");
                if (Get(ie, "prefer") is { } pr)
                    item.Prefer = Text(pr, w).ToLowerInvariant() switch
                    {
                        "far" => FurnishPrefer.Far,
                        "near" => FurnishPrefer.Near,
                        "any" => FurnishPrefer.Any,
                        _ => throw new BuildingFileException($"{w}: \"prefer\" is far, near or any (from the room's doors)"),
                    };
                if (Get(ie, "gap") is { } g) item.Gap = Range(Num(g, w), 0f, 2f, $"{w}: gap");
                if (Get(ie, "distance") is { } di) item.Distance = Range(Num(di, w), -1f, 4f, $"{w}: distance");
                if (Get(ie, "area") is { } ar)
                {
                    var a = Nums(ar, 2, w);
                    (item.MinArea, item.MaxArea) = (a[0], a[1]);
                }
                if (Get(ie, "turn") is { } tu) item.Turn = Num(tu, w);
                if (Get(ie, "scale") is { } sc) item.Scale = Range(Num(sc, w), 0.25f, 4f, $"{w}: scale");
                rule.Items.Add(item);
            }
            return rule;
        }
    }

    // ---- JSON helpers ------------------------------------------------------------------------------------------

    private static void Keys(JsonElement e, string w, params string[] keys)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new BuildingFileException($"{w}: expected an object {{ ... }}");
        foreach (var p in e.EnumerateObject())
            if (!keys.Contains(p.Name))
                throw new BuildingFileException($"{w}: unknown key \"{p.Name}\" (it takes: {string.Join(", ", keys)})");
    }

    private static JsonElement? Get(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var p in e.EnumerateObject())
            if (p.Name == name)
                return p.Value;
        return null;
    }

    private static JsonElement Need(JsonElement e, string name, string w) =>
        Get(e, name) ?? throw new BuildingFileException($"{w}: missing \"{name}\"");

    private static IEnumerable<JsonElement> List(JsonElement e, string w) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : throw new BuildingFileException($"{w}: expected a list [ ... ]");

    private static float Num(JsonElement e, string w) =>
        e.ValueKind == JsonValueKind.Number ? e.GetSingle() : throw new BuildingFileException($"{w}: expected a number, got {e}");

    private static float Range(float v, float lo, float hi, string w) =>
        v >= lo && v <= hi ? v : throw new BuildingFileException($"{w}: {v.ToString(CultureInfo.InvariantCulture)} is outside {lo.ToString(CultureInfo.InvariantCulture)} to {hi.ToString(CultureInfo.InvariantCulture)}");

    private static int Whole(JsonElement e, string w, int min, int max) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) && i >= min && i <= max
            ? i : throw new BuildingFileException($"{w}: expected a whole number from {min} to {max}, got {e}");

    private static bool Bool(JsonElement e, string w) =>
        e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : throw new BuildingFileException($"{w}: expected true or false, got {e}");

    private static string Text(JsonElement e, string w) =>
        e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } s ? s : throw new BuildingFileException($"{w}: expected text, got {e}");

    /// <summary>Plain ASCII text (the game's font has nothing else), at most <paramref name="max"/> characters.</summary>
    private static string Ascii(JsonElement e, string w, int max)
    {
        var s = e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new BuildingFileException($"{w}: expected text, got {e}");
        if (s.Length > max)
            throw new BuildingFileException($"{w}: at most {max} characters");
        if (s.Any(c => c < 32 || c > 126))
            throw new BuildingFileException($"{w}: plain ASCII only (no curly quotes, long dashes or accents)");
        return s;
    }

    private static float[] Nums(JsonElement e, int n, string w)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != n)
            throw new BuildingFileException($"{w}: expected a list of {n} numbers, got {e}");
        return e.EnumerateArray().Select(x => Num(x, w)).ToArray();
    }

    private static BRect Rect(JsonElement e, string w)
    {
        var v = Nums(e, 4, w);
        var r = new BRect(MathF.Min(v[0], v[2]), MathF.Min(v[1], v[3]), MathF.Max(v[0], v[2]), MathF.Max(v[1], v[3]));
        if (r.W < 0.5f || r.D < 0.5f)
            throw new BuildingFileException($"{w}: a rect is [x0, z0, x1, z1] at least 0.5 m each way, got {e}");
        if (MathF.Abs(r.X0) > 60 || MathF.Abs(r.X1) > 60 || MathF.Abs(r.Z0) > 60 || MathF.Abs(r.Z1) > 60)
            throw new BuildingFileException($"{w}: buildings fit within 60 m of their origin");
        return r;
    }

    private static Vector3 LiteralColor(JsonElement e, string w)
    {
        try
        {
            return PropFile.Color(e, w);
        }
        catch (PropFileException ex)
        {
            throw new BuildingFileException(ex.Message);
        }
    }
}
