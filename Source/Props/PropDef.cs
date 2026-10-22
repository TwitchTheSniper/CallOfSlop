using System.Numerics;

namespace CallOfSlop;

/// <summary>How a prop interacts with the world (the "collision" key of a prop file).</summary>
public enum PropCollision : byte
{
    /// <summary>Blocks movement, bullets and line of sight (its collider boxes join the map's solid boxes).</summary>
    Solid,

    /// <summary>Looks only: walked and shot straight through, like grass. Small litter.</summary>
    Cosmetic,
}

/// <summary>What can break a breakable prop (the "breakBy" list of its "breakable" block).</summary>
[Flags]
public enum PropDamage : byte
{
    None = 0,
    Bullet = 1,
    Melee = 2,
    Explosion = 4,
    Streak = 8,
    All = Bullet | Melee | Explosion | Streak,
}

/// <summary>The sound a prop makes when it breaks.</summary>
public enum PropBreakSound : byte
{
    Wood,
    Glass,
    Metal,
    Soft, // cardboard, plastic, paper
}

/// <summary>One box of a prop's model, in the prop's own frame (metres, origin on the ground at the prop's centre).</summary>
public sealed class PropPart
{
    public Vector3 Size;
    public Vector3 Offset; // centre of the box
    public Vector3 RotationDeg; // pitch (about X), yaw (about Y), roll (about Z), applied as yaw * pitch * roll
    public Vector3 Color;

    /// <summary>Texture layer from <see cref="Material"/>, or -1 for a plain colour.</summary>
    public int Layer = -1;

    public float TexScale = 1f;

    /// <summary>The part's "anim" block (cosmetic motion or light, see <see cref="PropAnimFile"/>), or null for a still part.</summary>
    public PropPartAnim? Anim;

    /// <summary>
    /// Built in code only (flag cloth pieces, <see cref="FlagPool"/>): the box's 8 corners in the prop's frame, indexed
    /// like <see cref="Bounds"/> (bit 0: +x side, bit 1: +y, bit 2: +z), so a part can be a slanted slab (a diagonal
    /// stripe cut to a cloth segment). Null for a normal box: <see cref="Size"/>, <see cref="Offset"/> and the rotation.
    /// </summary>
    public Vector3[]? Corners;

    public Quaternion Rotation => Quaternion.CreateFromYawPitchRoll(RotationDeg.Y * Deg, RotationDeg.X * Deg, RotationDeg.Z * Deg);

    private const float Deg = MathF.PI / 180f;

    /// <summary>The part's axis-aligned bounds in the prop's frame (after its rotation).</summary>
    public Box Bounds
    {
        get
        {
            if (Corners is { Length: 8 } cs)
            {
                Vector3 lo = cs[0], hi = cs[0];
                foreach (var c in cs)
                {
                    lo = Vector3.Min(lo, c);
                    hi = Vector3.Max(hi, c);
                }
                return new Box((lo + hi) / 2, (hi - lo) / 2);
            }
            var q = Rotation;
            var h = Size / 2;
            var ext = Vector3.Zero;
            for (var i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z);
                ext = Vector3.Max(ext, Vector3.Abs(Vector3.Transform(c, q)));
            }
            return new Box(Offset, ext);
        }
    }
}

/// <summary>The "breakable" block of a prop file.</summary>
public sealed class PropBreakable
{
    public int Health = 50;
    public PropDamage BreakBy = PropDamage.All;
    public int DebrisCount = 8;
    public float DebrisMin = 0.06f, DebrisMax = 0.18f;
    public float DebrisSpeed = 4f;
    public Vector3[] DebrisColors = []; // empty: taken from the parts (weighted by size)
    public PropBreakSound Sound = PropBreakSound.Wood;

    /// <summary>An explosive prop (fuel drum) goes off when it breaks: blast radius (0 = it doesn't) and damage at the centre.</summary>
    public float ExplodeRadius, ExplodeDamage;

    /// <summary>
    /// Seconds an explosive prop burns after the hit that breaks it before it goes off (0 = at once, the old behaviour).
    /// While lit it stays whole (sparks and a hiss on every PC); one lit by another prop's blast goes off sooner
    /// (<see cref="Game.ChainFuse"/>), so a cluster ripples instead of popping in one tick.
    /// </summary>
    public float ExplodeFuse;

    /// <summary>An explosive prop's blast knockback (m/s at the centre, falling off to nothing at <see cref="ExplodeRadius"/>; Knockback.cs).</summary>
    public float ExplodeKnockback = Knockback.DefaultProp;

