namespace CallOfSlop;

/// <summary>
/// The drawing blocks of dug ground (docs/sandbox/TERRAIN-DIGGING.md, "What it costs"): <see cref="Cells"/> x
/// <see cref="Cells"/> terrain cells each, with a change count. A renderer keeps one small mesh per block and makes again
/// only the blocks whose count moved, so a scoop of the drill costs the block or two it touched and not a whole chunk of
/// a worked site. Only blocks that have held dug ground are listed.
/// </summary>
public sealed class DigBlocks
{
    /// <summary>Terrain cells along a block's side.</summary>
    public const int Cells = 8;

    private readonly Dictionary<int, int> _version = new();
    private readonly List<int> _keys = new();

    /// <summary>Blocks touched so far, in the order they first were.</summary>
    public List<int> Keys => _keys;

    /// <summary>A block's change count (0: never touched).</summary>
    public int Version(int key) => _version.TryGetValue(key, out var v) ? v : 0;

    public void Bump(int key)
    {
        if (_version.TryGetValue(key, out var v))
            _version[key] = v + 1;
        else
        {
            _version[key] = 1;
            _keys.Add(key);
        }
    }

    /// <summary>Every listed block changed (the ground went back: their meshes empty out).</summary>
    public void BumpAll()
    {
        foreach (var k in _keys)
            _version[k]++;
    }

    public void Clear()
    {
        _version.Clear();
        _keys.Clear();
    }
}
