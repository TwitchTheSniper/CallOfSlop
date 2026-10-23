using System.Numerics;

namespace CallOfSlop;

/// <summary>What colour a <see cref="BodyPart"/> takes (resolved through <see cref="BodyColors"/>).</summary>
public enum BodyPaint : byte
{
    Shirt, ShirtDark, Sleeve, Vest, Pouch, Accent, Skin, SkinShade, Hair, Gear, GearDark, Eyes,
}

/// <summary>What a body wears on its head when no prestige hat is on (a hat replaces it; hair stays only at the back).</summary>
public enum Headwear : byte
{
    Hair, Buzz, Helmet, Cap, Bandana,
}

/// <summary>One box of the torso or head: size and centre in the reference frame (metres), its paint, and whether it is a small detail dropped at distance.</summary>
public readonly record struct BodyPart(Vector3 Size, Vector3 Center, BodyPaint Paint, bool Detail = false);

/// <summary>
/// Where the torso and head sit for one stance, in the upper body's frame (feet at the origin, -Z = facing). The sizes are
/// the old single torso and head boxes; the parts are laid out in that envelope, so the silhouette matches the hit box as before.
/// </summary>
public readonly record struct BodyStance(Vector3 TorsoSize, Vector3 TorsoAt, Vector3 HeadSize, Vector3 HeadAt, bool Lying)
{
    /// <summary>The torso frame in the upper body's frame. Lying (prone): the torso's up points forward (-Z) and its chest down (-Y).</summary>
    public Matrix4x4 TorsoFrame => Lying ? Matrix4x4.CreateRotationX(-MathF.PI / 2) * Matrix4x4.CreateTranslation(TorsoAt) : Matrix4x4.CreateTranslation(TorsoAt);

    /// <summary>Per-axis scale from the reference (standing) torso to this stance's torso, in the torso frame.</summary>
    public Vector3 TorsoScale => Lying
        ? new Vector3(TorsoSize.X / BodyModel.RefTorso.X, TorsoSize.Z / BodyModel.RefTorso.Y, TorsoSize.Y / BodyModel.RefTorso.Z)
        : TorsoSize / BodyModel.RefTorso;

    /// <summary>Per-axis scale from the reference (standing) head to this stance's head.</summary>
    public Vector3 HeadScale => HeadSize / BodyModel.RefHead;
}

/// <summary>A body's look, picked deterministically from the player's name (<see cref="BodyModel.VariantFor(string, byte)"/>), plus the APPEARANCE screen's picks (<see cref="PlayerLook"/>).</summary>
public readonly record struct BodyVariant(byte Skin, byte HairColor, Headwear Headwear, bool Beard, byte Gear);

/// <summary>The resolved colours of one body (team colour + variant).</summary>
public struct BodyColors
{
    public Vector3 Shirt, ShirtDark, Sleeve, Vest, Pouch, Accent, Skin, SkinShade, Hair, Gear, GearDark, Eyes;

    /// <summary>A soldier in <paramref name="color"/> (the player or team colour): shirt and shoulders in it (the shoulders match the arm sleeves), a vest muted towards olive, a bright team patch.</summary>
    public static BodyColors For(Vector3 color, in BodyVariant v)
    {
        var skin = BodyModel.SkinTones[v.Skin % BodyModel.SkinTones.Length];
        var gear = BodyModel.GearColors[v.Gear % BodyModel.GearColors.Length];
        var vest = Vector3.Lerp(color, new Vector3(0.24f, 0.25f, 0.2f), 0.5f) * 0.85f;
        return new BodyColors
        {
            Shirt = color * 0.8f, ShirtDark = color * 0.5f, Sleeve = color * 0.75f, Vest = vest, Pouch = vest * 0.72f, Accent = color * 1.1f,
            Skin = skin, SkinShade = skin * 0.86f, Hair = BodyModel.HairColors[v.HairColor % BodyModel.HairColors.Length], Gear = gear, GearDark = gear * 0.7f,
            Eyes = new Vector3(0.05f, 0.05f, 0.08f),
        };
    }

    /// <summary>A zombie: a torn shirt, green skin.</summary>
    public static BodyColors Zombie(Vector3 shirt, Vector3 skin) => new()
    {
        Shirt = shirt, ShirtDark = shirt * 0.7f, Sleeve = shirt, Vest = shirt, Pouch = shirt, Accent = shirt, Skin = skin, SkinShade = skin * 0.82f,
        Hair = skin * 0.5f, Gear = shirt, GearDark = shirt, Eyes = new Vector3(0.05f, 0.05f, 0.08f),
    };

