using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// A planetoid's plants (docs/planets/BIOMES.md): the biomes' grass, brush, trees and rocks round the camera.
///
/// How it stays cheap:
/// <list type="bullet">
/// <item>Plants are a fixed function of the world (<see cref="PlanetFlora"/>), made a cell at a time on a worker thread
/// (at most two at once, the nearest first) and uploaded once into the cell's own static buffer, at most a few cells a
/// frame. Cells further than their layer reaches are dropped; their buffers are kept for the next cells.</item>
/// <item>Each frame only picks cells: per plant type and mesh, a cell inside the view and within that type's reach is one
/// instanced draw. Ground cover is drawn within tens of metres, brush within about 150 m, trees and rocks within 300 to
/// 700 m (Foliage quality); beyond that a forest is the biome's darker ground colour.</item>
/// <item>The same shader as a flat map's foliage, told the planet's centre: each plant stands along its own "up". It fades
/// with the per-plant dither and cross-fades its near and far meshes, so nothing pops.</item>
/// <item>Nothing is allocated per frame; dug ground rebuilds the cells round it (at most every 2 s).</item>
/// </list>
/// </summary>
public sealed unsafe partial class FoliageRenderer
{
    private sealed class FloraCell
    {
        public long Key;
        public int Layer, Face, Ci, Cj;
        public GlBuffer Vbo;
        public bool HasVbo;
        public readonly int[] Start = new int[FoliageMeshes.TypeCount];
        public readonly int[] Count = new int[FoliageMeshes.TypeCount];
        public Vector3 Min, Max;
        public int Total;
        public int Wanted;      // the frame it was last wanted
        public float Distance;  // from the camera when last wanted
        public bool Queued, Building, Dirty, Made;
        public Built? Ready;    // a finished build waiting to be uploaded
    }

    private sealed class Built
    {
        public float[] Data = [];
        public int[] Start = new int[FoliageMeshes.TypeCount], Count = new int[FoliageMeshes.TypeCount];
        public Vector3 Min, Max;
        public int Total;
    }

    private const int MaxBuilding = 2, UploadsPerFrame = 3, KeepFrames = 30;

    /// <summary>How far trees and big rocks are drawn, per quality (Low, Medium, High).</summary>
    private static readonly float[] TreeReach = [280f, 400f, 600f]; // (beyond: the far trees)

    /// <summary>How far monoliths are drawn, per quality (Low, Medium, High; Off draws them as Low: they are solid).</summary>
    private static readonly float[] RockReach = [1500f, 2200f, 3000f];

    /// <summary>
    /// Far trees (Sean, 2026-11-23: "improve far-off tree rendering ... so they don't pop in"): a cell kind of its own, four
    /// times a tree cell across, holding one tree-layer point in <see cref="FarStride"/> squared as a wide cheap lump
    /// (<see cref="FoliageType.FarBroadleaf"/>, <see cref="FoliageType.FarConifer"/>), drawn from where the near trees thin
    /// out to <see cref="FarReach"/>. They are the same trees the near cells grow at those points.
    /// </summary>
    private const int FarLayer = PlanetFlora.Layers, FarStride = 3, FarCellTimes = 4;

    /// <summary>How far the far trees are drawn, per quality (Low, Medium, High).</summary>
    private static readonly float[] FarReach = [1200f, 1800f, 2500f];

    /// <summary>Where the near trees start to thin out, as a share of their reach (the far trees come in over the same band).</summary>
    private const float TreeFadeStart = 0.78f;

    /// <summary>A layer's drawing cell in grid cells (the far trees' is four tree cells).</summary>
    private static int GridOf(Planetoid pl, PlanetFlora flora, int layer) =>
        layer == FarLayer ? Math.Min(flora.CellGrid[2] * FarCellTimes, pl.N) : flora.CellGrid[layer];

    private static FoliageType FarOf(FoliageType t) =>
        t is FoliageType.Pine or FoliageType.Spruce or FoliageType.ScotsPine ? FoliageType.FarConifer : FoliageType.FarBroadleaf;

