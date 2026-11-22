using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The command view's close hologram (docs/space/COMMAND.md "The close hologram"): the picked craft, or the craft being boarded,
/// once it is large on the screen is cut again finer (one at a time, kept until its parts change), its outer skin bright and its
/// inner walls dim; its doors and hatches are drawn as green boxes; and while picking where to cut in, the pod's skirt, the hole and
/// the room behind it are drawn where the host would put them (Game.BoardPreview).
/// </summary>
public sealed partial class Ui
{
    private readonly CmdHolo[] _cmdFines = [new() { Fine = true }, new() { Fine = true }]; // (the carrier and the craft it boards can both be close)
    private const int CmdFineCells = 200, CmdFineLevels = 28, CmdFineLines = 12000;
    private const float CmdFineLevelLeast = 0.9f; // m between levels at the finest (a deck and its rooms read apart)
    private const float CmdFineFrom = 220; // px across the screen (at scale 1) from which the picked craft is shown fine

    /// <summary>A cheap key of what the parts are (a part swapped, holed or broken changes it).</summary>
    private static long CmdShapeOf(Construct c)
    {
        long k = c.PartCount;
        for (var i = 0; i < c.PartCount; i++)
            k = k * 31 + c.Parts[i].Def * 2 + (c.Parts[i].Broken ? 1 : 0);
        return k;
    }

    /// <summary>Is <paramref name="h"/> still <paramref name="c"/> as it is (looked at twice a second)?</summary>
    private static bool CmdHoloSame(CmdHolo h, Construct c, double now)
    {
        if (now - h.Checked < 0.5 && now >= h.Checked)
            return true;
        h.Checked = now;
        return CmdShapeOf(c) == h.Shape;
    }

    /// <summary>Should <paramref name="c"/> be shown fine: the picked craft or the one being boarded, large on the screen?</summary>
    private bool CmdHoloFineWanted(Construct c, in CmdCraft cr) =>
        cr.Px > CmdFineFrom * _scale && (c.Id == _cmdSel || c.Id == _cmdBoardShip);

    private CmdHolo CmdHoloFineOf(Construct c, double now)
    {
        CmdHolo? h = null;
        foreach (var f in _cmdFines)
            if (f.Id == c.Id && f.Parts == c.PartCount)
                h = f;
        if (h == null || !CmdHoloSame(h, c, now))
        {
            h ??= _cmdFines[0].Used <= _cmdFines[1].Used ? _cmdFines[0] : _cmdFines[1];
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            CmdHoloBuild(h, c, CmdFineCells, CmdFineLevelLeast, CmdFineLevels, CmdFineLines, true);
            Console.WriteLine(FormattableString.Invariant($"[command] close hologram of construct {c.Id}: {h.Count} lines, {h.Nx - 2} x {h.Nz - 2} cells of {h.Cell:0.00} m, {h.Ny} levels, {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0.0} ms"));
            h.Checked = now;
            if (c.Id == _cmdBoardShip)
            {
                _cmdRoomFor = -1; // (the room outline was cut from the old grid, and the spots from the old parts)
                CmdCutForget();
            }
        }
        h.Used = now;
        return h;
    }

    /// <summary>Marks each level's empty cells that are open to the grid's edge on that level (2): what is left empty (0) is inside.</summary>
    private void CmdHoloOutside(CmdHolo h)
    {
        var (nx, nz) = (h.Nx, h.Nz);
        var grid = h.Grid;
        if (_cmdFlood.Length < nx * nz)
            _cmdFlood = new int[nx * nz];
        for (var y = 0; y < h.Ny; y++)
        {
            var b = y * nz * nx;
            var n = 0;
            for (var x = 0; x < nx; x++)
            {
                Seed(x, 0);
                Seed(x, nz - 1);
            }
            for (var z = 0; z < nz; z++)
            {
                Seed(0, z);
                Seed(nx - 1, z);
            }
            while (n > 0)
            {
                var i = _cmdFlood[--n];
                var x = i % nx;
                var z = i / nx;
                if (x > 0)
                    Seed(x - 1, z);
                if (x + 1 < nx)
                    Seed(x + 1, z);
                if (z > 0)
                    Seed(x, z - 1);
                if (z + 1 < nz)
                    Seed(x, z + 1);
            }

            void Seed(int x, int z)
            {
                var i = z * nx + x;
                if (grid[b + i] != 0)
                    return;
                grid[b + i] = 2;
                _cmdFlood[n++] = i;
            }
        }
    }

