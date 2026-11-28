using System.Numerics;
using System.Text.Json;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Zero Day Works II's links (Sean, 2026-11-25: none were laid) and its extruders' Run-out Tables. Each half of the site (west and east,
/// mirrored) is linked the same way, by chutes laid square through the air (<see cref="Script.Duct"/>, as the old works lays them):
/// <list type="bullet">
/// <item>The front refinery: the 18 Ore Silos chained in two snakes of nine (iron ore, stone), each into its refiner (the big Smelter and
/// the big Foundry, on steel); the Smelter's metal into the first stock silo, which feeds the Foundry; the Foundry's steel into the second,
/// chained on to the third and fourth.</item>
/// <item>The back row: each refiner's Ore Silo behind it into it (iron ore, copper ore, stone, sand, sand, wood), each refiner into the
/// Materials Bin in front of it (metal, copper, steel, concrete, rubber, planks), the Foundries' second inputs from the bins and silos
/// beside them, the two spare bins chained as reserves (metal, steel).</item>
/// <item>The lines: each Extruder's Materials Bin chuted from a steel stock silo and into the Extruder's back; each Line Console wired to
/// its Extruder's Run (and its line's Panel Former, or its presses and Assembler) and one console a Building Station into its Orders;
/// the Die Racks chuted into their presses; each Assembler chuted from the steel, copper, concrete and rubber bins.</item>
/// </list>
/// A link the rules refuse is named in the log (a "[zeroday_works2]" line) and left out.
/// </summary>
public sealed partial class ZeroDayWorks2
{
    private sealed partial class Script
    {
        /// <summary>How far an Extruder moves back up its flow: a Run-out Table is 8 m, and the die's face (0.75 m ahead of its cell) lands on the run's first tile edge.</summary>
        public const float ExtruderBack = 7.75f;

        /// <summary>
        /// How far an Extruder's Materials Bin and Line Console move back with it (on the floor's whole metres): clear of the press it
        /// draws behind its die (ExtruderRender: its cylinder platen's back face 8.8 m up the flow from the die face), Sean 2026-11-26:
        /// at 8 m they stood inside it, 7 m behind the face.
        /// </summary>
        public const float BinBack = 11f;

        public const float SegmentMax = 7.5f; // (a chute's segment reaches at most 8 m)

        private readonly Dictionary<(string, float, float), Vector2> _shift = new();
        private readonly List<(string Id, Vector2 At, VPart Core, Vector2 Flow)> _labs = new();

        private static readonly string[] PressDies = ["Housing Die", "Bracket Die", "Plate Die", "Actuator Die"]; // (in the order the presses are laid: the two the press lines feed get the small blanks, brackets and hinges)
        private int _pressDie;
        private readonly List<(Vector2 At, VPart Core)> _stations = new();
        private readonly Dictionary<string, List<(Vector2 At, VPart P)>> _items = new();
        private readonly Dictionary<VPart, (Vector3 Pos, Quaternion Rot)> _free = new();
        private int _links, _linkMissed, _tables, _tablesMet;

        private static (string, float, float) Key(JsonElement it) => (Str(it, "name"), Num(it, "x"), Num(it, "z"));

        private Vector2 ShiftOf(JsonElement it) => Str(it, "t") == "part" && _shift.TryGetValue(Key(it), out var v) ? v : default;

        /// <summary>Each Extruder's Materials Bin and Line Console behind it (up its flow, within its width): moved back <see cref="BinBack"/> with it.</summary>
        private void FindShifts(List<JsonElement> items)
        {
            foreach (var ex in items.Where(i => Str(i, "t") == "lab" && Str(i, "id") == "extruder"))
            {
                var c = new Vector2(Num(ex, "x"), -Num(ex, "z"));
                var u = -Flow(Int(ex, "rot"));
                var across = new Vector2(u.Y, -u.X);
                foreach (var it in items.Where(i => Str(i, "name") is "Materials Bin" or "Line Console"))
                {
                    var rel = new Vector2(Num(it, "x"), -Num(it, "z")) - c;
                    if (Vector2.Dot(rel, u) is var along && along > 0 && along < Int(ex, "l") + 6 && MathF.Abs(Vector2.Dot(rel, across)) < Int(ex, "w") + 4)
                    {
                        _shift[Key(it)] = u * BinBack;
                        Note($"the {Str(it, "name")} at ({c.X + rel.X:0}, {c.Y + rel.Y:0}) moves {BinBack:0} m back with its Extruder, to ({c.X + rel.X + u.X * BinBack:0}, {c.Y + rel.Y + u.Y * BinBack:0})");
                    }
                }
            }
        }

        private void Register(string name, Vector2 at, VPart p)
        {
            if (!_items.TryGetValue(name, out var l))
                _items[name] = l = new List<(Vector2, VPart)>();
            l.Add((at, p));
        }

        /// <summary>A freeform weld's pose (as <see cref="BlueprintScript.Weld"/> makes it, unturned but for its first turn): a link's ports are read from it.</summary>
        private void Welded(VPart p, Vector3 centre, byte turn)
        {
            var rot = Rot24.Quat(turn);
            _free[p] = (ToRest(centre) - Vector3.Transform((p.Def.BoundsMin + p.Def.BoundsMax) * 0.5f, rot), rot);
        }

        /// <summary>The placed part named <paramref name="name"/> nearest the layout's <paramref name="at"/> (within <paramref name="r"/>), or null with a note.</summary>
        private VPart? Find(string name, Vector2 at, float r = 1.5f)
        {
            VPart? best = null;
            var bd = r;
            if (_items.TryGetValue(name, out var l))
                foreach (var (a, p) in l)
                    if (Vector2.Distance(a, at) is var d && d <= bd)
                        (bd, best) = (d, p);
            if (best == null)
                Note($"links: no {name} near ({at.X:0}, {at.Y:0})");
            return best;
        }

        // ---------------------------------------------------------------- Run-out Tables

        /// <summary>A Run-out Table on a Die Block's outBed, down the flow, and its stand on the floor under its middle (<paramref name="cell"/>: the block's middle).</summary>
        private void RunOut(VPart die, Vector2 cell, Vector2 flow)
        {
            try
            {
                var t = Onto(die, "outBed", "Run-out Table", "in");
                Paint(t, Line);
                _tables++;
                if (MeetsTile(t))
                    _tablesMet++;
                else
                    Note($"a Run-out Table at ({cell.X:0.##}, {cell.Y:0.##}) meets no Roller Tile at its far end");
                Piece("Run-out Table Stand", cell + flow * (0.75f + 4f), Facing(flow), Steel, "bottom", new Vector2(0, 4));
            }
            catch (VehicleKitException ex)
            {
                Note($"a Run-out Table at ({cell.X:0.##}, {cell.Y:0.##}) not placed: {ex.Message}");
                _missed++;
            }
        }

        /// <summary>Is the table welded on a Roller Tile (the run that takes its pieces on: RollerRuns.Butted)?</summary>
        private bool MeetsTile(VPart t)
        {
            for (var e = Live.FirstLink(t.Index); e >= 0; e = Live.NextLink(e))
                if (Live.DefOf(Live.OtherEnd(e)).IsRollerTile)
                    return true;
            return false;
        }

        // ---------------------------------------------------------------- laying a link

        /// <summary>A port's place and face on the site; a null port: the middle of the part's top, facing up (a bin's or a rack's).</summary>
        private (Vector3 At, Vector3 Face) PortOf(VPart p, string? name)
        {
            Vector3 at, dir;
            if (name == null)
            {
                at = new Vector3((p.Def.BoundsMin.X + p.Def.BoundsMax.X) * 0.5f, p.Def.BoundsMax.Y + 0.05f, (p.Def.BoundsMin.Z + p.Def.BoundsMax.Z) * 0.5f);
                dir = Vector3.UnitY;
            }
            else
            {
                var np = p.Def.NamedPorts.FirstOrDefault(x => x.Name == name) ?? throw new VehicleKitException($"a {p.Name} has no port \"{name}\"");
                (at, dir) = (np.At, ConstructParts.DirVector(np.Face));
            }
            if (_free.TryGetValue(p, out var w))
                return (FromRest(w.Pos + Vector3.Transform(at, w.Rot)), Vector3.Transform(dir, w.Rot));
            ref var cp = ref Live.Parts[p.Index];
            var face = Rot24.Apply(cp.Rot, dir);
            return (FromRest(cp.Pos + Rot24.Apply(cp.Rot, at)), face.Y > 0.9f || name == null ? Vector3.UnitY : face);
        }

        private static Vector3[] Cut(Vector3 from, params Vector3[] pts)
        {
            var nodes = new List<Vector3>();
            var last = from;
            foreach (var q in pts)
            {
                if (Vector3.Distance(q, last) < 0.05f)
                    continue;
                var n = (int)MathF.Ceiling(Vector3.Distance(q, last) / SegmentMax);
                for (var k = 1; k <= n; k++)
                    nodes.Add(Vector3.Lerp(last, q, k / (float)n));
                last = q;
            }
            return nodes.ToArray();
        }

        /// <summary>
        /// A chute laid square through the air (the old works' Duct): out of the first port along its face, up to <paramref name="height"/>,
        /// along x (or z first, <paramref name="zFirst"/>), then the other way, down into the second port along its face. Refused: a note.
        /// </summary>
        private void Chute(VPart? from, string? fromPort, VPart? to, string? toPort, float height, bool zFirst = false)
        {
            if (from == null || to == null)
            {
                _linkMissed++;
                return;
            }
            try
            {
                var (a, fa) = PortOf(from, fromPort);
                var (b, fb) = PortOf(to, toPort);
                Vector3 a1 = a + fa, b1 = b + fb;
                var mid = zFirst ? V(a1.X, height, b1.Z) : V(b1.X, height, a1.Z);
                Link("chute", from, to, fromPort, toPort, Stock, false, null, Cut(a, a1, V(a1.X, height, a1.Z), mid, V(b1.X, height, b1.Z), b1));
                _links++;
            }
            catch (VehicleKitException ex)
            {
                Note($"a chute {from.Name} -> {to.Name} not laid: {ex.Message}");
                _linkMissed++;
            }
        }

        /// <summary>A data cable from a Line Console's foot up to <paramref name="height"/>, along x then z, to over the target and down to it.</summary>
        private void Cable(VPart console, VPart? to, string toPort, float height, string? fromPort = null)
        {
            if (to == null)
            {
                _linkMissed++;
                return;
            }
            try
            {
                // (a console welded freeform, off a slab's lattice, has its pose in _free, as a target's)
                var at = _free.TryGetValue(console, out var cw) ? FromRest(cw.Pos) : FromRest(Live.Parts[console.Index].Pos);
                var t = _free.TryGetValue(to, out var w) ? FromRest(w.Pos) : FromRest(Live.Parts[to.Index].Pos);
                Link("data", console, to, fromPort, toPort, Crane, false, null, Cut(at, V(at.X, height, at.Z), V(t.X, height, at.Z), V(t.X, height, t.Z)));
                _links++;
            }
            catch (VehicleKitException ex)
            {
                Note($"a cable {console.Name} -> {to.Name} ({toPort}) not laid: {ex.Message}");
                _linkMissed++;
            }
        }

        private void Fill(VPart? store, string material)
        {
            if (store != null && store.Name != "Materials Bin") // (a bin takes any material and has no settings: it fills from its chute; a blueprint refuses a bin's "material", 2026-11-26)
                Config(store, ("material", material), ("fill", 100));
        }

        // ---------------------------------------------------------------- the works' links

        private void LayLinks()
        {
            foreach (var s in new[] { -1f, 1f })
            {
                Refinery(s);
                BackRow(s);
                BackRowElectronics(s);
                Lines(s);
            }
            Orders();
            Note($"links: {_links} laid, {_linkMissed} not; Run-out Tables: {_tables}, {_tablesMet} meeting their run");
        }

        private Vector2 P(float s, float x, float z) => new(s * x, z);

        /// <summary>A snake of stores (each chuted into the next) ending in <paramref name="into"/>, every link at its own height from <paramref name="up"/>.</summary>
        private void Snake(List<VPart?> stores, VPart? into, float up)
        {
            for (var k = 0; k + 1 < stores.Count; k++)
                Chute(stores[k], "Chute out", stores[k + 1], "Chute in", up + 0.5f * (k % 2));
            Chute(stores[^1], "Chute out", into, "Chute in", up + 1f);
        }

        /// <summary>The front refinery: ore silos to the big Smelter and Foundry, their metal and steel into the stock silos.</summary>
        private void Refinery(float s)
        {
            var smelter = Find("Smelter", P(s, 24, 87), 3);
            var foundry = Find("Foundry", P(s, 48, 87), 3);
            if (foundry != null)
                Config(foundry, ("recipe", 2)); // (steel: metal and stone)
            foreach (var (cols, stock, into, up) in new[] { (new[] { 14f, 20f, 26f }, "iron ore", smelter, 13f), (new[] { 44f, 38f, 32f }, "stone", foundry, 15f) })
            {
                var snake = new List<VPart?>();
                for (var c = 0; c < 3; c++)
                    foreach (var z in c % 2 == 0 ? new[] { 68f, 74f, 80f } : new[] { 80f, 74f, 68f })
                    {
                        var silo = Find("Ore Silo 4 m", P(s, cols[c], z));
                        Fill(silo, stock);
                        snake.Add(silo);
                    }
                Snake(snake, into, up);
            }
            // (the metal and steel stock: Ore Silos since 2026-11-27, ZeroDayWorks2.Restocked; their rims are 11.5 m up, so every chute out of
            // or along them runs over 12.5 m, a port's rise above the rim)
            var b = StockSilos(s);
            Fill(b[0], "metal");
            for (var k = 1; k < 4; k++)
                Fill(b[k], "steel");
            Chute(smelter, "Chute out", b[0], "Chute in", 17f); // (over the silos)
            Chute(b[0], "Chute out", foundry, "Chute in", 13f);
            Chute(foundry, "Chute out", b[1], "Chute in", 17.5f);
            Chute(b[1], "Chute out", b[2], "Chute in", 13.5f);
            Chute(b[2], "Chute out", b[3], "Chute in", 13.5f);
        }

        /// <summary>The front refinery's metal silo and its three steel silos (<see cref="Restocked"/>), west to east from the middle.</summary>
        private VPart?[] StockSilos(float s) => new[] { 16f, 24f, 32f, 40f }.Select(x => Find("Ore Silo 4 m", P(s, x, StockRowZ))).ToArray();

        /// <summary>The back row: six refiners, an Ore Silo behind each, the Materials Bins in front.</summary>
        private void BackRow(float s)
        {
            VPart? R(string name, float x) => Find(name, P(s, x, 122), 3);
            VPart? S(float x) => Find("Ore Silo 4 m", P(s, x, 133));
            VPart? B(float x) => Find("Materials Bin", P(s, x, 114));
            var (sm1, sm2, f1, f2, f3, saw) = (R("Smelter", 16), R("Smelter", 24), R("Foundry", 32), R("Foundry", 40), R("Foundry", 48), R("Sawmill", 56));
            foreach (var (f, recipe) in new[] { (f1, 2), (f2, 3), (f3, 5) })
                if (f != null)
                    Config(f, ("recipe", recipe)); // (steel, concrete, rubber)
            var up = 13f;
            foreach (var (x, refiner, stock) in new[] { (16f, sm1, "iron ore"), (24f, sm2, "copper ore"), (32f, f1, "stone"), (40f, f2, "sand"), (48f, f3, "sand"), (56f, saw, "wood") })
            {
                var silo = S(x);
                Fill(silo, stock);
                Chute(silo, "Chute out", refiner, "Chute in", up);
                up += 0.5f;
            }
            Chute(S(32), "Chute out", f2, "Chute in", up); // (the concrete Foundry's stone)
            var bins = new[] { 16f, 21f, 26f, 31f, 36f, 41f, 46f, 51f }.ToDictionary(x => x, B);
            foreach (var (x, m) in new[] { (16f, "metal"), (21f, "metal"), (26f, "copper"), (31f, "steel"), (36f, "steel"), (41f, "concrete"), (46f, "rubber"), (51f, "planks") })
                Fill(bins[x], m);
            var low = 4f;
            foreach (var (refiner, x) in new[] { (sm1, 16f), (sm2, 26f), (f1, 31f), (f2, 41f), (f3, 46f), (saw, 51f) })
            {
                Chute(refiner, "Chute out", bins[x], null, low);
                low += 0.5f;
            }
            Chute(bins[16], null, bins[21], null, low); // (the metal's reserve)
            Chute(bins[31], null, bins[36], null, low + 0.5f); // (the steel's)
            Chute(bins[21], null, f1, "Chute in", low + 1f); // (the steel Foundry's metal)
            Chute(bins[51], null, f3, "Chute in", low + 1.5f); // (the rubber Foundry's planks)
        }

        /// <summary>The lines: each Extruder's bin, its feed and its console; the presses' racks; the Assembler's stock.</summary>
        private void Lines(float s)
        {
            var bunker = StockSilos(s)[1..];
            var high = 16f;
            // Each Extruder's own console: the one behind it (up its flow, within 6 m of its line), as the consoles moved back with their
            // Extruders (BinBack): by distance alone one console was nearest two Extruders, and two lines shared it.
            var ownConsole = new Dictionary<VPart, VPart>();
            if (_items.TryGetValue("Line Console", out var cl))
                foreach (var l in _labs.Where(l => l.Id == "extruder" && MathF.Sign(l.At.X) == s))
                {
                    var side = new Vector2(l.Flow.Y, -l.Flow.X);
                    if (cl.Where(e => Vector2.Distance(e.At, l.At) < 20 && Vector2.Dot(e.At - l.At, l.Flow) < 0 && MathF.Abs(Vector2.Dot(e.At - l.At, side)) < 6)
                            .OrderBy(e => Vector2.Distance(e.At, l.At)).Select(e => e.P).FirstOrDefault() is { } con)
                        ownConsole[l.Core] = con;
                }
            foreach (var (id, at, core, flow) in _labs.Where(l => l.Id == "extruder" && MathF.Sign(l.At.X) == s && !IsElectronics(l.At)).OrderByDescending(l => l.At.Y))
            {
                var bin = Nearest("Materials Bin", at, 20); // (16 m, and 20 since the bins stand BinBack behind the moved Extruder)
                var console = ownConsole.GetValueOrDefault(core);
                Fill(bin, "steel");
                // (the big lines from the second and third bunkers, the small line and the press line from the fourth)
                var from = at.Y > 60 ? bunker[0] : at.Y < -60 ? bunker[1] : bunker[2];
                Chute(from, "Chute out", bin, null, high);
                high += 0.5f;
                Chute(bin, null, core, "Chute in", 4.5f);
                if (console == null)
                {
                    Note($"links: no Line Console within 20 m of the Extruder at ({at.X:0}, {at.Y:0})");
                    continue;
                }
                Cable(console, core, "Run", 6f); // (its Panel Former's, presses' and Assembler's Runs come from a station's console: Orders)
                _consoles.Add((at, console));
            }
            var pressConsole = NearestLab("extruder", P(s, 53, -39), 3) is { } first ? ownConsole.GetValueOrDefault(first) : null;
            LinesElectronics(s, pressConsole, ref high);
            // A press swaps its die from a Die Rack chuted into it (PressDieSwap): each rack into the press beside it.
            foreach (var x in new[] { 39f, 47f })
            {
                var rack = Find("Die Rack", P(s, x, -24), 2.5f); // (2.5 m: the Housing press's rack stands 2 m further out, ZeroDayWorks2.Revisions)
                var press = _labs.Where(l => l.Id == "press").OrderBy(l => Vector2.Distance(l.At, P(s, x, -30))).FirstOrDefault().Core;
                Chute(rack, null, press, null, 4f);
            }
            DieCell(s);
            JoinSecondRun(s);
            // The Assembler's stock (Orders pull their inputs from the stores chuted in), from the back row's bins, over everything at 18 m.
            var assembler = Find("Assembler", P(s, 33, -33));
            var up = 18f;
            foreach (var x in new[] { 36f, 26f, 41f, 46f, GlassBin, BronzeBin, ClothBin }) // (steel, copper, concrete, rubber; glass, bronze, cloth: ZeroDayWorks2.Electronics.cs)
            {
                Chute(Find("Materials Bin", P(s, x, 114)), null, assembler, "Chute in", up);
                if (s < 0 && _bigAsm != null) // (the big Assembler by the main station, from the same bins, a level up)
                    Chute(Find("Materials Bin", P(s, x, 114)), null, _bigAsm, "Chute in", up + 2.5f);
                up += 0.5f;
            }
        }

        /// <summary>
        /// The second blank Extruder's run (<see cref="Additions"/>) into its line: a conveyor from its last tile's Piece out, along the row
        /// of the line's turntables at the run's height, into the side of the turntable the first Extruder's run ends in (its In port that
        /// faces the new run), over the idle turntable beside it. That turntable passes a piece on into the press beds as the first run's.
        /// </summary>
        private void JoinSecondRun(float s)
        {
            var end = Find("Roller Tile", new Vector2(s * 61, -34), 0.5f);
            var entry = Find("Roller Tile", new Vector2(s * 53, -32), 0.5f);
            if (end == null || entry == null || !entry.Def.IsTurntable)
            {
                Note($"second run on side {s}: no run end or entry turntable");
                _linkMissed++;
                return;
            }
            try
            {
                var (a, fa) = PortOf(end, "Piece out");
                var toward = new Vector3(s, 0, 0); // (from the entry turntable out toward the new run)
                var port = entry.Def.NamedPorts.Where(np => np.Name.StartsWith("In ", StringComparison.Ordinal))
                    .OrderByDescending(np => Vector3.Dot(PortOf(entry, np.Name).Face, toward)).First().Name;
                var (b, fb) = PortOf(entry, port);
                Vector3 a1 = a + fa, b1 = b + fb;
                Link("conveyor", end, entry, "Piece out", port, Line, false, null, Cut(a, a1, b1));
                _links++;
                Note($"second run on side {s}: a conveyor from its end at ({a.X:0.#}, {a.Z:0.#}) into the turntable's {port} at ({b.X:0.#}, {b.Z:0.#})");
            }
            catch (VehicleKitException ex)
            {
                Note($"second run on side {s}: the conveyor not laid: {ex.Message}");
                _linkMissed++;
            }
        }

        /// <summary>
        /// A press pair's die cell (Sean, 2026-11-26: "a die rack and a loader arm that swaps out dies on the presses, because otherwise we'll
        /// need a press for every type in an area"): a Line Arm on the floor 3 m out from the middle of the pair's Cores (on the Cores' side,
        /// away from the beds, past the lids' swing), and a Die Wall (every die family on a shelf) 3.5 m past it. The arm reaches both die
        /// slots and the wall; it swaps a press's die for its head order's (PressDieArm.cs). Added beside Sean's pieces; none of his move.
        /// </summary>
        private void DieCell(float s)
        {
            var presses = _labs.Where(l => l.Id == "press" && MathF.Sign(l.At.X) == s).ToList();
            if (presses.Count != 2)
            {
                Note($"die cell: {presses.Count} presses on side {s}, not two: no die cell");
                return;
            }
            Vector2 SiteOf(VPart p)
            {
                var at = _free.TryGetValue(p, out var w) ? FromRest(w.Pos) : FromRest(Live.Parts[p.Index].Pos);
                return new Vector2(at.X, at.Z);
            }
            Vector2 DieOf(VPart core)
            {
                var sock = core.Def.Sockets[core.Def.PressDieSocket].At;
                var at = _free.TryGetValue(core, out var w) ? FromRest(w.Pos + Vector3.Transform(sock, w.Rot)) : FromRest(Live.Parts[core.Index].Pos + Rot24.Apply(Live.Parts[core.Index].Rot, sock));
                return new Vector2(at.X, at.Z);
            }
            var dies = (DieOf(presses[0].Core) + DieOf(presses[1].Core)) * 0.5f;
            var beds = (presses[0].At + presses[1].At) * 0.5f;
            var along = Vector2.Normalize(DieOf(presses[1].Core) - DieOf(presses[0].Core));
            var outward = new Vector2(-along.Y, along.X);
            if (Vector2.Dot(outward, dies - beds) < 0)
                outward = -outward;
            // (the Core's box reaches 1 m past its die slot on this side, and the arm's Core 1.06 m round its shoulder: 2.5 m out leaves 0.45 m)
            var arm = dies + outward * 2.5f;
            arm = new Vector2(MathF.Round(arm.X * 2) / 2, MathF.Round(arm.Y * 2) / 2);
            // The wall 2 m past the arm, unless Sean's two Die Racks stand on that side within reach: then in the gap between them, on their line.
            var racks = new[] { 39f, 47f }.Select(x => Find("Die Rack", P(s, x, -24), 2.5f)).Where(r => r != null).Select(r => SiteOf(r!)).ToList();
            var wall = dies + outward * 4.6f;
            if (racks.Count == 2 && racks.All(r => Vector2.Dot(r - dies, outward) is > 2f and < 6f))
            {
                var gap = (racks[0] + racks[1]) * 0.5f + along * (Vector2.Dot(along, new Vector2(0.75f, 0.375f)) * 0); // (the racks' origins are their corners: their middles are 0.75 m in)
                wall = new Vector2(gap.X + 0.6f * MathF.Sign(along.X), racks[0].Y + 0.375f);
            }
            var wallFace = MathF.Abs(outward.X) > MathF.Abs(outward.Y) ? new Vector2(1, 0) : new Vector2(0, 1); // (the shelves open toward the arm: the wall's long side across the outward line)
            var d = Def("Die Wall");
            var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
            var w = Piece("Die Wall", wall, Facing(wallFace), Steel, "-y.0.0", new Vector2(mid.X, mid.Z));
            var core = KitArm("Line Arm", arm, -outward);
            Note($"die cell on side {s}: dies at {DieOf(presses[0].Core)} {DieOf(presses[1].Core)}, racks {string.Join(" ", racks)}");
            Note($"die cell on side {s}: a Line Arm at ({arm.X:0.0}, {arm.Y:0.0}){(core == null ? " NOT placed" : "")}, a Die Wall at ({wall.X:0.0}, {wall.Y:0.0}){(w == null ? " NOT placed" : "")}");
        }

        /// <summary>A kit arm preset (ArmKitParts) stood on the floor at <paramref name="at"/>, its rest facing <paramref name="face"/>: its Arm Core, or null with a note.</summary>
        private VPart? KitArm(string preset, Vector2 at, Vector2 face)
        {
            var steps = ArmKitParts.PresetSteps(preset);
            var placed = new VPart?[steps.Count];
            VPart? core = null;
            for (var k = 0; k < steps.Count; k++)
            {
                var (p, on, socket, own) = steps[k];
                try
                {
                    placed[k] = on < 0 ? Piece(p, at, Facing(face), Crane) : placed[on] is { } under ? Onto(under, socket, p, own) : null;
                }
                catch (VehicleKitException ex)
                {
                    Note($"kit arm at ({at.X:0}, {at.Y:0}): the {p} not placed: {ex.Message}");
                    placed[k] = null;
                }
                if (placed[k] is not { } q)
                    return null;
                if (!p.StartsWith("Pressed Part", StringComparison.Ordinal))
                    Paint(q, Crane);
                if (p == "Arm Core")
                    core = q;
            }
            return core;
        }

        private readonly List<(Vector2 At, VPart Console)> _consoles = new();

        private readonly HashSet<(int, int)> _tileAt = new(); // (the layout's Roller Tiles, by whole metre)

        private readonly HashSet<(int, int)> _layBy = new(); // (the big stations' lay-by Roller Tiles, laid as Pad Tiles: their pick pads)

        private readonly Dictionary<(int, int), Vector2> _intoPress = new(); // (turntables butting a press bed, laid as Roller Tiles rolling into it: their flow)
        private readonly Dictionary<(int, int), Vector2> _towardStation = new(); // (a press line row's plain Roller Tiles: their flow, toward the row's station)
        private readonly Dictionary<(int, int), Vector2> _padAlong = new(); // (the Pad Tiles a press line's row ends on: their pad's z, along the row)
        private const float PadSide = 6; // (how far either side of a press row's line its station's pad may reach, m)
        private const float PressRow = 40; // (how far from a press its row's tiles toward the station are looked for, m)

        private const float LayBy = 4; // (m past a big station's bay side that its lay-by rows cover: two 2 m rows)

        private static (int, int) Round(Vector2 v) => ((int)MathF.Round(v.X), (int)MathF.Round(v.Y));

        private VPart? Nearest(string name, Vector2 at, float r) =>
            _items.TryGetValue(name, out var l) ? l.Where(e => Vector2.Distance(e.At, at) < r).OrderBy(e => Vector2.Distance(e.At, at)).Select(e => e.P).FirstOrDefault() : null;

        private VPart? NearestLab(string id, Vector2 at, float r) =>
            _labs.Where(e => e.Id == id && Vector2.Distance(e.At, at) < r).OrderBy(e => Vector2.Distance(e.At, at)).Select(e => e.Core).FirstOrDefault();

        /// <summary>
        /// What each Building Station orders through (a station takes one console; Sean, 2026-11-26: the frigate's hull pieces are made on the
        /// extrusion lines, never the Assembler). A line's station is the one nearest the end of its Laser Cutter (the line rolls Extruder,
        /// Panel Former, cutter, station bay): its first line's console goes into the station's Orders and is wired to every Extruder and Panel
        /// Former of the lines that end there, so a kit's plates and skins are ordered from them. The main station (no line ends there) takes the
        /// press line's console, wired to every Bake Oven, Press and Assembler on the site: its hull pieces are the ovens' orders, each oven builds
        /// its kit at its line's station, and its fittings the presses' and the Assemblers'.
        /// </summary>
        private void Orders()
        {
            var lines = new Dictionary<VPart, List<(Vector2 At, VPart Console)>>();
            // Each line console's own Laser Cutter: the pairing of consoles and cutters, each once, within 45 m, with the least distance in all
            // (2026-11-27: the big lines' cutters moved 8 m out, nearer a narrow line's console than its own cutter; paired by nearest each,
            // the narrow stations took no line and the main station's console lost its ovens, so every hull piece was "NOTHING MAKES").
            var cutters = _labs.Where(l => l.Id == "laser-cutter").ToList();
            var lineConsoles = _consoles.Where(c => !_labs.Any(l => l.Id == "press" && Vector2.Distance(l.At, c.At) < 20)).ToList();
            var bestPick = new int[lineConsoles.Count];
            var pick = new int[lineConsoles.Count];
            var bestCost = float.MaxValue;
            const float Unpaired = 45;
            void Pair(int i, float cost, ulong used)
            {
                if (cost >= bestCost)
                    return;
                if (i == lineConsoles.Count)
                {
                    bestCost = cost;
                    pick.CopyTo(bestPick, 0);
                    return;
                }
                for (var k = 0; k < cutters.Count; k++)
                    if ((used & (1UL << k)) == 0 && Vector2.Distance(cutters[k].At, lineConsoles[i].At) is var d and < Unpaired)
                    {
                        pick[i] = k;
                        Pair(i + 1, cost + d, used | (1UL << k));
                    }
                pick[i] = -1;
                Pair(i + 1, cost + Unpaired, used);
            }
            Pair(0, 0, 0);
            var cutterOf = new Dictionary<VPart, (VPart Core, Vector2 At)>();
            for (var i = 0; i < lineConsoles.Count; i++)
                if (bestPick[i] >= 0)
                    cutterOf[lineConsoles[i].Console] = (cutters[bestPick[i]].Core, cutters[bestPick[i]].At);
            foreach (var c in _consoles)
            {
                if (!cutterOf.TryGetValue(c.Console, out var cutter))
                    continue;
                var at = cutter.At; // (the cutter's middle, not its Core: a narrow cutter's Core stands at its far end, 2026-11-27)
                var station = _stations.OrderBy(s => Vector2.Distance(s.At, at)).FirstOrDefault().Core;
                if (station == null)
                    continue;
                if (!lines.TryGetValue(station, out var l))
                    lines[station] = l = new();
                l.Add(c);
            }
            var used = new HashSet<VPart>();
            foreach (var (station, l) in lines)
            {
                var console = l[0].Console;
                used.Add(console);
                var sa = _at.GetValueOrDefault(station);
                Note($"links: the Building Station at ({sa.X:0}, {sa.Y:0}) orders through the console of the Extruder at ({l[0].At.X:0}, {l[0].At.Y:0}), {l.Count} line(s)");
                Cable(console, station, "Orders", 7.5f);
                // (its lines' jobs shared: each order's pieces split so the lines either side of its bay finish together, Sean 2026-11-27;
                // a player changes it on the station's Line split setting)
                Config(station, ("lineSplit", "Share"));
                for (var k = 0; k < l.Count; k++)
                {
                    var (at, other) = l[k];
                    used.Add(other);
                    if (k > 0)
                        Cable(console, NearestLab("extruder", at, 12), "Run", 8f);
                    // (a Panel Former takes one input: its Run from this console only)
                    if (NearestLab("panel-former", at, 30) is { } former)
                        Cable(console, former, "Run", 6.5f + 0.5f * k);
                }
            }
            foreach (var (at, station) in _stations.Where(s => !lines.ContainsKey(s.Core)))
            {
                var best = _consoles.Where(c => !used.Contains(c.Console)).OrderBy(c => _labs.Any(l => l.Id == "press" && Vector2.Distance(l.At, c.At) < 20) ? 0 : 1)
                    .ThenBy(c => Vector2.Distance(c.At, at)).FirstOrDefault();
                if (best.Console == null)
                {
                    Note($"links: no console for the Building Station at ({at.X:0}, {at.Y:0})");
                    continue;
                }
                used.Add(best.Console);
                Note($"links: the Building Station at ({at.X:0}, {at.Y:0}) orders through the console of the Extruder at ({best.At.X:0}, {best.At.Y:0}): every oven, press and Assembler");
                Cable(best.Console, station, "Orders", 7.5f);
                // (a Press, an Assembler and a Panel Former take one input each: their Run from this console only; the other press line's
                // Extruder is run from here too, as its pieces feed those presses)
                foreach (var m in _labs.Where(l => l.Id is "bake-oven" or "press"))
                    Cable(best.Console, m.Core, "Run", 9f);
                foreach (var c in _consoles.Where(c => c.Console != best.Console && !used.Contains(c.Console) && _labs.Any(l => l.Id == "press" && Vector2.Distance(l.At, c.At) < 20)))
                    Cable(best.Console, NearestLab("extruder", c.At, 12), "Run", 8f);
                if (_items.TryGetValue("Assembler", out var asms))
                    foreach (var (_, asm) in asms)
                        Cable(best.Console, asm, "Run", 7f);
            }
        }
    }
}
