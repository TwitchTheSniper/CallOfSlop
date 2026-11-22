using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Zero Day works (Sean's approved plan, the "Zero Day factory layout" doc, 2026-11-22): one site of 96 x 239 m on a planet that
/// makes every piece of the Zero Day frigate and builds it in an open yard. North to south: the generator hall, the materials row,
/// the inline extruder strip, three crane bays (armour lines A and B, C and D, the general line) and the assembly yard.
/// Built step by step through the game's own rules (<see cref="BlueprintScript"/>), so a step the rules refuse fails here with its
/// reason. Reads the part files as the game loads them: run after the part generators. Split by area into partial files
/// (ZeroDayWorks.*.cs); this one holds the site's frame, the floor and the shared helpers.
/// </summary>
public sealed partial class ZeroDayWorks : IPartGenerator
{
    public string Name => "zeroday_works";

    public const string File = "Constructs/Blueprints/ZeroDayWorks.json";

    public void Make(PartOutput o) => o.File(File, Script.Build().Text());

    /// <summary>
    /// The script. Places are in metres: x east across the site (0 at its west edge, 96 at its east), y up, z north to south as
    /// negative numbers (0 at the site's north edge, -239 at its south edge). Every line runs south, the way the pieces flow.
    /// </summary>
    private sealed partial class Script : BlueprintScript
    {
        private Script() : base("Zero Day Works")
        {
        }

        public const float FloorY = 0.3f; // the floor slabs' tops
        public const float Slab = 8f; // the Works Floor Slab's side
        public const int SlabsX = 12, SlabsZ = 30; // 96 x 240 m
        public const float SiteLong = SlabsZ * Slab;

        // The site's bands, north to south (z of each band's north edge; doc "Top-down layout" and "Exterior").
        public const float GenHallZ = 0f; // the generator hall and its fuel yard: 24 m
        public const float AnnexZ = -24f; // the materials row under the annex roof: 16 m
        public const float ExtruderZ = -40f; // the inline extruder strip at the head of each bay: 28 m
        public const float BaysZ = -68f; // the three crane bays: 131 m
        public const float YardZ = -199f; // the open assembly yard: 40 m (the staging strip on its north side)
        public const float ShedNorth = AnnexZ, ShedSouth = YardZ; // the one steel shed, 96 x 175 m

        // The bays across x (doc "Crane bays"): bay 1 32 m, bay 2 40 m (two 20 m spans), bay 3 24 m. Rail lines on the column rows.
        public const float Bay1West = 0f, Bay2West = 32f, Bay3West = 72f, SiteEast = 96f;

        // Paint by section (look.md): a warm concrete floor, steel columns, one accent a line, yellow cranes and controls.
        public const string Floor = "#8c8576", Lane = "#d8b02a", Steel = "#4a5058", Rail = "#22262b", Crane = "#f0c21a";
        public const string Refining = "#d17a22", Stock = "#c9a24a", LineA = "#3d7ab8", LineB = "#3d8ab8", LineC = "#4f8f45", LineD = "#a8365e", General = "#2f8f8a", Power = "#b5452b";

        public static readonly Orient Up = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ); // own +z north (to the site's +z)
        public static readonly Orient South = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ); // own +z south, down the lines
        public static readonly Orient East = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX); // own +z east
        public static readonly Orient West = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitX); // own +z west

        /// <summary>Parts welded on freeform, unturned: where their own origin is in the site (their Pos is in the weld's frame).</summary>
        private readonly Dictionary<VPart, Vector3> _welded = new();

        /// <summary>The site's floor slabs (i east, j south): null where none is laid.</summary>
        private readonly VPart?[,] _tiles = new VPart?[SlabsX, SlabsZ];

        public static Script Build()
        {
            var s = new Script
            {
                Description = "The Zero Day works: a 96 x 239 m site that makes every piece of the Zero Day frigate and builds it in its open yard. " +
                              "North to south: generator hall, materials row, the inline extruders, three crane bays (armour lines A to D, the general line), the assembly yard. Set it on flat ground on a planet.",
                Group = "Factories",
            };
            s.Lay();
            // A body past Construct.MaxLinks drops every later link without a word (welds included, so machines lose their beds):
            // fail here, with room kept for what players add.
            if (s.Live.LinkCount > Construct.MaxLinks * 3 / 4 || s.Live.PartCount > Construct.MaxParts * 3 / 4)
                throw new VehicleKitException($"Zero Day Works: {s.Live.PartCount} parts and {s.Live.LinkCount} links, past three quarters of a body's caps ({Construct.MaxParts}, {Construct.MaxLinks})");
            return s;
        }

        public static Vector3 V(float x, float y, float z) => new(x, y, z);

        private void Lay()
        {
            LayFloor();
            LayFrame();
            LayMaterials();
            LayArmourLines();
            LayGeneralLine();
            LayPower();
            LayYard();
        }

        /// <summary>
        /// The floor: Works Floor Slabs (8 x 8 m, 0.3 m thick, a top socket every metre) over the whole site. A part stands on a whole metre;
        /// the 2 m machine cells' middles are the odd metres.
        /// </summary>
        private void LayFloor()
        {
            for (var j = 0; j < SlabsZ; j++)
                for (var i = 0; i < SlabsX; i++)
                {
                    var at = V(Slab * (i + 0.5f), 0.15f, -Slab * (j + 0.5f));
                    var t = i == 0 && j == 0 ? Root("Works Floor Slab 8 m", at, Up) : Put("Works Floor Slab 8 m", at, Up, on: i > 0 ? _tiles[i - 1, j] : _tiles[i, j - 1]);
                    _tiles[i, j] = t;
                    Paint(t, Floor);
                    Finish(t, "concrete");
                }
        }

        /// <summary>The floor slab under (x, z).</summary>
        private VPart Tile(float x, float z) =>
            _tiles[(int)MathF.Floor(x / Slab), (int)MathF.Floor(-z / Slab)] ?? throw new VehicleKitException($"no floor at ({x}, {z})");

        /// <summary>Paints a part and hands it back.</summary>
        private new VPart Paint(VPart p, string colour)
        {
            base.Paint(p, colour);
            return p;
        }

        /// <summary>A part standing on the floor by its own socket <paramref name="by"/> at (x, z).</summary>
        private VPart On(string part, float x, float z, Orient o, string by = "bottom", string? paint = null, string? finish = null)
        {
            var p = Stand(part, V(x, FloorY, z), o, Tile(x, z), by);
            if (paint != null)
                Paint(p, paint);
            if (finish != null)
                Finish(p, finish);
            return p;
        }

        /// <summary>A named port's place on a part (vehicle coordinates) and the way it faces.</summary>
        private (Vector3 At, Vector3 Face) PortOf(VPart p, string name)
        {
            var np = p.Def.NamedPorts.FirstOrDefault(x => x.Name == name) ?? throw new VehicleKitException($"a {p.Name} has no port \"{name}\"");
            if (_welded.TryGetValue(p, out var origin))
                return (origin + np.At, ConstructParts.DirVector(np.Face)); // (a freeform part's place is in its own weld's frame: unturned, from where it was welded)
            ref var cp = ref Live.Parts[p.Index];
            return (FromRest(cp.Pos + Rot24.Apply(cp.Rot, np.At)), Rot24.Apply(cp.Rot, ConstructParts.DirVector(np.Face)));
        }

        public const float SegmentMax = 7.5f; // (a chute's or a pipe's segment reaches at most 8 m)

        /// <summary>
        /// A routed link laid square through the air: out of the first port along its face, up (or down) to <paramref name="height"/>,
        /// along x, then along z, then down into the second port along its face; long straight runs are cut into segments of at most
        /// <see cref="SegmentMax"/>.
        /// </summary>
        private void Duct(string type, VPart from, string fromPort, VPart to, string toPort, float height, string paint, Vector2? size = null)
        {
            var (a, fa) = PortOf(from, fromPort);
            var (b, fb) = PortOf(to, toPort);
            var a1 = a + fa * 1.0f;
            var b1 = b + fb * 1.0f;
            var pts = new List<Vector3> { a1, V(a1.X, height, a1.Z), V(b1.X, height, a1.Z), V(b1.X, height, b1.Z), b1 };
            var nodes = new List<Vector3>();
            var last = a;
            foreach (var q in pts)
            {
                if (Vector3.Distance(q, last) < 0.05f)
                    continue;
                var n = (int)MathF.Ceiling(Vector3.Distance(q, last) / SegmentMax);
                for (var k = 1; k <= n; k++)
                    nodes.Add(Vector3.Lerp(last, q, k / (float)n));
                last = q;
            }
            // (drop nodes that lie on a straight line between their neighbours and are needed for no length limit)
            Link(type, from, to, fromPort, toPort, paint, false, size, nodes.ToArray());
        }
    }
}