    public readonly Vector3 Of(BodyPaint p) => p switch
    {
        BodyPaint.Shirt => Shirt, BodyPaint.ShirtDark => ShirtDark, BodyPaint.Sleeve => Sleeve, BodyPaint.Vest => Vest, BodyPaint.Pouch => Pouch,
        BodyPaint.Accent => Accent, BodyPaint.Skin => Skin, BodyPaint.SkinShade => SkinShade, BodyPaint.Hair => Hair, BodyPaint.Gear => Gear,
        BodyPaint.GearDark => GearDark, _ => Eyes,
    };
}

/// <summary>
/// The parts of one torso or head, built for a stance, variant and colour, ready to draw: as a scene-graph custom renderable
/// on the torso / head node (<see cref="PlayerRig"/>) or immediately (<see cref="PlayerRender"/>), with identical matrices.
/// Rebuilt in place only when the look changes (fixed capacity, no allocations). Detail parts are skipped while
/// <see cref="ShowDetail"/> is off (far away).
/// </summary>
public sealed class BodyParts : IRenderable
{
    public const int Capacity = 12;
    public readonly Matrix4x4[] Local = new Matrix4x4[Capacity];
    public readonly Renderable[] Draws = new Renderable[Capacity];
    public readonly bool[] Detail = new bool[Capacity];
    public readonly BodyPaint[] Paint = new BodyPaint[Capacity];
    public int Count;
    public bool ShowDetail = true;

    public void Clear() => Count = 0;

    /// <summary>Appends <paramref name="part"/> scaled per axis by <paramref name="scale"/> (centre and size).</summary>
    public void Add(in BodyPart part, Vector3 scale, in BodyColors colors)
    {
        if (Count == Capacity)
            return;
        Local[Count] = Matrix4x4.CreateScale(part.Size * scale) * Matrix4x4.CreateTranslation(part.Center * scale);
        Draws[Count] = Renderable.Box(colors.Of(part.Paint));
        Detail[Count] = part.Detail;
        Paint[Count] = part.Paint;
        Count++;
    }

    /// <summary>How many parts draw right now (detail parts only up close).</summary>
    public int Visible
    {
        get
        {
            var n = 0;
            for (var i = 0; i < Count; i++)
                if (ShowDetail || !Detail[i])
                    n++;
            return n;
        }
    }

    public void Emit(DrawList list, SceneNode node)
    {
        ref readonly var w = ref node.World;
        for (var i = 0; i < Count; i++)
            if (ShowDetail || !Detail[i])
                list.Add(Local[i] * w, Draws[i]);
    }

    /// <summary>Immediate mode: the same draws as <see cref="Emit"/> under <paramref name="frame"/> (the node's world matrix).</summary>
    public void DrawImmediate(Renderer g, in Matrix4x4 frame)
    {
        for (var i = 0; i < Count; i++)
            if (ShowDetail || !Detail[i])
            {
                ref readonly var r = ref Draws[i];
                g.Draw(Local[i] * frame, r.Color, r.Alpha, r.Mode, r.Material);
            }
    }
}

/// <summary>
/// The torso and head of the player / bot model (people and bots share it), in the same blocky style as the legs
/// (<see cref="LegAnimator.LegParts"/>) and arms (<see cref="ArmRig"/>): a shirt with chest, abdomen and shoulders that meet
/// the arms, a plate carrier (front and back plates, cummerbund, mag pouches, a team patch), collar and neck; a head with
/// cranium, jaw, brow, nose, eyes and ears, plus hair or headwear and a beard per seeded variant (bots by personality).
/// The head node, the cranium top and its width are where the old head box was, so prestige hats sit exactly as before
/// (a hat replaces the headwear). Presentation only: hit boxes come from <see cref="World.Hitbox(Vector3, Stance, float)"/>.
/// Zombies get a torn-shirt, green-skinned variant of the same shapes (<see cref="DrawZombieUpper"/>).
/// </summary>
public static class BodyModel
{
    /// <summary>The reference (standing) torso and head box sizes the parts are laid out in.</summary>
    public static readonly Vector3 RefTorso = new(0.8f, 0.5f, 0.5f), RefHead = new(0.45f, 0.4f, 0.45f);

