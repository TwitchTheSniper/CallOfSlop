using System.Numerics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// Floor sheets (docs/render/MERGED-MESHES.md "Floor sheets", Sean 2026-11-27): a part type marked <c>"sheet": true</c> (a Pad
/// Tile, a Bay Block) is a floor tile, and a rectangle of like tiles in a merged chunk is drawn as ONE box with the tile's look
/// baked into a texture, in place of every tile's 70 to 110 detail boxes. Zero Day Works II has 588 Pad Tiles and 1,068 Bay
/// Blocks: 141,000 of its 200,000 drawn boxes, 1.8 M of its 4.5 M triangles.
///
/// Each kind's look is baked on the CPU (<see cref="Bake"/>), straight from its boxes as seen from above and from each side,
/// once per welded-seam case (a tile's kerb and skirt go where a neighbour is welded: MachineBlocks.cs): 16 top layers (which of
/// its four sides are welded) and 4 per side (which of the side's two ends are). The sheet shader (Renderer.Sheets.cs) works out
/// which tile of the rectangle a pixel is on and which of its sides are welded (inside the rectangle: all; on its edge: as the
/// tiles there are), and reads that layer. A tile in a rectangle is at full health, in one paint (each paint its own baked kind), without a finish, not frosted or
/// burning; any of those and it is drawn box by box as before (a change re-plans its chunk). The sheet's top is the tile's main
/// top surface (the height covering most of it), so what stands on a tile stands on the sheet.
/// </summary>
public static partial class ConstructRender
{
    /// <summary>Floor sheets on (the default); <c>--no-sheets</c> draws every tile box by box.</summary>
    public static bool SheetsOn = true;

    /// <summary>The baked texture's side in texels (a 2 m tile: about 8 mm a texel), and the layers a kind takes (16 tops, 4 x 4 sides).</summary>
    public const int SheetTexels = 256, SheetLayersPerKind = 32, SheetMaxKinds = 64;

    /// <summary>One baked tile kind: a type with the welded-face bits that aren't its four sides (up, down, a machine's cell, a merged square).</summary>
    public sealed class SheetKind
    {
        public ConstructPartDef Def = null!;
        public byte High;
        public uint Paint; // the paint tool's colour its tiles wear (0: the type's own)
        public int Index;
        public float Cell, Bottom, Top; // the tile's step (m), and its sheet's bottom and top in its own frame
    }

    private static readonly List<SheetKind> _sheetKinds = new();
    private static readonly Dictionary<(ConstructPartDef, byte, uint), int> _sheetKindOf = new();
    private static readonly HashSet<ConstructPartDef> _sheetRefused = new(ReferenceEqualityComparer.Instance as IEqualityComparer<ConstructPartDef>);

    /// <summary>Bumped when a kind is added: the renderer bakes and uploads the texture again.</summary>
    public static int SheetVersion { get; private set; }

    public static IReadOnlyList<SheetKind> SheetKinds => _sheetKinds;

    /// <summary>Tiles drawn as part of a sheet, and sheets, in the chunks built so far (the bench, the F9 window).</summary>
    public static long SheetTiles, SheetBoxes;

    /// <summary>Can type <paramref name="d"/> go in a sheet at all: marked, merged whole (no moving work boxes, no glass), a square cell.</summary>
    private static bool SheetType(ConstructPartDef d)
    {
        if (!d.Sheet || !SheetsOn)
            return false;
        if (StaticBoxes(d) <= 0 || _defWork.Contains(d) || _defPanes.Contains(d) || d.Logic == CLogic.Lamp)
        {
            if (_sheetRefused.Add(d))
                Console.WriteLine($"[render] REFUSED floor sheet for {d.Name}: it has boxes that move, glow or are glass (drawn box by box)");
            return false;
        }
        return d.BlockCell > 0.5f;
    }