    private readonly Dictionary<long, FloraCell> _cells = new();
    private readonly List<FloraCell> _cellList = new();
    private readonly List<FloraCell> _queue = new();
    private readonly Stack<GlBuffer> _spare = new();
    private Planetoid? _flPlanet;
    private FoliageQuality _flQuality;
    private bool _flRocksOnly;
    private PlanetClearings? _flClearOf;
    private int _flClearVersion = -1;
    private readonly List<Obb> _flClear = new(); // the clearings the uploaded cells were folded for
    private int _flFrame, _flBuilding, _flDug;
    private double _flDugAt;
    private Stats _flLog;

    /// <summary>Cells held and plants uploaded (the perf window, checks).</summary>
    public (int Cells, int Plants) PlanetFloraStats
    {
        get
        {
            var n = 0;
            foreach (var c in _cellList)
                n += c.HasVbo ? c.Total : 0;
            return (_cellList.Count, n);
        }
    }

    /// <summary>How far a type is drawn on a planet at a quality.</summary>
    private static float PlanetReach(FoliageType t, FoliageQuality q)
    {
        var i = Math.Clamp((int)q - 1, 0, 2);
        if (Foliage.IsMonolith(t))
            return RockReach[i];
        if (t is FoliageType.FarBroadleaf or FoliageType.FarConifer)
            return FarReach[i];
        if (Foliage.IsTree(t) || t == FoliageType.Stem)
            return TreeReach[i];
        if (t == FoliageType.Boulder)
            return TreeReach[i] * 0.6f;
        return Foliage.FadeDistance(t, q);
    }

    private static float LayerReach(int layer, FoliageQuality q)
    {
        if (layer == FarLayer)
            return FarReach[Math.Clamp((int)q - 1, 0, 2)];
        var r = 0f;
        for (var t = 0; t < FoliageMeshes.TypeCount; t++)
            if (PlanetFlora.LayerOf((FoliageType)t) == layer)
                r = MathF.Max(r, PlanetReach((FoliageType)t, q));
        return r;
    }

