using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A client's picture of the mass horde's crowd (docs/zombies/MASS-HORDE.md, "Network"): per network cell, how many crowd
/// zombies are in it and which way they are heading, from <see cref="Msg.ZCrowd"/> (protocol 71). The client never
/// knows where each crowd zombie is: it fills each cell itself, the same way on every client, from the cell's index and
/// the zombie's number in it (<see cref="Place"/>). Approximate (a cell's zombies drift along its heading and wrap round
/// inside it) and consistent. The individual zombies near the players come in <see cref="Msg.ZEnts"/> as ever.
/// </summary>
public sealed class CrowdPicture
{
    public int Cols, Rows;
    public float Cell = 4f, MinX, MinZ, Y;

    /// <summary>The round the picture belongs to (a keyframe starts a new one); -1 none.</summary>
    public int Round = -1;

    public byte[] Count = [], Head = [];

    /// <summary>Crowd zombies in the picture (the cells' counts summed).</summary>
    public int Total;

    /// <summary>Messages read and bytes in them (self-test, log).</summary>
    public int Reads;

    public long BytesRead;

    public void Clear()
    {
        Round = -1;
        Total = 0;
        Array.Clear(Count);
        Array.Clear(Head);
    }

    /// <summary>A keyframe: the grid's shape (allocates only when it changes) and every cell empty.</summary>
    public void Reset(int round, int cols, int rows, float cell, float minX, float minZ, float y)
    {
        Round = round;
        Cols = cols;
        Rows = rows;
        Cell = cell;
        MinX = minX;
        MinZ = minZ;
        Y = y;
        var n = cols * rows;
        if (Count.Length != n)
        {
            Count = new byte[n];
            Head = new byte[n];
        }
        else
        {
            Array.Clear(Count);
            Array.Clear(Head);
        }
        Total = 0;
    }

    public void Set(int cell, byte count, byte head)
    {
        if ((uint)cell >= (uint)Count.Length)
            return;
        Total += count - Count[cell];
        Count[cell] = count;
        Head[cell] = head;
    }

    /// <summary>
    /// Where crowd zombie <paramref name="k"/> of cell <paramref name="cell"/> stands at <paramref name="time"/> (flat x, z)
    /// and its yaw: a fixed spot in the cell from a hash, drifting along the cell's heading at <paramref name="speed"/> and
    /// wrapping round inside the cell (standing still with no heading). The same on every client.
    /// </summary>
    public Vector2 Place(int cell, int k, double time, float speed, out float yaw)
    {
        var h = HordeCrowd.Hash((uint)cell * 2654435761u ^ (uint)k * 40503u ^ 0x5EEDu);
        var u = (h & 0xFFFF) / 65536f;
        var v = (h >> 16) / 65536f;
        var c = cell % Cols;
        var r = cell / Cols;
        var ox = MinX + c * Cell;
        var oz = MinZ + r * Cell;
        var hb = Head[cell];
        // A little spread in yaw per zombie, so a cell doesn't march in step.
        yaw = HordeCrowd.HeadingYaw(hb) + ((h >> 8 & 255) / 255f - 0.5f) * 0.5f;
        var px = u * Cell;
        var pz = v * Cell;
        if (hb != 0)
        {
            var phase = (float)((time * speed * (0.8 + 0.4 * ((h >> 4 & 255) / 255.0))) % Cell);
            var f = World.FlatForward(yaw);
            px += f.X * phase;
            pz += f.Z * phase;
        }
        px -= MathF.Floor(px / Cell) * Cell;
        pz -= MathF.Floor(pz / Cell) * Cell;
        return new Vector2(ox + px, oz + pz);
    }
}