    private int[] _cmdFlood = [];

    /// <summary>Its lines: the skin bright by level, inner walls dimmer (the fine one), every few on a small coarse one.</summary>
    private void CmdHoloDrawLines(ImDrawListPtr dl, CmdHolo h, in CmdCraft cr, ReadOnlySpan<float2> ax, Vector3 com, Vector3 colour, float fade, float flicker)
    {
        var at = cr.At;
        var skip = h.Fine ? 1 : cr.Px < 60 * _scale ? 3 : cr.Px < 140 * _scale ? 2 : 1;
        var inner = Vector3.Lerp(colour, CmdDim, 0.45f);
        var top = h.Ny - 1;
        var thin = h.Fine && h.Ny > 6; // (the fine one's outlines every other level, its ribs whole: a wash of lines reads as nothing)
        for (var i = 0; i < h.Count; i += skip)
        {
            if (thin)
            {
                var yi = h.Level[i] * top;
                var r = MathF.Round(yi);
                if (MathF.Abs(yi - r) < 0.01f && ((int)r & 1) == 1 && (int)r != top)
                    continue;
            }
            var a = h.A[i] - com;
            var b = h.B[i] - com;
            var pa = at + ax[0] * a.X + ax[1] * a.Y + ax[2] * a.Z;
            var pb = at + ax[0] * b.X + ax[1] * b.Y + ax[2] * b.Z;
            if (h.Hull[i])
                dl.AddLine(pa, pb, Hud.Colour(colour, (0.28f + 0.6f * h.Level[i]) * fade * flicker), _scale);
            else
                dl.AddLine(pa, pb, Hud.Colour(inner, (0.12f + 0.26f * h.Level[i]) * fade * flicker), _scale);
        }
    }

    /// <summary>What the close hologram adds over its lines: doors and hatches, and the cut-in preview while boarding.</summary>
    private void CmdHoloExtras(ImDrawListPtr dl, Game g, Construct c, CmdHolo h, in CmdCraft cr, ReadOnlySpan<float2> ax, Vector3 com, float fade, double now)
    {
        var boarding = c.Id == _cmdBoardShip;
        if (!h.Fine && !boarding)
            return;
        // Doors and hatches: green boxes (the one under the cursor while picking a door, bright).
        if (c.EnsureDoors())
            for (var k = 0; k < c.DoorCount; k++)
            {
                var part = c.Doors[k].Part;
                if (part < 0 || part >= c.PartCount)
                    continue;
                ModuleSockets.RestBounds(c, part, out var mn, out var mx); // (rest frame: a door on a freeform weld too)
                var lit = boarding && _cmdBoardSeal && part == _cmdBoardHoverPart;
                CmdHoloBox(dl, cr.At, ax, mn - com, mx - com, CmdGreen, (lit ? 1f : boarding && _cmdBoardSeal ? 0.75f : 0.45f) * fade, lit ? 2f : 1f);
            }
        if (boarding && !_cmdBoardSeal && _cmdCutOn)
            CmdCutDraw(dl, c, h, cr, ax, com, fade, now);
    }

