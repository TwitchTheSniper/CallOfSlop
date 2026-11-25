using System.Numerics;
using System.Text.Json;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Zero Day Works II's links (Sean, 2026-11-25: none were laid) and its extruders' Run-out Tables. Each half of the site (west and east,
/// mirrored) is linked the same way, by chutes laid square through the air (<see cref="Script.Duct"/>, as the old works lays them):
/// <list type="bullet">
/// <item>The front refinery: the 18 Ore Silos chained in two snakes of nine (iron ore, stone), each into its refiner (the big Smelter and
/// the big Foundry, on steel); the Smelter's metal into the first Ore Bunker, which feeds the Foundry; the Foundry's steel into the second,
/// chained on to the third and fourth.</item>
/// <item>The back row: each refiner's Ore Silo behind it into it (iron ore, copper ore, stone, sand, sand, wood), each refiner into the
/// Materials Bin in front of it (metal, copper, steel, concrete, rubber, planks), the Foundries' second inputs from the bins and silos
/// beside them, the two spare bins chained as reserves (metal, steel).</item>
/// <item>The lines: each Extruder's Materials Bin chuted from a steel Ore Bunker and into the Extruder's back; each Line Console wired to
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

        /// <summary>How far an Extruder's Materials Bin and Line Console move back with it (on the floor's whole metres, behind the plinths).</summary>
        public const float BinBack = 8f;

        public const float SegmentMax = 7.5f; // (a chute's segment reaches at most 8 m)

        private readonly Dictionary<(string, float, float), Vector2> _shift = new();
        private readonly List<(string Id, Vector2 At, VPart Core, Vector2 Flow)> _labs = new();
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
        private void Cable(VPart console, VPart? to, string toPort, float height)
        {
            if (to == null)
            {
                _linkMissed++;
                return;
            }
            try
            {
                ref var cp = ref Live.Parts[console.Index];
                var at = FromRest(cp.Pos);
                var t = _free.TryGetValue(to, out var w) ? FromRest(w.Pos) : FromRest(Live.Parts[to.Index].Pos);
                Link("data", console, to, null, toPort, Crane, false, null, Cut(at, V(at.X, height, at.Z), V(t.X, height, at.Z), V(t.X, height, t.Z)));
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
            if (store != null)
                Config(store, ("material", material), ("fill", 100));
        }

        // ---------------------------------------------------------------- the works' links

        private void LayLinks()
        {
            foreach (var s in new[] { -1f, 1f })
            {
                Refinery(s);
                BackRow(s);
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

        /// <summary>The front refinery: ore silos to the big Smelter and Foundry, their metal and steel into the Ore Bunkers.</summary>
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
            var b = new[] { 16f, 24f, 32f, 40f }.Select(x => Find("Ore Bunker 4 m", P(s, x, 58))).ToArray();
            Fill(b[0], "metal");
            for (var k = 1; k < 4; k++)
                Fill(b[k], "steel");
            Chute(smelter, "Chute out", b[0], "Chute in", 17f); // (over the silos)
            Chute(b[0], "Chute out", foundry, "Chute in", 6.5f);
            Chute(foundry, "Chute out", b[1], "Chute in", 17.5f);
            Chute(b[1], "Chute out", b[2], "Chute in", 4f);
            Chute(b[2], "Chute out", b[3], "Chute in", 4f);
        }

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
            var bunker = new[] { 24f, 32f, 40f }.Select(x => Find("Ore Bunker 4 m", P(s, x, 58))).ToArray();
            var high = 16f;
            foreach (var (id, at, core, flow) in _labs.Where(l => l.Id == "extruder" && MathF.Sign(l.At.X) == s).OrderByDescending(l => l.At.Y))
            {
                var bin = Nearest("Materials Bin", at, 16);
                var console = Nearest("Line Console", at, 16);
                Fill(bin, "steel");
                // (the big lines from the second and third bunkers, the small line and the press line from the fourth)
                var from = at.Y > 60 ? bunker[0] : at.Y < -60 ? bunker[1] : bunker[2];
                Chute(from, "Chute out", bin, null, high);
                high += 0.5f;
                Chute(bin, null, core, "Chute in", 4.5f);
                if (console == null)
                    continue;
                Cable(console, core, "Run", 6f);
                if (NearestLab("panel-former", at, 30) is { } former)
                    Cable(console, former, "Run", 6.5f);
                foreach (var press in _labs.Where(l => l.Id == "press" && Vector2.Distance(l.At, at) < 20))
                    Cable(console, press.Core, "Run", 6.5f);
                if (_labs.Any(l => l.Id == "press" && Vector2.Distance(l.At, at) < 20) && Nearest("Assembler", at, 25) is { } asm)
                    Cable(console, asm, "Run", 7f);
                _consoles.Add((at, console));
            }
            // A press swaps its die from a Die Rack chuted into it (PressDieSwap): each rack into the press beside it.
            foreach (var x in new[] { 39f, 47f })
            {
                var rack = Find("Die Rack", P(s, x, -24));
                var press = _labs.Where(l => l.Id == "press").OrderBy(l => Vector2.Distance(l.At, P(s, x, -30))).FirstOrDefault().Core;
                Chute(rack, null, press, null, 4f);
            }
            // The Assembler's stock (Orders pull their inputs from the stores chuted in), from the back row's bins, over everything at 18 m.
            var assembler = Find("Assembler", P(s, 33, -33));
            var up = 18f;
            foreach (var x in new[] { 36f, 26f, 41f, 46f })
            {
                Chute(Find("Materials Bin", P(s, x, 114)), null, assembler, "Chute in", up);
                up += 0.5f;
            }
        }

        private readonly List<(Vector2 At, VPart Console)> _consoles = new();

        private readonly HashSet<(int, int)> _tileAt = new(); // (the layout's Roller Tiles, by whole metre)

        private static (int, int) Round(Vector2 v) => ((int)MathF.Round(v.X), (int)MathF.Round(v.Y));

        private VPart? Nearest(string name, Vector2 at, float r) =>
            _items.TryGetValue(name, out var l) ? l.Where(e => Vector2.Distance(e.At, at) < r).OrderBy(e => Vector2.Distance(e.At, at)).Select(e => e.P).FirstOrDefault() : null;

        private VPart? NearestLab(string id, Vector2 at, float r) =>
            _labs.Where(e => e.Id == id && Vector2.Distance(e.At, at) < r).OrderBy(e => Vector2.Distance(e.At, at)).Select(e => e.Core).FirstOrDefault();

        /// <summary>Each Building Station (a station takes one console) wired from the nearest Line Console not yet used.</summary>
        private void Orders()
        {
            var used = new HashSet<VPart>();
            foreach (var (at, station) in _stations)
            {
                var best = _consoles.Where(c => !used.Contains(c.Console)).OrderBy(c => Vector2.Distance(c.At, at)).FirstOrDefault();
                if (best.Console == null)
                {
                    Note($"links: no console for the Building Station at ({at.X:0}, {at.Y:0})");
                    continue;
                }
                used.Add(best.Console);
                Cable(best.Console, station, "Orders", 7.5f);
            }
        }
    }
}