    /// <summary>The kind for type <paramref name="d"/> with welded bits <paramref name="high"/> (its sides' bits taken off), made and baked on first use; -1: too many kinds.</summary>
    private static int SheetKindOf(ConstructPartDef d, byte high, uint paint)
    {
        if (_sheetKindOf.TryGetValue((d, high, paint), out var k))
            return k;
        if (_sheetKinds.Count >= SheetMaxKinds)
            return -1;
        var kind = new SheetKind { Def = d, High = high, Paint = paint, Index = _sheetKinds.Count, Cell = d.BlockCell };
        var lo = float.MaxValue;
        for (var b = 0; b < d.Boxes.Length; b++)
            if (InChunk(d, b))
                lo = MathF.Min(lo, d.Boxes[b].Center.Y - d.Boxes[b].Half.Y);
        kind.Bottom = lo == float.MaxValue ? 0 : lo;
        kind.Top = MainTop(d, (byte)(high | AllSides));
        _sheetKinds.Add(kind);
        _sheetKindOf[(d, high, paint)] = kind.Index;
        SheetVersion++;
        Console.WriteLine($"[render] floor sheet kind {kind.Index}: {d.Name} (welds 0x{high:X2}, paint {paint:X}), {kind.Cell:0.##} m cell, top {kind.Top:0.###} m");
        return kind.Index;
    }

    private const byte AllSides = 1 | 2 | 16 | 32; // +x, -x, +z, -z (1 << a socket's direction)

    /// <summary>A sheet combination's welded faces: bit 0 +x, 1 -x, 2 +z, 3 -z (the shader's order) as a part's BlockJoined bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte SidesOf(int combo) => (byte)(((combo & 1) != 0 ? 1 : 0) | ((combo & 2) != 0 ? 2 : 0) | ((combo & 4) != 0 ? 16 : 0) | ((combo & 8) != 0 ? 32 : 0));

    /// <summary>Is box <paramref name="b"/> drawn on a tile welded on <paramref name="joined"/>?</summary>
    private static bool SheetShows(ConstructPartDef d, int b, byte joined) => InChunk(d, b) && !(d.Boxes[b].BySeams && d.Boxes[b].SeamHidden(joined));

    /// <summary>The height that covers most of the tile seen from above (its deck, not a bolt or a kerb), in its own frame.</summary>
    private static float MainTop(ConstructPartDef d, byte joined)
    {
        const int N = 64;
        var h = new float[N * N];
        Array.Fill(h, float.MinValue);
        var half = d.BlockCell * 0.5f;
        for (var b = 0; b < d.Boxes.Length; b++)
        {
            if (!SheetShows(d, b, joined))
                continue;
            ref readonly var box = ref d.Boxes[b];
            var top = box.Center.Y + box.Half.Y;
            Span(box.Center.X - box.Half.X, box.Center.X + box.Half.X, half, N, out var x0, out var x1);
            Span(box.Center.Z - box.Half.Z, box.Center.Z + box.Half.Z, half, N, out var z0, out var z1);
            for (var z = z0; z < z1; z++)
                for (var x = x0; x < x1; x++)
                    h[z * N + x] = MathF.Max(h[z * N + x], top);
        }
        var votes = new Dictionary<int, int>();
        foreach (var v in h)
            if (v > float.MinValue)
            {
                var mm = (int)MathF.Round(v * 1000);
                votes[mm] = votes.GetValueOrDefault(mm) + 1;
            }
        return votes.Count == 0 ? 1f : votes.MaxBy(kv => kv.Value).Key / 1000f;
    }

    /// <summary>Texel columns (or rows) from <paramref name="a"/> to <paramref name="b"/> metres over -half..half in <paramref name="n"/> texels.</summary>
    private static void Span(float a, float b, float half, int n, out int i0, out int i1)
    {
        i0 = Math.Clamp((int)MathF.Ceiling((a + half) / (2 * half) * n - 0.5f), 0, n);
        i1 = Math.Clamp((int)MathF.Floor((b + half) / (2 * half) * n - 0.5f) + 1, 0, n);
    }

