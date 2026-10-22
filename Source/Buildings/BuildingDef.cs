using System.Numerics;

namespace CallOfSlop;

/// <summary>A building file, a furnishing file or a building op is wrong (the message says which file and where).</summary>
public sealed class BuildingFileException(string message) : Exception(message);

/// <summary>A rectangle on the ground plan (metres, building coordinates): x0 &lt; x1, z0 &lt; z1.</summary>
public readonly record struct BRect(float X0, float Z0, float X1, float Z1)
{
    public float W => X1 - X0;
    public float D => Z1 - Z0;
    public float Area => W * D;
    public Vector2 Center => new((X0 + X1) / 2, (Z0 + Z1) / 2);

    /// <summary>Shrunk by <paramref name="m"/> on every side (grown when negative).</summary>
    public BRect Inset(float m) => new(X0 + m, Z0 + m, X1 - m, Z1 - m);

    public bool Contains(float x, float z, float eps = 0f) => x >= X0 - eps && x <= X1 + eps && z >= Z0 - eps && z <= Z1 + eps;

    /// <summary>Do the two rectangles share area (touching edges don't count)?</summary>
    public bool Overlaps(BRect o, float eps = 0.001f) => o.X0 < X1 - eps && o.X1 > X0 + eps && o.Z0 < Z1 - eps && o.Z1 > Z0 + eps;

    public BRect Union(BRect o) => new(MathF.Min(X0, o.X0), MathF.Min(Z0, o.Z0), MathF.Max(X1, o.X1), MathF.Max(Z1, o.Z1));

    /// <summary>This rectangle minus <paramref name="cut"/>, as up to four pieces (north, south, west, east strips).</summary>
    public IEnumerable<BRect> Minus(BRect cut)
    {
        if (!Overlaps(cut))
        {
            yield return this;
            yield break;
        }
        var cz0 = MathF.Max(Z0, cut.Z0);
        var cz1 = MathF.Min(Z1, cut.Z1);
        if (cut.Z0 > Z0 + 0.001f)
            yield return new BRect(X0, Z0, X1, cut.Z0);
        if (cut.Z1 < Z1 - 0.001f)
            yield return new BRect(X0, cut.Z1, X1, Z1);
        if (cut.X0 > X0 + 0.001f)
            yield return new BRect(X0, cz0, cut.X0, cz1);
        if (cut.X1 < X1 - 0.001f)
            yield return new BRect(cut.X1, cz0, X1, cz1);
    }

    /// <summary>Pieces of every rectangle in <paramref name="rects"/> minus every rectangle in <paramref name="cuts"/>.</summary>
    public static List<BRect> Subtract(IEnumerable<BRect> rects, IEnumerable<BRect> cuts)
    {
        var list = rects.ToList();
        foreach (var c in cuts)
            list = list.SelectMany(r => r.Minus(c)).Where(r => r.W > 0.01f && r.D > 0.01f).ToList();
        return list;
    }
}

/// <summary>A colour in a building file: a literal colour, or a palette key ("$wall") resolved per variant.</summary>
public readonly record struct ColorRef(Vector3 Value, string? Key = null)
{
    public Vector3 Resolve(IReadOnlyDictionary<string, Vector3> palette) =>
        Key != null && palette.TryGetValue(Key, out var c) ? c : Value;
}

/// <summary>A surface: a colour and a texture layer (<see cref="Material"/>, or -1 for plain colour).</summary>
public readonly record struct BSurface(ColorRef Color, int Layer);

public enum RoofKind : byte { Flat, Gable, Hip, Shed, None }

public enum OpeningKind : byte
{
    /// <summary>A doorway with a door leaf that opens, closes and breaks.</summary>
    Door,

    /// <summary>A window with shootable glass.</summary>
    Window,

    /// <summary>A doorway without a door (an arch, an open-plan gap).</summary>
    Arch,

    /// <summary>A hole with no glass at any height (a ruin's breach, a hatch, a serving hatch).</summary>
    Hole,
}