    /// <summary>Picks, builds and draws a planetoid's plants. The caller has checked the planet has biomes.</summary>
    private void DrawPlanetFlora(Planetoid pl, PlanetFlora flora, Renderer gfx, FoliageQuality quality, double now, ConstructWorld? constructs)
    {
        // Foliage Off still draws the monoliths (as Low): they are solid ground, not decoration.
        var rocksOnly = quality == FoliageQuality.Off;
        if (rocksOnly && !flora.Grows(PlanetFlora.MonolithLayer))
        {
            FreeFlora();
            return;
        }
        if (rocksOnly)
            quality = FoliageQuality.Low;
        if (rocksOnly != _flRocksOnly)
        {
            FreeFlora();
            _flRocksOnly = rocksOnly;
        }
        if (!ReferenceEquals(pl, _flPlanet) || quality != _flQuality)
        {
            FreeFlora();
            _flPlanet = pl;
            _flQuality = quality;
        }
        var t0 = Stopwatch.GetTimestamp();
        _flFrame++;
        var cam = gfx.CameraPosition;
        var rel = cam - pl.Center;
        var camDist = rel.Length();
        if (camDist < 1)
            return;
        var up = rel / camDist;
        var alt = camDist - (float)pl.Radius - (float)pl.HeightAt(up.X, up.Y, up.Z);

        // Anchored bases (Sean, 2026-11-23): the cells round a clearing that came or went are made again, and fold its plants
        // as they upload (PlanetClearings.cs).
        if (constructs != null)
            constructs.FloraClear.Update(constructs, pl);
        var clear = constructs?.FloraClear;
        if (!ReferenceEquals(clear, _flClearOf) || clear != null && clear.Version != _flClearVersion)
        {
            foreach (var c in _cellList)
                if (c.Layer != PlanetFlora.MonolithLayer && (Touches(c, _flClear) || clear != null && Touches(c, clear.Boxes)))
                    c.Dirty = true;
            _flClear.Clear();
            if (clear != null)
                _flClear.AddRange(clear.Boxes);
            _flClearOf = clear;
            _flClearVersion = clear?.Version ?? -1;
        }

        // Dug ground: build the cells again (the old ones draw until the new are ready).
        if (pl.DugVersion != _flDug && now - _flDugAt > 2)
        {
            _flDug = pl.DugVersion;
            _flDugAt = now;
            foreach (var c in _cellList)
                c.Dirty = true;
        }

        for (var layer = rocksOnly ? PlanetFlora.MonolithLayer : 0; layer < PlanetFlora.Layers; layer++)
            if (flora.Grows(layer))
                WantCells(pl, flora, layer, LayerReach(layer, quality), up, cam, alt);
        if (!rocksOnly && flora.Grows(2))
            WantCells(pl, flora, FarLayer, LayerReach(FarLayer, quality), up, cam, alt);

        // Drop what isn't wanted; queue what is and needs making. (The workers' ends set Building and Ready under the lock.)
        Monitor.Enter(_cells);
        for (var i = _cellList.Count - 1; i >= 0; i--)
        {
            var c = _cellList[i];
            if (_flFrame - c.Wanted > KeepFrames && !c.Building)
            {
                if (c.HasVbo)
                    _spare.Push(c.Vbo);
                _cells.Remove(c.Key);
                _cellList.RemoveAt(i);
                if (c.Queued)
                    _queue.Remove(c);
                continue;
            }
            if ((!c.Made && c.Ready == null || c.Dirty) && !c.Queued && !c.Building && c.Wanted == _flFrame)
            {
                c.Queued = true;
                _queue.Add(c);
            }
        }
        StartBuilds(flora, quality);
        Monitor.Exit(_cells);
        var uploads = 0;
        foreach (var c in _cellList)
            if (Volatile.Read(ref c.Ready) != null && uploads < UploadsPerFrame)
            {
                UploadCell(c);
                uploads++;
            }

        if (!_ready && !Init())
            return;
        ExtractPlanes(gfx.ViewProj);
        _program.Use();
        var vp = gfx.ViewProj;
        Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
        Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
        var fog = gfx.FogColor;
        var fogRange = gfx.FogRange;
        Native.glUniform3f(_locFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_locFogRange, fogRange.X, fogRange.Y);
        Native.glUniform1f(_locTime, (float)(now % 3600.0));
        var pc = pl.Center;
        Native.glUniform4f(_locPlanet, pc.X, pc.Y, pc.Z, 1f);
        _vao.Bind();

        long draws = 0, instances = 0;
        for (var t = 0; t < FoliageMeshes.TypeCount; t++)
        {
            var type = (FoliageType)t;
            if (PlanetFlora.LayerOf(type) < 0 && type is not (FoliageType.Stem or FoliageType.FarBroadleaf or FoliageType.FarConifer) || rocksOnly && !Foliage.IsMonolith(type))
                continue;
            var fadeEnd = PlanetReach(type, quality);
            var fadeStart = fadeEnd * (Foliage.IsTree(type) || type == FoliageType.Stem ? TreeFadeStart : Foliage.FadeStartFraction);
            var lodStart = Foliage.LodDistance(type, quality);
            var lodEnd = lodStart * 1.3f;
            if (type is FoliageType.FarBroadleaf or FoliageType.FarConifer)
            {
                // (no near mesh: they come in, dithered, over the band where the near trees thin out)
                var trees = TreeReach[Math.Clamp((int)quality - 1, 0, 2)];
                lodStart = trees * TreeFadeStart;
                lodEnd = trees * 0.97f;
            }
            Native.glUniform4f(_locFade, fadeStart, fadeEnd, lodStart, lodEnd);
            var (sway, wrap) = Material(type);
            for (var lod = 0; lod < 2; lod++)
            {
                Native.glUniform4f(_locMat, lod, sway, wrap, 0f);
                var mesh = FoliageMeshes.Mesh(type, lod);
                if (mesh.IndexCount == 0)
                    continue;
                foreach (var c in _cellList)
                {
                    if (!c.HasVbo || c.Count[t] == 0)
                        continue;
                    var near = NearDistance(cam, c.Min, c.Max);
                    if (near >= fadeEnd || (lod == 1 ? FarDistance(cam, c.Min, c.Max) <= lodStart : near >= lodEnd) || !InFrustum(c.Min, c.Max))
                        continue;
                    Native.glBindBuffer(GL_ARRAY_BUFFER, c.Vbo);
                    DrawRun(mesh, c.Start[t], c.Count[t]);
                    draws++;
                    instances += c.Count[t];
                }
            }
        }
        Native.glUniform4f(_locPlanet, 0f, 0f, 0f, 0f);
        gfx.Rebind();

        var cpuMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Accumulate(ref _flLog, now, draws, instances, cpuMs);
        Accumulate(ref Window, now, draws, instances, cpuMs);
        if (now - _flLog.Start >= LogInterval)
        {
            var (cells, plants) = PlanetFloraStats;
            Console.WriteLine($"[foliage] planet, {quality}: {cells} cells, {plants} plants uploaded, {_flLog.AvgInstances:0} drawn/frame in {_flLog.AvgDraws:0.0} draw calls, "
                + $"CPU {_flLog.AvgCpuMs:0.000} ms, {_queue.Count} cells queued");
            _flLog = new Stats { Start = now };
        }
    }

