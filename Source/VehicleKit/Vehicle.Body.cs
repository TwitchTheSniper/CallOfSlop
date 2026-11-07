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
    public Vehicle Deck(string tag, float y, Rect r, string slot, Rect[]? holes = null, int smallest = 1, Rect[]? whole = null)
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
        var wanted = (whole ?? []).ToList();
        foreach (var h in wanted)
            for (var i = 0; i < nx; i++)
            for (var j = 0; j < nz; j++)
                if (h.Contains(r.X0 + (i + 0.5f) * Cell, r.Z0 + (j + 0.5f) * Cell))
                    used[i, j] = true;
        if (Worn != null)
            WornEdges(tag, used, nx, nz); // (kit 3's wear: an open deck's edge rusted out, from the wear layer's own fork)
        for (var progress = true; progress;) // (a plate far from the frame fits once its neighbour is on)
        {
            progress = false;
            for (var k = wanted.Count - 1; k >= 0; k--)
            {
                var h = wanted[k];
                try
                {
                    Wholes[(tag, h)] = Put("Plate Steel 1 m", new Vector3((h.X0 + h.X1) * 0.5f, y + 0.031f, (h.Z0 + h.Z1) * 0.5f), Orient.Facing(Vector3.UnitY), 0.03f);
                    list.Add(Wholes[(tag, h)]);
                    wanted.RemoveAt(k);
                    progress = true;
                }
                catch (VehicleKitException)
                {
                    // (nothing beside it yet)
                }
            }
            foreach (var (i, j) in cells)
                if (!used[i, j] && Plate(y, r, used, nx, nz, i, j, Style.Part(d, slot), d, list, smallest))
                    progress = true;
        }
        if (wanted.Count > 0)
            throw new VehicleKitException($"{Name}: the {tag} deck: nothing beside the whole plate at {wanted[0]}");
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

    /// <summary>The whole 1 m plates decks laid, by tag and rectangle.</summary>
    public Dictionary<(string Tag, Rect R), VPart> Wholes { get; } = new();

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
    /// <paramref name="height"/> tall (0.5, 1, or 2: only 1 m wide plates come that tall, so a run that isn't whole metres leaves a slit). <paramref name="skip"/>: a stretch left open for a door. <paramref name="gap"/>: the share of plates missing. Runs along z when the face is ±x, along x when it is ±z.
    /// </summary>
    public Vehicle Wall(string tag, Vector3 face, float at, float from, float to, float bottom, float height, string slot, double gap = 0, (float From, float To)? skip = null, bool strict = false)
    {
        var d = Dice.Fork("wall " + tag);
        var alongZ = MathF.Abs(face.X) > 0.5f;
        var list = PanelList(tag);
        var s = from;
        while (s < to - 0.01f)
        {
            var material = Style.Part(d, slot);
            if (skip is { } door && s >= door.From - 0.01f && s < door.To - 0.01f)
            {
                s = door.To;
                continue;
            }
            var left = (skip is { } next && s < next.From ? MathF.Min(to, next.From) : to) - s;
            var run = left >= 1f && (height == 2f || d.Chance(0.6)) ? 1f : left >= 0.5f ? 0.5f : 0.25f;
            var size = height == 2f ? run == 1f ? "1x2 m" : "" : height == 1f ? run == 1f ? "1 m" : run == 0.5f ? "0.5x1 m" : "" : run == 1f ? "0.5x1 m" : run == 0.5f ? "0.5 m" : "";
            if (size.Length == 0 || d.Chance(gap))
            {
                s += run;
                continue;
            }
            var jamb = skip is { } opening && (MathF.Abs(s + run - opening.From) < 0.01f || MathF.Abs(s - opening.To) < 0.01f); // (a door sits flush with a plate, whatever else the wall is)
            var name = !jamb && ConstructParts.Find(Tile(material, size)) != null ? Tile(material, size) : Tile("Plate Steel", size);
            var t = Thick(Def(name));
            var centre = alongZ ? new Vector3(at + face.X * t, bottom + height * 0.5f, s + run * 0.5f) : new Vector3(s + run * 0.5f, bottom + height * 0.5f, at + face.Z * t);
            var fit = alongZ ? new Vector3(0, height * 0.5f, run * 0.5f) : new Vector3(run * 0.5f, height * 0.5f, 0);
            try
            {
                list.Add(Put(name, centre, Orient.Facing(face), 0.09f, fit: fit));
            }
            catch (VehicleKitException) when (!strict)
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
        _welded.Add(at);
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

    /// <summary>Up to <paramref name="count"/> of the style's <paramref name="slot"/> parts (they stand on a "base" socket) on free sockets of <paramref name="on"/> that face <paramref name="dir"/>, never where one would z-fight with what is already there.</summary>
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
                Onto(p, socket, Style.Part(d, slot), "base", 0, clear: true);
                left--;
            }
            catch (VehicleKitException)
            {
                // (no room there, or a face on one already there, such as an engine's rocker cover poking up under a hood's top socket: the next panel)
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

    /// <summary>
    /// The cab behind an engine at z -1.425: a seat sill, the driver on the left, sometimes a passenger, the style's cab shell, a floor on the rails.
    /// <paramref name="shell"/>: the cab part a shape grammar chose (null: the style's "cab" slot rolls one, as kit 1 always did).
    /// </summary>
    public Vehicle OpenCab(float edge = 0.875f, string? shell = null)
    {
        const float sill = 0.375f;
        Sill(-0.175f, 0.5f);
        Driver(new Vector3(-0.5f, sill, -0.175f));
        if (Dice.Fork("crew").Chance(Style.Limit("thirdSeat")))
            Passenger(new Vector3(0.5f, sill, -0.175f));
        var cab = shell ?? Style.Part(Dice.Fork("cab"), Slot("cab"));
        if (cab != "none")
        {
            var feet = cab == "Windscreen Frame" ? -0.675f : 0.075f;
            var by = Array.Exists(Def(cab).Sockets, s => s.Name == "footL") ? "footL" : "footL0";
            PanelList("cabshell").Add(Stand(cab, new Vector3(-RailX, Half, feet), Orient.Forward, null, by, 0.06f));
        }
        return Deck("cab", Half, new Rect(-edge, edge, -0.8f, -0.3f), "deck");
    }

    /// <summary>
    /// The hood: one of the style's "hood" parts on the engine ("plates": tiled from sheets), its grille or plow, fenders over the front wheels.
    /// <paramref name="part"/>, <paramref name="noseOf"/>: what a shape grammar's nose module chose (null: the style's slots roll them, as kit 1 always did).
    /// </summary>
    public Vehicle Hood(string? part = null, string[]? noseOf = null)
    {
        if (Panels.GetValueOrDefault("cabshell")?.Any(p => p.Name == "Cab Over Shell") == true)
            return this; // (the cab is its own nose)
        var d = Dice.Fork("hood");
        var kind = part ?? Style.Part(d, "hood");
        if (kind == "plates")
            return PlatedHood();
        var on = HoodOn ?? Engine!;
        var hood = Stand(kind, SocketPoint(on, "+y"), Orient.Forward, on, "engine");
        PanelList("hood").Add(hood);
        var nose = noseOf != null ? FactionStyle.Noted("nose", d.Pick(noseOf)) : Style.Part(d, "nose");
        if (nose != "none")
            PanelList("hoodsides").Add(Onto(hood, "front", nose, "back", 0));
        var fender = Style.Part(d, "fender");
        if (fender != "none" && !NoFenders)
            foreach (var (socket, s) in new[] { ("+x0", 1f), ("-x0", -1f) })
                if (Array.Exists(hood.Def.Sockets, x => x.Name == socket))
                    try
                    {
                        PanelList("hoodsides").Add(Stand(fender, SocketPoint(hood, socket), new Orient(Vector3.UnitX, new Vector3(s, 0, 0), Vector3.UnitY, Vector3.UnitY), hood, "mount"));
                    }
                    catch (VehicleKitException)
                    {
                        // (a wide wheel or a wing in the way: bare tyre)
                    }
        return this;
    }

    private Vehicle PlatedHood()
    {
        var y = SocketPoint(HoodOn ?? Engine!, "+y").Y;
        Deck("hood", y, new Rect(-0.625f, 0.625f, -2.05f, -0.8f), "sheet", smallest: 2); // (no quarter plates: their bolts land on an engine's details, and a slot in the hood is a vent)
        foreach (var s in new[] { -1f, 1f })
            Wall("hoodsides", new Vector3(s, 0, 0), s * 0.625f, -2.05f, -0.8f, y - 0.42f, 0.5f, "sheet", 0.3);
        return Wall("grille", -Vector3.UnitZ, -2.05f, -0.625f, 0.625f, y - 0.42f, 0.5f, "grille");
    }

    /// <summary>
    /// What the check calls stability, on the vehicle as built so far: half the wheels' track over the centre of mass's height above the lowest tyre's foot
    /// (VehicleChecks; the check measures it on the spawned body, a little lower on its springs).
    /// </summary>
    public float Stability()
    {
        C.Rebuild();
        float left = 0, right = 0, ground = float.MaxValue;
        for (var k = 0; k < C.WheelCount; k++)
        {
            ref var wh = ref C.Wheels[k];
            (left, right) = (MathF.Min(left, wh.Centre.X), MathF.Max(right, wh.Centre.X));
            ground = MathF.Min(ground, wh.Centre.Y - wh.Travel * 0.5f - wh.Radius);
        }
        return C.WheelCount == 0 ? 0 : (right - left) * 0.5f / MathF.Max(0.1f, C.Com.Y - ground);
    }

    /// <summary>How much of its bounding box the body fills from the side and from above, as the check's silhouette rule measures it (<see cref="VehiclePlan.Outline"/>).</summary>
    public (float Side, float Top) Fill()
    {
        C.Rebuild();
        var (side, top, _) = VehiclePlan.Outline(C);
        return (side, top);
    }

    /// <summary>Every part's name, for <see cref="VehicleVariety"/>.</summary>
    public IEnumerable<string> PartNames() => Enumerable.Range(0, C.PartCount).Select(i => C.DefOf(i).Name);

    /// <summary>The whole vehicle's bounding size.</summary>
    public Vector3 Size()
    {
        var (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        for (var i = 0; i < C.PartCount; i++)
        {
            C.PartBounds(i, out var a, out var b);
            (min, max) = (Vector3.Min(min, a), Vector3.Max(max, b));
        }
        return max - min;
    }

    /// <summary>Pickup Bed Side on the bed's edge, its arch over the wheel, near <paramref name="at"/>.</summary>
    public void Sides(Vector3 at, float side) =>
        PanelList("sides").Add(Put("Pickup Bed Side", at, new Orient(Vector3.UnitX, new Vector3(side, 0, 0), Vector3.UnitY, Vector3.UnitY), 0.35f));

    /// <summary>Bed Rails along an edge at x from z0 to z1, each a metre or two of tube on posts.</summary>
    public void TubeRails(float x, float z0, float z1, float y, float side)
    {
        for (var z = z0; z + 0.9f < z1; z += 1.25f)
            try
            {
                PanelList("sides").Add(Put("Bed Rail 1 m", new Vector3(x + side * 0.05f, y + 0.2f, z + 0.5f), new Orient(Vector3.UnitX, new Vector3(side, 0, 0), Vector3.UnitY, Vector3.UnitY), 0.3f));
            }
            catch (VehicleKitException)
            {
                // (no edge socket there)
            }
    }

    /// <summary>The fuel drum standing on a bolt deck near <paramref name="at"/> (its foot).</summary>
    public VPart DrumOn(Vector3 at) => Put("Fuel Drum", at + new Vector3(0, 0.44f, 0), Orient.Forward, 0.3f, null, "-y", onTop: true);

    /// <summary>Loose crates on a bed: a Cargo Volume out of sight on <paramref name="mount"/>'s back, a box round the crates.</summary>
    public void Crates(VPart mount, Vector3 centre, Vector3 size, float floor, Dice d)
    {
        var volume = Put("Cargo Volume", CentreOf(mount) + new Vector3(0, 0, 0.225f), Orient.Forward, 0.12f, mount, "-z");
        CargoBay(volume, centre + new Vector3(0, 0.5f, 0), size, "loot", d.Int(1, 2), new Vector3(centre.X, floor + 0.08f, centre.Z - 0.3f), new Vector3(centre.X, floor + 0.08f, centre.Z + 0.3f));
    }

    /// <summary>A rail's socket row nearest z, front rails or tail.</summary>
    public float RailZ(float z)
    {
        var grid = z > RearAxleZ ? TailGrid : FrontAxleZ + Half * 2;
        return grid + MathF.Round((z - grid) / 0.25f) * 0.25f;
    }

    /// <summary>
    /// What a floor at 0.625 stands on from z0 to z1. "stilts": a two-layer sill each metre, in the open. "skirted": the same behind leaning skirts from the rails out to the floor's edge.
    /// "tub": a closed Chassis Tub on the rails, no stilts at all. Clear of the differential and the gaps; <paramref name="gaps"/>: z where a room's end wall drops past the floor.
    /// </summary>
    public Vehicle Under(string kind, float z0, float z1, params float[] gaps)
    {
        bool Clear(float z, float half) => z - half > RearAxleZ + 0.3f || z + half < RearAxleZ - 0.32f;
        bool NoGap(float a, float b) => !gaps.Any(g => g > a - 0.2f && g < b + 0.2f);
        if (kind == "tub")
            for (var z = RailZ(z0 + 0.25f); z < z1 - 0.2f;)
            {
                var (name, len) = z + 1.875f < z1 && Clear(z + 0.875f, 1f) && NoGap(z, z + 2f) ? ("Chassis Tub 2 m", 2f) : ("Chassis Tub 1 m", 1f);
                if (z + len - 0.125f < z1 + 0.05f && Clear(z + len / 2 - 0.125f, len / 2) && NoGap(z - 0.125f, z + len - 0.125f))
                    PanelList("under").Add(Stand(name, new Vector3(-RailX, Half, z), Orient.Forward, null, "footL0", 0.03f));
                else
                    len = 0.25f;
                z = RailZ(z + len);
            }
        else
            for (var z = RailZ(z0 + 0.125f); z < z1; z = RailZ(z + 1f))
                if (Clear(z, 0.13f) && NoGap(z, z))
                    try
                    {
                        Sill(z, layers: FloorY < 0.5f ? 1 : 2);
                    }
                    catch (VehicleKitException)
                    {
                        // (no rail there)
                    }
        Skirted = kind == "skirted";
        return this;
    }

    /// <summary>The floor's height over the rails: a recipe's <see cref="Gear.Floor"/> when its wheels stand clear of the body.</summary>
    public float FloorY { get; set; } = 0.625f;

    /// <summary>The last <see cref="Under"/> asked for a skirt: the next rooms put one under their floors.</summary>
    private bool Skirted;

    /// <summary>A sloped hull side under a floor's edge, from z0 to z1, clear of the rear wheels.</summary>
    public Vehicle Skirts(Rect r)
    {
        foreach (var s in new[] { -1f, 1f })
            for (var z = r.Z0 + 0.5f; z < r.Z1; z += 1f)
                if (MathF.Abs(z - RearAxleZ) > 1.1f)
                    try
                    {
                        PanelList("skirts").Add(Put("Hull Skirt 1 m", new Vector3((s > 0 ? r.X1 : r.X0) - s * 0.3f, 0.35f, z), new Orient(Vector3.UnitZ, new Vector3(s, 0, 0), Vector3.UnitY, Vector3.UnitY), 0.25f));
                    }
                    catch (VehicleKitException)
                    {
                        // (a plate with no socket on that bay, or something hanging there: an open bay)
                    }
        return this;
    }

    /// <summary>The last dressing, shared by every shape: ram bars, crooked patches over the <paramref name="walls"/> tags, spikes, trophies and paint, each from its own fork of the seed.</summary>
    public Vehicle Trim(params string[] walls)
    {
        if (Panels.ContainsKey("grille")) // (a body hood has its own nose)
            RamBars(0.375f, -2.175f, Dice.Fork("ram"));
        var d = Dice.Fork("crooked");
        var panels = walls.Where(Panels.ContainsKey).SelectMany(t => Panels[t]).ToList();
        foreach (var w in d.Shuffled(panels).Take(d.Int(2, (int)Style.Limit("crookedPlates"))))
        {
            var c = CentreOf(w);
            var face = MathF.Abs(c.X) > 0.6f ? new Vector3(MathF.Sign(c.X), 0, 0) : c.Z > 0 ? Vector3.UnitZ : -Vector3.UnitZ;
            Crooked("patches", Style.Part(d, "patch"), w, c + face * 0.06f + new Vector3(0, (float)d.Between(-0.08, 0.08), 0), face, d);
        }
        if (Style.Parts.ContainsKey("piper"))
            Piper(); // (before the trophies take the hood's sockets)
        d = Dice.Fork("show");
        var sides = Panels.GetValueOrDefault("sides") ?? [];
        Dress("spike", sides.Where(p => CentreOf(p).X < 0), -Vector3.UnitX, d.Int(1, 3), d);
        Dress("spike", sides.Where(p => CentreOf(p).X > 0), Vector3.UnitX, d.Int(1, 3), d);
        Dress("trophy", Panels.GetValueOrDefault("hood") ?? [], Vector3.UnitY, d.Int(1, 2), d);
        Dress("trophy", Panels.GetValueOrDefault("cabshell") ?? [], Vector3.UnitY, 1, d);
        Dress("trophy", Panels.GetValueOrDefault("headboard") ?? [], -Vector3.UnitZ, d.Int(1, 2), d);
        if (Style.Parts.ContainsKey("mast"))
            Kit();
        d = Dice.Fork("paint");
        var frame = Style.Colour(d, "frame"); // (one colour: stacked beams' bands line up, and paint in one look doesn't fight)
        foreach (var p in Parts.Where(p => p.Name.StartsWith(Beam, StringComparison.Ordinal)))
            Paint(p, frame);
        foreach (var (tag, list) in Panels)
            if (tag is not "furniture")
                PaintAll(list.Where(p => p.Def.Slots.Length == 0), tag is "hood" or "hoodsides" or "headboard" or "tail" ? "body" : "rust", d);
        var war = Style.Colour(d, "war");
        foreach (var p in d.Shuffled((Panels.GetValueOrDefault("hood") ?? []).Concat(Panels.GetValueOrDefault("tail") ?? []).Concat(Panels.GetValueOrDefault("hoodsides") ?? [])).Take(d.Int(2, 4)))
            Paint(p, war);
        return this;
    }

    /// <summary>
    /// A style that has "mast" and "gear" slots dresses more than the flanks and the hood (the IT Department's dishes and masts): masts on
    /// the rooms' roofs, spikes down the rooms' outer walls, and gear standing on open beds and porches. Its own fork, so the rest of a
    /// seed's vehicle is as it was, and a style without the slots (bandits) never rolls it.
    /// </summary>
    private void Kit()
    {
        var d = Dice.Fork("kit");
        Dress("mast", Rooms.SelectMany(r => r.Roof), Vector3.UnitY, d.Int(1, 3), d);
        var walls = Panels.Where(kv => kv.Key.EndsWith("walls", StringComparison.Ordinal)).SelectMany(kv => kv.Value).ToList();
        Dress("spike", walls.Where(p => CentreOf(p).X < -0.6f), -Vector3.UnitX, d.Int(1, 2), d);
        Dress("spike", walls.Where(p => CentreOf(p).X > 0.6f), Vector3.UnitX, d.Int(1, 2), d);
        Dress("gear", (Panels.GetValueOrDefault("bed") ?? []).Concat(Panels.GetValueOrDefault("porch") ?? []), Vector3.UnitY, d.Int(1, 3), d);
        Dress("mast", Panels.GetValueOrDefault("cabshell") ?? [], Vector3.UnitY, 1, d);
    }

    /// <summary>
    /// A style with a "piper" slot (the Clan MacSlop's Pipe Box: docs/sandbox/MEDIA.md, "Pipe Boxes") stands exactly one on every vehicle, up
    /// front where it can: the hood, then the cab, a room's roof, the bed, a porch, the headboard. Its own fork, so nothing else of a seed moves.
    /// A shape with nowhere to stand one throws, so a sweep skips that seed.
    /// </summary>
    private void Piper()
    {
        if (!Parts.Any(p => p.Def.Role == PartRole.Seat))
            return; // (a trailer: nobody drives it, so its pipes would never play)
        var d = Dice.Fork("piper");
        var before = Parts.Count;
        var sides = Panels.GetValueOrDefault("sides") ?? [];
        var places = new (IEnumerable<VPart> On, Vector3 Dir)[]
        {
            (Panels.GetValueOrDefault("hood") ?? [], Vector3.UnitY),
            (Panels.GetValueOrDefault("cabshell") ?? [], Vector3.UnitY),
            (Rooms.SelectMany(r => r.Roof), Vector3.UnitY),
            (Panels.GetValueOrDefault("bed") ?? [], Vector3.UnitY),
            (Panels.GetValueOrDefault("porch") ?? [], Vector3.UnitY),
            (Panels.GetValueOrDefault("headboard") ?? [], -Vector3.UnitZ),
            (sides.Where(p => CentreOf(p).X < 0), -Vector3.UnitX),
            (sides.Where(p => CentreOf(p).X > 0), Vector3.UnitX),
        };
        foreach (var (on, dir) in places)
        {
            Dress("piper", on, dir, 1, d);
            if (Parts.Count > before)
                return;
        }
        throw new VehicleKitException("nowhere to stand the Pipe Box (the \"piper\" slot)");
    }

    /// <summary>Paints every part of a list from a palette role.</summary>
    public Vehicle PaintAll(IEnumerable<VPart> parts, string role, Dice d)
    {
        foreach (var p in parts)
            Paint(p, Style.Colour(d, role));
        return this;
    }
}
