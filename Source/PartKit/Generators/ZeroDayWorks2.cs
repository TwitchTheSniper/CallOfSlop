using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CallOfSlop.EngineKit;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Zero Day Works II (Sean's layout from the Factory Lab artifact, 2026-11-25): the new Zero Day factory floor as a real blueprint, every
/// piece where his layout puts it, with no walls, roof or shell. The layout is data, <see cref="Layout"/> (the lab's export, copied
/// unchanged: metres, y up, +z south in the lab); this lays it through the game's own rules (<see cref="BlueprintScript"/>), so a piece the
/// rules refuse is named in the log (a "[zeroday_works2]" line) and left out, and nothing else moves. Reads the part files as the game
/// loads them: run after the part generators. The old Zero Day Works (ZeroDayWorks*.cs) is untouched.
/// <para>Lab to site: x stays, z turns round (the lab's +z south is the site's -z). A part item's rot is its flow by the lab's chain table
/// (0 south, 1 west, 2 north, 3 east: the lab's portsOf turns 1 and 3 the other way, but its runs and lines flow toward the main station
/// with these). A lab machine's bed cells and Core cell are where the lab draws them (three.js turns its local frame by rot).</para>
/// </summary>
public sealed partial class ZeroDayWorks2 : IPartGenerator
{
    public string Name => "zeroday_works2";

    public const string File = "Constructs/Blueprints/ZeroDayWorks2.json";
    public const string Layout = "Constructs/Factories/Layouts/ZeroDayWorks2.json";

    public const string GensetFile = "Constructs/Blueprints/ZeroDayWorks2Genset.json";
    public const string GensetName = "Zero Day Works II Genset";
    public const string Terminal = "Heavy Power Terminal";

    /// <summary>The works' sets: the Engine Kit's large militia set (800 kW asked, its head's 714 kW into the circuit).</summary>
    private static EngineRequest GensetAsk() => EngineKitGenset.Request(new EngineRequest { PowerKw = EngineKitCli.Presets.First(p => p.Name == "large").Kw, Fuel = "petrol", Tier = 1, Layout = EngineLayout.Auto, Boost = EngineBoost.Auto, Size = -1, Seed = 0 });

    /// <summary>
    /// Two files: the genset, a blueprint of its own (a body runs one engine, so each set is a body: Sean, 2026-11-25) on its skid with a
    /// Heavy Power Terminal, and the works, which spawns four of them beside it ("beside"), each on a heavy power cable to a terminal on the floor.
    /// </summary>
    public void Make(PartOutput o)
    {
        var set = EngineKitGenset.Make(GensetAsk(), "militia", GensetName, Terminal);
        if (!set.Built)
            throw new VehicleKitException($"Zero Day Works II's genset: {set.Note}");
        o.File(GensetFile, set.Text);
        o.File(File, Script.Build(o.Read(Layout)).Text());
    }

    private sealed partial class Script : BlueprintScript
    {
        private Script() : base("Zero Day Works II")
        {
        }

        public const float FloorY = 0.3f; // the Works Floor Slabs' tops
        public const float Slab = 8f;
        // The slabs' edges (where the floor has no socket) on x = 4 and z = 6 (mod 8): the residues the fewest of the layout's pieces stand on.
        public const float EdgeX = 4f, EdgeZ = 6f;

        public const string Floor = "#8c8576", Steel = "#4a5058", Line = "#3d7ab8", Stock = "#c9a24a", Station = "#2f8f8a", Oven = "#a8365e",
            Refining = "#d17a22", Power = "#b5452b", Crane = "#f0c21a";

        private readonly Dictionary<(int I, int J), VPart> _slabs = new();
        private readonly Dictionary<(int, int), List<VPart>> _near = new(); // (placed parts by 4 m bucket of their footprint's middle)
        private readonly Dictionary<VPart, Vector2> _at = new(); // (each placed piece's footprint middle)
        private readonly List<string> _notes = new();
        private int _welded, _chained, _missed;

        private static Vector3 V(float x, float y, float z) => new(x, y, z);

        private static Orient Facing(Vector2 d) => new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, new Vector3(d.X, 0, d.Y));

        private static Orient XFacing(Vector2 d) => new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitX, new Vector3(d.X, 0, d.Y));

        /// <summary>A part item's flow on the site for its lab rot (the lab's chain table: 0 +z, 1 -x, 2 -z, 3 +x in the lab; z turned round).</summary>
        private static Vector2 Flow(int rot) => (((rot % 4) + 4) % 4) switch { 0 => new(0, -1), 1 => new(-1, 0), 2 => new(0, 1), _ => new(1, 0) };

        /// <summary>A lab machine's local point (x across, z along) on the site: three.js turns it by rot quarter turns about y, then z turns round.</summary>
        private static Vector2 Local(float x0, float z0, int rot, float lx, float lz)
        {
            var t = rot * MathF.PI / 2;
            float c = MathF.Round(MathF.Cos(t)), s = MathF.Round(MathF.Sin(t));
            return new Vector2(x0 + lx * c + lz * s, -(z0 - lx * s + lz * c));
        }

        private void Note(string line)
        {
            _notes.Add(line);
            Console.WriteLine($"[zeroday_works2] {line}");
        }

        public static Script Build(string layoutText)
        {
            var s = new Script
            {
                Description = "Zero Day Works II: Sean's Zero Day factory floor from the Factory Lab (2026-11-25), every machine, roller run, pad, store, refiner, rover dock and genset where his layout puts them, " +
                              "on Works Floor Slabs with no walls or roof. The main Building Station in the middle, the wide and narrow lines either side, materials north and south. Set it on flat ground on a planet.",
                Group = "Factories",
            };
            using var doc = JsonDocument.Parse(layoutText);
            var items = doc.RootElement.GetProperty("blankFactory").GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
            s.Lay(items);
            s.LayLinks();
            if (s.Live.LinkCount > Construct.MaxLinks * 3 / 4 || s.Live.PartCount > Construct.MaxParts * 3 / 4)
                throw new VehicleKitException($"Zero Day Works II: {s.Live.PartCount} parts and {s.Live.LinkCount} links, past three quarters of a body's caps ({Construct.MaxParts}, {Construct.MaxLinks})");
            s.Note($"{s.Live.PartCount} parts, {s.Live.LinkCount} links; {s._chained} welded on a neighbour's side (on a slab's edge), {s._welded} welded freeform, {s._missed} not placed");
            return s;
        }

        private static string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        private static float Num(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : 0;
        private static int Int(JsonElement e, string k) => (int)MathF.Round(Num(e, k));
        private static bool Flag(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;

        // ---------------------------------------------------------------- the floor

        private (int, int) SlabOf(float x, float z) => ((int)MathF.Floor((x - EdgeX) / Slab), (int)MathF.Floor((z - EdgeZ) / Slab));

        private VPart Tile(float x, float z)
        {
            if (_slabs.TryGetValue(SlabOf(x, z), out var t))
                return t;
            throw new VehicleKitException($"no floor at ({x}, {z})");
        }

        private void LayFloor(float x0, float x1, float z0, float z1)
        {
            var (i0, j0) = SlabOf(x0, z0);
            var (i1, j1) = SlabOf(x1, z1);
            for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                {
                    var at = V(EdgeX + Slab * (i + 0.5f), 0.15f, EdgeZ + Slab * (j + 0.5f));
                    var t = _slabs.Count == 0 ? Root("Works Floor Slab 8 m", at, Up) : Put("Works Floor Slab 8 m", at, Up, on: i > i0 ? _slabs[(i - 1, j)] : _slabs[(i, j - 1)]);
                    _slabs[(i, j)] = t;
                    Paint(t, Floor);
                    Finish(t, "concrete");
                }
            Note($"floor: {(i1 - i0 + 1) * (j1 - j0 + 1)} slabs, x {EdgeX + Slab * i0:0} to {EdgeX + Slab * (i1 + 1):0}, z {EdgeZ + Slab * j0:0} to {EdgeZ + Slab * (j1 + 1):0}");
        }

        private static readonly Orient Up = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ);

        // ---------------------------------------------------------------- placing one piece

        private void Keep(VPart p, Vector2 at, string paint)
        {
            Paint(p, paint);
            _at[p] = at;
            var k = ((int)MathF.Floor(at.X / 4), (int)MathF.Floor(at.Y / 4));
            if (!_near.TryGetValue(k, out var l))
                _near[k] = l = new List<VPart>();
            l.Add(p);
        }

        private IEnumerable<VPart> Near(Vector2 at, float r)
        {
            int kx0 = (int)MathF.Floor((at.X - r) / 4), kx1 = (int)MathF.Floor((at.X + r) / 4), kz0 = (int)MathF.Floor((at.Y - r) / 4), kz1 = (int)MathF.Floor((at.Y + r) / 4);
            for (var a = kx0; a <= kx1; a++)
                for (var b = kz0; b <= kz1; b++)
                    if (_near.TryGetValue((a, b), out var l))
                        foreach (var p in l)
                            yield return p;
        }

        /// <summary>
        /// A part with its footprint's middle at <paramref name="at"/> (own <paramref name="foot"/> there, its socket <paramref name="by"/> on
        /// the floor), turned <paramref name="o"/>: stood on the floor; on a slab's edge (no socket), welded on a neighbour's side socket that
        /// puts it there; else welded freeform. Null (and a note) when none takes it.
        /// </summary>
        private VPart? Piece(string part, Vector2 at, Orient o, string paint, string by = "bottom", Vector2 foot = default, bool chainFirst = false)
        {
            var def = Def(part);
            var turn = FirstTurn(o);
            var sock = Array.Find(def.Sockets, s => s.Name == by);
            if (sock.Name == null)
                throw new VehicleKitException($"a {part} has no socket \"{by}\"");
            var off = Rot24.Apply(turn, sock.At - new Vector3(foot.X, 0, foot.Y));
            var stand = V(at.X + off.X, FloorY, at.Y + off.Z);
            string? why = null;
            if (chainFirst && Chain(part, at, turn, foot) is { } c0)
            {
                Keep(c0, at, paint);
                return c0;
            }
            if (_slabs.ContainsKey(SlabOf(stand.X, stand.Z)))
                try
                {
                    var p = Stand(part, stand, o, Tile(stand.X, stand.Z), by);
                    Keep(p, at, paint);
                    return p;
                }
                catch (VehicleKitException ex)
                {
                    why = ex.Message;
                }
            if (!chainFirst && Chain(part, at, turn, foot) is { } c)
            {
                Keep(c, at, paint);
                return c;
            }
            if (why != null && why.Contains("in the way"))
            {
                Note($"a {part} at ({at.X:0.##}, {at.Y:0.##}) not placed: {why}");
                _missed++;
                return null;
            }
            try
            {
                var mid = Rot24.Apply(turn, (def.BoundsMin + def.BoundsMax) * 0.5f - new Vector3(foot.X, 0, foot.Y));
                var centre = V(at.X + mid.X, FloorY + 0.003f + ((def.BoundsMax.Y - def.BoundsMin.Y) * 0.5f), at.Y + mid.Z);
                var p = Weld(part, Tile(centre.X, centre.Z), centre, o, 0, 0, 0);
                Welded(p, centre, turn);
                _welded++;
                Keep(p, at, paint);
                return p;
            }
            catch (VehicleKitException ex)
            {
                Note($"a {part} at ({at.X:0.##}, {at.Y:0.##}) not placed: {why ?? ""} / {ex.Message}");
                _missed++;
                return null;
            }
        }

        /// <summary>
        /// A part welded on a placed neighbour's side socket so its footprint's middle lands on <paramref name="at"/> (within 0.2 m) at turn
        /// <paramref name="turn"/>, its socket heights meeting. Null when no neighbour has one.
        /// </summary>
        private VPart? Chain(string part, Vector2 at, byte turn, Vector2 foot)
        {
            var def = Def(part);
            var bottom = Array.Find(def.Sockets, s => ConstructParts.DirVector(s.Dir).Y < -0.9f);
            var baseY = bottom.Name != null ? -bottom.At.Y : 0f; // (the new part's origin over the floor top)
            foreach (var n in Near(at, 5f).ToList())
            {
                ref var np = ref Live.Parts[n.Index];
                var nd = n.Def;
                for (var s = 0; s < nd.Sockets.Length; s++)
                {
                    var face = Rot24.Apply(np.Rot, ConstructParts.DirVector(nd.Sockets[s].Dir));
                    if (MathF.Abs(face.Y) > 0.1f)
                        continue;
                    var sw = FromRest(np.Pos + Rot24.Apply(np.Rot, nd.Sockets[s].At));
                    for (var b = 0; b < def.Sockets.Length; b++)
                    {
                        var bs = def.Sockets[b];
                        var bf = Rot24.Apply(turn, ConstructParts.DirVector(bs.Dir));
                        if (Vector3.Dot(bf, face) > -0.9f)
                            continue;
                        var bw = Rot24.Apply(turn, bs.At - new Vector3(foot.X, 0, foot.Y));
                        var want = V(at.X + bw.X, FloorY + baseY + bs.At.Y, at.Y + bw.Z);
                        if (MathF.Abs(want.X - sw.X) > 0.2f || MathF.Abs(want.Z - sw.Z) > 0.2f || MathF.Abs(want.Y - sw.Y) > 0.06f)
                            continue;
                        for (var sp = 0; sp < 4; sp++)
                        {
                            if (TurnOf(n, nd.Sockets[s].Name, part, bs.Name, sp) != turn)
                                continue;
                            try
                            {
                                var p = Onto(n, nd.Sockets[s].Name, part, bs.Name, sp);
                                _chained++;
                                return p;
                            }
                            catch (VehicleKitException)
                            {
                            }
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>A Core welded on a bed cell's side by its own socket <paramref name="by"/>: on the neighbour at <paramref name="bed"/>, facing it.</summary>
        private VPart? CoreOn(string part, string by, VPart? bed, Vector2 core, string paint, bool upright = true)
        {
            if (bed == null)
            {
                Note($"a {part} at ({core.X:0.##}, {core.Y:0.##}): its bed cell isn't there");
                _missed++;
                return null;
            }
            var d = Vector2.Normalize(_at[bed] - core);
            ref var np = ref Live.Parts[bed.Index];
            var nd = bed.Def;
            for (var s = 0; s < nd.Sockets.Length; s++)
            {
                var face = Rot24.Apply(np.Rot, ConstructParts.DirVector(nd.Sockets[s].Dir));
                if (Vector2.Dot(new Vector2(face.X, face.Z), -d) < 0.9f || MathF.Abs(face.Y) > 0.1f)
                    continue;
                for (var sp = 0; sp < 4; sp++)
                {
                    if (TurnOf(bed, nd.Sockets[s].Name, part, by, sp) is not { } t || (upright && Rot24.Apply(t, Vector3.UnitY).Y < 0.99f))
                        continue;
                    try
                    {
                        var p = Onto(bed, nd.Sockets[s].Name, part, by, sp);
                        Keep(p, core, paint);
                        return p;
                    }
                    catch (VehicleKitException)
                    {
                    }
                }
            }
            Note($"a {part} at ({core.X:0.##}, {core.Y:0.##}) won't weld on its bed's side");
            _missed++;
            return null;
        }

        // ---------------------------------------------------------------- the layout

        private static readonly Dictionary<string, string> BedOf = new()
        {
            ["panel-former"] = "Bed Block", ["laser-cutter"] = "Laser Bed Block", ["bake-oven"] = "Oven Bed Block", ["building-station"] = "Bay Block", ["press"] = "Press Bed Block",
        };

        private static readonly Dictionary<string, string> PaintOf = new()
        {
            ["panel-former"] = Line, ["laser-cutter"] = Line, ["bake-oven"] = Oven, ["building-station"] = Station, ["press"] = Steel, ["extruder"] = Line,
        };

        private void Lay(List<JsonElement> items)
        {
            // The floor under everything, a slab past the farthest piece.
            FindShifts(items);
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var it in items)
            {
                var r = Str(it, "t") switch { "lab" => MathF.Max(Num(it, "w"), Num(it, "l")) + 2 + (Str(it, "id") == "extruder" ? ExtruderBack : 0), "rover" => MathF.Max(Num(it, "w"), Num(it, "l")) * 2, _ => Str(it, "name").StartsWith("Kit Genset") ? 6 : 3 };
                var c = new Vector2(Num(it, "x"), -Num(it, "z")) + ShiftOf(it);
                x0 = MathF.Min(x0, c.X - r);
                x1 = MathF.Max(x1, c.X + r);
                z0 = MathF.Min(z0, c.Y - r);
                z1 = MathF.Max(z1, c.Y + r);
            }
            LayFloor(x0 - 1, x1 + 1, z0 - 1, z1 + 1);
            // The big square stations' lay-bys (the two rows of Roller Tiles along each side of the bay, where a line's pieces come to rest)
            // are their pick pads: laid as Pad Tiles in the same cells, Sean 2026-11-26 ("Swap them to Pad Tiles"), so the rovers pick
            // from them (a pad lies wholly past a bay side: ConstructStationPads).
            foreach (var st in items.Where(i => Str(i, "t") == "lab" && Str(i, "id") == "building-station" && Int(i, "w") == Int(i, "l")))
            {
                float half = Int(st, "w"), sx = Num(st, "x"), sz = Num(st, "z");
                foreach (var it in items.Where(i => Str(i, "name") == "Roller Tile" && !Flag(i, "swivel")))
                {
                    float dx = MathF.Abs(Num(it, "x") - sx), dz = MathF.Abs(Num(it, "z") - sz);
                    if (dx < half && dz > half && dz < half + LayBy)
                        _layBy.Add(Round(new Vector2(Num(it, "x"), -Num(it, "z"))));
                }
            }
            // The press lines' Turntable Tiles that butt a Press's bed are laid as Roller Tiles rolling into it: a short run from the
            // turntables into the bed (Sean 2026-11-26, "Lay a short roller run"), since a Turntable Tile passes only onto runs, never
            // into a bed, and the press line's blanks met no way into a press.
            // (only the one in line with the turntable the press line's run comes into: a run of welded tiles is one conveyor from what
            // butts its in end, so a whole column would start at the middle turntable, which nothing feeds)
            var swivels = items.Where(i => Str(i, "name") == "Roller Tile" && Flag(i, "swivel")).Select(i => Round(new Vector2(Num(i, "x"), -Num(i, "z")))).ToHashSet();
            var plain = items.Where(i => Str(i, "name") == "Roller Tile" && !Flag(i, "swivel")).Select(i => Round(new Vector2(Num(i, "x"), -Num(i, "z")))).ToHashSet();
            bool Entry((int X, int Z) s) => swivels.Contains(s) && (plain.Contains((s.X + 2, s.Z)) || plain.Contains((s.X - 2, s.Z)) || plain.Contains((s.X, s.Z + 2)) || plain.Contains((s.X, s.Z - 2)));
            foreach (var pr in items.Where(i => Str(i, "t") == "lab" && Str(i, "id") == "press"))
            {
                float half = Int(pr, "w"), px = Num(pr, "x"), pz = -Num(pr, "z");
                foreach (var it in items.Where(i => Str(i, "name") == "Roller Tile" && Flag(i, "swivel")))
                {
                    float dx = Num(it, "x") - px, dz = -Num(it, "z") - pz;
                    if (MathF.Abs(MathF.Abs(dx) - (half + 1)) < 0.1f && MathF.Abs(dz) < half)
                        _intoPress[Round(new Vector2(Num(it, "x"), -Num(it, "z")))] = new Vector2(-MathF.Sign(dx), 0);
                    else if (MathF.Abs(MathF.Abs(dz) - (half + 1)) < 0.1f && MathF.Abs(dx) < half)
                        _intoPress[Round(new Vector2(Num(it, "x"), -Num(it, "z")))] = new Vector2(0, -MathF.Sign(dz));
                }
            }
            foreach (var (cell, into) in _intoPress.ToList())
                if (!Entry(((int)(cell.Item1 - 2 * into.X), (int)(cell.Item2 - 2 * into.Y))))
                    _intoPress.Remove(cell);
            // The plain Roller Tiles in a press line's row, between its presses and the building station the row ends at (beside the presses'
            // beds and the block Assembler's), roll toward that station: the layout laid them facing the other way, so a pressed part rode
            // away from the station and dropped loose (2026-11-26, Sean: carry pressed parts on to the main station). Only their facing
            // changes; each piece stays where his layout puts it, and pieces roll through the beds on the way (ConstructConveyor.PassesThrough).
            var stationsAt = items.Where(i => Str(i, "t") == "lab" && Str(i, "id") == "building-station").Select(i => (At: new Vector2(Num(i, "x"), -Num(i, "z")), Span: (float)Math.Max(Int(i, "w"), Int(i, "l")))).ToList();
            foreach (var pr in items.Where(i => Str(i, "t") == "lab" && Str(i, "id") == "press"))
            {
                float half = Int(pr, "w"), px = Num(pr, "x"), pz = -Num(pr, "z");
                if (stationsAt.Where(v => MathF.Abs(v.At.Y - pz) < v.Span).OrderBy(v => MathF.Abs(v.At.X - px)).Select(v => (Vector2?)v.At).FirstOrDefault() is not { } st)
                    continue; // (the station the row runs to: the nearest along the row whose side spans it)
                var toward = MathF.Sign(st.X - px);
                foreach (var it in items.Where(i => Str(i, "name") == "Roller Tile" && !Flag(i, "swivel")))
                {
                    float tx = Num(it, "x"), tz = -Num(it, "z");
                    var cell = Round(new Vector2(tx, tz));
                    if (MathF.Abs(tz - pz) < half && (tx - px) * toward > 0 && (st.X - tx) * toward > 0 && MathF.Abs(tx - px) < PressRow && !_intoPress.ContainsKey(cell) && !_layBy.Contains(cell))
                        _towardStation[cell] = new Vector2(toward, 0);
                }
                // (the Pad Tiles the row ends on, by the station: turned so the pad's own z runs along the row, as a pad takes a part only
                // along its z and the row's pieces lie along it: a 5.6 m Leg Pod Housing waited at the pad's edge, 2026-11-26)
                foreach (var it in items.Where(i => Str(i, "name") == "Pad Tile"))
                {
                    float tx = Num(it, "x"), tz = -Num(it, "z");
                    if (MathF.Abs(tz - pz) < half + PadSide && (tx - px) * toward > 0 && (st.X - tx) * toward > 0 && MathF.Abs(tx - px) < PressRow + PadSide)
                        _padAlong[Round(new Vector2(tx, tz))] = new Vector2(toward, 0);
                }
                // (and the tiles between a press and the next press up the row, which the layout also laid facing up it)
                foreach (var it in items.Where(i => Str(i, "name") == "Roller Tile" && !Flag(i, "swivel")))
                {
                    float tx = Num(it, "x"), tz = -Num(it, "z");
                    var cell = Round(new Vector2(tx, tz));
                    if (MathF.Abs(tz - pz) < half && MathF.Abs(tx - px) < half + 2.5f && !_intoPress.ContainsKey(cell) && !_layBy.Contains(cell))
                        _towardStation[cell] = new Vector2(toward, 0);
                }
            }
            foreach (var it in items.Where(i => Str(i, "name") == "Roller Tile"))
                if (!_layBy.Contains(Round(new Vector2(Num(it, "x"), -Num(it, "z")))))
                    _tileAt.Add(Round(new Vector2(Num(it, "x"), -Num(it, "z"))));
            foreach (var it in items.Where(i => Str(i, "t") == "rover"))
                _roverAt.Add(Round(new Vector2(Num(it, "x"), -Num(it, "z"))));

            // Pieces on the floor first (block parts, then the bigger ones), the lab machines, then what welds on them.
            var parts = items.Where(i => Str(i, "t") == "part").ToList();
            var cores = new[] { "Smelter", "Foundry", "Sawmill", "Assembler" };
            foreach (var it in parts.Where(i => !cores.Contains(Str(i, "name")) && !Str(i, "name").StartsWith("Kit Genset")))
                PartItem(it, items);
            foreach (var it in items.Where(i => Str(i, "t") == "lab"))
                LabItem(it);
            foreach (var it in parts.Where(i => cores.Contains(Str(i, "name"))))
                PartItem(it, items);
            foreach (var it in items.Where(i => Str(i, "t") == "rover"))
                RoverItem(it);
            PadMasts();
            var g = 0;
            foreach (var it in parts.Where(i => Str(i, "name").StartsWith("Kit Genset")))
                Genset(new Vector2(Num(it, "x"), -Num(it, "z")), g++);
        }

        private void PartItem(JsonElement it, List<JsonElement> items)
        {
            var name = Str(it, "name");
            var at = new Vector2(Num(it, "x"), -Num(it, "z")) + ShiftOf(it);
            var flow = Flow(Int(it, "rot"));
            var placed = PlacePartItem(it, items, name, at, flow);
            if (placed != null)
                Register(name, at, placed);
        }

        private VPart? PlacePartItem(JsonElement it, List<JsonElement> items, string name, Vector2 at, Vector2 flow)
        {
            switch (name)
            {
                case "Roller Tile" when _layBy.Contains(Round(new Vector2(Num(it, "x"), -Num(it, "z")))):
                    return Piece("Pad Tile", at, Facing(new Vector2(-flow.Y, flow.X)), Stock); // (a big station's lay-by: its pick pad, turned so its own z runs along the bay's side: a pad takes a part only along its z, and the line's pieces lie that way)
                case "Roller Tile" when _intoPress.TryGetValue(Round(new Vector2(Num(it, "x"), -Num(it, "z"))), out var into):
                    return Piece("Roller Tile", at, Facing(into), Line); // (a turntable beside a press's bed: a short run into it)
                case "Roller Tile" when _towardStation.TryGetValue(Round(new Vector2(Num(it, "x"), -Num(it, "z"))), out var on):
                    return Piece("Roller Tile", at, Facing(on), Line); // (a press line's row: rolling toward its station)
                case "Roller Tile":
                    return Piece(Flag(it, "swivel") ? "Turntable Tile" : "Roller Tile", at, Facing(flow), Line);
                case "Pad Tile" when _padAlong.TryGetValue(Round(new Vector2(Num(it, "x"), -Num(it, "z"))), out var along):
                    return Piece(name, at, Facing(along), Stock); // (a press line's pad by its station: its z along the row)
                case "Pad Tile":
                    return Piece(name, at, Facing(flow), Stock);
                case "Hearth Block" or "Crucible Block":
                    return Piece(name, at, Facing(flow), Refining);
                case "Smelter" or "Foundry" or "Sawmill":
                {
                    // (the Core's one side socket, +x, toward its bed: the nearest Hearth or Crucible Block of the layout)
                    var bed = name == "Smelter" ? "Hearth Block" : "Crucible Block";
                    var best = float.MaxValue;
                    var dir = flow;
                    var seat = at;
                    foreach (var o in items)
                        if (Str(o, "name") == bed && new Vector2(Num(o, "x"), -Num(o, "z")) is var b && Vector2.Distance(b, at) is var dd && dd < best && dd < 8)
                        {
                            best = dd;
                            var v = b - at;
                            dir = MathF.Abs(v.X) > MathF.Abs(v.Y) ? new Vector2(MathF.Sign(v.X), 0) : new Vector2(0, MathF.Sign(v.Y));
                            // (seated: the Core's cell butts that bed cell on the 2 m grid, its +x side socket on the block's;
                            // the lab lays it about 1 m off and between two columns, which joins nothing)
                            seat = b - dir * 2;
                        }
                    if (name != "Sawmill" && best < 8 && Vector2.Distance(seat, at) > 0.01f)
                        Note($"the {name} laid at ({at.X:0}, {at.Y:0}) is seated at ({seat.X:0}, {seat.Y:0}), butting its {bed}s");
                    return Piece(name, name == "Sawmill" ? at : seat, XFacing(dir), Refining);
                }
                case "Assembler" or "Assembly Bed Block":
                    return Piece(name, at, Facing(flow), Steel);
                case "Ore Silo 4 m" or "Ore Bunker 4 m" or "Materials Bin":
                    return Piece(name, at, Facing(flow), Stock);
                case "Line Console":
                {
                    var p = Piece(name, at, Facing(flow), Crane);
                    if (p != null)
                        Config(p, ("startOn", 0));
                    return p;
                }
                case "Die Rack":
                {
                    var d = Def(name);
                    var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
                    return Piece(name, at, Facing(flow), Steel, "-y.0.0", new Vector2(mid.X, mid.Z));
                }
                default:
                    return Piece(name, at, Facing(flow), Steel);
            }
        }

        /// <summary>A lab machine: its bed cells w x l (2 m) and its Core where the lab draws it; the lab's flow by its chain table.</summary>
        private void LabItem(JsonElement it)
        {
            var id = Str(it, "id");
            int w = Int(it, "w"), l = Int(it, "l"), rot = Int(it, "rot");
            float x = Num(it, "x"), z = Num(it, "z");
            float sx = w * 2, sz = l * 2;
            var flow = Flow(rot);
            var paint = PaintOf.GetValueOrDefault(id, Steel);
            var coreSide = id is "building-station" or "extruder" ? -1 : 1;
            Vector2 Cell(int i, int j) => Local(x, z, rot, -sx / 2 + 1 + 2 * i, -sz / 2 + 1 + 2 * j);
            // (the row furthest down the flow: a former's and a cutter's Out Tables)
            var lastRow = Vector2.Dot(Cell(0, l - 1) - Cell(0, 0), flow) > 0 ? l - 1 : 0;
            // A Bake Oven's tunnel runs from its Core's row (its way in) to the far row (its way out): its Core stands by the row up the
            // flow, so a kit rolled out of its line's station bay comes in at the in end and the baked part rolls on toward the main station
            // (Sean's layout drew each oven's Core at its down-flow end, where the kits rolled into the tunnel's out end: 2026-11-26).
            var far = id == "bake-oven" ? lastRow == 0 && l > 1 : Flag(it, "coreFar");
            var coreLocal = new Vector2(coreSide * (sx / 2 + 1), far ? sz / 2 - 1 : -sz / 2 + 1);
            var core = Local(x, z, rot, coreLocal.X, coreLocal.Y);

            if (id == "extruder")
            {
                if (Extruder(it, w, l, rot, x, z, flow) is { } ex)
                    _labs.Add((id, new Vector2(x, -z), ex, flow));
                return;
            }
            var cells = new VPart?[w, l];
            for (var j = 0; j < l; j++)
                for (var i = 0; i < w; i++)
                {
                    var part = BedOf[id];
                    if (id is "panel-former" or "laser-cutter" && j == lastRow && l > 1)
                        part = "Out Table";
                    cells[i, j] = Piece(part, Cell(i, j), Facing(flow), paint);
                }
            // The Core: on the side cell beside its own cell (the bed's edge cell in the Core's row).
            var edge = coreSide > 0 ? w - 1 : 0;
            var row = far ? l - 1 : 0;
            var by = cells[edge, row];
            switch (id)
            {
                case "panel-former":
                {
                    var c = CoreOn("Panel Former", "+z", by, core, paint);
                    if (c != null)
                    {
                        OrdersOnly(c);
                        _labs.Add((id, new Vector2(x, -z), c, flow));
                    }
                    break;
                }
                case "laser-cutter":
                {
                    var c = CoreOn("Laser Cutter", "+x", by, core, paint);
                    if (c != null)
                    {
                        OrdersOnly(c);
                        _labs.Add((id, new Vector2(x, -z), c, flow));
                    }
                    break;
                }
                case "bake-oven":
                {
                    if (CoreOn("Bake Oven", "+z", by, core, paint) is { } c)
                        _labs.Add((id, new Vector2(x, -z), c, flow));
                    break;
                }
                case "building-station":
                    if (!Flag(it, "noCoreDock") && CoreOn("Building Station", "+x.0", by, core, paint) is { } st)
                        _stations.Add((new Vector2(x, -z), st));
                    break;
                case "press":
                {
                    // (the Press Core up the flow from the bed's upstream corner cell, its own +x (the bed's W, its weld onto the bed) along the
                    // flow and its own +z across the bed: the bed's L then runs across the line and the lid's hinge, on the bed's long edge farther
                    // from the Core's row, lies beside the line, parallel to it, so a piece rolls straight through, Sean 2026-11-26 ("presses need
                    // to rotate 90 degrees"); its bays (own -z) face out, away from the bed; stood by its middle socket, as the old works stands
                    // its presses)
                    var ownZ = new Vector2(-flow.Y, flow.X);
                    var up0 = lastRow == 0 ? l - 1 : 0;
                    var mid = Cell(w / 2, up0);
                    var col = Vector2.Dot(mid - Cell(0, up0), ownZ) > 0 ? 0 : w - 1;
                    core = Cell(col, up0) - flow * 2;
                    by = cells[col, up0];
                    var c = Piece("Press", core, XFacing(flow), paint, "-y.4.4", new Vector2(1, 1), chainFirst: true);
                    if (c != null)
                    {
                        OrdersOnly(c);
                        // Each press starts with a die for the frigate's pressed fittings (a blueprint can't shelve dies on a Die Rack, so the
                        // rest wait for a player's): housings, the leg pods' brackets, hinges and turret rings, the canopy plates (2026-11-26).
                        if (_pressDie < PressDies.Length)
                            Onto(c, "dieslot", PressDies[_pressDie++], "mount");
                        _labs.Add((id, new Vector2(x, -z), c, flow));
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// A Mast Block on each corner tile of the pads the press lines' rows end on (<see cref="_padAlong"/>): a pad holds what lies under
        /// its corner masts' top, and with none only a metre (a pressed Leg Pod Housing XL is 1.25 m tall; 2026-11-26). Their corners are
        /// off the row's line, so the run's pieces come onto the pad between them.
        /// </summary>
        private void PadMasts()
        {
            if (_padAlong.Count == 0 || !_items.TryGetValue("Pad Tile", out var tiles))
                return;
            foreach (var side in _padAlong.GroupBy(kv => kv.Value.X))
            {
                int x0 = side.Min(kv => kv.Key.Item1), x1 = side.Max(kv => kv.Key.Item1), z0 = side.Min(kv => kv.Key.Item2), z1 = side.Max(kv => kv.Key.Item2);
                foreach (var (cx, cz) in new[] { (x0, z0), (x0, z1), (x1, z0), (x1, z1) })
                    foreach (var (at, tile) in tiles)
                        if (Round(at) == (cx, cz))
                        {
                            Paint(Onto(tile, "top", "Mast Block", "bottom"), Steel);
                            break;
                        }
            }
        }

        /// <summary>
        /// A Sheet Extruder: its Core on an Extruder Plinth beside the die bank, w Die Blocks welded along its own +x on plinths, the die face
        /// toward the lab's flow at the footprint's downstream row (raised a metre, so the die's foot is level with the Roller Tiles' tops).
        /// </summary>
        private VPart? Extruder(JsonElement it, int w, int l, int rot, float x, float z, Vector2 flow)
        {
            float sx = w * 2, sz = l * 2;
            // (moved back up the flow by ExtruderBack, so its Run-out Tables fill the 8 m to the run Sean laid: the die's face lands on the
            // run's first tile edge; its Materials Bin and Line Console move back with it, FindShifts)
            var back = -flow * ExtruderBack;
            // (the dies' row: the footprint's row furthest down the flow)
            var rowZ = Vector2.Dot(Local(x, z, rot, 0, sz / 2 - 1) - Local(x, z, rot, 0, -sz / 2 + 1), flow) >= 0 ? sz / 2 - 1 : -sz / 2 + 1;
            var o = Facing(flow);
            var turn = FirstTurn(o);
            var ownX = Rot24.Apply(turn, Vector3.UnitX);
            var bankDir = Local(x, z, rot, 1, rowZ) - Local(x, z, rot, 0, rowZ); // (the lab's local +x on the site)
            var coreLocalX = -sx / 2 - 1;
            if (Vector2.Dot(new Vector2(ownX.X, ownX.Z), bankDir) < 0)
            {
                coreLocalX = sx / 2 + 1; // (the dies go on the Core's own +x: past the bank's other end when the site's turn puts its +x there)
                Note($"the Extruder at ({x}, {-z}): its Core stands at the die bank's other end from the lab's (the dies weld on the Core's own +x)");
            }
            // (and across by a metre when that puts more of its die cells on the run's tile columns: a table meets a tile only square on its middle)
            Vector2 Cell(int k) => Local(x, z, rot, coreLocalX < 0 ? -sx / 2 + 1 + 2 * k : sx / 2 - 1 - 2 * k, rowZ) + back;
            int Meets(Vector2 side) => Enumerable.Range(0, w).Count(k => _tileAt.Contains(Round(Cell(k) + side + flow * (0.75f + ExtruderBack + 0.25f + 1f))));
            var aside = new[] { Vector2.Zero, bankDir, -bankDir }.OrderByDescending(Meets).First();
            if (aside != Vector2.Zero)
            {
                back += aside;
                Note($"the Extruder at ({x}, {-z}) moves a metre across, to ({aside.X:0}, {aside.Y:0}), so its die cells meet its run's tiles");
            }
            // The die cells' plinths first, then the Core's (on a slab's edge it welds on a die plinth's side).
            for (var k = 0; k < w; k++)
                if (Piece("Extruder Plinth", Cell(k), o, Steel, "bottom ahead") is { } pl)
                    Finish(pl, "concrete");
            var at = Local(x, z, rot, coreLocalX, rowZ) + back;
            var plinth = Piece("Extruder Plinth", at, o, Steel, "bottom ahead");
            if (plinth == null)
                return null;
            Finish(plinth, "concrete");
            if (_free.ContainsKey(plinth))
                Note($"the Extruder at ({at.X}, {at.Y}): its plinth is welded freeform");
            ref var pp = ref Live.Parts[plinth.Index];
            var top = FromRest(pp.Pos + Rot24.Apply(pp.Rot, Array.Find(plinth.Def.Sockets, s => s.Name == "top").At));
            VPart core;
            try
            {
                core = Stand("Extruder", top, o, plinth, "bottom");
            }
            catch (VehicleKitException ex)
            {
                Note($"the Extruder at ({at.X}, {at.Y}) not placed: {ex.Message}");
                _missed++;
                return null;
            }
            Keep(core, at, Line);
            OrdersOnly(core);
            var cell = core;
            for (var k = 0; k < w; k++)
            {
                var c2 = Cell(k);
                try
                {
                    cell = Onto(cell, "+x", "Die Block", "-x");
                    Keep(cell, c2, Line);
                }
                catch (VehicleKitException ex)
                {
                    Note($"a Die Block at ({c2.X}, {c2.Y}) not placed: {ex.Message}");
                    _missed++;
                    break;
                }
                RunOut(cell, c2, flow);
            }
            return core;
        }

        private readonly HashSet<(int, int)> _roverAt = new(); // (the layout's rover docks, by whole metre)

        /// <summary>
        /// A dock's spot slid along its bay's edge (2 m a step, away from the oven, past the layout's other docks) when a Bake Oven's Core
        /// stands on it: an oven's Core goes at its bed's in end (<see cref="LabItem"/>), where the small lines' layout has a dock (2026-11-26).
        /// </summary>
        private Vector2 ClearOfOvenCores(Vector2 at, Vector2 mouth)
        {
            var side = new Vector2(mouth.Y, -mouth.X);
            bool Hits(Vector2 p) => _labs.Any(l => l.Id == "bake-oven" && _at.TryGetValue(l.Core, out var c)
                && MathF.Abs(Vector2.Dot(c - p, mouth)) < 3f && MathF.Abs(Vector2.Dot(c - p, side)) < 2f);
            var oven = _labs.Where(l => l.Id == "bake-oven" && _at.TryGetValue(l.Core, out var c) && Hits(at)).Select(l => l.At).FirstOrDefault();
            if (!Hits(at))
                return at;
            var away = Vector2.Dot(at - oven, side) >= 0 ? side : -side;
            for (var k = 1; k <= 6; k++)
            {
                var p = at + away * (2f * k);
                if (!Hits(p) && !_roverAt.Contains(Round(p)))
                {
                    Note($"the dock at ({at.X:0}, {at.Y:0}) slides {2 * k} m along its bay's edge, to ({p.X:0}, {p.Y:0}): a Bake Oven's Core stands at its bed's in end there");
                    return p;
                }
            }
            return at;
        }

        /// <summary>A Dock Expansion (or, for a station's Core, the Building Station pad) with its mouth (own +x) toward the bay.</summary>
        private void RoverItem(JsonElement it)
        {
            var kind = Str(it, "kind") is { Length: > 0 } k ? k : "medium";
            var core = Flag(it, "core");
            var part = core ? "Building Station" : kind switch { "small" => "Dock Expansion", "large" => "Large Dock Expansion", _ => "Medium Dock Expansion" };
            var rot = Int(it, "rot");
            var t = rot * MathF.PI / 2;
            var mouth = new Vector2(-MathF.Round(MathF.Sin(t)), MathF.Round(MathF.Cos(t))); // (the lab's local -z, z turned round)
            var at = new Vector2(Num(it, "x"), -Num(it, "z"));
            var d = Def(part);
            var footX = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f;
            var foot = new Vector2(MathF.Round(footX), 0); // (small -1, medium -2, large -5: the pad's middle behind its mouth)
            var by = d.Sockets.First(s => s.Name.StartsWith("-y.")).Name;
            if (!core)
                at = ClearOfOvenCores(at, mouth);
            var p = Piece(part, at, XFacing(mouth), core ? Station : Crane, by, foot);
            if (core && p != null)
                _stations.Add((at, p));
        }

        /// <summary>
        /// A large militia engine generator set (<see cref="GensetAsk"/>) as a body of its own beside the works (<see cref="GensetName"/>: a body runs
        /// one engine, so four on the works ran one), its skid's middle at <paramref name="at"/>, standing on the floor where it stood when it was
        /// welded on; a Heavy Power Terminal on the floor just past its skid's +x edge, by its own, so the heavy power cable between them is short.
        /// </summary>
        private void Genset(Vector2 at, int n)
        {
            var r = GensetAsk();
            var engine = EngineKit.EngineKit.Request(r);
            if (!engine.Built || engine.Plan is null)
            {
                Note($"genset {n}: the engine wouldn't build: {engine.Note}");
                _missed++;
                return;
            }
            var head = EngineKitGenset.HeadFor(engine.Kw);
            var (tanks, batteries) = EngineKitGenset.Stores(engine);
            var (fx0, fx1, fz0, fz1) = EngineKitGenset.Footprint(engine, head, tanks, batteries);
            var mid = new Vector2((fx0 + fx1) * 0.5f, (fz0 + fz1) * 0.5f);
            var c0 = new Vector2(2 * MathF.Round((at.X - mid.X - 1) / 2) + 1, 2 * MathF.Round((at.Y - mid.Y - 1) / 2) + 1); // (its root Foundation's middle, on an odd metre: never a slab's edge)
            // The set's own frame: its root Foundation (2 m, top at 0.3 m over the set's origin) at c0 on the floor.
            var fd = Def("Foundation");
            Beside.Add((GensetName, ToRest(V(c0.X, FloorY + 0.15f, c0.Y) - (fd.BoundsMin + fd.BoundsMax) * 0.5f), 0));
            var term = EngineKitGenset.TerminalAt(engine, head, tanks, batteries);
            var edge = c0.X + 2 * MathF.Ceiling((0.25f + term.X + 0.6f - 1) / 2) + 1; // (the skid's +x edge: EngineKitGenset.Build's Foundations)
            var spot = new Vector2(edge + 0.5f, c0.Y + 0.25f + term.Z);
            if (Piece(Terminal, spot, Up, Power) is { } t)
                Note($"genset {n}: a {engine.Name} ({engine.Kw:0} kW) on a {head}, its own body (root Foundation at ({c0.X}, {c0.Y})), cabled to a {Terminal} at ({spot.X}, {spot.Y})");
            else
                Note($"genset {n}: no {Terminal} beside it: the set isn't cabled");
        }
    }
}
