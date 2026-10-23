using System.Numerics;

namespace CallOfSlop;

/// <summary>Settings > Video "Gore": how messy animal props and blast kills get (<see cref="Gibs"/>).</summary>
public enum GoreMode : byte
{
    /// <summary>Every gib, blood splats where the meat lands.</summary>
    Full,

    /// <summary>About half the gibs (body parts and a few chunks), no blood splats.</summary>
    Reduced,

    /// <summary>No gibs at all: animals poof into feathers or dust, blast kills fall as normal ragdolls.</summary>
    Off,
}

/// <summary>What an animal prop's skin bursts into besides meat and bone (the "style" of its "gibs" block).</summary>
public enum GibStyle : byte
{
    /// <summary>Cats, dogs, cows: tufts of fur in the animal's colours.</summary>
    Fur,

    /// <summary>Pigeons, chickens, ducks: a puff of feathers that flutter down slowly.</summary>
    Bird,

    /// <summary>Sheep: chunky white wool tufts.</summary>
    Wool,
}

/// <summary>The input of a body burst: where and how a player, bot or zombie died, and the colours it wore.</summary>
public struct GibBody
{
    public Vector3 Feet;
    public float Yaw;
    public Stance Stance;
    public Vector3 Velocity;

    /// <summary>The blast that killed it, and its push (m/s at the centre; 0 = no blast known: the parts burst outwards from the chest).</summary>
    public Vector3 BlastAt;
    public float Strength;

    /// <summary>The player whose body it was (the death camera follows the biggest chunk), 255 for a zombie.</summary>
    public byte Owner;

    /// <summary>From the kill (<see cref="Ragdolls.Seed"/>), so every PC throws the same pieces.</summary>
    public uint Seed;

    public Vector3 Vest, Sleeve, Skin, Hair, Trousers, Boot, Glove;
}

/// <summary>
/// Meaty gibs: what an animal prop bursts into when it's shot, knifed or blown up enough (a "gibs" block in its
/// "breakable", see Props/README.md), and what a player, bot or zombie killed right next to a blast bursts into instead
/// of a ragdoll (the server's "gibbed" flag on the kill, <see cref="Game.ShouldGib"/>).
///
/// Chunky low-poly boxes like the prop debris: red and pink meat, a few bone-white bits, plus feathers (birds), wool
/// (sheep) or fur, and for a body its recognisable parts in the soldier's colours (torso, head, arms, a hand, legs and
/// boots). Pieces are thrown from the hit or the blast, tumble, bounce, slide to a stop and lie there for
/// <see cref="Life"/> seconds before they dither out. Meat that lands leaves a small blood splat (a "Gore" impact decal,
/// see Decals/Gore) on Full gore.
///
/// Cosmetic and client-side: every PC builds the same burst from the break or kill event it already gets (the seed and
/// starting state come only from that event), nothing is sent per piece. A fixed pool of <see cref="Capacity"/> pieces
/// (the oldest recycled first), plain arrays, no allocation after start-up; drawn in the prop debris' instanced call
/// (<see cref="PropRenderer"/>). Cleared with every new map and match.
/// </summary>
public static class Gibs
{
    /// <summary>Most pieces alive at once (all bursts together). A new burst when the pool is full takes over the oldest.</summary>
    public const int Capacity = 320;

    /// <summary>Most pieces one burst throws.</summary>
    public const int MaxPerBurst = 40;

    /// <summary>Seconds a piece lies about (plus up to a second of its own); the last <see cref="FadeTime"/> it dithers out.</summary>
    public const float Life = 15f, FadeTime = 1.5f;

    /// <summary>Most blood splats one burst leaves (Full gore only).</summary>
    public const int MaxSplatsPerBurst = 5;

    /// <summary>The impact-decal "player" the splats count against (a cap of <see cref="ImpactPool.PerPlayer"/> splats on the map).</summary>
    public const byte SplatOwner = 255;

    /// <summary>The player's setting (Program applies it every frame). Servers and the self-test default to Full.</summary>
    public static GoreMode Mode = GoreMode.Full;