    /// <summary>Beyond this distance from the camera (<see cref="LegAnimator.Viewer"/>) the detail parts (pouches, patch, collar, eyes, ears) are skipped.</summary>
    public static float DetailRange = 30f;

    /// <summary>Stand, crouch, prone: the old torso and head boxes (unchanged), which the parts fill.</summary>
    public static readonly BodyStance[] Stances = MakeStances();

    private static BodyStance[] MakeStances()
    {
        var headH = World.PlayerHeight - World.HeadStart;
        const float hip = LegAnimator.StandPelvis;
        const float body = 0.85f, top = 1.2f, chip = LegAnimator.CrouchPelvis - 0.04f;
        return
        [
            new(new Vector3(0.8f, World.HeadStart - hip, 0.5f), new Vector3(0, (World.HeadStart + hip) / 2, 0), new Vector3(0.45f, headH, 0.45f),
                new Vector3(0, World.HeadStart + headH / 2, 0), false),
            new(new Vector3(0.8f, body - chip, 0.6f), new Vector3(0, (body + chip) / 2, 0), new Vector3(0.45f, top - body, 0.45f),
                new Vector3(0, (body + top) / 2, -0.05f), false),
            new(new Vector3(0.6f, 0.4f, 0.6f), new Vector3(0, 0.2f, -0.27f), new Vector3(0.4f, 0.38f, 0.4f), new Vector3(0, 0.3f, -0.75f), true),
        ];
    }

    /// <summary>The stance index the model uses (0 stand, 1 crouch, 2 prone).</summary>
    public static int StanceIndex(Stance s) => s switch { Stance.Prone => 2, Stance.Crouch => 1, _ => 0 };

    // ---- Parts (reference frames: the torso box centre, the head box centre; -Z = facing) ----

    /// <summary>Torso: shirt (abdomen, chest), shoulders (sleeve colour, spanning the chest so one box does both), plate carrier, pouches, patch, collar, neck.</summary>
    public static readonly BodyPart[] TorsoParts =
    [
        new(new(0.6f, 0.3f, 0.36f), new(0, -0.1f, 0.01f), BodyPaint.Shirt), // abdomen
        new(new(0.72f, 0.24f, 0.42f), new(0, 0.13f, 0), BodyPaint.Shirt), // chest
        new(new(0.8f, 0.15f, 0.3f), new(0, 0.2f, 0.03f), BodyPaint.Sleeve), // shoulders (the arms start here)
        new(new(0.5f, 0.34f, 0.07f), new(0, 0.05f, -0.235f), BodyPaint.Vest), // front plate
        new(new(0.5f, 0.36f, 0.06f), new(0, 0.06f, 0.23f), BodyPaint.Vest), // back plate
        new(new(0.64f, 0.14f, 0.44f), new(0, -0.12f, 0), BodyPaint.Vest), // cummerbund
        new(new(0.42f, 0.1f, 0.06f), new(0, -0.06f, -0.29f), BodyPaint.Pouch, true), // mag pouches
        new(new(0.14f, 0.07f, 0.012f), new(-0.13f, 0.15f, -0.275f), BodyPaint.Accent, true), // team patch
        new(new(0.3f, 0.06f, 0.28f), new(0, 0.26f, 0.01f), BodyPaint.ShirtDark, true), // collar
        new(new(0.17f, 0.12f, 0.17f), new(0, 0.28f, 0.02f), BodyPaint.Skin), // neck (reaches into the head)
    ];

    /// <summary>The cranium: its top is the old head box's top and it is nearly as wide, so hats fit as before.</summary>
    public static readonly BodyPart Cranium = new(new(0.44f, 0.24f, 0.44f), new(0, 0.08f, 0.005f), BodyPaint.Skin);