    /// <summary>
    /// A kind's 32 layers as RGBA8, <see cref="SheetTexels"/> square each: 16 tops (by the combination of welded sides, the
    /// shader's order), then for +x, -x, +z and -z four sides each (by which of the side's two ends are welded). A layer's
    /// rows run along +z on top and up the side on a side (row 0 first, as GL reads them).
    /// </summary>
    public static byte[] Bake(SheetKind k)
    {
        const int N = SheetTexels;
        var px = new byte[SheetLayersPerKind * N * N * 4];
        var d = k.Def;
        var depth = new float[N * N];
        var col = new Vector3[N * N];
        for (var layer = 0; layer < SheetLayersPerKind; layer++)
        {
            byte joined;
            int view; // 0 top, 1 +x, 2 -x, 3 +z, 4 -z
            if (layer < 16)
                (view, joined) = (0, (byte)(k.High | SidesOf(layer)));
            else
            {
                var side = (layer - 16) >> 2;
                var ends = (layer - 16) & 3; // bit 0: the + end along the side welded, bit 1: the - end
                view = side + 1;
                var along = side < 2 ? (byte)(((ends & 1) != 0 ? 16 : 0) | ((ends & 2) != 0 ? 32 : 0)) : (byte)(((ends & 1) != 0 ? 1 : 0) | ((ends & 2) != 0 ? 2 : 0));
                var behind = side switch { 0 => (byte)2, 1 => (byte)1, 2 => (byte)32, _ => (byte)16 }; // (the far side counts as welded: the tile behind it is the sheet's own)
                joined = (byte)(k.High | along | behind);
            }
            BakeView(k, view, joined, depth, col);
            var o = layer * N * N * 4;
            for (var t = 0; t < N * N; t++)
            {
                var c = col[t];
                px[o + t * 4] = (byte)Math.Clamp((int)(c.X * 255f + 0.5f), 0, 255);
                px[o + t * 4 + 1] = (byte)Math.Clamp((int)(c.Y * 255f + 0.5f), 0, 255);
                px[o + t * 4 + 2] = (byte)Math.Clamp((int)(c.Z * 255f + 0.5f), 0, 255);
                px[o + t * 4 + 3] = 255;
            }
        }
        return px;
    }

