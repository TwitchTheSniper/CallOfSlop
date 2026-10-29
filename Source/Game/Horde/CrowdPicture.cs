using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A client's picture of the mass horde's crowd (docs/zombies/MASS-HORDE.md, "Network"): per network cell, how many crowd
/// zombies are in it and which way they are heading, from <see cref="Msg.ZCrowd"/> (protocol 71). The client never
/// knows where each crowd zombie is: its persistent visual agents (<see cref="CrowdAgents"/>) stand in for each cell's
/// zombies, walking its heading. The individual zombies near the players come in <see cref="Msg.ZEnts"/> as ever.
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
}
