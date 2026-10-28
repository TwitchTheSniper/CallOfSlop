using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// This frame's crowd instances for <see cref="Renderer.DrawCrowd"/> (docs/zombies/MASS-HORDE.md, "Rendering"): every
/// crowd zombie the camera can see, sorted into three levels of detail by distance (<see cref="CrowdRig"/>: near, mid,
/// far), <see cref="Floats"/> floats each: position (x, y, z), yaw, metres walked (the walk cycle's frame), brightness,
/// how much of the debug tier colour, and held (0 or 1). The host fills it from the crowd's particles, a client from
/// its picture of the cells (culled by cell). CPU only, so the self-test can time it; no allocation once grown.
/// </summary>
public sealed class CrowdInstances
{
    public const int Floats = 8, Lods = 3;

    /// <summary>Near rig up to <see cref="NearRange"/> m from the camera, mid up to <see cref="MidRange"/>, far beyond.</summary>
    public float NearRange = 35f, MidRange = 80f;

    /// <summary>The most zombies drawn in one cell (the top layer of a dense blob; CrowdTuning.DrawCap).</summary>
    public int DrawCap = 28;

    /// <summary>Tier colours (1: crowd yellow, held orange; 0: zombie colours).</summary>
    public float DebugMix;

    public readonly float[][] Data = [new float[Floats * 1024], new float[Floats * 4096], new float[Floats * 16384]];
    public readonly int[] Count = new int[Lods];

    /// <summary>Zombies and cells left out (outside the view) this frame.</summary>
    public int Culled, CellsCulled, CellsDrawn;

    public int Total => Count[0] + Count[1] + Count[2];

    public void Begin()
    {
        Array.Clear(Count);
        Culled = CellsCulled = CellsDrawn = 0;
    }

    private void Add(Vector3 cam, float x, float y, float z, float yaw, float walked, float bright, bool held)
    {
        var dx = x - cam.X;
        var dz = z - cam.Z;
        var d2 = dx * dx + dz * dz;
        var lod = d2 < NearRange * NearRange ? 0 : d2 < MidRange * MidRange ? 1 : 2;
        ref var arr = ref Data[lod];
        var n = Count[lod];
        if ((n + 1) * Floats > arr.Length)
            Array.Resize(ref arr, arr.Length * 2);
        var o = n * Floats;
        arr[o] = x;
        arr[o + 1] = y;
        arr[o + 2] = z;
        arr[o + 3] = yaw;
        arr[o + 4] = walked;
        arr[o + 5] = bright;
        arr[o + 6] = DebugMix;
        arr[o + 7] = held ? 1 : 0;
        Count[lod] = n + 1;
    }

    private static float Bright(uint h) => 0.82f + (h & 1023) / 1023f * 0.36f;

    /// <summary>The host: every crowd particle in view (a sphere test each).</summary>
    public void FromHost(HordeCrowd c, Vector3 cam)
    {
        var y = c.Y;
        for (var i = 0; i < c.Count; i++)
        {
            float x = c.X[i], z = c.Z[i];
            if (!ZombieAnim.Visible(new Vector3(x, y + 0.9f, z), 1.1f))
            {
                Culled++;
                continue;
            }
            float vx = c.VX[i], vz = c.VZ[i];
            var moving = vx * vx + vz * vz > 0.04f;
            var yaw = moving ? MathF.Atan2(vx, -vz) : MathF.Atan2(c.DX[i], -c.DZ[i]);
            var h = HordeCrowd.Hash((uint)i);
            // Metres walked: along its heading (continuous while it walks; a held one stands in its last pose).
            var f = World.FlatForward(yaw);
            var walked = x * f.X + z * f.Z + (h >> 10 & 255) / 64f;
            Add(cam, x, y, z, yaw, walked, Bright(h), c.Held[i]);
        }
        // The mass tier's cells: their top layer, placed as a client places a cell's zombies.
        if (c.MassTotal == 0)
            return;
        var size = c.MassCell;
        var r = size * 0.75f + 1f;
        for (var cell = 0; cell < c.MassCount.Length; cell++)
        {
            var m = c.MassCount[cell];
            if (m == 0)
                continue;
            var centre = c.MassCentre(cell);
            if (!ZombieAnim.Visible(centre + new Vector3(0, 0.9f, 0), r))
            {
                CellsCulled++;
                Culled += m;
                continue;
            }
            CellsDrawn++;
            float vx = c.MassVX[cell], vz = c.MassVZ[cell];
            var head = vx * vx + vz * vz > 1e-4f ? HordeCrowd.HeadingByte(MathF.Atan2(vx, -vz)) : (byte)0;
            var n = Math.Min(m, DrawCap);
            Culled += m - n;
            for (var k = 0; k < n; k++)
            {
                var at = CrowdPicture.PlaceIn(c.MassCols, c.MinX, c.MinZ, size, head, cell, k, MassTime, MassSpeed, out var yaw);
                var f = World.FlatForward(yaw);
                var h = HordeCrowd.Hash((uint)cell * 977u + (uint)k);
                var walked = head == 0 ? (h >> 10 & 255) / 64f : at.X * f.X + at.Y * f.Z + (h >> 10 & 255) / 64f;
                // (With the tier colours the mass tier is a darker yellow.)
                Add(cam, at.X, y, at.Y, yaw, walked, Bright(h) * (DebugMix > 0 ? 0.55f : 1f), false);
            }
        }
    }

    /// <summary>The host's clock and the crowd's speed for placing mass cells' zombies (set before <see cref="FromHost"/>).</summary>
    public double MassTime;

    public float MassSpeed = 1.5f;

    /// <summary>A client: each network cell in view (a sphere round the cell), its zombies placed by <see cref="CrowdPicture.Place"/>.</summary>
    public void FromPicture(CrowdPicture p, Vector3 cam, double time, float speed)
    {
        if (p.Round < 0)
            return;
        var r = p.Cell * 0.75f + 1f;
        for (var cell = 0; cell < p.Count.Length; cell++)
        {
            int n = p.Count[cell];
            if (n == 0)
                continue;
            var cx = p.MinX + (cell % p.Cols + 0.5f) * p.Cell;
            var cz = p.MinZ + (cell / p.Cols + 0.5f) * p.Cell;
            if (!ZombieAnim.Visible(new Vector3(cx, p.Y + 0.9f, cz), r))
            {
                CellsCulled++;
                Culled += n;
                continue;
            }
            CellsDrawn++;
            var still = p.Head[cell] == 0;
            Culled += Math.Max(0, n - DrawCap);
            n = Math.Min(n, DrawCap); // (the top layer of a dense cell)
            for (var k = 0; k < n; k++)
            {
                var at = p.Place(cell, k, time, speed, out var yaw);
                var f = World.FlatForward(yaw);
                var h = HordeCrowd.Hash((uint)cell * 977u + (uint)k);
                var walked = still ? (h >> 10 & 255) / 64f : at.X * f.X + at.Y * f.Z + (h >> 10 & 255) / 64f;
                Add(cam, at.X, p.Y, at.Y, yaw, walked, Bright(h), still);
            }
        }
    }
}
