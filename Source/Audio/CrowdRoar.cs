using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The mass horde's crowd roar (docs/zombies/MASS-HORDE.md, "Audio"): the crowd's cells (the same counts the crowd
/// surface reads: the server's own, a client's picture) grouped into clusters of <see cref="Group"/> x <see cref="Group"/>
/// cells, and the <see cref="Voices"/> clusters that matter most to the listener (zombies over distance squared) each
/// get a looping roar (<see cref="Audio.CrowdRoarAt"/>) at the cluster's centre of mass, as loud as its density: a few
/// hundred zombies is a full roar, a dozen a murmur. Picked again every <see cref="PickEvery"/> seconds; between picks
/// each voice eases to its cluster's spot and loudness, and a voice keeps its cluster when the cluster is still there,
/// so the roar never jumps. The individual zombies near the players keep their own voices (<see cref="ZombieSounds"/>,
/// under its cap): the roar is the crowd behind them. Allocation-free; nothing off crowd maps.
/// </summary>
public sealed class CrowdRoar
{
    public const int Voices = 4, Group = 3;

    public const float PickEvery = 0.25f;

    /// <summary>A cluster of this many zombies roars at full gain (<see cref="FullGain"/>); fewer in proportion to the square root.</summary>
    public float Full = 240f, FullGain = 0.9f;

    /// <summary>Clusters under this many zombies are too thin to roar.</summary>
    public int MinZombies = 12;

    /// <summary>Each voice: its cluster's spot and loudness (the goal), what it sounds at now, its loop variant.</summary>
    public readonly Vector3[] Goal = new Vector3[Voices], At = new Vector3[Voices];

    public readonly float[] GoalGain = new float[Voices], Gain = new float[Voices];

    public readonly int[] Variant = new int[Voices];

    public readonly int[] Zombies = new int[Voices];

    private readonly bool[] _live = new bool[Voices];
    private readonly bool[] _taken = new bool[Voices];
    private float _sincePick = PickEvery;

    // This pick's best clusters.
    private readonly Vector3[] _pickAt = new Vector3[Voices];
    private readonly float[] _pickScore = new float[Voices];
    private readonly int[] _pickN = new int[Voices];
    private int _picks;

    /// <summary>Picks made and voices sounding (self-test, log).</summary>
    public int Picks, Sounding;

    /// <summary>Everything off (a new map, the crowd gone).</summary>
    public void Clear(Audio audio)
    {
        for (var v = 0; v < Voices; v++)
        {
            _live[v] = false;
            Gain[v] = GoalGain[v] = 0;
            audio.CrowdRoarStop(v);
        }
        Sounding = 0;
        _sincePick = PickEvery;
    }

    /// <summary>Every frame on a crowd map: <paramref name="cells"/> is the bound surface (its counts), <paramref name="listener"/> the ear.</summary>
    public void Frame(Audio audio, CrowdSurface cells, Vector3 listener, float dt)
    {
        _sincePick += dt;
        if (_sincePick >= PickEvery)
        {
            _sincePick = 0;
            Pick(cells, listener);
            Assign();
            Picks++;
        }
        // Ease towards the goals (a quarter of the way a frame at 60 Hz: about 0.1 s), fading out voices without a cluster.
        var k = 1f - MathF.Exp(-dt * 12f);
        Sounding = 0;
        for (var v = 0; v < Voices; v++)
        {
            if (!_live[v])
            {
                Gain[v] += (0 - Gain[v]) * k;
                if (Gain[v] < 0.01f)
                {
                    Gain[v] = 0;
                    audio.CrowdRoarStop(v);
                    continue;
                }
            }
            else
            {
                At[v] += (Goal[v] - At[v]) * k;
                Gain[v] += (GoalGain[v] - Gain[v]) * k;
            }
            audio.CrowdRoarAt(v, Variant[v], At[v], Gain[v]);
            Sounding++;
        }
    }

    /// <summary>The <see cref="Voices"/> clusters that matter most: zombies over distance squared (never nearer than 8 m).</summary>
    private void Pick(CrowdSurface cells, Vector3 listener)
    {
        _picks = 0;
        if (!cells.Bound)
            return;
        var cell = cells.Cell;
        for (var r0 = 0; r0 < cells.Rows; r0 += Group)
            for (var c0 = 0; c0 < cells.Cols; c0 += Group)
            {
                var n = 0;
                float sx = 0, sz = 0;
                for (var r = r0; r < r0 + Group; r++)
                    for (var c = c0; c < c0 + Group; c++)
                    {
                        var m = cells.CountAt(c, r);
                        if (m == 0)
                            continue;
                        n += m;
                        sx += m * (cells.MinX + (c + 0.5f) * cell);
                        sz += m * (cells.MinZ + (r + 0.5f) * cell);
                    }
                if (n < MinZombies)
                    continue;
                var at = new Vector3(sx / n, cells.FloorY + 1.2f, sz / n);
                var dx = at.X - listener.X;
                var dz = at.Z - listener.Z;
                var score = n / MathF.Max(64f, dx * dx + dz * dz);
                // Insertion into the best few (highest score first).
                if (_picks == Voices && score <= _pickScore[_picks - 1])
                    continue;
                var i = _picks < Voices ? _picks++ : _picks - 1;
                while (i > 0 && _pickScore[i - 1] < score)
                {
                    _pickScore[i] = _pickScore[i - 1];
                    _pickAt[i] = _pickAt[i - 1];
                    _pickN[i] = _pickN[i - 1];
                    i--;
                }
                _pickScore[i] = score;
                _pickAt[i] = at;
                _pickN[i] = n;
            }
    }

    /// <summary>Each picked cluster to the voice already nearest it (within a cluster's width), else a free voice, else the quietest.</summary>
    private void Assign()
    {
        Array.Clear(_taken);
        var reach = 1.5f * Group * 4f; // (about a cluster and a half; the cell size hardly varies)
        for (var p = 0; p < _picks; p++)
        {
            var best = -1;
            var bestD = reach * reach;
            for (var v = 0; v < Voices; v++)
            {
                if (_taken[v] || !_live[v])
                    continue;
                var d = (Goal[v] - _pickAt[p]).LengthSquared();
                if (d < bestD)
                {
                    bestD = d;
                    best = v;
                }
            }
            if (best < 0)
                for (var v = 0; v < Voices && best < 0; v++)
                    if (!_taken[v] && !_live[v])
                        best = v;
            if (best < 0)
            {
                var quiet = float.MaxValue;
                for (var v = 0; v < Voices; v++)
                    if (!_taken[v] && Gain[v] < quiet)
                    {
                        quiet = Gain[v];
                        best = v;
                    }
            }
            if (best < 0)
                break;
            _taken[best] = true;
            if (!_live[best])
            {
                // A new voice: it starts at its cluster (no glide across the map), with the next loop variant along.
                At[best] = _pickAt[p];
                Gain[best] = 0;
                Variant[best] = (Picks + best) % Synth.CrowdRoarVariants;
                _live[best] = true;
            }
            Goal[best] = _pickAt[p];
            Zombies[best] = _pickN[p];
            GoalGain[best] = FullGain * MathF.Min(1f, MathF.Sqrt(_pickN[p] / Full));
        }
        for (var v = 0; v < Voices; v++)
            if (!_taken[v])
                _live[v] = false;
    }
}
