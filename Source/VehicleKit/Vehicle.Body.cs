using System.Numerics;

namespace CallOfSlop.VehicleKit;

public sealed partial class Vehicle
{
    /// <summary>Plate sizes in quarter metres, the name's size words after them: the biggest that fits is not always taken, which is what makes a deck look patched.</summary>
    private static readonly (int W, int D, string Size)[] Sizes = [(4, 8, "1x2 m"), (8, 4, "1x2 m"), (4, 4, "1 m"), (2, 4, "0.5x1 m"), (4, 2, "0.5x1 m"), (2, 2, "0.5 m"), (1, 1, "0.25 m")];

    public const float Cell = 0.25f;

    /// <summary>A rectangle of a deck (metres): x0, x1, z0, z1.</summary>
    public readonly record struct Rect(float X0, float X1, float Z0, float Z1)
    {
        public bool Contains(float x, float z) => x > X0 && x < X1 && z > Z0 && z < Z1;
    }

    /// <summary>The panels a body method fitted, by the name the generator gave them ("bed", "hood"...), for spikes and paint.</summary>
    public Dictionary<string, List<VPart>> Panels { get; } = new();

    private List<VPart> PanelList(string tag)
    {
        if (!Panels.TryGetValue(tag, out var list))
            Panels[tag] = list = new List<VPart>();
        return list;
    }

