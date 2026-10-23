using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The frenzy sparks, embers, ring bursts and tracer glow trails: purely cosmetic, spawned on each PC from the tracers it
/// already draws (nothing extra is sent). A fixed pool (<see cref="Capacity"/>, the oldest recycled when full), plain
/// arrays, no allocation after start-up. Each particle is a streak: a box stretched along its velocity (sparks) or along
/// a fixed axis (trails), packed by <see cref="Write"/> for the one instanced draw in <see cref="FrenzyFxRenderer"/>.
/// </summary>
public static class FrenzyParticles
{
    /// <summary>Most particles alive at once (level 6, 16 players firing LMGs stays under it; see the self-test).</summary>
    public const int Capacity = 2048;

    /// <summary>Floats per instance: centre + width (4), axis (full length vector) + alpha (4), colour + unused (4).</summary>
    public const int FloatsPerInstance = 12;

    private struct P
    {
        public Vector3 Pos, Vel, Axis, Color;
        public float Width, Stretch, Age, Life, Gravity, Drag, Alpha;
        public bool Fixed;
    }

    private static readonly P[] Pool = new P[Capacity];
    private static int _count, _recycle;

    /// <summary>Particles alive right now.</summary>
    public static int Count => _count;

    /// <summary>Particles spawned since start-up (stats and the self-test).</summary>
    public static long Spawned { get; private set; }

    public static void Clear() => _count = 0;

    private static ref P Next()
    {
        Spawned++;
        if (_count < Capacity)
            return ref Pool[_count++];
        // Full: take over the oldest-ish slot (round robin over the pool).
        _recycle = (_recycle + 1) % Capacity;
        return ref Pool[_recycle];
    }

    /// <summary>A moving spark: drawn as a streak <paramref name="stretch"/> seconds of travel long.</summary>
    public static void Spark(Vector3 pos, Vector3 vel, Vector3 color, float width, float life, float gravity, float drag = 1.5f, float stretch = 0.035f, float alpha = 1f)
    {
        ref var p = ref Next();
        p.Pos = pos;
        p.Vel = vel;
        p.Axis = default;
        p.Color = color;
        p.Width = width;
        p.Stretch = stretch;
        p.Age = 0;
        p.Life = MathF.Max(life, 0.02f);
        p.Gravity = gravity;
        p.Drag = drag;
        p.Alpha = alpha;
        p.Fixed = false;
    }

    /// <summary>A still glow streak from <paramref name="from"/> to <paramref name="to"/> that fades over <paramref name="life"/> seconds (tracer trails).</summary>
    public static void Trail(Vector3 from, Vector3 to, Vector3 color, float width, float life, float alpha)
    {
        ref var p = ref Next();
        p.Pos = (from + to) * 0.5f;
        p.Vel = default;
        p.Axis = to - from;
        p.Color = color;
        p.Width = width;
        p.Stretch = 0;
        p.Age = 0;
        p.Life = MathF.Max(life, 0.02f);
        p.Gravity = 0;
        p.Drag = 0;
        p.Alpha = alpha;
        p.Fixed = true;
    }

    /// <summary>Ages and moves every particle by <paramref name="dt"/> seconds; dead ones are dropped (swap-remove).</summary>
    public static void Update(float dt)
    {
        if (dt <= 0)
            return;
        for (var i = 0; i < _count;)
        {
            ref var p = ref Pool[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                Pool[i] = Pool[--_count];
                continue;
            }
            if (!p.Fixed)
            {
                if (GravityFrames.Curved)
                    p.Vel -= GravityFrames.UpAt(p.Pos) * (p.Gravity * dt); // a planet: sparks fall towards its centre
                else
                    p.Vel.Y -= p.Gravity * dt;
                p.Vel *= MathF.Max(0f, 1f - p.Drag * dt);
                p.Pos += p.Vel * dt;
            }
            i++;
        }
    }

    /// <summary>
    /// Packs up to <paramref name="max"/> live particles into <paramref name="dst"/> from instance <paramref name="start"/>
    /// on; returns how many were written. Sparks fade linearly, trails ease out (bright, then a long tail).
    /// </summary>
    public static int Write(float[] dst, int start, int max)
    {
        var n = Math.Min(_count, max);
        var o = start * FloatsPerInstance;
        for (var i = 0; i < n; i++, o += FloatsPerInstance)
        {
            ref readonly var p = ref Pool[i];
            var t = p.Age / p.Life;
            var fade = p.Fixed ? (1f - t) * (1f - t) : 1f - t;
            var axis = p.Fixed ? p.Axis : p.Vel * p.Stretch;
            dst[o] = p.Pos.X;
            dst[o + 1] = p.Pos.Y;
            dst[o + 2] = p.Pos.Z;
            dst[o + 3] = p.Width;
            dst[o + 4] = axis.X;
            dst[o + 5] = axis.Y;
            dst[o + 6] = axis.Z;
            dst[o + 7] = p.Alpha * fade;
            dst[o + 8] = p.Color.X;
            dst[o + 9] = p.Color.Y;
            dst[o + 10] = p.Color.Z;
            dst[o + 11] = 0f;
        }
        return n;
    }

    /// <summary>Writes one instance (glow boxes drawn every frame: weapon auras, the viewmodel glow). Returns the next index.</summary>
    public static int Put(float[] dst, int index, Vector3 center, Vector3 axis, float width, Vector3 color, float alpha)
    {
        if ((index + 1) * FloatsPerInstance > dst.Length || alpha <= 0.002f)
            return index;
        var o = index * FloatsPerInstance;
        dst[o] = center.X;
        dst[o + 1] = center.Y;
        dst[o + 2] = center.Z;
        dst[o + 3] = width;
        dst[o + 4] = axis.X;
        dst[o + 5] = axis.Y;
        dst[o + 6] = axis.Z;
        dst[o + 7] = alpha;
        dst[o + 8] = color.X;
        dst[o + 9] = color.Y;
        dst[o + 10] = color.Z;
        dst[o + 11] = 0f;
        return index + 1;
    }
}