    public static readonly Vector3 MeatRed = new(0.56f, 0.07f, 0.06f), MeatPink = new(0.8f, 0.38f, 0.37f), MeatDark = new(0.3f, 0.03f, 0.03f),
        BoneWhite = new(0.9f, 0.87f, 0.78f), WoolWhite = new(0.88f, 0.86f, 0.8f);

    private enum Kind : byte
    {
        Meat,
        Bone,
        Feather,
        Wool,
        Fur,
        Part, // a body part (torso, head, boot...)
    }

    private struct Piece
    {
        public Vector3 Pos, Vel, Size, Color, SpinAxis;
        public Quaternion Rot;
        public float Spin, Age, Life, Floor, Gravity, Drag;
        public Kind Kind;
        public byte Owner;
        public bool Resting, Landed, Splat, Torso;
    }

    private static readonly Piece[] Pool = new Piece[Capacity];
    private static int _count, _frame;
    private static uint _hash;

    /// <summary>Pieces alive right now.</summary>
    public static int Count => _count;

    /// <summary>Pieces and bursts since start-up, splats placed, and pieces that took over a live one (pool full).</summary>
    public static long Spawned { get; private set; }

    /// <inheritdoc cref="Spawned"/>
    public static long Bursts { get; private set; }

    /// <inheritdoc cref="Spawned"/>
    public static long Splats { get; private set; }

    /// <inheritdoc cref="Spawned"/>
    public static long Recycled { get; private set; }

    /// <summary>Pieces the last burst threw, and a hash of their starting state (two PCs given the same event get the same hash).</summary>
    public static int LastBurstCount { get; private set; }

    /// <inheritdoc cref="LastBurstCount"/>
    public static uint LastBurstHash => _hash;

    public static void Clear() => _count = 0;

    /// <summary>How many pieces a burst of <paramref name="full"/> throws on the current setting (0 on Off).</summary>
    public static int CountFor(int full) => Mode switch
    {
        GoreMode.Off => 0,
        GoreMode.Reduced => Math.Max(3, (full + 1) / 2),
        _ => Math.Clamp(full, 0, MaxPerBurst),
    };

    /// <summary>A seed for a prop's burst from its break event (prop index and where the hit came from, to the millimetre): the same on every PC.</summary>
    public static uint SeedFor(int index, Vector3 from)
    {
        var h = 2166136261u;
        void Mix(int v) => h = (h ^ (uint)v) * 16777619u;
        Mix(index);
        Mix((int)MathF.Round(from.X * 1000));
        Mix((int)MathF.Round(from.Y * 1000));
        Mix((int)MathF.Round(from.Z * 1000));
        return h;
    }

    // ---- Animal props ------------------------------------------------------------------------------------------