    private static float Thick(ConstructPartDef d) => (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f;

    private string Tile(string material, string size) => $"{material} {size}";

    /// <summary>
    /// A deck lying flat with its underside at <paramref name="y"/>: patched from plates of the style's <paramref name="slot"/> in seeded sizes, none where <paramref name="holes"/> say, working out from the
    /// frame so each plate has something to fit on. Edges are odd eighths (±0.125, ±0.375...), the plates' grid. A plate no socket takes leaves a gap: that's a rusted-out corner.
    /// </summary>
    public Vehicle Deck(string tag, float y, Rect r, string slot, Rect[]? holes = null, int smallest = 1)
    {
        var d = Dice.Fork("deck " + tag);
        var nx = (int)MathF.Round((r.X1 - r.X0) / Cell);
        var nz = (int)MathF.Round((r.Z1 - r.Z0) / Cell);
        var used = new bool[nx, nz];
        var cells = new List<(int I, int J)>();
        for (var i = 0; i < nx; i++)
        for (var j = 0; j < nz; j++)
        {
            float cx = r.X0 + (i + 0.5f) * Cell, cz = r.Z0 + (j + 0.5f) * Cell;
            used[i, j] = holes != null && holes.Any(h => h.Contains(cx, cz));
            cells.Add((i, j));
        }
        cells = cells.OrderBy(c => MathF.Abs(r.X0 + (c.I + 0.5f) * Cell)).ThenBy(c => c.J).ToList();
        var list = PanelList(tag);
        for (var progress = true; progress;) // (a plate far from the frame fits once its neighbour is on)
        {
            progress = false;
            foreach (var (i, j) in cells)
                if (!used[i, j] && Plate(y, r, used, nx, nz, i, j, Style.Part(d, slot), d, list, smallest))
                    progress = true;
        }
        return this;
    }

    /// <summary>One plate of a deck covering cell (i, j), the first size that fits; false when none does.</summary>
    private bool Plate(float y, Rect r, bool[,] used, int nx, int nz, int i, int j, string material, Dice d, List<VPart> list, int smallest)
    {
        foreach (var (w, h, size) in d.Shuffled(Sizes.Where(s => s.W > 1)).OrderBy(s => s.W * s.D < 8 ? 1 : 0).Append(Sizes[^1]))
        {
            var name = Tile(material, size);
            if (w < smallest || ConstructParts.Find(name) == null || !Room(used, nx, nz, i, j, w, h, out var i0, out var j0))
                continue;
            var (x0, z0) = (r.X0 + i0 * Cell, r.Z0 + j0 * Cell);
            try
            {
                list.Add(Put(name, new Vector3(x0 + w * Cell * 0.5f, y + Thick(Def(name)), z0 + h * Cell * 0.5f), Orient.Facing(Vector3.UnitY), 0.03f, fit: new Vector3(w * Cell * 0.5f, 0, h * Cell * 0.5f)));
                for (var a = i0; a < i0 + w; a++)
                for (var b = j0; b < j0 + h; b++)
                    used[a, b] = true;
                return true;
            }
            catch (VehicleKitException)
            {
                // (no socket for this size here: the next one)
            }
        }
        return false;
    }

    private static bool Room(bool[,] used, int nx, int nz, int i, int j, int w, int h, out int i0, out int j0)
    {
        foreach (var (ax, az) in new[] { (i, j), (i - w + 1, j), (i, j - h + 1), (i - w + 1, j - h + 1) })
        {
            if (ax < 0 || az < 0 || ax + w > nx || az + h > nz)
                continue;
            var free = true;
            for (var a = ax; a < ax + w && free; a++)
            for (var b = az; b < az + h && free; b++)
                free = !used[a, b];
            if (free)
            {
                (i0, j0) = (ax, az);
                return true;
            }
        }
        (i0, j0) = (i, j);
        return false;
    }

    /// <summary>
    /// A wall along the edge of what's already on: plates upright with their outer face at <paramref name="face"/> side, from <paramref name="from"/> to <paramref name="to"/> along the wall, bottom edge at <paramref name="bottom"/>,
    /// <paramref name="height"/> tall (0.5 or 1). <paramref name="gap"/>: the share of plates missing. Runs along z when the face is ±x, along x when it is ±z.
    /// </summary>
    public Vehicle Wall(string tag, Vector3 face, float at, float from, float to, float bottom, float height, string slot, double gap = 0)
    {
        var d = Dice.Fork("wall " + tag);
        var alongZ = MathF.Abs(face.X) > 0.5f;
        var list = PanelList(tag);
        var s = from;
        while (s < to - 0.01f)
        {
            var material = Style.Part(d, slot);
            var left = to - s;
            var run = left >= 1f && d.Chance(0.6) ? 1f : left >= 0.5f ? 0.5f : 0.25f;
            var size = height == 1f ? run == 1f ? "1 m" : run == 0.5f ? "0.5x1 m" : "" : run == 1f ? "0.5x1 m" : run == 0.5f ? "0.5 m" : "";
            if (size.Length == 0 || d.Chance(gap))
            {
                s += run;
                continue;
            }
            var name = ConstructParts.Find(Tile(material, size)) != null ? Tile(material, size) : Tile("Plate Steel", size);
            var t = Thick(Def(name));
            var centre = alongZ ? new Vector3(at + face.X * t, bottom + height * 0.5f, s + run * 0.5f) : new Vector3(s + run * 0.5f, bottom + height * 0.5f, at + face.Z * t);
            var fit = alongZ ? new Vector3(0, height * 0.5f, run * 0.5f) : new Vector3(run * 0.5f, height * 0.5f, 0);
            try
            {
                list.Add(Put(name, centre, Orient.Facing(face), 0.09f, fit: fit));
            }
            catch (VehicleKitException)
            {
                // (nothing there to hang it on: a gap, as a deck leaves one)
            }
            s += run;
        }
        return this;
    }

    /// <summary>A plate welded on off the grid (a blueprint's freeform step): its centre at <paramref name="at"/>, its face towards <paramref name="face"/>, tilted up to the style's limit. For patches over holes and crooked doors.</summary>
    public VPart Crooked(string tag, string part, VPart on, Vector3 at, Vector3 face, Dice d)
    {
        var tilt = Style.Limit("tiltDegrees");
        var p = Weld(part, on, at, Orient.Facing(face), d.Between(-tilt, tilt), d.Between(-tilt, tilt), d.Between(-tilt, tilt));
        PanelList(tag).Add(p);
        return p;
    }

    /// <summary>A free socket of <paramref name="p"/> that faces <paramref name="dir"/>, or null.</summary>
    public string? FreeSocket(VPart p, Vector3 dir, Dice d)
    {
        var picks = new List<string>();
        var def = C.DefOf(p.Index);
        for (var s = 0; s < def.Sockets.Length; s++)
        {
            if (C.SocketUsed(p.Index, s))
                continue;
            C.SocketAt(p.Index, s, out _, out var face);
            if (Vector3.Dot(ConstructParts.DirVector(face), dir) > 0.9f)
                picks.Add(def.Sockets[s].Name);
        }
        return picks.Count == 0 ? null : d.Pick(picks);
    }

    /// <summary>Up to <paramref name="count"/> of the style's <paramref name="slot"/> parts (they stand on a "base" socket) on free sockets of <paramref name="on"/> that face <paramref name="dir"/>.</summary>
    public Vehicle Dress(string slot, IEnumerable<VPart> on, Vector3 dir, int count, Dice d)
    {
        var left = count;
        foreach (var p in d.Shuffled(on))
        {
            if (left <= 0)
                break;
            if (FreeSocket(p, dir, d) is not { } socket)
                continue;
            try
            {
                Onto(p, socket, Style.Part(d, slot), "base", 0);
                left--;
            }
            catch (VehicleKitException)
            {
                // (no room there: the next panel)
            }
        }
        return this;
    }

    /// <summary>
    /// Cargo: a Cargo Volume already fitted (<paramref name="volume"/>) set to start frozen with its box at <paramref name="centre"/>, and <paramref name="count"/> loose parts of the style's
    /// <paramref name="slot"/> standing at the first of <paramref name="spots"/> (their feet). Each stays a body of its own: shoot the volume, or lift a crate out, and it's loot.
    /// </summary>
    public Vehicle CargoBay(VPart volume, Vector3 centre, Vector3 size, string slot, int count, params Vector3[] spots)
    {
        var d = Dice.Fork("cargo " + volume.Step);
        var at = CentreOf(volume);
        Config(volume, ("startOn", true), ("sizeX", (double)size.X), ("sizeY", (double)size.Y), ("sizeZ", (double)size.Z),
            ("atX", Math.Round(centre.X - at.X, 1)), ("atY", Math.Round(centre.Y - at.Y, 1)), ("atZ", Math.Round(centre.Z - at.Z, 1)));
        foreach (var spot in spots.Take(count))
        {
            var part = Style.Part(d, slot);
            Cargo(part, spot + new Vector3(0, -Def(part).BoundsMin.Y, 0));
        }
        return this;
    }

    /// <summary>A bumper on the nose, two posts on the nose rails at <paramref name="z"/> rising to <paramref name="top"/>, a bar across them (off centre by the grid's half step, a side the dice pick) and the style's spikes on the bar.</summary>
    public Vehicle RamBars(float top, float z, Dice d)
    {
        var bumper = Onto(NoseL[^1], "-z", "Bumper", "back0", 0);
        var ram = PanelList("ram");
        ram.Add(bumper);
        var posts = new List<VPart>();
        foreach (var s in new[] { -1f, 1f })
            posts.Add(Put("Steel Tube 0.5 m", new Vector3(s * RailX, top, z), Orient.Along(Vector3.UnitY), 0.03f));
        var bar = Put("Steel Tube 1 m", new Vector3(d.Chance(0.5) ? -0.125f : 0.125f, top + 0.025f, z - 0.115f), Orient.Along(Vector3.UnitX), 0.12f, posts[0]);
        ram.Add(bar);
        return Dress("spike", [bar], -Vector3.UnitZ, d.Int(2, 3), d);
    }

    /// <summary>Paints every part of a list from a palette role.</summary>
    public Vehicle PaintAll(IEnumerable<VPart> parts, string role, Dice d)
    {
        foreach (var p in parts)
            Paint(p, Style.Colour(d, role));
        return this;
    }
}
