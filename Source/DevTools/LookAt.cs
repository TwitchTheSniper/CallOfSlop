using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>What the look-at inspector found under the crosshair.</summary>
public enum LookAtKind : byte
{
    None,

    /// <summary>A part of a placed prop (<see cref="LookAtHit.Prop"/>, <see cref="LookAtHit.Part"/>).</summary>
    Prop,

    /// <summary>A box of a building's shell, door or window (<see cref="LookAtHit.Box"/>, <see cref="LookAtHit.Building"/>).</summary>
    Building,

    /// <summary>A plain map box (<see cref="LookAtHit.Box"/>).</summary>
    Box,

    /// <summary>The terrain.</summary>
    Terrain,
}

/// <summary>One look-at result: what was hit, where, and which file op made it. Indices are 0-based; text shows them 1-based.</summary>
public readonly record struct LookAtHit(LookAtKind Kind, float Distance, Vector3 Point, Vector3 Normal, int Prop = -1, int Part = -1, int Box = -1, int Building = -1, int FileOp = -1)
{
    public bool Found => Kind != LookAtKind.None;
}

/// <summary>
/// The dev look-at inspector's ray (Source/UI/UiDevTools.cs): what the crosshair is on, down to the part of a prop
/// (tested against every part's turned box, so the cup of an anemometer is told from its pole), the box of a building or
/// the map op that made a box. A brute-force walk over the map's boxes and props: run only while the inspector is open (a
/// map of 50 000 boxes costs well under a millisecond). Allocation free. <see cref="Reference"/> is the one-line text the
/// copy key puts on the clipboard for a Playtest Desk report.
/// </summary>
public static class LookAt
{
    /// <summary>The nearest thing along the ray within <paramref name="maxDist"/> metres (<paramref name="dir"/> normalised).</summary>
    public static LookAtHit Probe(Map map, Vector3 origin, Vector3 dir, float maxDist)
    {
        var best = maxDist;
        var hit = default(LookAtHit);

        // Map boxes (prop colliders are skipped: the props' own parts are tested below).
        var solid = map.Solid;
        for (var i = 0; i < solid.Count; i++)
        {
            if (map.IsPropCollider(i))
                continue;
            var b = solid[i];
            if (b.Half.X <= 0 || b.Half.Y <= 0 || b.Half.Z <= 0)
                continue; // a removed box (an open door, a broken window)
            if (b.Raycast(origin, dir, best) is { } t && t > 1e-4f && t < best)
            {
                best = t;
                var p = origin + dir * t;
                hit = new LookAtHit(map.IsBuildingBox(i) ? LookAtKind.Building : LookAtKind.Box, t, p, BoxNormal(p - b.Center, b.Half), Box: i);
            }
        }

        // Props, part by part, in each prop's own frame.
        var props = map.PropInstances;
        for (var i = 0; i < props.Count; i++)
        {
            var inst = props[i];
            if (map.IsPropBroken(i) || inst.Scale <= 0)
                continue;
            if (inst.Bounds.Raycast(origin, dir, best) is not { } tb || tb >= best)
            {
                if (!Inside(inst.Bounds, origin))
                    continue;
            }
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, inst.Yaw);
            var unturn = Quaternion.Conjugate(turn);
            var lo = Vector3.Transform(origin - inst.Position, unturn) / inst.Scale;
            var ld = Vector3.Transform(dir, unturn) / inst.Scale; // (t stays in world metres)
            var parts = inst.Def.Parts;
            for (var k = 0; k < parts.Count; k++)
            {
                var part = parts[k];
                Vector3 po, pd, half;
                Quaternion q;
                if (part.Corners is { Length: 8 })
                {
                    var bb = part.Bounds;
                    q = Quaternion.Identity;
                    po = lo - bb.Center;
                    pd = ld;
                    half = bb.Half;
                }
                else
                {
                    q = part.Rotation;
                    var iq = Quaternion.Conjugate(q);
                    po = Vector3.Transform(lo - part.Offset, iq);
                    pd = Vector3.Transform(ld, iq);
                    half = part.Size / 2;
                }
                if (new Box(Vector3.Zero, half).Raycast(po, pd, best) is { } t && t > 1e-4f && t < best)
                {
                    best = t;
                    var n = Vector3.Transform(Vector3.Transform(BoxNormal(po + pd * t, half), q), turn);
                    hit = new LookAtHit(LookAtKind.Prop, t, origin + dir * t, n, Prop: i, Part: k);
                }
            }
        }