    /// <summary>Head: cranium, jaw and chin, brow, nose, eyes (one dark band split by the nose bridge), ears (one box through the head).</summary>
    public static readonly BodyPart[] HeadParts =
    [
        Cranium,
        new(new(0.36f, 0.17f, 0.38f), new(0, -0.115f, -0.02f), BodyPaint.Skin), // jaw and chin
        new(new(0.38f, 0.045f, 0.05f), new(0, 0.035f, -0.21f), BodyPaint.SkinShade), // brow
        new(new(0.065f, 0.1f, 0.07f), new(0, -0.04f, -0.235f), BodyPaint.SkinShade), // nose
        new(new(0.25f, 0.035f, 0.012f), new(0, 0.004f, -0.217f), BodyPaint.Eyes, true), // eyes
        new(new(0.5f, 0.085f, 0.075f), new(0, -0.02f, 0.03f), BodyPaint.SkinShade, true), // ears
    ];

    private static readonly BodyPart HairTop = new(new(0.452f, 0.05f, 0.456f), new(0, 0.19f, 0.012f), BodyPaint.Hair);
    private static readonly BodyPart HairBack = new(new(0.452f, 0.16f, 0.05f), new(0, 0.07f, 0.215f), BodyPaint.Hair);
    private static readonly BodyPart BuzzTop = new(new(0.446f, 0.03f, 0.446f), new(0, 0.19f, 0.003f), BodyPaint.Hair);

    /// <summary>Headwear parts by <see cref="Headwear"/>.</summary>
    public static readonly BodyPart[][] HeadwearParts =
    [
        [HairTop, HairBack],
        [BuzzTop],
        [
            new(new(0.5f, 0.14f, 0.5f), new(0, 0.155f, 0.01f), BodyPaint.Gear), // helmet shell
            new(new(0.53f, 0.03f, 0.54f), new(0, 0.085f, 0.015f), BodyPaint.GearDark), // rim
        ],
        [
            new(new(0.456f, 0.075f, 0.46f), new(0, 0.175f, 0.01f), BodyPaint.Gear), // patrol cap crown
            new(new(0.3f, 0.02f, 0.13f), new(0, 0.145f, -0.285f), BodyPaint.GearDark), // peak
        ],
        [
            new(new(0.452f, 0.055f, 0.452f), new(0, 0.115f, 0), BodyPaint.Accent), // bandana in the team colour
            BuzzTop,
        ],
    ];

    /// <summary>A short full beard round the jaw.</summary>
    public static readonly BodyPart Beard = new(new(0.372f, 0.1f, 0.4f), new(0, -0.155f, -0.025f), BodyPaint.Hair);

    // ---- Variants ----

    public static readonly Vector3[] SkinTones =
    [
        new(0.93f, 0.78f, 0.66f), new(0.87f, 0.7f, 0.56f), new(0.8f, 0.6f, 0.45f), new(0.66f, 0.47f, 0.33f), new(0.52f, 0.36f, 0.24f), new(0.38f, 0.26f, 0.18f),
    ];

    public static readonly Vector3[] HairColors =
    [
        new(0.08f, 0.07f, 0.06f), new(0.2f, 0.13f, 0.08f), new(0.33f, 0.22f, 0.12f), new(0.62f, 0.5f, 0.3f), new(0.45f, 0.44f, 0.42f), new(0.5f, 0.24f, 0.1f),
    ];

    /// <summary>Helmet and cap colours: olive, tan, black, grey-green.</summary>
    public static readonly Vector3[] GearColors = [new(0.3f, 0.32f, 0.22f), new(0.55f, 0.47f, 0.33f), new(0.13f, 0.13f, 0.14f), new(0.36f, 0.39f, 0.33f)];