public enum DoorMaterial : byte { Wood, Metal, Glass }

/// <summary>One opening in a wall: the point "at" lies on the wall's centre line.</summary>
public sealed class BOpening
{
    public OpeningKind Kind;
    public float X, Z;
    public float Width = 1f;
    public float Bottom; // above the floor
    public float Top = 2.1f;
    public DoorMaterial Material = DoorMaterial.Wood;
    public bool HingeRight;
    public bool SwingOut;
    public bool StartOpen;
    public bool Front;
    public int Health; // 0: from the material
    public ColorRef? Color; // door leaf / frame colour (default: the "door" palette key)
    public bool Auto; // made by "autoWindows"
}

/// <summary>Automatic windows along every exterior wall of a floor (or one room).</summary>
public sealed class BAutoWindows
{
    public float Spacing = 3f;
    public float Width = 1.2f;
    public float Sill = 0.9f;
    public float Head = 2.2f;
    public int Health;
}

public enum FurnishMode : byte { Auto, None, Explicit }

/// <summary>An explicit furniture placement in a room (room-centre relative, or against a named wall).</summary>
public sealed class BPlacement
{
    public required string Prop;
    public float X, Z; // from the room's centre
    public char Wall; // 'N', 'S', 'E', 'W' or 0
    public float Along; // along the wall from its middle (with Wall)
    public float Yaw; // degrees, added to the wall facing
    public float Y; // height above the floor
    public float Scale = 1f;
}

public sealed class BRoom
{
    public required string Id;
    public required string Type;
    public required List<BRect> Rects;
    public BSurface? Floor, Wall, Ceiling;
    public FurnishMode Furnish = FurnishMode.Auto;
    public readonly List<BPlacement> Props = new();
    public bool Windows = true;
    public BAutoWindows? AutoWindows;
    public readonly List<string> OpenTo = new();
    public int Seed;
    public string? Rules; // furnishing rule type to use instead of Type
}

public sealed class BFloor
{
    public float Height;
    public readonly List<BRoom> Rooms = new();
    public readonly List<BOpening> Openings = new();
    public BAutoWindows? AutoWindows;
    public bool Repeat;
}

public sealed class BStair
{
    public int Floor;
    public BRect Rect;
    public int Dx, Dz; // climbing direction
    public BSurface? Surface;
    public bool Railing = true;

    /// <summary>On a "repeat" floor: every other copy uses this rect and direction instead (so flights alternate, like a switchback).</summary>
    public BRect AltRect;

    public int AltDx, AltDz;
    public bool HasAlt => AltRect.W > 0;
}

public sealed class BRoof
{
    public RoofKind Kind = RoofKind.Flat;
    public char Ridge; // 'x', 'z' or 0 (along the longer side)
    public float Pitch = 30f;
    public float Overhang = 0.3f;
    public BSurface? Surface;
    public float Parapet = 0.6f;
    public int ShedDx, ShedDz = 1; // a shed roof rises towards this side
}

public enum ExtraKind : byte { Block, Prop, Porch, Balcony, Awning, Platform }

public sealed class BExtra
{
    public ExtraKind Kind;
    public BRect Rect;
    public int Floor;
    public float Height = 2.6f;
    public float Railing = 1f;
    public bool Roof = true;
    public bool Posts = true;
    public Vector3 Min, Max;
    public BSurface? Surface;
    public string? Prop;
    public Vector3 At;
    public float Yaw;
    public float Scale = 1f;
    public bool OnRoof; // a prop: its y is above the roof (follows the floor count)
    public BRect StairRect; // platform: its stairs (zero size: none)
    public int StairDx, StairDz;
}

public sealed class BAnchor
{
    public required string Type; // spawn, hill, flag, point
    public string Name = "";
    public int Floor;
    public float X, Z;
}

