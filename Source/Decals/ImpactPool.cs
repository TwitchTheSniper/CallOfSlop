using System.Numerics;

namespace CallOfSlop;

/// <summary>Settings > Video > Bullet impacts: how many impact decals stay on the map at once.</summary>
public enum ImpactDecalQuality : byte
{
    Off,
    Low,
    Medium,
    High,
}

/// <summary>
/// The bullet-impact decals of the map being played: a fixed ring of slots (no allocation per shot) mirrored into a
/// float array the renderer uploads as per-instance data. The rules:
/// <list type="bullet">
/// <item>Capacity from the setting (<see cref="CapacityFor"/>: High 1024, Medium 512, Low 128, Off none).</item>
/// <item>Oldest first: once the ring is within <see cref="Reserve"/> of full, the impact that will be overwritten next
/// starts fading, so it has faded out by the time its slot is reused (a pop only happens under extreme fire).</item>
/// <item>Each player keeps at most <see cref="PerPlayer"/> impacts: their oldest fades as they add one more.</item>
/// <item>Under pressure (ring three quarters full, or a player near their cap), shotgun pellets landing close together
/// in the same shot merge into one slightly bigger mark instead of taking a slot each.</item>
/// <item>An impact on a box that breaks or moves (props, doors, windows) fades out at once (<see cref="OnSolidChanged"/>).</item>
/// <item>A new map clears the pool.</item>
/// </list>
/// Placement is deterministic: every client gets the same end points from the server, so the same marks appear for all.
/// </summary>
public sealed class ImpactPool
{
    /// <summary>Impact decals one player may have at once.</summary>
    public const int PerPlayer = 64;

    /// <summary>Seconds an impact takes to fade out.</summary>
    public const float FadeTime = 0.6f;

    /// <summary>Per instance: position (3), size; normal (3), turn; atlas layer, birth time, fade start (-1: none), shade.</summary>
    public const int FloatsPerInstance = 12;

    /// <summary>Pellets of one shot closer than this (plus their size) merge under pressure.</summary>
    public const float MergeRadius = 0.12f;

    /// <summary>The impacts the setting keeps.</summary>
    public static int CapacityFor(ImpactDecalQuality q) => q switch
    {
        ImpactDecalQuality.High => 1024,
        ImpactDecalQuality.Medium => 512,
        ImpactDecalQuality.Low => 128,
        _ => 0,
    };

    /// <summary>How far impacts are drawn (metres, they fade over the last fifth) for a setting.</summary>
    public static float DrawDistanceFor(ImpactDecalQuality q) => q switch
    {
        ImpactDecalQuality.High => 70f,
        ImpactDecalQuality.Medium => 50f,
        _ => 35f,
    };

    private struct Slot
    {
        public Vector3 Pos, Normal;
        public float Size, Born, FadeStart;
        public long Seq;
        public int Solid;
        public byte Owner;
        public bool Live;
    }

    private Slot[] _slots = [];
    private float[] _instances = [];
    private readonly long[] _ownerSeq = new long[256 * PerPlayer]; // each player's impacts (sequence numbers), a ring of PerPlayer
    private readonly int[] _ownerStart = new int[256], _ownerCount = new int[256];
    private long _head, _tail;
    private double _epoch, _now;
    private Map? _map;

    /// <summary>Slots in the ring.</summary>
    public int Capacity { get; private set; }

    /// <summary>Slots the next adds may take before the oldest have to fade (an eighth of the ring, at least 4).</summary>
    public int Reserve => Capacity <= 0 ? 0 : Math.Max(4, Capacity / 8);

    /// <summary>Slots in use (impacts showing or fading).</summary>
    public int Count => (int)(_head - _tail);

    /// <summary>The per-instance data (<see cref="FloatsPerInstance"/> floats per slot, <see cref="Capacity"/> slots).</summary>
    public float[] Instances => _instances;

    /// <summary>The range of slots changed since <see cref="ClearDirty"/> (DirtyMax &lt; DirtyMin: none).</summary>
    public int DirtyMin { get; private set; } = int.MaxValue;

    public int DirtyMax { get; private set; } = -1;

    private readonly int[] _dirty = new int[32];

    /// <summary>
    /// The slots changed since <see cref="ClearDirty"/>, one by one (a new impact and the one it fades are usually far
    /// apart in the ring, so uploading them apart is much less than the range between). Empty when <see cref="DirtyOverflow"/>.
    /// </summary>
    public ReadOnlySpan<int> DirtySlots => DirtyOverflow ? ReadOnlySpan<int>.Empty : _dirty.AsSpan(0, DirtyCount);

    public int DirtyCount { get; private set; }