    /// <summary>A box (rest frame, from the centre of mass) as its twelve edges.</summary>
    private void CmdHoloBox(ImDrawListPtr dl, float2 at, ReadOnlySpan<float2> ax, Vector3 mn, Vector3 mx, Vector3 colour, float alpha, float width)
    {
        Span<float2> p = stackalloc float2[8];
        for (var i = 0; i < 8; i++)
        {
            var v = new Vector3((i & 1) != 0 ? mx.X : mn.X, (i & 2) != 0 ? mx.Y : mn.Y, (i & 4) != 0 ? mx.Z : mn.Z);
            p[i] = at + ax[0] * v.X + ax[1] * v.Y + ax[2] * v.Z;
        }
        var col = Hud.Colour(colour, alpha);
        for (var i = 0; i < 8; i++)
            for (var bit = 1; bit < 8; bit <<= 1)
                if ((i & bit) == 0)
                    dl.AddLine(p[i], p[i | bit], col, width * _scale);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The cut-in preview
    // ---------------------------------------------------------------------------------------------------------------

    private bool _cmdCutOn; // a preview to draw
    private int _cmdCutPart = -2, _cmdCutWant = -2; // the part it was worked out for, and the one under the cursor now
    private double _cmdCutSince;
    private Vector3 _cmdCutAt, _cmdCutOut, _cmdCutUp, _cmdCutFeet;
    private int _cmdCutRoom, _cmdCutHit;
    private float _cmdCutDeep;
    private int _cmdRoomFor = -1, _cmdRoomCount;
    private Vector3[] _cmdRoomA = new Vector3[CmdRoomLines], _cmdRoomB = new Vector3[CmdRoomLines];
    private byte[] _cmdRoomMark = [];
    private const int CmdRoomLines = 2400, CmdRoomCells = 6000;
    private const double CmdCutWait = 0.06; // s the cursor rests on a part before the spot is worked out (it casts many rays)

    /// <summary>
    /// Each frame while picking where to cut in: once the cursor rests on another part, the spot is worked out (Game.BoardPreview) a few
    /// milliseconds a frame until it is found, then kept for that part while the pick lasts, so going back over a part costs nothing.
    /// </summary>
    private void CmdCutUpdate(Game g, Construct t, int part, double now)
    {
        if (part != _cmdCutWant)
        {
            _cmdCutWant = part;
            _cmdCutSince = now;
        }
        if (part < 0)
        {
            _cmdCutOn = false;
            _cmdCutPart = -2;
            return;
        }
        if (part == _cmdCutPart || now - _cmdCutSince < CmdCutWait || g.Constructs.Get(_cmdBoardCarrier) is not { } carrier)
            return;
        if (_cmdCutKnownFor != t.Id)
        {
            _cmdCutKnown.Clear();
            _cmdCutKnownFor = t.Id;
        }
        CmdCut r;
        if (!_cmdCutKnown.TryGetValue(part, out r))
        {
            r.On = g.BoardPreview(carrier, t, part, CmdCutBudgetMs, out var done, out r.At, out r.Out, out r.Up, out r.Room, out r.Feet, out r.Deep, out r.Hit);
            if (!done)
            {
                _cmdCutOn = false; // (still working: next frame goes on)
                _cmdBoardHoverText = "FINDING WHERE THE POD CUTS IN...";
                return;
            }
            if (_cmdCutKnown.Count >= 256)
                _cmdCutKnown.Clear();
            _cmdCutKnown[part] = r;
        }
        _cmdCutPart = part;
        (_cmdCutOn, _cmdCutAt, _cmdCutOut, _cmdCutUp, _cmdCutRoom, _cmdCutFeet, _cmdCutDeep, _cmdCutHit) = (r.On, r.At, r.Out, r.Up, r.Room, r.Feet, r.Deep, r.Hit);
        _cmdRoomFor = -1;
        _cmdBoardHoverText = !_cmdCutOn ? "NO HULL TO CUT HERE"
            : _cmdCutRoom == 1 ? "CUT IN HERE: A ROOM BEHIND, FLOOR LEVEL"
            : _cmdCutRoom == 2 ? "CUT IN HERE: A ROOM BEHIND, A STEP UP OR DOWN"
            : "CUT IN HERE: NO ROOM FOUND BEHIND";
    }

    private struct CmdCut
    {
        public bool On;
        public Vector3 At, Out, Up, Feet;
        public int Room, Hit;
        public float Deep;
    }

    private readonly Dictionary<int, CmdCut> _cmdCutKnown = new(256); // part -> its worked-out spot, for the craft being boarded
    private ushort _cmdCutKnownFor;
    private const double CmdCutBudgetMs = 2.5; // ms a frame the preview's search may take

    /// <summary>Forget the worked-out spots (a new pick, or the target's parts changed).</summary>
    private void CmdCutForget()
    {
        _cmdCutKnown.Clear();
        _cmdCutKnownFor = 0;
        _cmdCutOn = false;
        _cmdCutPart = _cmdCutWant = -2;
    }

    /// <summary>The pod's skirt on the hull, the hole it cuts (a door's size, from the room's floor up), the tunnel in and the room behind.</summary>
    private void CmdCutDraw(ImDrawListPtr dl, Construct c, CmdHolo h, in CmdCraft cr, ReadOnlySpan<float2> ax, Vector3 com, float fade, double now)
    {
        var (at0, ex, ey, ez, c0) = (cr.At, ax[0], ax[1], ax[2], com);
        float2 P(Vector3 v)
        {
            v -= c0;
            return at0 + ex * v.X + ey * v.Y + ez * v.Z;
        }
        var o = _cmdCutOut;
        var up = _cmdCutUp;
        var side = Vector3.Normalize(Vector3.Cross(up, o));
        var face = _cmdCutAt;
        var pulse = 0.75f + 0.25f * MathF.Sin((float)now * 6f);
        // The hull piece it cuts, and the pod's skirt where its nose meets the skin.
        if (_cmdCutHit >= 0 && _cmdCutHit < c.PartCount)
        {
            c.PartBounds(_cmdCutHit, out var hmn, out var hmx);
            CmdHoloBox(dl, at0, ax, hmn - com, hmx - com, CmdAmber, 0.35f * fade, 1f);
        }
        const float sw = 1.35f, sh = 1.05f;
        CmdCutRect(dl, P(face - side * sw - up * sh), P(face + side * sw - up * sh), P(face + side * sw + up * sh), P(face - side * sw + up * sh), CmdAmber, 0.5f * fade, false);
        // The hole, as OpenHole cuts it: from just over the room's floor, a door's height, the cutter ring's width.
        var bottom = _cmdCutRoom != 0 ? Math.Clamp(Vector3.Dot(_cmdCutFeet - face, up) + 0.02f, -1.4f, -0.4f) : -1f;
        var top = bottom + ConstructBreach.HoleTall;
        var w = ConstructBreach.HoleHalfW;
        var depth = _cmdCutRoom != 0 ? _cmdCutDeep + 0.15f : ConstructBreach.HoleBlind;
        var colour = _cmdCutRoom == 1 ? CmdAmber : _cmdCutRoom == 2 ? CmdAmber : CmdRed;
        Span<Vector3> mouth = [face - side * w + up * bottom, face + side * w + up * bottom, face + side * w + up * top, face - side * w + up * top];
        var p0 = P(mouth[0]);
        var p1 = P(mouth[1]);
        var p2 = P(mouth[2]);
        var p3 = P(mouth[3]);
        CmdCutRect(dl, p0, p1, p2, p3, colour, pulse * fade, true);
        // The tunnel in, to the room's wall.
        for (var k = 0; k < 4; k++)
        {
            var a = mouth[k];
            var b = mouth[k] - o * depth;
            CmdLine(dl, P(a), P(b), colour, 0.55f * fade);
        }
        CmdCutRect(dl, P(mouth[0] - o * depth), P(mouth[1] - o * depth), P(mouth[2] - o * depth), P(mouth[3] - o * depth), colour, 0.45f * fade, false);
        // The way in: an arrow from outside to the hole.
        var mid = face + up * ((bottom + top) * 0.5f);
        CmdGlow(dl, P(mid + o * 3.5f), P(mid + o * 0.4f), colour, 0.8f * fade);
        CmdBrackets(dl, P(mid), MathF.Max(12 * _scale, (p2 - p0).Length() * 0.7f), colour, pulse * fade, 1.2f); // (the spot, however small the hull is drawn)
        // The room behind: its outline on its floor, and where a person lands.
        if (_cmdCutRoom == 0)
            return;
        if (_cmdRoomFor != _cmdCutPart)
            CmdRoomCut(h);
        var floorCol = Hud.Colour(CmdWhite, 0.8f * fade * pulse);
        for (var i = 0; i < _cmdRoomCount; i++)
            dl.AddLine(P(_cmdRoomA[i]), P(_cmdRoomB[i]), floorCol, 1.5f * _scale);
        var feet = P(_cmdCutFeet);
        CmdRing(dl, feet, 5 * _scale, CmdWhite, fade, 1.5f);
        CmdText(dl, feet + new float2(9 * _scale, -6 * _scale), 10.5f, CmdWhite, 0.9f * fade, "ROOM");
    }

    private void CmdCutRect(ImDrawListPtr dl, float2 a, float2 b, float2 c, float2 d, Vector3 colour, float alpha, bool filled)
    {
        if (filled)
        {
            var fill = Hud.Colour(colour, 0.22f * alpha);
            ImDrawListExtensions.AddTriangleFilled(dl, in a, in b, in c, fill);
            ImDrawListExtensions.AddTriangleFilled(dl, in a, in c, in d, fill);
            CmdGlow(dl, a, b, colour, alpha);
            CmdGlow(dl, b, c, colour, alpha);
            CmdGlow(dl, c, d, colour, alpha);
            CmdGlow(dl, d, a, colour, alpha);
            return;
        }
        CmdLine(dl, a, b, colour, alpha);
        CmdLine(dl, b, c, colour, alpha);
        CmdLine(dl, c, d, colour, alpha);
        CmdLine(dl, d, a, colour, alpha);
    }

    /// <summary>
    /// The room behind the hole as an outline on its floor: the fine grid's inside cells joined to the one the person lands in, on
    /// the level at waist height over that floor (a doorway lets it run on into the next room; outside cells never join).
    /// </summary>
    private void CmdRoomCut(CmdHolo h)
    {
        _cmdRoomFor = _cmdCutPart;
        _cmdRoomCount = 0;
        if (!h.Fine || h.Id != _cmdBoardShip)
            return;
        var (nx, nz, cell, min) = (h.Nx, h.Nz, h.Cell, h.Min);
        var y = Math.Clamp((int)MathF.Floor((_cmdCutFeet.Y + 1f - min.Y) / h.High), 0, h.Ny - 1);
        var b = y * nz * nx;
        var grid = h.Grid;
        if (_cmdRoomMark.Length < nx * nz)
            _cmdRoomMark = new byte[nx * nz];
        Array.Clear(_cmdRoomMark, 0, nx * nz);
        if (_cmdFlood.Length < nx * nz)
            _cmdFlood = new int[nx * nz];
        // The cell the person lands in (or the nearest inside one round it).
        var fx = (int)MathF.Floor((_cmdCutFeet.X - min.X) / cell) + 1;
        var fz = (int)MathF.Floor((_cmdCutFeet.Z - min.Z) / cell) + 1;
        var start = -1;
        for (var r = 0; r <= 3 && start < 0; r++)
            for (var dz = -r; dz <= r && start < 0; dz++)
                for (var dx = -r; dx <= r && start < 0; dx++)
                {
                    var x = fx + dx;
                    var z = fz + dz;
                    if (x >= 0 && x < nx && z >= 0 && z < nz && grid[b + z * nx + x] == 0)
                        start = z * nx + x;
                }
        if (start < 0)
            return;
        var n = 0;
        var seen = 1;
        _cmdRoomMark[start] = 1;
        _cmdFlood[n++] = start;
        while (n > 0 && seen < CmdRoomCells)
        {
            var i = _cmdFlood[--n];
            var x = i % nx;
            var z = i / nx;
            Visit(x - 1, z);
            Visit(x + 1, z);
            Visit(x, z - 1);
            Visit(x, z + 1);
        }
        // Its edges, on the floor.
        var fy = _cmdCutFeet.Y + 0.05f;
        for (var z = 0; z < nz && _cmdRoomCount < CmdRoomLines; z++)
            for (var x = 0; x < nx && _cmdRoomCount < CmdRoomLines; x++)
            {
                if (_cmdRoomMark[z * nx + x] == 0)
                    continue;
                var x0 = min.X + (x - 1) * cell;
                var z0 = min.Z + (z - 1) * cell;
                if (x == 0 || _cmdRoomMark[z * nx + x - 1] == 0)
                    Edge(new Vector3(x0, fy, z0), new Vector3(x0, fy, z0 + cell));
                if (x + 1 >= nx || _cmdRoomMark[z * nx + x + 1] == 0)
                    Edge(new Vector3(x0 + cell, fy, z0), new Vector3(x0 + cell, fy, z0 + cell));
                if (z == 0 || _cmdRoomMark[(z - 1) * nx + x] == 0)
                    Edge(new Vector3(x0, fy, z0), new Vector3(x0 + cell, fy, z0));
                if (z + 1 >= nz || _cmdRoomMark[(z + 1) * nx + x] == 0)
                    Edge(new Vector3(x0, fy, z0 + cell), new Vector3(x0 + cell, fy, z0 + cell));
            }

        void Visit(int x, int z)
        {
            if (x < 0 || x >= nx || z < 0 || z >= nz)
                return;
            var i = z * nx + x;
            if (_cmdRoomMark[i] != 0 || grid[b + i] != 0)
                return;
            _cmdRoomMark[i] = 1;
            _cmdFlood[n++] = i;
            seen++;
        }

        void Edge(Vector3 a, Vector3 e)
        {
            if (_cmdRoomCount >= CmdRoomLines)
                return;
            _cmdRoomA[_cmdRoomCount] = a;
            _cmdRoomB[_cmdRoomCount++] = e;
        }
    }
}
