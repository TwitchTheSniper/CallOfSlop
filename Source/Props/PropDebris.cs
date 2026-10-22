using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The pieces a breakable prop shatters into: purely cosmetic, simulated on every PC from the break event (nothing is
/// sent per piece). Pieces fly out from the hit (explosions push them outwards), tumble, bounce and settle on whatever is
/// under them, then fade out and are gone after a few seconds.
///
/// A fixed pool (<see cref="Capacity"/> pieces, the oldest recycled when it's full), plain arrays and no allocation after
/// start-up. <see cref="Write"/> packs the live pieces for the instanced debris draw in <see cref="PropRenderer"/>.
/// </summary>
public static class PropDebris
{
    /// <summary>Most pieces alive at once. A new break when the pool is full takes over the oldest pieces.</summary>
    public const int Capacity = 384;

    /// <summary>Floats per piece in <see cref="Write"/>: position + fade (4), rotation quaternion (4), size (4), colour (4).</summary>
    public const int FloatsPerPiece = 16;

    /// <summary>Seconds a piece lives (before its own random extra of up to 1.5 s); the last <see cref="FadeTime"/> it fades out.</summary>
    public const float MinLife = 3.5f;

    public const float FadeTime = 1.2f;

    private struct Piece
    {
        public Vector3 Pos, Vel, Size, Color, SpinAxis;
        public Quaternion Rot;
        public float Spin, Age, Life, Floor;
        public bool Resting;
    }

    private static readonly Piece[] Pool = new Piece[Capacity];
    private static int _count;
    private static int _frame;

    /// <summary>Pieces alive right now.</summary>
    public static int Count => _count;

    /// <summary>Pieces spawned since start-up (for stats and the self-test).</summary>
    public static long Spawned { get; private set; }

    public static void Clear() => _count = 0;

    /// <summary>
    /// Shatters <paramref name="prop"/>: its "debris" pieces burst from inside its bounds, away from <paramref name="from"/>
    /// (the shooter, the swing or the blast centre). Deterministic for a seed (the same on every PC, though nothing depends on it).
    /// </summary>
    public static void Spawn(in PropInstance prop, Vector3 from, PropDamage kind, int seed)
    {
        var br = prop.Def.Breakable;
        if (br == null || br.DebrisCount <= 0)
            return;
        var rng = new Foliage.Rng((uint)seed * 2654435761u ^ 0x5eedu);
        var blast = kind is PropDamage.Explosion or PropDamage.Streak;
        var b = prop.Bounds;
        var count = Math.Min(br.DebrisCount, Capacity);
        for (var i = 0; i < count; i++)
        {
            var slot = _count < Capacity ? _count++ : Oldest();
            ref var p = ref Pool[slot];
            p.Pos = b.Center + new Vector3((rng.Next() - 0.5f) * 1.6f * b.Half.X, (rng.Next() - 0.5f) * 1.6f * b.Half.Y, (rng.Next() - 0.5f) * 1.6f * b.Half.Z);
            var s = (br.DebrisMin + (br.DebrisMax - br.DebrisMin) * rng.Next()) * MathF.Sqrt(prop.Scale);
            p.Size = new Vector3(s * (0.5f + rng.Next()), s * (0.35f + rng.Next() * 0.8f), s * (0.5f + rng.Next()));
            p.Color = prop.Def.DebrisColor(rng.Next()) * (0.85f + rng.Next() * 0.25f);
            var away = p.Pos - from;
            away.Y = MathF.Max(away.Y, 0f);
            away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : Vector3.UnitY;
            var speed = br.DebrisSpeed * (0.45f + rng.Next() * 0.8f) * (blast ? 1.8f : 1f);
            var scatter = new Vector3(rng.Next() - 0.5f, rng.Next() * 0.5f, rng.Next() - 0.5f) * speed;
            p.Vel = away * speed + scatter + new Vector3(0, 1.5f + rng.Next() * 2f, 0);
            p.SpinAxis = Vector3.Normalize(new Vector3(rng.Next() - 0.5f, rng.Next() - 0.5f, rng.Next() - 0.5f) + new Vector3(0.01f));
            p.Spin = 3f + rng.Next() * 9f;
            p.Rot = Quaternion.CreateFromAxisAngle(p.SpinAxis, rng.Next() * MathF.Tau);
            p.Age = 0;
            p.Life = MinLife + rng.Next() * 1.5f;
            p.Floor = FloorUnder(p.Pos);
            p.Resting = false;
            Spawned++;
        }
    }

    /// <summary>
    /// Shatters a plain box (a building's door or window glass): <paramref name="count"/> pieces of <paramref name="sizeMin"/>
    /// to <paramref name="sizeMax"/> metres burst from inside it, away from <paramref name="from"/>, in colours between
    /// <paramref name="colorA"/> and <paramref name="colorB"/>. Same rules as <see cref="Spawn"/>.
    /// </summary>
    public static void SpawnBox(Box b, Vector3 from, PropDamage kind, int seed, int count, float sizeMin, float sizeMax, float speed, Vector3 colorA, Vector3 colorB)
    {
        var rng = new Foliage.Rng((uint)seed * 2654435761u ^ 0xb0c5u);
        var blast = kind is PropDamage.Explosion or PropDamage.Streak;
        count = Math.Min(count, Capacity);
        for (var i = 0; i < count; i++)
        {
            var slot = _count < Capacity ? _count++ : Oldest();
            ref var p = ref Pool[slot];
            p.Pos = b.Center + new Vector3((rng.Next() - 0.5f) * 1.8f * b.Half.X, (rng.Next() - 0.5f) * 1.8f * b.Half.Y, (rng.Next() - 0.5f) * 1.8f * b.Half.Z);
            var s = sizeMin + (sizeMax - sizeMin) * rng.Next();
            p.Size = new Vector3(s * (0.5f + rng.Next()), s * (0.35f + rng.Next() * 0.8f), s * (0.3f + rng.Next() * 0.5f));
            p.Color = Vector3.Lerp(colorA, colorB, rng.Next()) * (0.85f + rng.Next() * 0.25f);
            var away = p.Pos - from;
            away.Y = MathF.Max(away.Y, 0f);
            away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : Vector3.UnitY;
            var v = speed * (0.45f + rng.Next() * 0.8f) * (blast ? 1.8f : 1f);
            p.Vel = away * v + new Vector3(rng.Next() - 0.5f, rng.Next() * 0.5f, rng.Next() - 0.5f) * v + new Vector3(0, 1f + rng.Next() * 1.5f, 0);
            p.SpinAxis = Vector3.Normalize(new Vector3(rng.Next() - 0.5f, rng.Next() - 0.5f, rng.Next() - 0.5f) + new Vector3(0.01f));
            p.Spin = 3f + rng.Next() * 9f;
            p.Rot = Quaternion.CreateFromAxisAngle(p.SpinAxis, rng.Next() * MathF.Tau);
            p.Age = 0;
            p.Life = MinLife + rng.Next() * 1.5f;
            p.Floor = FloorUnder(p.Pos);
            p.Resting = false;
            Spawned++;
        }
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

    /// <summary>The ground, terrain or box top under a point (a short ray down), or far below if there's nothing.</summary>
    private static float FloorUnder(Vector3 p)
    {
        var d = World.Query.Raycast(p + new Vector3(0, 0.3f, 0), -Vector3.UnitY, 12f, QueryFilter.All);
        return d >= 12f ? p.Y - 50f : p.Y + 0.3f - d;
    }

    /// <summary>Moves, bounces, settles, ages and removes pieces. Call once per frame.</summary>
    public static void Update(float dt)
    {
        if (_count == 0 || dt <= 0)
            return;
        dt = MathF.Min(dt, 0.1f);
        _frame++;
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
            if ((i + _frame) % 12 == 0)
                p.Floor = FloorUnder(p.Pos); // flew over a kerb or off a ledge
            p.Vel.Y -= World.Gravity * dt;
            p.Pos += p.Vel * dt;
            if (World.Map.Boundary is { } boundary)
                boundary.KeepInside(ref p.Pos, ref p.Vel, 0.05f, 0.3f); // pieces stay inside a shaped map
            p.Rot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(p.SpinAxis, p.Spin * dt) * p.Rot);
            var low = MathF.Min(p.Size.X, MathF.Min(p.Size.Y, p.Size.Z)) * 0.5f;
            if (p.Pos.Y - low <= p.Floor)
            {
                p.Pos.Y = p.Floor + low;
                if (p.Vel.Y < -1.5f)
                {
                    p.Vel.Y = -p.Vel.Y * 0.3f; // bounce
                    p.Vel.X *= 0.55f;
                    p.Vel.Z *= 0.55f;
                    p.Spin *= 0.5f;
                }
                else
                {
                    p.Vel = Vector3.Zero;
                    p.Spin = 0;
                    p.Resting = true;
                    // Settle flat: snap to the nearest quarter turn about the vertical, lying on a face.
                    var fwd = Vector3.Transform(Vector3.UnitX, p.Rot);
                    p.Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-fwd.Z, fwd.X));
                }
            }
        }
    }

    /// <summary>Writes the live pieces (see <see cref="FloatsPerPiece"/>) into <paramref name="into"/>; returns how many.</summary>
    public static int Write(float[] into)
    {
        var n = Math.Min(_count, into.Length / FloatsPerPiece);
        for (var i = 0; i < n; i++)
        {
            ref readonly var p = ref Pool[i];
            var o = i * FloatsPerPiece;
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
        return n;
    }

    /// <summary>Self-test: is every live piece settled at or above the floor it found and not older than its life?</summary>
    public static (int Resting, float LowestAboveFloor) Inspect()
    {
        var resting = 0;
        var lowest = float.MaxValue;
        for (var i = 0; i < _count; i++)
        {
            if (Pool[i].Resting)
                resting++;
            lowest = MathF.Min(lowest, Pool[i].Pos.Y - Pool[i].Floor);
        }
        return (resting, _count == 0 ? 0 : lowest);
    }
}