    /// <summary>One view of a tile: each texel the colour of the box nearest the viewer there, shaded by how far it sits back.</summary>
    private static void BakeView(SheetKind k, int view, byte joined, float[] depth, Vector3[] col)
    {
        const int N = SheetTexels;
        var d = k.Def;
        var half = k.Cell * 0.5f;
        var height = MathF.Max(0.01f, k.Top - k.Bottom);
        Array.Fill(depth, float.MinValue);
        Array.Fill(col, new Vector3(0.05f));
        // Axes: u across the layer, v down it (rows), w towards the viewer (bigger: nearer).
        for (var b = 0; b < d.Boxes.Length; b++)
        {
            if (!SheetShows(d, b, joined))
                continue;
            ref readonly var box = ref d.Boxes[b];
            var lo = box.Center - box.Half;
            var hi = box.Center + box.Half;
            float u0, u1, v0, v1, near;
            int uAxis, vAxis;
            if (view == 0)
                (u0, u1, v0, v1, near, uAxis, vAxis) = (lo.X, hi.X, lo.Z, hi.Z, hi.Y, 0, 2);
            else if (view <= 2)
                (u0, u1, v0, v1, near, uAxis, vAxis) = (lo.Z, hi.Z, lo.Y, hi.Y, view == 1 ? hi.X : -lo.X, 2, 1);
            else
                (u0, u1, v0, v1, near, uAxis, vAxis) = (lo.X, hi.X, lo.Y, hi.Y, view == 3 ? hi.Z : -lo.Z, 0, 1);
            if (view == 0 && lo.Y > k.Top + 0.25f)
                continue; // (well above the deck: a post or a frame, not the floor's face)
            int i0, i1, j0, j1;
            Span(u0, u1, half, N, out i0, out i1);
            if (view == 0)
                Span(v0, v1, half, N, out j0, out j1);
            else
            {
                // Up the side: from the sheet's bottom to its top (what stands above the deck isn't the floor's side).
                j0 = Math.Clamp((int)MathF.Ceiling((v0 - k.Bottom) / height * N - 0.5f), 0, N);
                j1 = Math.Clamp((int)MathF.Floor((v1 - k.Bottom) / height * N - 0.5f) + 1, 0, N);
            }
            if (i0 >= i1 || j0 >= j1)
                continue;
            // A cylinder seen down its axis is a disc (a bolt from above); any other way it is its box.
            var round = box.Shape != 0 && PartShapes.KindOf(box.Shape) is "cylinder" or "tube" or "capsule" && AxisOf(in box) == (view == 0 ? 1 : view <= 2 ? 0 : 2);
            var cu = (u0 + u1) * 0.5f;
            var cv = (v0 + v1) * 0.5f;
            var ru = MathF.Max(1e-4f, (u1 - u0) * 0.5f);
            var rv = MathF.Max(1e-4f, (v1 - v0) * 0.5f);
            var colour = box.Glow ? box.Color : BakeColour(in box, k.Paint);
            for (var j = j0; j < j1; j++)
            {
                var vm = view == 0 ? -half + (j + 0.5f) / N * 2 * half : k.Bottom + (j + 0.5f) / N * height;
                for (var i = i0; i < i1; i++)
                {
                    var t = j * N + i;
                    if (near <= depth[t])
                        continue;
                    if (round)
                    {
                        var um = -half + (i + 0.5f) / N * 2 * half;
                        var du = (um - cu) / ru;
                        var dv = (vm - cv) / rv;
                        if (du * du + dv * dv > 1f)
                            continue;
                    }
                    depth[t] = near;
                    col[t] = colour;
                }
            }
            _ = uAxis;
            _ = vAxis;
        }
        // Shade: what sits back from the face darker (a recess, the gap between plates), and a dark line where a nearer box's edge
        // throws its shadow, so the detail reads without its relief.
        // The tiles' gap: a tile's boxes stop a few millimetres short of its cell, and drawn alone that gap reads as a dark line
        // between tiles; a texel is wider than it, so the outermost ring stands for it.
        if (view == 0 && GapShort(d, half))
            for (var e = 0; e < N; e++)
            {
                col[e] *= 0.55f;
                col[(N - 1) * N + e] *= 0.55f;
                col[e * N] *= 0.55f;
                col[e * N + N - 1] *= 0.55f;
            }
        var face = view == 0 ? k.Top : half;
        for (var t = 0; t < N * N; t++)
        {
            if (depth[t] == float.MinValue)
                continue;
            var back = MathF.Max(0, face - depth[t]);
            var s = 0.78f + 0.22f * Math.Clamp(1f - back / 0.12f, 0f, 1f);
            var i = t % N;
            var j = t / N;
            if (i > 0 && depth[t - 1] > depth[t] + 0.004f || j > 0 && depth[t - N] > depth[t] + 0.004f)
                s *= 0.8f;
            col[t] *= s;
        }
    }

    /// <summary>
    /// A box's colour as the sheet draws it (lit plainly, as a box with no material is): its paint through its material's
    /// tint, a metal's diffuse share and a little of the sky it would show (Surfaces.Glsl, SurfaceLit), so bare steel reads as
    /// dark as it does drawn alone.
    /// </summary>
    private static Vector3 BakeColour(in CPartBox box, uint paint)
    {
        var baseCol = ConstructPaint.BoxColour(paint, in box);
        var row = Surfaces.OnPaint(box.Surface, paint, box.Paintable);
        if (row < Surfaces.FirstShaded || row >= Surfaces.File.Rows.Length)
            return baseCol;
        var r = Surfaces.Row(row);
        var albedo = Vector3.Lerp(r.Colour, baseCol, r.Tint);
        var sky = Vector3.Lerp(new Vector3(0.04f), albedo, r.Metal) * (0.35f * (1f - r.Rough * 0.5f));
        return albedo * (1f - r.Metal * 0.92f) + sky;
    }

    /// <summary>Do a tile's boxes stop short of its cell's edge (a gap between tiles)?</summary>
    private static bool GapShort(ConstructPartDef d, float half)
    {
        var reach = 0f;
        for (var b = 0; b < d.Boxes.Length; b++)
            if (InChunk(d, b) && !d.Boxes[b].BySeams)
                reach = MathF.Max(reach, MathF.Max(MathF.Abs(d.Boxes[b].Center.X) + d.Boxes[b].Half.X, MathF.Abs(d.Boxes[b].Center.Z) + d.Boxes[b].Half.Z));
        return reach < half - 0.0005f;
    }