    /// <summary>
    /// An animal prop that just broke: meat, bone and its feathers, wool or fur burst out of its bounds, away from
    /// <paramref name="from"/> (the shooter, the swing or the blast; a blast throws them twice as hard). On Off gore it
    /// only poofs into a little puff of feathers or dust (prop debris). Returns the pieces thrown.
    /// </summary>
    public static int SpawnProp(in PropInstance prop, Vector3 from, PropDamage kind, uint seed)
    {
        if (prop.Def.Breakable is not { GibCount: > 0 } br)
            return 0;
        var b = prop.Bounds;
        if (Mode == GoreMode.Off)
        {
            Vector3 c0, c1;
            if (br.GibStyle == GibStyle.Bird)
                (c0, c1) = (prop.Def.DebrisColor(0.2f), prop.Def.DebrisColor(0.75f));
            else if (br.GibStyle == GibStyle.Wool)
                (c0, c1) = (WoolWhite, WoolWhite * 0.85f);
            else
                (c0, c1) = (new Vector3(0.56f, 0.52f, 0.45f), new Vector3(0.72f, 0.68f, 0.6f));
            PropDebris.SpawnBox(b, from, kind, (int)seed, Math.Clamp(br.GibCount / 2, 3, 12), 0.02f, 0.07f, 2.2f, c0, c1);
            return 0;
        }
        var count = CountFor(br.GibCount);
        var rng = new Foliage.Rng(seed ^ 0x61b5u);
        var blast = kind is PropDamage.Explosion or PropDamage.Streak;
        var s = Math.Clamp(MathF.Cbrt(8f * b.Half.X * b.Half.Y * b.Half.Z) * 0.2f, 0.045f, 0.2f);
        var splats = Mode == GoreMode.Full ? Math.Min(MaxSplatsPerBurst, 1 + count / 5) : 0;
        BeginBurst();
        for (var i = 0; i < count; i++)
        {
            var r = rng.Next();
            var k = i == 0 ? Kind.Meat // the first is always meat (it carries a splat)
                : br.GibStyle switch
                {
                    GibStyle.Bird => r < 0.4f ? Kind.Feather : r < 0.5f ? Kind.Bone : Kind.Meat,
                    GibStyle.Wool => r < 0.35f ? Kind.Wool : r < 0.45f ? Kind.Bone : Kind.Meat,
                    _ => r < 0.2f ? Kind.Fur : r < 0.32f ? Kind.Bone : Kind.Meat,
                };
            var pos = b.Center + new Vector3((rng.Next() - 0.5f) * 1.6f * b.Half.X, (rng.Next() - 0.5f) * 1.6f * b.Half.Y, (rng.Next() - 0.5f) * 1.6f * b.Half.Z);
            var g = s * (0.7f + rng.Next() * 0.6f);
            var size = k switch
            {
                Kind.Bone => new Vector3(g * 0.3f, g * 0.3f, g * 1.2f),
                Kind.Feather => new Vector3(g * 0.55f, 0.012f, g * 0.25f),
                Kind.Wool => new Vector3(g * (0.9f + rng.Next() * 0.3f), g * 0.8f, g * (0.9f + rng.Next() * 0.3f)),
                Kind.Fur => new Vector3(g, g * 0.35f, g * 0.8f),
                _ => new Vector3(g * (0.7f + rng.Next() * 0.5f), g * (0.55f + rng.Next() * 0.4f), g * (0.7f + rng.Next() * 0.5f)),
            };
            var color = k switch
            {
                Kind.Bone => BoneWhite * (0.92f + rng.Next() * 0.1f),
                Kind.Feather or Kind.Fur => prop.Def.DebrisColor(rng.Next()) * (0.9f + rng.Next() * 0.15f),
                Kind.Wool => WoolWhite * (0.88f + rng.Next() * 0.14f),
                _ => MeatColor(ref rng),
            };
            var away = pos - from;
            away.Y = MathF.Max(away.Y, 0f);
            away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : Vector3.UnitY;
            var speed = (k == Kind.Feather ? 2f : 3.4f) * (0.5f + rng.Next() * 0.8f) * (blast ? 2.2f : 1f);
            var vel = away * speed + new Vector3(rng.Next() - 0.5f, rng.Next() * 0.5f, rng.Next() - 0.5f) * speed + new Vector3(0, 2f + rng.Next() * 2.5f, 0);
            Add(ref rng, pos, size, color, vel, k, 255, k == Kind.Meat && splats-- > 0, false);
        }
        return count;
    }

    // ---- Bodies --------------------------------------------------------------------------------------------------