        // The ground.
        if (map.TerrainCollision is { } terrain)
        {
            var t = terrain.Raycast(origin, dir, best);
            if (t < best && t > 1e-4f)
            {
                var p = origin + dir * t;
                hit = new LookAtHit(LookAtKind.Terrain, t, p, terrain.NormalAt(p.X, p.Z));
            }
        }

        if (!hit.Found)
            return hit;
        // Which building and which file op.
        var building = -1;
        if (hit.Kind == LookAtKind.Building)
            for (var b = 0; b < map.PlacedBuildings.Count; b++)
            {
                var pb = map.PlacedBuildings[b];
                if (hit.Box >= pb.ShellStart && hit.Box < pb.ShellStart + pb.ShellCount)
                    building = b;
            }
        if (hit.Kind == LookAtKind.Building && building < 0)
        {
            foreach (var d in map.BuildingDoors)
                if (d.SolidIndex == hit.Box)
                    building = d.Building;
            foreach (var w in map.BuildingWindows)
                if (w.SolidIndex == hit.Box)
                    building = w.Building;
        }
        var op = hit.Kind switch
        {
            LookAtKind.Prop => map.FileOpOfProp(hit.Prop),
            LookAtKind.Box or LookAtKind.Building => map.FileOpOfBox(hit.Box),
            _ => -1,
        };
        return hit with { Building = building, FileOp = op };
    }

    private static bool Inside(Box b, Vector3 p) =>
        MathF.Abs(p.X - b.Center.X) <= b.Half.X && MathF.Abs(p.Y - b.Center.Y) <= b.Half.Y && MathF.Abs(p.Z - b.Center.Z) <= b.Half.Z;

    /// <summary>The face normal of a box (centred on the origin) at a point on its surface.</summary>
    private static Vector3 BoxNormal(Vector3 local, Vector3 half)
    {
        float ax = MathF.Abs(local.X) / MathF.Max(half.X, 1e-6f), ay = MathF.Abs(local.Y) / MathF.Max(half.Y, 1e-6f), az = MathF.Abs(local.Z) / MathF.Max(half.Z, 1e-6f);
        if (ax >= ay && ax >= az)
            return new Vector3(MathF.Sign(local.X), 0, 0);
        return ay >= az ? new Vector3(0, MathF.Sign(local.Y), 0) : new Vector3(0, 0, MathF.Sign(local.Z));
    }

    // ---- Text -----------------------------------------------------------------------------------

    /// <summary>A prop's file as a path from the game folder (and the repository): Props/..., Maps/&lt;Pack&gt;/Props/... or Buildings/&lt;Name&gt;/Props/...</summary>
    public static string PropPath(PropDef def)
    {
        var src = (def.FlagBase ?? def).SourceFile?.Replace('\\', '/') ?? "";
        if (src.Length == 0)
            return "(built in code)";
        if (def.Pack != null)
            return "Maps/" + src;
        return src.StartsWith("Buildings/", StringComparison.Ordinal) ? src : "Props/" + src;
    }

    /// <summary>A map's file as a path from the game folder: Maps/&lt;Pack&gt;/&lt;File&gt;.json.</summary>
    public static string MapPath(Map map) => map.SourceFile is { Length: > 0 } s ? "Maps/" + s.Replace('\\', '/') : "(built in code)";

    /// <summary>The file op as text: "op #8 PlaceProp" (1-based, as the map loader's errors count), or "" when no op made it.</summary>
    public static string OpText(Map map, int fileOp) =>
        fileOp >= 0 && fileOp < map.FileOpMarks.Count ? $"op #{map.FileOpMarks[fileOp].OpNumber} {map.FileOpNames[fileOp]}" : "";

    /// <summary>A part's animation in a few words: "anim spin y 90 rpm", "anim swing x", or "" for a still part.</summary>
    public static string AnimText(PropPart part)
    {
        if (part.Anim is not { } a)
            return "";
        var s = "anim " + a.Type.ToString().ToLowerInvariant();
        if (a.Type is PropAnimType.Spin or PropAnimType.Swing or PropAnimType.Bob or PropAnimType.Shake)
            s += " " + AxisText(a.Axis);
        if (a.Rpm != 0)
            s += " " + F(a.Rpm) + " rpm";
        return s;
    }

    /// <summary>"x", "-y", "z" for a unit axis, else the vector.</summary>
    public static string AxisText(Vector3 axis)
    {
        if (MathF.Abs(MathF.Abs(axis.X) - 1) < 1e-4f)
            return axis.X > 0 ? "x" : "-x";
        if (MathF.Abs(MathF.Abs(axis.Y) - 1) < 1e-4f)
            return axis.Y > 0 ? "y" : "-y";
        if (MathF.Abs(MathF.Abs(axis.Z) - 1) < 1e-4f)
            return axis.Z > 0 ? "z" : "-z";
        return V(axis, "0.##");
    }

    /// <summary>
    /// The short, stable reference line the copy key puts on the clipboard, e.g.
    /// <c>Animated Props / Props/Animated/Anemometer.json part #3 of 12 (anim spin y 90 rpm) / Maps/Dev/AnimatedProps.json op #8 PlaceProp / at (-33.8, 1.6, -61.1)</c>.
    /// Part, box and op numbers are 1-based, as the loaders' error messages count them.
    /// </summary>
    public static string Reference(Map map, in LookAtHit hit)
    {
        if (!hit.Found)
            return $"{map.Name} / nothing under the crosshair";
        var sb = new StringBuilder();
        sb.Append(map.Name).Append(" / ");
        switch (hit.Kind)
        {
            case LookAtKind.Prop:
            {
                var inst = map.PropInstances[hit.Prop];
                var part = inst.Def.Parts[hit.Part];
                sb.Append(PropPath(inst.Def)).Append(" \"").Append(inst.Def.Name).Append("\" part #").Append(hit.Part + 1).Append(" of ").Append(inst.Def.Parts.Count);
                var anim = AnimText(part);
                if (anim.Length > 0)
                    sb.Append(" (").Append(anim).Append(')');
                break;
            }
            case LookAtKind.Building when hit.Building >= 0:
            {
                var b = map.PlacedBuildings[hit.Building];
                var path = b.Def.SourceFile?.Replace('\\', '/') ?? "";
                sb.Append(path.StartsWith("Buildings/", StringComparison.Ordinal) ? path : "Buildings/" + path).Append(" \"").Append(b.Def.Name).Append('"');
                if (hit.Box >= b.ShellStart && hit.Box < b.ShellStart + b.ShellCount)
                    sb.Append(" shell box #").Append(hit.Box - b.ShellStart + 1).Append(" of ").Append(b.ShellCount);
                sb.Append(map.IsBuildingDoorBox(hit.Box) ? " (door)" : map.IsBuildingGlass(hit.Box) ? " (window glass)" : "");
                break;
            }
            case LookAtKind.Building:
            case LookAtKind.Box:
                sb.Append("box ").Append(hit.Box).Append(hit.Kind == LookAtKind.Building ? " (building)" : "");
                if (hit.FileOp >= 0)
                {
                    var m = map.FileOpMarks[hit.FileOp];
                    var count = map.FileOpMarks[hit.FileOp + 1].FirstBox - m.FirstBox;
                    sb.Append(" (#").Append(hit.Box - m.FirstBox + 1).Append(" of ").Append(count).Append(" of its op)");
                }
                break;
            case LookAtKind.Terrain:
                sb.Append("terrain");
                break;
        }
        var op = OpText(map, hit.FileOp);
        sb.Append(" / ").Append(MapPath(map));
        if (op.Length > 0)
            sb.Append(' ').Append(op);
        else if (hit.Kind == LookAtKind.Prop && hit.Prop >= map.ClutterStart)
            sb.Append(" (scattered by a Clutter op)");
        sb.Append(" / at ").Append(V(hit.Point, "0.0"));
        return sb.ToString();
    }

    /// <summary>The inspector's detail lines for a hit (more than the reference: the normal, the part's box, the placed prop's turn and scale).</summary>
    public static List<string> Details(Map map, in LookAtHit hit)
    {
        var lines = new List<string>();
        if (!hit.Found)
        {
            lines.Add("Nothing under the crosshair (within range).");
            return lines;
        }
        lines.Add($"Map: {map.Name} ({MapPath(map)})");
        switch (hit.Kind)
        {
            case LookAtKind.Prop:
            {
                var inst = map.PropInstances[hit.Prop];
                var def = inst.Def;
                var part = def.Parts[hit.Part];
                lines.Add($"Prop: {def.Name} ({def.Category}, {(def.IsSolid ? "solid" : "cosmetic")}{(def.Breakable != null ? ", breakable" : "")}{(def.FlagId != null ? ", flag " + def.FlagId : "")})");
                lines.Add($"File: {PropPath(def)}");
                lines.Add($"Placed: #{hit.Prop + 1} at {V(inst.Position, "0.00")}, yaw {F(inst.Yaw * 180f / MathF.PI)} deg, scale {F(inst.Scale)}");
                lines.Add($"Part #{hit.Part + 1} of {def.Parts.Count}: size {V(part.Size, "0.###")}, offset {V(part.Offset, "0.###")}"
                          + (part.RotationDeg != Vector3.Zero ? $", rotation {V(part.RotationDeg, "0.#")}" : ""));
                var tags = new List<string>();
                if (part.Layer >= 0)
                    tags.Add("material " + (PropFile.Materials.FirstOrDefault(m => m.Value == part.Layer).Key ?? part.Layer.ToString(CultureInfo.InvariantCulture)));
                if (part.Glass > 0)
                    tags.Add("glass " + F(part.Glass));
                if (part.Glow > 0)
                    tags.Add("glow " + F(part.Glow));
                if (part.Corners != null)
                    tags.Add("cloth piece");
                var anim = AnimText(part);
                if (anim.Length > 0)
                    tags.Add(anim + (part.Anim!.Pivot is { } pv ? $" about {V(pv, "0.##")}" : ""));
                lines.Add($"Colour {V(part.Color, "0.##")}" + (tags.Count > 0 ? ", " + string.Join(", ", tags) : ""));
                break;
            }
            case LookAtKind.Building when hit.Building >= 0:
            {
                var b = map.PlacedBuildings[hit.Building];
                lines.Add($"Building: {b.Def.Name} (#{hit.Building + 1}), file {b.Def.SourceFile?.Replace('\\', '/')}");
                break;
            }
        }
        if (hit.Box >= 0)
        {
            var box = map.Solid[hit.Box];
            lines.Add($"Box {hit.Box}: {V(box.Min, "0.##")} to {V(box.Max, "0.##")}");
        }
        var op = OpText(map, hit.FileOp);
        lines.Add(op.Length > 0 ? $"Made by map {op}" : hit.Kind == LookAtKind.Terrain ? "Made by the map's \"terrain\" section" : "Made by no single op (foliage, Clutter, the boundary or a generated part)");
        lines.Add($"Hit {V(hit.Point, "0.00")}, normal {V(hit.Normal, "0.##")}, {F(hit.Distance)} m away");
        return lines;
    }

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string V(Vector3 v, string fmt) =>
        $"({v.X.ToString(fmt, CultureInfo.InvariantCulture)}, {v.Y.ToString(fmt, CultureInfo.InvariantCulture)}, {v.Z.ToString(fmt, CultureInfo.InvariantCulture)})";
}