    /// <summary>More slots changed than the list holds: upload the range DirtyMin..DirtyMax instead.</summary>
    public bool DirtyOverflow { get; private set; }

    /// <summary>Slots ever written (the highest slot used + 1): the renderer draws this many instances.</summary>
    public int HighWater { get; private set; }

    /// <summary>Counters since the last <see cref="Clear"/>: impacts added, merged into another, slots taken while still showing, faded by the per-player cap, removed with their box.</summary>
    public long Added, Merged, Popped, PlayerFaded, Removed;

    /// <summary>The map the impacts are on.</summary>
    public Map? Map => _map;

    /// <summary>Shader time: seconds since the pool was cleared (birth and fade times are on this clock).</summary>
    public float Time => (float)(_now - _epoch);

    /// <summary>Changes the capacity (clears the pool). No-op if unchanged.</summary>
    public void Resize(int capacity)
    {
        capacity = Math.Clamp(capacity, 0, 8192);
        if (capacity == Capacity && _slots.Length == capacity)
            return;
        Capacity = capacity;
        _slots = new Slot[capacity];
        _instances = new float[capacity * FloatsPerInstance];
        Clear(_map, _now);
    }

    /// <summary>Removes every impact (a new map, a new match, a settings change).</summary>
    public void Clear(Map? map, double now)
    {
        _map = map;
        _epoch = now - 16.0; // shader time starts at 16 s, so every fade time (even one set a little in the past) is positive
        _now = now;
        Array.Clear(_slots);
        Array.Clear(_instances);
        Array.Clear(_ownerStart);
        Array.Clear(_ownerCount);
        _head = _tail = 0;
        HighWater = 0;
        Added = Merged = Popped = PlayerFaded = Removed = 0;
        DirtyMin = 0;
        DirtyMax = Math.Max(Capacity - 1, -1); // everything re-uploads (zeros: nothing drawn)
        DirtyOverflow = true;
    }

    /// <summary>Moves the pool's clock (the renderer calls it every frame; adds move it too).</summary>
    public void Tick(double now)
    {
        if (now > _now)
            _now = now;
    }

    public void ClearDirty()
    {
        DirtyMin = int.MaxValue;
        DirtyMax = -1;
        DirtyCount = 0;
        DirtyOverflow = false;
    }

    private void Dirty(int slot)
    {
        if (slot < DirtyMin)
            DirtyMin = slot;
        if (slot > DirtyMax)
            DirtyMax = slot;
        if (DirtyOverflow)
            return;
        for (var i = 0; i < DirtyCount; i++)
            if (_dirty[i] == slot)
                return;
        if (DirtyCount < _dirty.Length)
            _dirty[DirtyCount++] = slot;
        else
            DirtyOverflow = true;
    }

    /// <summary>
    /// Adds an impact (allocation-free). <paramref name="pellet"/>: one of several pellets of the same shot (may merge
    /// under pressure). <paramref name="solid"/>: the box it is on (-1: terrain or none). Returns false if it merged or
    /// the pool is off.
    /// </summary>
    public bool Add(Map map, Vector3 pos, Vector3 normal, float size, float rotation, int layer, byte owner, int solid, double now, bool pellet, float shade = 1f)
    {
        if (Capacity == 0)
            return false;
        if (!ReferenceEquals(map, _map))
            Clear(map, now);
        Tick(now);
        var t = Time;
        ExpireTail(t);

        int oStart = _ownerStart[owner], oCount = _ownerCount[owner];
        var pressure = Count > Capacity * 3 / 4 || oCount >= PerPlayer * 3 / 4;
        if (pellet && pressure && TryMerge(owner, oStart, oCount, pos, normal, size, t))
        {
            Merged++;
            return false;
        }

        // Per player: the oldest of theirs fades to make room.
        if (oCount >= PerPlayer)
        {
            var seq = _ownerSeq[owner * PerPlayer + oStart];
            StartFade(seq, t, ref PlayerFaded);
            oStart = (oStart + 1) % PerPlayer;
            oCount--;
        }

        // Ring full: take the oldest slot (popping it if it is still showing).
        if (_head - _tail >= Capacity)
        {
            ref var old = ref _slots[(int)(_tail % Capacity)];
            if (old.Live && old.Seq == _tail && (old.FadeStart < 0 || t - old.FadeStart < FadeTime))
                Popped++;
            old.Live = false;
            _tail++;
        }
        // Nearly full: start fading the impact Reserve adds ahead of being overwritten.
        var fadeSeq = _head + Reserve - Capacity;
        if (fadeSeq >= _tail)
        {
            long dummy = 0;
            StartFade(fadeSeq, t, ref dummy);
        }

        var slot = (int)(_head % Capacity);
        ref var s = ref _slots[slot];
        s.Pos = pos;
        s.Normal = normal;
        s.Size = size;
        s.Born = t;
        s.FadeStart = -1;
        s.Seq = _head;
        s.Solid = solid;
        s.Owner = owner;
        s.Live = true;
        var k = slot * FloatsPerInstance;
        _instances[k] = pos.X;
        _instances[k + 1] = pos.Y;
        _instances[k + 2] = pos.Z;
        _instances[k + 3] = size;
        _instances[k + 4] = normal.X;
        _instances[k + 5] = normal.Y;
        _instances[k + 6] = normal.Z;
        _instances[k + 7] = rotation;
        _instances[k + 8] = layer;
        _instances[k + 9] = t;
        _instances[k + 10] = -1f;
        _instances[k + 11] = shade;
        Dirty(slot);
        if (slot + 1 > HighWater)
            HighWater = slot + 1;

        _ownerSeq[owner * PerPlayer + (oStart + oCount) % PerPlayer] = _head;
        _ownerStart[owner] = oStart;
        _ownerCount[owner] = oCount + 1;
        _head++;
        Added++;
        return true;
    }