    /// <summary>
    /// The bot personality in a bot's name ("[BOT] Ace (Marksman)"), or null for people. Names are replicated, so every
    /// machine picks the same look (clients don't know <see cref="Player.Bot"/>). No allocations.
    /// </summary>
    public static BotPersonality? PersonalityOf(string name)
    {
        if (!name.StartsWith("[BOT]", StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        var open = name.LastIndexOf('(');
        if (open < 0)
            return null;
        var inner = name.AsSpan(open + 1, name.Length - open - 2);
        foreach (var prof in BotPersonalities.All)
            if (MemoryExtensions.Equals(inner, prof.Name, StringComparison.Ordinal))
                return prof.Kind;
        return null;
    }

    /// <summary>
    /// The look for a player: a hash of the name (or the id when there is no name), so it is the same on every machine and
    /// every match. Bots pick headwear by personality (Anchors and Tacticians wear helmets, Marksmen caps, Rushers
    /// bandanas or buzz cuts...); people get a mix (a prestige hat replaces it).
    /// </summary>
    public static BodyVariant VariantFor(string name, byte id)
    {
        var h = 2166136261u;
        if (string.IsNullOrEmpty(name))
            h = (h ^ id) * 16777619u;
        else
            foreach (var c in name)
                h = (h ^ c) * 16777619u;
        // murmur3 finaliser: every bit of the name moves every field
        h ^= h >> 16;
        h *= 0x85ebca6bu;
        h ^= h >> 13;
        h *= 0xc2b2ae35u;
        h ^= h >> 16;
        var roll = (int)((h >> 8) % 100);
        var beardRoll = (int)((h >> 18) % 100);
        Headwear hw;
        int beard;
        switch (PersonalityOf(name ?? ""))
        {
            case BotPersonality.Rusher:
                (hw, beard) = (roll < 60 ? Headwear.Bandana : Headwear.Buzz, 30);
                break;
            case BotPersonality.Marksman:
                (hw, beard) = (roll < 70 ? Headwear.Cap : Headwear.Hair, 40);
                break;
            case BotPersonality.Flanker:
                (hw, beard) = (roll < 50 ? Headwear.Hair : Headwear.Cap, 25);
                break;
            case BotPersonality.Anchor:
                (hw, beard) = (Headwear.Helmet, 60);
                break;
            case BotPersonality.Tactician:
                (hw, beard) = (roll < 70 ? Headwear.Helmet : Headwear.Cap, 35);
                break;
            case BotPersonality.Chaotic:
                (hw, beard) = ((Headwear)(roll % 5), 50);
                break;
            default:
                (hw, beard) = (roll < 45 ? Headwear.Hair : roll < 65 ? Headwear.Buzz : roll < 80 ? Headwear.Cap : roll < 90 ? Headwear.Helmet : Headwear.Bandana, 25);
                break;
        }
        return new BodyVariant((byte)(h % (uint)SkinTones.Length), (byte)((h >> 4) % (uint)HairColors.Length), hw, beardRoll < beard,
            (byte)((h >> 26) % (uint)GearColors.Length));
    }

    /// <summary>
    /// The look a player is drawn with: the name's look (<see cref="VariantFor(string, byte)"/>) with whatever the player
    /// picked on the APPEARANCE screen on top (<see cref="Player.Look"/>, Source/Cosmetics/Appearance.cs; bots and players
    /// who never picked anything: all Auto, so unchanged).
    /// </summary>
    public static BodyVariant VariantFor(Player p) => VariantFor(p.Name, p.Id, p.Look);

    /// <summary>The name's look with a picked <paramref name="look"/> on top (Auto axes keep the name's).</summary>
    public static BodyVariant VariantFor(string name, byte id, in PlayerLook look) => look.Apply(VariantFor(name, id));

    /// <summary>Whether a body at <paramref name="at"/> draws its detail parts (near the camera, or no camera set).</summary>
    public static bool WantsDetail(Vector3 at)
    {
        var v = LegAnimator.Viewer;
        return float.IsNaN(v.X) || Vector3.DistanceSquared(v, at) <= DetailRange * DetailRange;
    }

    /// <summary>
    /// Fills <paramref name="torso"/> (torso frame) and <paramref name="head"/> (head node frame) for a stance. With a prestige
    /// hat on (<paramref name="hat"/>) the headwear is left off (hair keeps only its back part, under the hat).
    /// </summary>
    public static void Build(BodyParts torso, BodyParts head, int stance, in BodyVariant v, in BodyColors c, bool hat)
    {
        var s = Stances[stance];
        var ts = s.TorsoScale;
        torso.Clear();
        foreach (var p in TorsoParts)
            torso.Add(p, ts, c);
        var hs = s.HeadScale;
        head.Clear();
        foreach (var p in HeadParts)
            head.Add(p, hs, c);
        if (!hat)
            foreach (var p in HeadwearParts[(int)v.Headwear])
                head.Add(p, hs, c);
        else if (v.Headwear == Headwear.Hair)
            head.Add(HairBack, hs, c);
        if (v.Beard)
            head.Add(Beard, hs, c);
    }

    /// <summary>How many headwear parts <see cref="Build"/> adds for a variant, with or without a prestige hat (the hat's own boxes not counted).</summary>
    public static int HeadwearCount(in BodyVariant v, bool hat) =>
        !hat ? HeadwearParts[(int)v.Headwear].Length : v.Headwear == Headwear.Hair ? 1 : 0;

    /// <summary>Most parts a torso / head can have (the vertex budget's worst case).</summary>
    public static int MaxParts => TorsoParts.Length + HeadParts.Length + HeadwearParts.Max(h => h.Length) + 1;

    private static readonly BodyParts ScratchTorso = new(), ScratchHead = new();

    /// <summary>
    /// Immediate mode (<see cref="PlayerRender.DrawImmediate"/>): <paramref name="p"/>'s torso and head for a stance under
    /// <paramref name="up"/> (the upper body to world), the same draws as the scene graph's torso and head nodes.
    /// </summary>
    public static void DrawImmediate(Renderer g, Player p, int stance, in Matrix4x4 up, bool hat, bool? detail = null)
    {
        var v = VariantFor(p);
        var c = BodyColors.For(p.Color, v);
        Build(ScratchTorso, ScratchHead, stance, v, c, hat);
        ScratchTorso.ShowDetail = ScratchHead.ShowDetail = detail ?? WantsDetail(p.RenderPosition);
        var s = Stances[stance];
        ScratchTorso.DrawImmediate(g, s.TorsoFrame * up);
        ScratchHead.DrawImmediate(g, Matrix4x4.CreateTranslation(s.HeadAt) * up);
    }

    // ---- Zombies ----

    /// <summary>
    /// A zombie's torso and head in its upper-body frame (hips at the origin, as Program.DrawZombie draws it): a torn shirt
    /// over a bare belly with a hanging rag, cranium, slack jaw and heavy brow (the old two boxes became four, six up close). No vest, no headwear.
    /// </summary>
    public static readonly BodyPart[] ZombieParts =
    [
        new(new(0.62f, 0.4f, 0.36f), new(0, 0.41f, 0), BodyPaint.Shirt), // torn shirt (rides up)
        new(new(0.54f, 0.24f, 0.3f), new(0, 0.1f, 0.01f), BodyPaint.SkinShade), // bare belly
        new(new(0.35f, 0.24f, 0.35f), new(0, 0.87f, -0.04f), BodyPaint.Skin), // cranium
        new(new(0.27f, 0.14f, 0.29f), new(0, 0.68f, -0.07f), BodyPaint.SkinShade), // slack jaw, sunk onto the shoulders (no neck)
        new(new(0.2f, 0.16f, 0.02f), new(0.13f, 0.15f, -0.17f), BodyPaint.ShirtDark, true), // hanging rag
        new(new(0.31f, 0.045f, 0.05f), new(0, 0.885f, -0.215f), BodyPaint.SkinShade, true), // heavy brow over the glowing eyes
    ];

    private static readonly Matrix4x4[] ZombieLocal = ZombieParts.Select(p => Matrix4x4.CreateScale(p.Size) * Matrix4x4.CreateTranslation(p.Center)).ToArray();

    /// <summary>Which <see cref="ZombieParts"/> belong to the head (cranium, jaw, brow): they follow the head frame when it tilts.</summary>
    public static readonly bool[] ZombieHeadPart = [false, false, true, true, false, true];

    /// <summary>Draws a zombie's torso and head under <paramref name="upper"/>. Returns the number of boxes drawn.</summary>
    public static int DrawZombieUpper(Renderer g, in Matrix4x4 upper, Vector3 shirt, Vector3 skin, bool detail) => DrawZombieUpper(g, upper, upper, shirt, skin, detail);

    /// <summary>
    /// Draws a zombie's torso under <paramref name="upper"/> and its head parts under <paramref name="head"/> (the upper frame
    /// turned about the neck: the head hangs and nods, Source/Render/ZombieAnim.cs). Returns the number of boxes drawn.
    /// </summary>
    public static int DrawZombieUpper(Renderer g, in Matrix4x4 upper, in Matrix4x4 head, Vector3 shirt, Vector3 skin, bool detail)
    {
        var c = BodyColors.Zombie(shirt, skin);
        var n = 0;
        for (var i = 0; i < ZombieParts.Length; i++)
        {
            if (ZombieParts[i].Detail && !detail)
                continue;
            g.Draw(ZombieLocal[i] * (ZombieHeadPart[i] ? head : upper), c.Of(ZombieParts[i].Paint));
            n++;
        }
        return n;
    }
}