    /// <summary>
    /// A player, bot or zombie blown apart: its parts (torso, head, arms, a hand, legs, boots) in its own colours plus
    /// meat and bone, flung from the blast. Nothing on Off gore (the caller makes a ragdoll instead). Returns the pieces thrown.
    /// </summary>
    public static int SpawnBody(in GibBody d)
    {
        if (Mode == GoreMode.Off)
            return 0;
        var full = Mode == GoreMode.Full;
        var rng = new Foliage.Rng(d.Seed ^ 0xb0d1u);
        var toWorld = Matrix4x4.CreateRotationY(-d.Yaw) * Matrix4x4.CreateTranslation(d.Feet);
        var squash = d.Stance switch { Stance.Crouch => 0.72f, Stance.Prone => 0.35f, _ => 1f };
        var chest = Vector3.Transform(new Vector3(0, 1.15f * squash, 0), toWorld);
        var center = d.Strength > 0 ? d.BlastAt : chest - new Vector3(0, 0.6f, 0);
        var strength = d.Strength > 0 ? Math.Clamp(d.Strength, 5f, 18f) : 6f;
        var inherit = d.Velocity;
        if (!float.IsFinite(inherit.X + inherit.Y + inherit.Z) || inherit.LengthSquared() > 144f)
            inherit = Vector3.Zero;
        var splats = full ? MaxSplatsPerBurst : 0;
        BeginBurst();
        for (var i = 0; i < BodyParts.Length; i++)
        {
            ref readonly var part = ref BodyParts[i];
            if (!full && !part.Reduced)
                continue;
            var local = new Vector3(part.At.X, part.At.Y * squash, part.At.Z);
            var color = part.Paint switch
            {
                0 => d.Vest,
                1 => d.Skin,
                2 => d.Hair,
                3 => d.Sleeve,
                4 => d.Glove,
                5 => d.Trousers,
                _ => d.Boot,
            };
            AddBodyPiece(ref rng, Vector3.Transform(local, toWorld), part.Size, color, Kind.Part, center, strength, inherit, d.Owner, i == 0, false);
        }
        var meat = full ? 14 : 5;
        var bone = full ? 4 : 1;
        for (var i = 0; i < meat + bone; i++)
        {
            var isBone = i >= meat;
            var local = new Vector3((rng.Next() - 0.5f) * 0.5f, (0.5f + rng.Next() * 1.1f) * squash, (rng.Next() - 0.5f) * 0.35f);
            var g = 0.07f + rng.Next() * 0.07f;
            var size = isBone ? new Vector3(g * 0.35f, g * 0.35f, g * 1.6f) : new Vector3(g * (0.8f + rng.Next() * 0.5f), g * (0.6f + rng.Next() * 0.4f), g * (0.8f + rng.Next() * 0.5f));
            var color = isBone ? BoneWhite * (0.92f + rng.Next() * 0.1f) : MeatColor(ref rng);
            AddBodyPiece(ref rng, Vector3.Transform(local, toWorld), size, color, isBone ? Kind.Bone : Kind.Meat, center, strength, inherit, d.Owner, false, !isBone && splats-- > 0);
        }
        return LastBurstCount;
    }

    private readonly record struct BodyPart(Vector3 At, Vector3 Size, byte Paint, bool Reduced);

    /// <summary>The soldier's recognisable parts (body space, feet at the origin): paint 0 vest, 1 skin, 2 hair, 3 sleeve, 4 glove, 5 trousers, 6 boot. The first is the torso.</summary>
    private static readonly BodyPart[] BodyParts =
    [
        new(new Vector3(0, 1.15f, 0), new Vector3(0.42f, 0.48f, 0.26f), 0, true), // torso (the biggest chunk: the death camera's)
        new(new Vector3(0, 1.6f, 0), new Vector3(0.24f, 0.26f, 0.25f), 1, true), // head
        new(new Vector3(0, 1.75f, 0.01f), new Vector3(0.26f, 0.06f, 0.27f), 2, false), // hair or helmet top
        new(new Vector3(0.31f, 1.2f, 0), new Vector3(0.12f, 0.32f, 0.12f), 3, false), // upper arms
        new(new Vector3(-0.31f, 1.2f, 0), new Vector3(0.12f, 0.32f, 0.12f), 3, true),
        new(new Vector3(0.32f, 0.86f, -0.04f), new Vector3(0.1f, 0.07f, 0.13f), 4, true), // a hand
        new(new Vector3(0, 0.86f, 0), new Vector3(0.36f, 0.16f, 0.24f), 5, false), // pelvis
        new(new Vector3(0.12f, 0.55f, 0), new Vector3(0.15f, 0.44f, 0.15f), 5, true), // legs
        new(new Vector3(-0.12f, 0.55f, 0), new Vector3(0.15f, 0.44f, 0.15f), 5, false),
        new(new Vector3(0.12f, 0.07f, -0.04f), new Vector3(0.13f, 0.12f, 0.27f), 6, true), // boots
        new(new Vector3(-0.12f, 0.07f, -0.04f), new Vector3(0.13f, 0.12f, 0.27f), 6, false),
    ];

    /// <summary>Number of body-part pieces a burst throws on Full and Reduced gore (self-test).</summary>
    public static (int Full, int Reduced) BodyPartCounts => (BodyParts.Length, BodyParts.Count(p => p.Reduced));