    /// <summary>Grows one of this player's impacts from the same moment instead of adding another (shotgun pellets).</summary>
    private bool TryMerge(byte owner, int oStart, int oCount, Vector3 pos, Vector3 normal, float size, float t)
    {
        for (var i = oCount - 1; i >= 0 && i >= oCount - 12; i--)
        {
            var seq = _ownerSeq[owner * PerPlayer + (oStart + i) % PerPlayer];
            if (seq < _tail)
                break;
            var slot = (int)(seq % Capacity);
            ref var s = ref _slots[slot];
            if (!s.Live || s.Seq != seq || s.FadeStart >= 0 || t - s.Born > 0.05f)
                continue;
            if (Vector3.Dot(s.Normal, normal) < 0.9f || Vector3.DistanceSquared(s.Pos, pos) > MathF.Pow(MergeRadius + size, 2))
                continue;
            var grown = MathF.Min(MathF.Sqrt(s.Size * s.Size + size * size * 0.5f), size * 1.8f);
            if (grown > s.Size)
            {
                s.Size = grown;
                _instances[slot * FloatsPerInstance + 3] = grown;
                Dirty(slot);
            }
            return true;
        }
        return false;
    }

    private void StartFade(long seq, float t, ref long counter)
    {
        if (seq < _tail || seq >= _head || Capacity == 0)
            return;
        var slot = (int)(seq % Capacity);
        ref var s = ref _slots[slot];
        if (!s.Live || s.Seq != seq || s.FadeStart >= 0)
            return;
        s.FadeStart = t;
        _instances[slot * FloatsPerInstance + 10] = t;
        Dirty(slot);
        counter++;
    }

    /// <summary>Frees fully faded impacts at the old end of the ring.</summary>
    private void ExpireTail(float t)
    {
        while (_tail < _head)
        {
            ref var s = ref _slots[(int)(_tail % Capacity)];
            if (s.Live && s.Seq == _tail && (s.FadeStart < 0 || t - s.FadeStart < FadeTime))
                break;
            s.Live = false;
            _tail++;
        }
    }

    /// <summary>A box of the map changed (a prop broke, a door opened): impacts on it fade out quickly.</summary>
    public void OnSolidChanged(Map map, int index)
    {
        if (!ReferenceEquals(map, _map) || Capacity == 0)
            return;
        var quick = Time - FadeTime * 0.8f;
        for (var seq = _tail; seq < _head; seq++)
        {
            var slot = (int)(seq % Capacity);
            ref var s = ref _slots[slot];
            if (!s.Live || s.Seq != seq || s.Solid != index)
                continue;
            if (s.FadeStart < 0 || s.FadeStart > quick)
            {
                s.FadeStart = quick;
                _instances[slot * FloatsPerInstance + 10] = quick;
                Dirty(slot);
                Removed++;
            }
        }
    }

    /// <summary>Impacts showing now (not fading), and how many belong to <paramref name="owner"/> (for logs and the self-test).</summary>
    public int Showing(int owner = -1)
    {
        var n = 0;
        for (var seq = _tail; seq < _head; seq++)
        {
            ref var s = ref _slots[(int)(seq % Capacity)];
            if (s.Live && s.Seq == seq && s.FadeStart < 0 && (owner < 0 || s.Owner == owner))
                n++;
        }
        return n;
    }

    /// <summary>The size of the impact in slot <paramref name="slot"/> (self-test).</summary>
    public float SizeAt(int slot) => _instances[slot * FloatsPerInstance + 3];
}