    /// <summary>A shaped box's axis in the part's frame (0 x, 1 y, 2 z): its front's (a cylinder is made along Z).</summary>
    private static int AxisOf(in CPartBox box) => box.Front == 0 ? 2 : (box.Front - 1) / 2;

    // ---- Rectangles in a chunk (BuildChunk) ----

    private struct SheetTile
    {
        public int Member; // its place in the chunk's members
        public long Group;
        public int X, Z; // its cell in its group's grid
        public byte Joined;
        public bool Used;
    }

    private static SheetTile[] _sheetTile = [];
    private static readonly Dictionary<(long, int, int), int> _sheetAt = new();
    private static int[] _sheetOrder = [];
    private static long[] _sheetOrderKey = [];

    /// <summary>A sheet: one slot of the chunk (its matrix, bounds and look code) standing for <see cref="Tiles"/> tiles.</summary>
    private struct SheetRect
    {
        public int Member; // the first tile's place in the members (the slot's member)
        public Matrix4x4 Model;
        public Vector3 Lo, Hi;
        public float Code; // kind x 16 + the welded rectangle edges (bit 0 +x, 1 -x, 2 +z, 3 -z)
        public int Tiles;
    }

    private static SheetRect[] _sheetRects = [];
    private static bool[] _sheetMember = [];

    /// <summary>Can part <paramref name="i"/> go in a sheet as it is now (its look the type's own, upright, of a sheet type)?</summary>
    private static bool SheetReady(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        var d = p.D;
        if (!SheetType(d))
            return false;
        if (p.Finish != 0 || p.Broken || p.Burn > 0 || p.Health < d.Health || c.PartScale[i] > 1 || Frosted(c, i))
            return false;
        return Rot24.Apply(p.Rot, Vector3.UnitY).Y > 0.99f;
    }