    private static void AddBodyPiece(ref Foliage.Rng rng, Vector3 pos, Vector3 size, Vector3 color, Kind k, Vector3 center, float strength, Vector3 inherit,
        byte owner, bool torso, bool splat)
    {
        var to = pos - center;
        var dist = to.Length();
        var dir = dist > 0.05f ? to / dist : Vector3.UnitY;
        dir = Vector3.Normalize(dir + new Vector3(0, 0.6f, 0) + new Vector3(rng.Next() - 0.5f, rng.Next() * 0.3f, rng.Next() - 0.5f) * 0.7f);
        var heavy = MathF.Cbrt(size.X * size.Y * size.Z) > 0.2f ? 0.6f : 1f; // the torso flies less far than a hand
        var vel = dir * (strength * heavy * (0.55f + rng.Next() * 0.6f)) + inherit * 0.5f;
        Add(ref rng, pos, size, color, vel, k, owner, splat, torso);
    }

    // ---- Pool ----------------------------------------------------------------------------------------------------

    private static Vector3 MeatColor(ref Foliage.Rng rng)
    {
        var r = rng.Next();
        var c = r < 0.5f ? Vector3.Lerp(MeatRed, MeatPink, r * 1.2f) : r < 0.85f ? MeatRed : MeatDark;
        return c * (0.88f + rng.Next() * 0.2f);
    }

    private static void BeginBurst()
    {
        Bursts++;
        LastBurstCount = 0;
        _hash = 2166136261u;
    }

    private static void Mix(Vector3 v)
    {
        _hash = (_hash ^ (uint)BitConverter.SingleToInt32Bits(v.X)) * 16777619u;
        _hash = (_hash ^ (uint)BitConverter.SingleToInt32Bits(v.Y)) * 16777619u;
        _hash = (_hash ^ (uint)BitConverter.SingleToInt32Bits(v.Z)) * 16777619u;
    }

    private static void Add(ref Foliage.Rng rng, Vector3 pos, Vector3 size, Vector3 color, Vector3 vel, Kind k, byte owner, bool splat, bool torso)
    {
        int slot;
        if (_count < Capacity)
            slot = _count++;
        else
        {
            slot = Oldest();
            Recycled++;
        }
        ref var p = ref Pool[slot];
        p.Pos = pos;
        p.Vel = vel;
        p.Size = size;
        p.Color = color;
        p.SpinAxis = Vector3.Normalize(new Vector3(rng.Next() - 0.5f, rng.Next() - 0.5f, rng.Next() - 0.5f) + new Vector3(0.01f));
        p.Spin = k == Kind.Feather ? 2f + rng.Next() * 3f : 4f + rng.Next() * 9f;
        p.Rot = Quaternion.CreateFromAxisAngle(p.SpinAxis, rng.Next() * MathF.Tau);
        p.Age = 0;
        p.Life = Life + rng.Next();
        p.Gravity = k is Kind.Feather ? 0.18f : k is Kind.Wool or Kind.Fur ? 0.8f : 1f;
        p.Drag = k is Kind.Feather ? 2.6f : 0.15f;
        p.Kind = k;
        p.Owner = owner;
        p.Torso = torso;
        p.Splat = splat;
        p.Resting = p.Landed = false;
        p.Floor = FloorUnder(pos);
        Spawned++;
        LastBurstCount++;
        Mix(pos);
        Mix(vel);
        Mix(size);
        Mix(color);
    }

    private static int Oldest()
    {
        var best = 0;
        var most = float.MinValue;
        for (var i = 0; i < _count; i++)
        {
            var left = Pool[i].Age - Pool[i].Life;
            if (left > most)
            {
                most = left;
                best = i;
            }
        }
        return best;
    }