    /// <summary>Marks the cells of a layer within <paramref name="reach"/> of the camera as wanted (making new ones).</summary>
    private void WantCells(Planetoid pl, PlanetFlora flora, int layer, float reach, Vector3 up, Vector3 cam, float alt)
    {
        if (alt > reach)
            return;
        var g = GridOf(pl, flora, layer);
        var across = pl.N / g;
        var cellM = (float)(g * pl.Cell);
        var k = (int)MathF.Ceiling(reach / cellM) + 1;
        var ground = (float)pl.Radius + (float)pl.HeightAt(up.X, up.Y, up.Z);
        for (var f = 0; f < 6; f++)
        {
            Planetoid.FaceBasis(f, out var fn, out var fu, out var fv);
            var dn = Vector3.Dot(up, fn);
            if (dn < 0.3f)
                continue; // (a face more than about 70 degrees round: a 700 m reach never gets there)
            var gi = (Planetoid.Unwarp(Vector3.Dot(up, fu) / dn) + 1) * 0.5 * pl.N / g;
            var gj = (Planetoid.Unwarp(Vector3.Dot(up, fv) / dn) + 1) * 0.5 * pl.N / g;
            int i0 = Math.Max(0, (int)gi - k), i1 = Math.Min(across - 1, (int)gi + k);
            int j0 = Math.Max(0, (int)gj - k), j1 = Math.Min(across - 1, (int)gj + k);
            for (var cj = j0; cj <= j1; cj++)
            for (var ci = i0; ci <= i1; ci++)
            {
                pl.DirOf(f, (ci + 0.5) * g, (cj + 0.5) * g, out var x, out var y, out var z);
                var mid = pl.Center + new Vector3((float)x, (float)y, (float)z) * ground;
                var d = Vector3.Distance(mid, cam) - cellM * 0.75f;
                if (d > reach)
                    continue;
                var key = ((long)layer << 60) | ((long)f << 56) | ((long)ci << 28) | (uint)cj;
                if (!_cells.TryGetValue(key, out var c))
                {
                    c = new FloraCell { Key = key, Layer = layer, Face = f, Ci = ci, Cj = cj };
                    _cells[key] = c;
                    _cellList.Add(c);
                }
                c.Wanted = _flFrame;
                c.Distance = MathF.Max(d, 0);
            }
        }
    }

    private void StartBuilds(PlanetFlora flora, FoliageQuality quality)
    {
        while (_flBuilding < MaxBuilding && _queue.Count > 0)
        {
            var best = 0;
            for (var i = 1; i < _queue.Count; i++)
                if (_queue[i].Distance < _queue[best].Distance)
                    best = i;
            var c = _queue[best];
            _queue.RemoveAt(best);
            c.Queued = false;
            c.Building = true;
            c.Dirty = false;
            _flBuilding++;
            var (layer, face, ci, cj) = (c.Layer, c.Face, c.Ci, c.Cj);
            Task.Run(() => BuildCell(flora, quality, layer, face, ci, cj)).ContinueWith(t =>
            {
                lock (_cells)
                {
                    if (t.IsCompletedSuccessfully)
                        c.Ready = t.Result;
                    else
                        Console.WriteLine($"[foliage] a planet cell failed to build: {t.Exception?.GetBaseException().Message}");
                    c.Building = false;
                    _flBuilding--;
                }
            });
        }
    }