    /// <summary>
    /// The chunk's tiles in rectangles (each grown greedily along +x, then +z, over tiles welded to each other), into
    /// <see cref="_sheetRects"/>; marks the members they take in <see cref="_sheetMember"/>. Returns the rectangles.
    /// </summary>
    private static int SheetRectangles(Construct c, MergeChunk ch)
    {
        Grow(ref _sheetMember, ch.MemberCount);
        Array.Clear(_sheetMember, 0, ch.MemberCount);
        if (!SheetsOn)
            return 0;
        var n = 0;
        for (var k = 0; k < ch.MemberCount; k++)
        {
            var i = ch.Members[k];
            if (i >= c.PartCount || !SheetReady(c, i))
                continue;
            ref var p = ref c.Parts[i];
            var joined = i < c.BlockJoined.Length ? c.BlockJoined[i] : (byte)0;
            var high = (byte)(joined & ~AllSides);
            var kind = SheetKindOf(p.D, high, p.Paint);
            if (kind < 0)
                continue;
            var cell = p.D.BlockCell;
            var ax = Rot24.Apply(p.Rot, Vector3.UnitX);
            var az = Rot24.Apply(p.Rot, Vector3.UnitZ);
            var fx = Vector3.Dot(p.Pos, ax) / cell;
            var fz = Vector3.Dot(p.Pos, az) / cell;
            var gx = (int)MathF.Floor(fx + 0.5f);
            var gz = (int)MathF.Floor(fz + 0.5f);
            // (a group: kind, turn, height and where its grid lies: tiles of one group line up on one lattice)
            var offX = (int)MathF.Round((fx - gx) * 200);
            var offZ = (int)MathF.Round((fz - gz) * 200);
            var group = (long)kind << 48 ^ (long)p.Rot << 40 ^ (long)(MathF.Round(p.Pos.Y * 200) + 100000) << 16 ^ (long)(offX + 128) << 8 ^ (offZ + 128);
            Grow(ref _sheetTile, n + 1);
            _sheetTile[n] = new SheetTile { Member = k, Group = group, X = gx, Z = gz, Joined = joined };
            n++;
        }
        if (n < 2)
            return 0;
        _sheetAt.Clear();
        Grow(ref _sheetOrder, n);
        Grow(ref _sheetOrderKey, n);
        for (var t = 0; t < n; t++)
        {
            ref var st = ref _sheetTile[t];
            _sheetAt[(st.Group, st.X, st.Z)] = t;
            _sheetOrderKey[t] = st.Group * 31 + ((long)st.Z << 20) + st.X;
            _sheetOrder[t] = t;
        }
        Array.Sort(_sheetOrderKey, _sheetOrder, 0, n);
        var rects = 0;
        for (var o = 0; o < n; o++)
        {
            ref var a = ref _sheetTile[_sheetOrder[o]];
            if (a.Used)
                continue;
            // Along +x: each next tile welded to the last on that side.
            var w = 1;
            while (At(a.Group, a.X + w, a.Z) is var t && t >= 0 && !_sheetTile[t].Used && Welded(At(a.Group, a.X + w - 1, a.Z), t, 1, 2))
                w++;
            // Along +z: a whole row at a time, welded to the row before and along itself.
            var h = 1;
            while (true)
            {
                var ok = true;
                for (var x = 0; x < w && ok; x++)
                {
                    var t = At(a.Group, a.X + x, a.Z + h);
                    ok = t >= 0 && !_sheetTile[t].Used && Welded(At(a.Group, a.X + x, a.Z + h - 1), t, 16, 32) && (x == 0 || Welded(At(a.Group, a.X + x - 1, a.Z + h), t, 1, 2));
                }
                if (!ok)
                    break;
                h++;
            }
            if (w * h < 2)
                continue; // (a lone tile: box by box, as before)
            byte edges = 0b1111; // (an edge counts as welded when every tile along it is)
            for (var x = 0; x < w; x++)
                for (var z = 0; z < h; z++)
                {
                    ref var st = ref _sheetTile[At(a.Group, a.X + x, a.Z + z)];
                    st.Used = true;
                    _sheetMember[st.Member] = true;
                    if (x == w - 1 && (st.Joined & 1) == 0)
                        edges &= 0b1110;
                    if (x == 0 && (st.Joined & 2) == 0)
                        edges &= 0b1101;
                    if (z == h - 1 && (st.Joined & 16) == 0)
                        edges &= 0b1011;
                    if (z == 0 && (st.Joined & 32) == 0)
                        edges &= 0b0111;
                }
            ref var p0 = ref c.Parts[ch.Members[a.Member]];
            var kind = (int)(a.Group >> 48);
            var sk = _sheetKinds[kind];
            var cell = sk.Cell;
            var ax = Rot24.Apply(p0.Rot, Vector3.UnitX);
            var az = Rot24.Apply(p0.Rot, Vector3.UnitZ);
            var size = new Vector3(w * cell, sk.Top - sk.Bottom, h * cell);
            var centre = p0.Pos + ax * ((w - 1) * 0.5f * cell) + az * ((h - 1) * 0.5f * cell) + Vector3.UnitY * ((sk.Bottom + sk.Top) * 0.5f);
            var halfBody = Rot24.ApplyHalf(p0.Rot, size * 0.5f);
            Grow(ref _sheetRects, rects + 1);
            _sheetRects[rects++] = new SheetRect
            {
                Member = a.Member,
                Model = Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(Rot24.Quat(p0.Rot)) * Matrix4x4.CreateTranslation(centre),
                Lo = centre - halfBody,
                Hi = centre + halfBody,
                Code = kind * 16 + edges,
                Tiles = w * h,
            };
        }
        return rects;
    }

    /// <summary>A tile of the group at a cell, or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int At(long group, int x, int z) => _sheetAt.TryGetValue((group, x, z), out var t) ? t : -1;

    /// <summary>Are tiles <paramref name="a"/> and <paramref name="b"/> (b one cell on from a) welded across the face between them?</summary>
    private static bool Welded(int a, int b, byte aBit, byte bBit) =>
        a >= 0 && b >= 0 && (_sheetTile[a].Joined & aBit) != 0 && (_sheetTile[b].Joined & bBit) != 0;
}