/// <summary>
/// One building type from Buildings/&lt;Name&gt;/building.json (see Buildings/README.md): rooms on floors, openings, stairs,
/// a roof, exterior extras, furnishing, palettes and its own props. <see cref="BuildingGen"/> turns it into boxes, doors,
/// windows and props for one placement.
/// </summary>
public sealed class BuildingDef
{
    public required string Name;
    public string Theme = "";
    public string Category = "residential";
    public string Description = "";
    public string Author = "";
    public string Folder = "";
    public string? SourceFile;
    public int Seed;
    public float WallThickness = 0.25f;
    public float StoreyHeight = 3f;
    public float DoorHeight = 2.1f;
    public BSurface ExteriorWall = new(new ColorRef(new Vector3(0.62f, 0.58f, 0.52f), "wall"), (int)Material.Concrete);
    public BSurface InteriorWall = new(new ColorRef(new Vector3(0.86f, 0.83f, 0.76f), "interior"), -1);
    public BSurface FloorSurface = new(new ColorRef(new Vector3(0.5f, 0.42f, 0.32f), "floor"), (int)Material.Wood);
    public BSurface CeilingSurface = new(new ColorRef(new Vector3(0.9f, 0.9f, 0.88f), "ceiling"), -1);
    public BSurface TrimSurface = new(new ColorRef(new Vector3(0.9f, 0.9f, 0.9f), "trim"), -1);
    public ColorRef DoorColor = new(new Vector3(0.45f, 0.3f, 0.2f), "door");
    public readonly List<BFloor> Floors = new();
    public readonly List<BStair> Stairs = new();
    public BRoof Roof = new();
    public readonly List<BExtra> Extras = new();
    public readonly List<BAnchor> Anchors = new();

    /// <summary>Palettes by name, in file order (the first is the default). Each maps a key ("wall") to a colour.</summary>
    public readonly List<(string Name, Dictionary<string, Vector3> Colors)> Palettes = new();

    /// <summary>The building's own props (Buildings/&lt;Name&gt;/Props), looked up before the map's props.</summary>
    public PropDef[] LocalProps = [];

    /// <summary>The building's own furnishing rules (Buildings/&lt;Name&gt;/Furnishing), used before the shared ones.</summary>
    public readonly Dictionary<string, FurnishRule> LocalRules = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The furnishing rule for a room type: the building's own, else the shared one, else null.</summary>
    public FurnishRule? Rule(string type) => LocalRules.TryGetValue(type, out var r) ? r : CallOfSlop.Buildings.Rule(type);

    /// <summary>Fingerprint of building.json and its props (whitespace and comments aside).</summary>
    public uint FileHash;

    /// <summary>How many floors a copy gets when its variant doesn't say ("floorCount"; default: the floors listed).</summary>
    public int DefaultFloors;

    public int MinFloors => Floors.Count;
    public int MaxFloors => Floors.Any(f => f.Repeat) ? 12 : Floors.Count;

    /// <summary>The palette named <paramref name="name"/> (null or unknown: the first).</summary>
    public Dictionary<string, Vector3> Palette(string? name)
    {
        foreach (var p in Palettes)
            if (name != null && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return p.Colors;
        return Palettes.Count > 0 ? Palettes[0].Colors : new Dictionary<string, Vector3>();
    }

    /// <summary>A prop by name: the building's own first, then <paramref name="scope"/> ("global:" skips the building).</summary>
    public PropDef? FindProp(string name, PropScope scope)
    {
        if (!PropScope.IsGlobal(name, out _))
            foreach (var p in LocalProps)
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return p;
        return scope.Find(name);
    }
}

/// <summary>The "variant" of one Building op: what may differ between two copies of the same building.</summary>
public sealed class BuildingVariant
{
    public string? Palette;
    public int Floors; // 0: the file's own count
    public bool Mirror;
    public RoofKind? Roof;
    public bool Furnish = true;
    public string Doors = "closed"; // closed, open, random

    public static readonly BuildingVariant Default = new();
}