    /// <summary>A cell's plants, sorted by type, as instance data (a worker thread).</summary>
    private static Built BuildCell(PlanetFlora flora, FoliageQuality quality, int layer, int face, int ci, int cj)
    {
        var lists = new List<PlanetFlora.Plant>[FoliageMeshes.TypeCount];
        var total = 0;
        if (layer == FarLayer)
        {
            // One tree-layer point in FarStride squared, as a far lump (the same tree its near cell grows there).
            var g = Math.Min(flora.CellGrid[2] * FarCellTimes, flora.Planet.N);
            var (fa0, fa1) = flora.PointsInCell(2, ci, g);
            var (fb0, fb1) = flora.PointsInCell(2, cj, g);
            for (var b = (fb0 + FarStride - 1) / FarStride * FarStride; b < fb1; b += FarStride)
            for (var a = (fa0 + FarStride - 1) / FarStride * FarStride; a < fa1; a += FarStride)
            {
                if (!flora.At(2, face, a, b, out var p) || !Foliage.IsTree(p.Type))
                    continue;
                p.Type = FarOf(p.Type);
                p.Color *= 0.92f; // (a mass of canopies is a little darker than one lit tree)
                (lists[(int)p.Type] ??= new()).Add(p);
                total++;
            }
            return Pack(lists, total);
        }
        var (a0, a1) = flora.PointsInCell(layer, ci);
        var (b0, b1) = flora.PointsInCell(layer, cj);
        for (var b = b0; b < b1; b++)
        for (var a = a0; a < a1; a++)
        {
            if (!flora.At(layer, face, a, b, out var p) || !Foliage.IsMonolith(p.Type) && p.Rand >= Foliage.DensityFor(p.Type, quality))
                continue;
            (lists[(int)p.Type] ??= new()).Add(p);
            total++;
            if (Foliage.IsTree(p.Type))
            {
                // Its trunk: a thin stem as tall as its share of the tree, in its bark colour.
                var s = p;
                s.Type = FoliageType.Stem;
                s.Scale = p.Scale * PlanetFlora.StemShare(p.Type);
                s.Color = p.Bark;
                (lists[(int)FoliageType.Stem] ??= new()).Add(s);
                total++;
            }
        }
        return Pack(lists, total);
    }

    /// <summary>A cell's plants, by type, as instance data and bounds.</summary>
    private static Built Pack(List<PlanetFlora.Plant>?[] lists, int total)
    {
        var r = new Built { Data = new float[total * FoliageBatches.FloatsPerInstance], Total = total };
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var o = 0;
        for (var t = 0; t < FoliageMeshes.TypeCount; t++)
        {
            r.Start[t] = o / FoliageBatches.FloatsPerInstance;
            if (lists[t] is not { } list)
                continue;
            r.Count[t] = list.Count;
            var (reach, height) = FoliageMeshes.Extent[t];
            var ext = MathF.Max(reach, height) + 0.15f;
            foreach (var p in list)
            {
                var d = r.Data;
                d[o] = p.Pos.X;
                d[o + 1] = p.Pos.Y;
                d[o + 2] = p.Pos.Z;
                d[o + 3] = p.Scale;
                d[o + 4] = p.Color.X;
                d[o + 5] = p.Color.Y;
                d[o + 6] = p.Color.Z;
                d[o + 7] = p.Yaw01;
                o += FoliageBatches.FloatsPerInstance;
                var e = new Vector3(ext * p.Scale);
                min = Vector3.Min(min, p.Pos - e);
                max = Vector3.Max(max, p.Pos + e);
            }
        }
        r.Min = min;
        r.Max = max;
        return r;
    }

    private void UploadCell(FloraCell c)
    {
        Built b;
        lock (_cells)
        {
            b = c.Ready!;
            c.Ready = null;
        }
        if (!_ready && !Init())
            return;
        c.Made = true;
        if (b.Total == 0)
        {
            // (an empty cell stays held, so it isn't made again while it is wanted)
            if (c.HasVbo)
                _spare.Push(c.Vbo);
            c.HasVbo = false;
            c.Total = 0;
            Array.Clear(c.Count);
            return;
        }
        if (!c.HasVbo)
        {
            c.Vbo = _spare.Count > 0 ? _spare.Pop() : GenBuffer();
            c.HasVbo = true;
        }
        if (_flPlanet is { DugVersion: not 0, Flora: { } flora })
        {
            // Plants on dug ground fold away (scale 0): the dug ground is read here, on the main thread, never by the workers.
            var d = b.Data;
            var rocks = b.Count.Length > 0 && b.Total > 0 && c.Layer == PlanetFlora.MonolithLayer; // (monoliths stand whatever is dug round them)
            for (var o = 0; o < d.Length && !rocks; o += FoliageBatches.FloatsPerInstance)
                if (flora.IsDug(new Vector3(d[o], d[o + 1], d[o + 2])))
                    d[o + 3] = 0;
        }
        if (_flPlanet is { } here)
            FoldClearings(b, c.Layer, here);
        Native.glBindBuffer(GL_ARRAY_BUFFER, c.Vbo);
        fixed (float* p = b.Data)
            Native.glBufferData(GL_ARRAY_BUFFER, b.Data.Length * sizeof(float), p, GL_STATIC_DRAW);
        Array.Copy(b.Start, c.Start, c.Start.Length);
        Array.Copy(b.Count, c.Count, c.Count.Length);
        c.Min = b.Min;
        c.Max = b.Max;
        c.Total = b.Total;
    }