    /// <summary>A planet: falls towards the centre, bounces, slides to a stop and lies flat on the curved ground (cosmetic).</summary>
    private static void StepCurved(ref Piece p, int i, float dt)
    {
        if ((i + _frame) % 12 == 0)
            p.Floor = FloorUnder(p.Pos);
        var low = MathF.Min(p.Size.X, MathF.Min(p.Size.Y, p.Size.Z)) * 0.5f;
        p.Rot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(p.SpinAxis, p.Spin * dt) * p.Rot);
        if (!CurvedFall.Step(ref p.Pos, ref p.Vel, p.Floor, low, p.Gravity, dt, out var up, out var into))
            return;
        var tangent = p.Vel + up * into;
        if (into > 2f)
        {
            p.Vel = tangent * 0.6f + up * (into * 0.25f); // a bounce
            p.Spin *= 0.5f;
            return;
        }
        p.Vel = tangent * MathF.Exp(-6f * dt);
        p.Spin *= MathF.Exp(-6f * dt);
        if (p.Vel.LengthSquared() < 0.04f)
        {
            p.Vel = Vector3.Zero;
            p.Spin = 0;
            p.Resting = true;
            p.Rot = CurvedFall.LieFlat(p.Rot, p.Pos);
        }
    }

    private static float FloorUnder(Vector3 p)
    {
        if (GravityFrames.Curved)
            return CurvedFall.FloorUnder(p); // a planet: a height above its centre (GravityFrames.cs)
        var d = World.Query.Raycast(p + new Vector3(0, 0.3f, 0), -Vector3.UnitY, 12f, QueryFilter.All);
        return d >= 12f ? p.Y - 50f : p.Y + 0.3f - d;
    }

    /// <summary>Where player <paramref name="id"/>'s torso chunk is, if they were just blown apart (the death camera looks at it).</summary>
    public static Vector3? BodyPoint(byte id)
    {
        if (id == 255)
            return null;
        var best = -1;
        for (var i = 0; i < _count; i++)
            if (Pool[i].Torso && Pool[i].Owner == id && (best < 0 || Pool[i].Age < Pool[best].Age))
                best = i; // the newest death's
        return best < 0 ? null : Pool[best].Pos;
    }

    /// <summary>A later blast throws the gibs around it again (cosmetic; called with the prop debris' push).</summary>
    public static void Push(Vector3 center, float radius, float strength)
    {
        if (_count == 0 || !(strength > 0) || !(radius > 0))
            return;
        for (var i = 0; i < _count; i++)
        {
            ref var p = ref Pool[i];
            var to = p.Pos - center;
            var d = to.Length();
            if (d >= radius)
                continue;
            var dir = d > 0.05f ? to / d : Vector3.UnitY;
            p.Vel += Vector3.Normalize(dir + new Vector3(0, 0.6f, 0)) * (strength * 1.2f * (1f - d / radius));
            p.Spin = MathF.Max(p.Spin, 6f);
            p.Resting = false;
            p.Floor = FloorUnder(p.Pos);
        }
    }

    /// <summary>Moves, bounces, slides, settles, ages and removes pieces; meat landing for the first time may leave a splat. Once per frame.</summary>
    public static void Update(float dt, double now)
    {
        if (_count == 0 || dt <= 0)
            return;
        dt = MathF.Min(dt, 0.1f);
        _frame++;
        var friction = MathF.Exp(-6f * dt);
        for (var i = 0; i < _count; i++)
        {
            ref var p = ref Pool[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                Pool[i] = Pool[--_count]; // swap with the last live piece
                i--;
                continue;
            }
            if (p.Resting)
                continue;
            if (GravityFrames.Curved)
            {
                StepCurved(ref p, i, dt);
                continue;
            }
            if ((i + _frame) % 12 == 0)
                p.Floor = FloorUnder(p.Pos); // slid over a kerb or flew off a ledge
            p.Vel.Y -= World.Gravity * p.Gravity * dt;
            if (p.Drag > 0)
                p.Vel *= MathF.Exp(-p.Drag * dt);
            if (p.Kind == Kind.Feather)
                p.Vel.X += MathF.Sin(p.Age * 6f + p.SpinAxis.X * 9f) * 1.2f * dt; // flutter
            p.Pos += p.Vel * dt;
            if (World.Map.Boundary is { } boundary)
                boundary.KeepInside(ref p.Pos, ref p.Vel, 0.05f, 0.3f);
            p.Rot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(p.SpinAxis, p.Spin * dt) * p.Rot);
            var low = MathF.Min(p.Size.X, MathF.Min(p.Size.Y, p.Size.Z)) * 0.5f;
            if (p.Pos.Y - low > p.Floor)
                continue;
            p.Pos.Y = p.Floor + low;
            if (!p.Landed)
            {
                p.Landed = true;
                if (p.Splat)
                    TrySplat(p.Pos, p.Size.X + p.Size.Z, now);
            }
            if (p.Vel.Y < -2f && p.Kind != Kind.Feather)
            {
                p.Vel.Y = -p.Vel.Y * 0.25f; // a wet bounce
                p.Vel.X *= 0.6f;
                p.Vel.Z *= 0.6f;
                p.Spin *= 0.5f;
                continue;
            }
            // On the ground: slide to a stop.
            p.Vel.Y = 0;
            p.Vel.X *= friction;
            p.Vel.Z *= friction;
            p.Spin *= friction;
            if (p.Vel.X * p.Vel.X + p.Vel.Z * p.Vel.Z < 0.04f || p.Kind == Kind.Feather)
            {
                p.Vel = Vector3.Zero;
                p.Spin = 0;
                p.Resting = true;
                var fwd = Vector3.Transform(Vector3.UnitX, p.Rot);
                p.Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-fwd.Z, fwd.X)); // lie flat
            }
        }
    }

    /// <summary>A blood splat under a piece of meat that just landed (Full gore, impact decals on). Cosmetic.</summary>
    private static void TrySplat(Vector3 at, float size, double now)
    {
        if (Mode != GoreMode.Full || ImpactDecals.Pool.Capacity == 0 || Decals.ImpactsFor("Gore").Length == 0)
            return;
        if (!World.Query.Raycast(at + new Vector3(0, 0.3f, 0), -Vector3.UnitY, 0.9f, QueryFilter.Shots, out var hit))
            return;
        if (ImpactDecals.Place(World.Map, hit, -Vector3.UnitY, SplatOwner, now, false, Math.Clamp(0.55f + size * 1.5f, 0.55f, 1.1f), "Gore"))
            Splats++;
    }

    /// <summary>Appends the live pieces (<see cref="PropDebris.FloatsPerPiece"/> floats each) after the first <paramref name="at"/>; returns the new count.</summary>
    public static int Write(float[] into, int at)
    {
        const int f = PropDebris.FloatsPerPiece;
        var n = Math.Min(_count, into.Length / f - at);
        for (var i = 0; i < n; i++)
        {
            ref readonly var p = ref Pool[i];
            var o = (at + i) * f;
            into[o] = p.Pos.X;
            into[o + 1] = p.Pos.Y;
            into[o + 2] = p.Pos.Z;
            into[o + 3] = Math.Clamp((p.Life - p.Age) / FadeTime, 0f, 1f);
            into[o + 4] = p.Rot.X;
            into[o + 5] = p.Rot.Y;
            into[o + 6] = p.Rot.Z;
            into[o + 7] = p.Rot.W;
            into[o + 8] = p.Size.X;
            into[o + 9] = p.Size.Y;
            into[o + 10] = p.Size.Z;
            into[o + 11] = 0;
            into[o + 12] = p.Color.X;
            into[o + 13] = p.Color.Y;
            into[o + 14] = p.Color.Z;
            into[o + 15] = 0;
        }
        return at + Math.Max(n, 0);
    }

    /// <summary>Self-test: resting pieces, the lowest any piece sits above its floor, and the oldest age.</summary>
    public static (int Resting, float LowestAboveFloor, float OldestAge) Inspect()
    {
        var resting = 0;
        var lowest = float.MaxValue;
        var oldest = 0f;
        for (var i = 0; i < _count; i++)
        {
            if (Pool[i].Resting)
                resting++;
            lowest = MathF.Min(lowest, Pool[i].Pos.Y - Pool[i].Floor);
            oldest = MathF.Max(oldest, Pool[i].Age);
        }
        return (resting, _count == 0 ? 0 : lowest, oldest);
    }

    /// <summary>One line for the smoke log.</summary>
    public static string Stats => $"{_count}/{Capacity} gibs alive, {Bursts} bursts, {Spawned} spawned, {Recycled} recycled, {Splats} splats, gore {Mode}";
}