    /// <summary>
    /// A pressure plate (the "trigger" block of "explode"; 0 = none): a living player, bot or zombie whose feet come within
    /// this many metres of the prop (horizontally, scaled with the prop) and no more than <see cref="TriggerHeight"/> above
    /// its base lights it (a click), and it goes off after <see cref="ExplodeFuse"/>. A prop with a trigger is a mine: its
    /// blast is neutral and hurts everyone (see Game.PropExplode), and its kills read "Landmine".
    /// </summary>
    public float TriggerRadius;

    /// <summary>How high above the prop's base (metres) feet still press the plate: jump over it and nothing happens.</summary>
    public float TriggerHeight = 0.5f;

    /// <summary>Has a pressure plate (a landmine).</summary>
    public bool IsMine => TriggerRadius > 0;
}

/// <summary>
/// One prop type from a prop file (see Props/README.md): a model built from boxes, how it collides, scatter hints for the
/// Clutter map op and optionally how it breaks. Props are drawn instanced (<see cref="PropRenderer"/>); solid ones also
/// add collider boxes to the map (<see cref="Map.PlaceProp"/>).
/// </summary>
public sealed class PropDef
{
    public required string Name;
    public string Category = "Misc";
    public string Description = "";
    public string Author = "";
    public string? SourceFile;
    public PropCollision Collision = PropCollision.Solid;
    public readonly List<PropPart> Parts = new();

    /// <summary>Collision boxes in the prop's frame (solid props only).</summary>
    public Box[] Colliders = [];

    /// <summary>The whole model's bounds in the prop's frame.</summary>
    public Box Bounds;

    // Scatter hints (used by the Clutter op).
    public bool RandomYaw = true;
    public float YawStep, YawJitter;
    public float ScaleMin = 0.9f, ScaleMax = 1.1f;
    public bool GroundSnap = true;
    public float Weight = 1f;

    /// <summary>Cosmetic props dither out between 80% and 100% of this distance (metres). Solid props never fade.</summary>
    public float FadeDistance;

    public PropBreakable? Breakable;

    /// <summary>The "flag" block: which parts are the flag's cloth, so a flag design from <see cref="FlagPool"/> can replace it. Null for other props.</summary>
    public PropFlagCloth? FlagCloth;

    /// <summary>For a flag variant built by <see cref="FlagPool.Variant"/>: the design's id ("us", "uk"...); null for a prop as its file has it.</summary>
    public string? FlagId;

    /// <summary>For a flag variant: the prop it was built from (same name, colliders and fingerprint; only the cloth differs).</summary>
    public PropDef? FlagBase;

    /// <summary>Position in <see cref="Props.All"/>, or for a pack prop in its pack's <see cref="PropScope.Library"/> (the mesh index).</summary>
    public int Index;

    /// <summary>
    /// The pack folder (in Maps) a pack-local prop comes from (Maps/&lt;Pack&gt;/Props), or null for a prop in the global
    /// Props folder. Pack props are only visible to that pack's maps.
    /// </summary>
    public string? Pack;

    /// <summary>Unique across every library: the name for a global prop, "Pack/Name" for a pack prop (two packs may both have a "Crate").</summary>
    /// <remarks>A flag variant adds "~id" ("Flag on a Pole~uk"), so each design is its own instanced mesh.</remarks>
    public string Key => (Pack == null ? Name : Pack + "/" + Name) + (FlagId == null ? "" : "~" + FlagId);

    /// <summary>
    /// A fingerprint of a pack prop's file (its JSON with whitespace and comments dropped; 0 for global props). Maps
    /// mix it into their fingerprint, so a player with a different copy of the pack's prop gets the map mismatch message.
    /// </summary>
    public uint FileHash;

    public bool IsSolid => Collision == PropCollision.Solid;

    /// <summary>The default fade distance for a cosmetic prop of this size: small litter goes first.</summary>
    public static float DefaultFade(Box bounds)
    {
        var size = MathF.Max(bounds.Half.X, MathF.Max(bounds.Half.Y, bounds.Half.Z)) * 2;
        return Math.Clamp(28f + size * 55f, 28f, 90f);
    }

    /// <summary>The debris colours: the breakable block's, or the parts' colours weighted by size.</summary>
    public Vector3 DebrisColor(float r)
    {
        if (Breakable is { DebrisColors.Length: > 0 } b)
            return b.DebrisColors[Math.Min((int)(r * b.DebrisColors.Length), b.DebrisColors.Length - 1)];
        var total = 0f;
        foreach (var p in Parts)
            total += MathF.Cbrt(p.Size.X * p.Size.Y * p.Size.Z);
        var pick = r * total;
        foreach (var p in Parts)
        {
            pick -= MathF.Cbrt(p.Size.X * p.Size.Y * p.Size.Z);
            if (pick <= 0)
                return p.Color;
        }
        return Parts[^1].Color;
    }
}