    /// <summary>Does a cell's plants' box reach any of these clearings (with the widest margin)?</summary>
    private static bool Touches(FloraCell c, List<Obb> boxes)
    {
        if (!c.Made)
            return false;
        var m = new Vector3(PlanetClearings.TreeMargin + 3);
        foreach (var b in boxes)
        {
            b.Bounds(out var lo, out var hi);
            if (hi.X + m.X >= c.Min.X && lo.X - m.X <= c.Max.X && hi.Y + m.Y >= c.Min.Y && lo.Y - m.Y <= c.Max.Y && hi.Z + m.Z >= c.Min.Z && lo.Z - m.Z <= c.Max.Z)
                return true;
        }
        return false;
    }

    /// <summary>Folds away (scale 0) a built cell's plants whose foot stands in a clearing (the main thread).</summary>
    private void FoldClearings(Built b, int layer, Planetoid pl)
    {
        if (_flClear.Count == 0 || b.Total == 0 || layer == PlanetFlora.MonolithLayer)
            return;
        var m = new Vector3(PlanetClearings.TreeMargin + 3);
        var any = false;
        foreach (var box in _flClear)
        {
            box.Bounds(out var lo, out var hi);
            if (hi.X + m.X >= b.Min.X && lo.X - m.X <= b.Max.X && hi.Y + m.Y >= b.Min.Y && lo.Y - m.Y <= b.Max.Y && hi.Z + m.Z >= b.Min.Z && lo.Z - m.Z <= b.Max.Z)
            {
                any = true;
                break;
            }
        }
        if (!any)
            return;
        var d = b.Data;
        for (var t = 0; t < FoliageMeshes.TypeCount; t++)
        {
            if (b.Count[t] == 0)
                continue;
            var type = (FoliageType)t;
            var margin = Foliage.IsTree(type) || type == FoliageType.Stem ? PlanetClearings.TreeMargin : PlanetClearings.PlantMargin;
            for (var i = 0; i < b.Count[t]; i++)
            {
                var o = (b.Start[t] + i) * FoliageBatches.FloatsPerInstance;
                var foot = new Vector3(d[o], d[o + 1], d[o + 2]);
                var up = Vector3.Normalize(foot - pl.Center);
                foreach (var box in _flClear)
                    if (PlanetClearings.Covers(box, foot, up, margin))
                    {
                        d[o + 3] = 0;
                        break;
                    }
            }
        }
    }

    /// <summary>Releases every planet cell (another world, quality Off, or a flat map).</summary>
    private void FreeFlora()
    {
        if (_cellList.Count == 0 && _spare.Count == 0)
            return;
        foreach (var c in _cellList)
            if (c.HasVbo)
                _spare.Push(c.Vbo);
        _cellList.Clear();
        _cells.Clear();
        _queue.Clear();
        while (_spare.Count > 0)
        {
            var b = _spare.Pop();
            Native.glDeleteBuffers(1, &b);
        }
        _flPlanet = null;
    }

    private static float NearDistance(Vector3 cam, Vector3 min, Vector3 max) => Vector3.Distance(cam, Vector3.Clamp(cam, min, max));

    private static float FarDistance(Vector3 cam, Vector3 min, Vector3 max)
    {
        var far = new Vector3(MathF.Max(MathF.Abs(min.X - cam.X), MathF.Abs(max.X - cam.X)), MathF.Max(MathF.Abs(min.Y - cam.Y), MathF.Abs(max.Y - cam.Y)),
            MathF.Max(MathF.Abs(min.Z - cam.Z), MathF.Abs(max.Z - cam.Z)));
        return far.Length();
    }
}
