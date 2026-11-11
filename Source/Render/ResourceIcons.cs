using System.Numerics;

namespace CallOfSlop;

/// <summary>The family a loose resource's icon is drawn as (Recycling.json's "shapes"; <see cref="ResourceIcons"/>).</summary>
public enum ResourceShape : byte
{
    Ore, // rough lumps
    Ingot, // a stack of ingots
    Plank, // a stack of planks
    Log, // a pile of logs
    Heap, // a heap of powder or grit
    Drum, // drums of a liquid
    Gas, // gas cylinders
    Block, // cut blocks
    Sheet, // a stack of sheets
    Roll, // rolls
    Board, // circuit boards
    Crate, // a crate of rounds
    Coil, // coils of wire
    Beam, // a stack of I-beams
    Crystal, // a cluster of crystals
}

/// <summary>
/// The icons of loose resources (docs/sandbox/constructs/production.md "The recipe picker"): each material is drawn as a small 3D
/// stack of its family's shape (<see cref="ResourceShape"/>, Recycling.json's "shapes") in its own colour, made of boxes and the
/// part shapes (cylinder, wedge, sphere...) like a part's tile, so it goes into the same atlas as the parts (<see cref="SandboxTiles"/>,
/// slots of <see cref="SandboxTiles.Kind.Resource"/>) and is lit and framed the same way. No image files: the boxes are made here.
/// </summary>
public static class ResourceIcons
{
    public static readonly string[] Names = ["ore", "ingot", "plank", "log", "heap", "drum", "gas", "block", "sheet", "roll", "board", "crate", "coil", "beam", "crystal"];

    public static ResourceShape? Parse(string name)
    {
        var k = Array.IndexOf(Names, name);
        return k < 0 ? null : (ResourceShape)k;
    }

    /// <summary>A material the file gives no shape: ore for a name ending in "ore", else a heap.</summary>
    public static ResourceShape Default(string material) => material.EndsWith("ore", StringComparison.Ordinal) ? ResourceShape.Ore : ResourceShape.Heap;

    private static ushort _cyl, _tube, _sphere, _bevel, _ingot, _cone, _point;

    /// <summary>The part shapes the icons use (made once, shared: <see cref="PartShapes.For"/>).</summary>
    private static void EnsureShapes()
    {
        if (_cyl != 0)
            return;
        _tube = PartShapes.For(new PartBox { Shape = "tube", Size = Vector3.One, Segments = 20, Inner = 0.55f });
        _sphere = PartShapes.For(new PartBox { Shape = "sphere", Size = Vector3.One, Segments = 12 });
        _bevel = PartShapes.For(new PartBox { Shape = "bevel", Size = Vector3.One, Radius = 0.3f });
        _ingot = PartShapes.For(new PartBox { Shape = "wedge", Size = Vector3.One, Taper = new Vector2(0.78f, 0.7f) });
        _cone = PartShapes.For(new PartBox { Shape = "cylinder", Size = Vector3.One, Segments = 20, Taper = new Vector2(0.14f, 0.14f) });
        _point = PartShapes.For(new PartBox { Shape = "wedge", Size = Vector3.One, Taper = new Vector2(0.08f, 0.08f) });
        _cyl = PartShapes.For(new PartBox { Shape = "cylinder", Size = Vector3.One, Segments = 20 });
    }

    // A shape's own front (-Z) turned up (+Y), and its Z turned along X (a log lying).
    private static readonly Quaternion Up = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
    private static readonly Quaternion AlongX = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);

    private static void Box(List<TileBox> into, Vector3 size, Quaternion turn, Vector3 at, Vector3 colour, ushort shape = 0) =>
        into.Add(new TileBox(Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(at), colour, shape));

    private static Vector3 Shade(Vector3 c, float k) => Vector3.Clamp(c * k, Vector3.Zero, Vector3.One);

    /// <summary>
    /// A colour that reads on the tile's dark card: a dark material (rubber) is lifted to a dim grey of its hue, so its shape still shows.
    /// </summary>
    public static Vector3 Readable(Vector3 c)
    {
        var lum = 0.3f * c.X + 0.59f * c.Y + 0.11f * c.Z;
        const float Floor = 0.3f;
        return lum >= Floor ? c : lum <= 0.001f ? new Vector3(Floor) : Vector3.Clamp(c * (Floor / lum), Vector3.Zero, Vector3.One);
    }

    /// <summary>A material's icon boxes (its family, in its colour; <paramref name="seed"/> varies the lumps).</summary>
    public static void Boxes(ResourceShape shape, Vector3 colour, int seed, List<TileBox> into)
    {
        EnsureShapes();
        var c = Readable(colour);
        var rng = new Random(seed * 7919 + 13);
        float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
        switch (shape)
        {
            case ResourceShape.Ore:
            {
                // Rough lumps: a ring of five, two on them, each turned its own way; a few glints of the metal's colour.
                for (var k = 0; k < 7; k++)
                {
                    var top = k >= 5;
                    var a = top ? k * 2.4f : k * MathF.Tau / 5 + R(-0.2f, 0.2f);
                    var r = top ? 0.12f : 0.3f;
                    var s = top ? R(0.26f, 0.32f) : R(0.24f, 0.34f);
                    var turn = Quaternion.CreateFromYawPitchRoll(R(0, MathF.Tau), R(-0.6f, 0.6f), R(-0.6f, 0.6f));
                    var at = new Vector3(MathF.Cos(a) * r, top ? 0.26f : 0.08f, MathF.Sin(a) * r);
                    Box(into, new Vector3(s, s * R(0.7f, 0.9f), s * R(0.8f, 1f)), turn, at, Shade(c, R(0.72f, 1.05f)), _bevel);
                    if (k % 3 == 1)
                        Box(into, new Vector3(0.07f, 0.05f, 0.06f), turn, at + Vector3.Transform(new Vector3(0, s * 0.38f, 0), turn), Shade(c, 1.45f));
                }
                break;
            }
            case ResourceShape.Ingot:
            {
                // Three ingots side by side, two across them.
                for (var k = 0; k < 3; k++)
                    Box(into, new Vector3(0.5f, 0.17f, 0.1f), Up, new Vector3(0, 0.05f, (k - 1) * 0.19f), Shade(c, k == 1 ? 1.08f : 0.95f), _ingot);
                var turned = Quaternion.Concatenate(Up, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
                for (var k = 0; k < 2; k++)
                    Box(into, new Vector3(0.5f, 0.17f, 0.1f), turned, new Vector3((k - 0.5f) * 0.2f, 0.152f, 0), Shade(c, k == 0 ? 1.12f : 1f), _ingot);
                break;
            }
            case ResourceShape.Plank:
            {
                // Two layers of three planks, the top one a little along and turned.
                for (var layer = 0; layer < 2; layer++)
                {
                    var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, layer * 0.08f);
                    for (var k = 0; k < 3; k++)
                        Box(into, new Vector3(1f, 0.05f, 0.16f), turn, new Vector3(layer * 0.08f, layer * 0.052f, (k - 1) * 0.17f), Shade(c, (k + layer) % 2 == 0 ? 1.06f : 0.88f));
                }
                break;
            }
            case ResourceShape.Log:
            {
                // Three logs with two on them, their cut ends paler.
                const float D = 0.24f, L = 0.9f;
                var end = Shade(c, 1.4f) + new Vector3(0.06f);
                for (var k = 0; k < 5; k++)
                {
                    var top = k >= 3;
                    var z = top ? (k - 3.5f) * D * 1.02f : (k - 1) * D * 1.02f;
                    var y = top ? D * 0.86f : 0;
                    var dx = R(-0.04f, 0.04f);
                    Box(into, new Vector3(D, D, L), AlongX, new Vector3(dx, y, z), Shade(c, R(0.85f, 1.05f)), _cyl);
                    Box(into, new Vector3(D * 0.86f, D * 0.86f, 0.012f), AlongX, new Vector3(dx + L * 0.5f + 0.004f, y, z), end, _cyl);
                    Box(into, new Vector3(D * 0.86f, D * 0.86f, 0.012f), AlongX, new Vector3(dx - L * 0.5f - 0.004f, y, z), end, _cyl);
                }
                break;
            }
            case ResourceShape.Heap:
            {
                // A cone with a few lumps round its foot.
                Box(into, new Vector3(0.9f, 0.9f, 0.42f), Up, new Vector3(0, 0.21f, 0), c, _cone);
                for (var k = 0; k < 5; k++)
                {
                    var a = k * MathF.Tau / 5 + 0.4f;
                    var s = R(0.14f, 0.2f);
                    Box(into, new Vector3(s, s * 0.6f, s), Quaternion.Identity, new Vector3(MathF.Cos(a) * 0.46f, s * 0.15f, MathF.Sin(a) * 0.46f), Shade(c, R(0.8f, 0.95f)), _sphere);
                }
                break;
            }
            case ResourceShape.Drum:
            {
                // Two drums, each with two rolled hoops and a bung.
                for (var k = 0; k < 2; k++)
                {
                    var at = new Vector3(k * 0.62f - 0.31f, 0.42f, k * 0.25f);
                    Box(into, new Vector3(0.56f, 0.56f, 0.84f), Up, at, Shade(c, k == 0 ? 1f : 0.9f), _cyl);
                    for (var h = -1; h <= 1; h += 2)
                        Box(into, new Vector3(0.585f, 0.585f, 0.035f), Up, at + new Vector3(0, h * 0.15f, 0), Shade(c, 0.7f), _cyl);
                    Box(into, new Vector3(0.08f, 0.08f, 0.03f), Up, at + new Vector3(0.14f, 0.435f, 0), new Vector3(0.25f), _cyl);
                }
                break;
            }
            case ResourceShape.Gas:
            {
                // Two tall cylinders with domed tops, a band and a valve.
                for (var k = 0; k < 2; k++)
                {
                    var at = new Vector3(k * 0.32f - 0.16f, 0.45f, k * 0.12f);
                    Box(into, new Vector3(0.28f, 0.28f, 0.9f), Up, at, Shade(c, k == 0 ? 1f : 0.9f), _cyl);
                    Box(into, new Vector3(0.28f, 0.16f, 0.28f), Quaternion.Identity, at + new Vector3(0, 0.45f, 0), Shade(c, k == 0 ? 1f : 0.9f), _sphere);
                    Box(into, new Vector3(0.29f, 0.29f, 0.05f), Up, at + new Vector3(0, 0.3f, 0), Shade(c, 0.62f), _cyl);
                    Box(into, new Vector3(0.07f, 0.07f, 0.12f), Up, at + new Vector3(0, 0.56f, 0), new Vector3(0.3f), _cyl);
                    Box(into, new Vector3(0.1f, 0.03f, 0.03f), Quaternion.Identity, at + new Vector3(0.03f, 0.6f, 0), new Vector3(0.75f, 0.2f, 0.15f));
                }
                break;
            }
            case ResourceShape.Block:
            {
                // Four cut blocks and one on them.
                for (var k = 0; k < 4; k++)
                    Box(into, new Vector3(0.4f, 0.24f, 0.26f), Quaternion.Identity, new Vector3((k % 2 - 0.5f) * 0.41f, 0.12f, (k / 2 - 0.5f) * 0.27f), Shade(c, R(0.82f, 1.05f)), _bevel);
                Box(into, new Vector3(0.4f, 0.24f, 0.26f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.25f), new Vector3(0.05f, 0.362f, 0.02f), Shade(c, 1.08f), _bevel);
                break;
            }
            case ResourceShape.Sheet:
            {
                // Five sheets stacked a little out of line.
                for (var k = 0; k < 5; k++)
                    Box(into, new Vector3(0.8f, 0.03f, 0.56f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, R(-0.06f, 0.06f)), new Vector3(R(-0.03f, 0.03f), k * 0.034f, R(-0.03f, 0.03f)), Shade(c, k % 2 == 0 ? 1.05f : 0.85f));
                break;
            }
            case ResourceShape.Roll:
            {
                // Two rolls side by side, one on them, their cores showing.
                for (var k = 0; k < 3; k++)
                {
                    var top = k == 2;
                    var at = new Vector3(top ? 0.03f : 0, top ? 0.3f : 0, top ? 0 : (k - 0.5f) * 0.36f);
                    Box(into, new Vector3(0.34f, 0.34f, 0.8f), AlongX, at, Shade(c, top ? 1.08f : 0.9f), _tube);
                    Box(into, new Vector3(0.19f, 0.19f, 0.84f), AlongX, at, new Vector3(0.55f, 0.45f, 0.32f), _cyl);
                }
                break;
            }
            case ResourceShape.Board:
            {
                // Two boards, chips on the top one and a gold edge.
                for (var k = 0; k < 2; k++)
                {
                    var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, k * 0.2f);
                    var y = k * 0.034f;
                    Box(into, new Vector3(0.7f, 0.03f, 0.48f), turn, new Vector3(0, y, 0), Shade(c, k == 0 ? 0.85f : 1.05f));
                    if (k == 0)
                        continue;
                    for (var j = 0; j < 4; j++)
                        Box(into, new Vector3(j == 0 ? 0.2f : 0.11f, 0.03f, j == 0 ? 0.16f : 0.09f), turn,
                            new Vector3(j == 0 ? -0.12f : 0.18f, y + 0.03f, j == 0 ? 0 : (j - 2) * 0.13f), new Vector3(0.12f));
                    Box(into, new Vector3(0.04f, 0.032f, 0.4f), turn, Vector3.Transform(new Vector3(0.33f, 0, 0), turn) + new Vector3(0, y + 0.002f, 0), new Vector3(0.9f, 0.72f, 0.25f));
                }
                break;
            }
            case ResourceShape.Crate:
            {
                // A crate with a darker lid band and handles, three rounds on it.
                Box(into, new Vector3(0.7f, 0.38f, 0.42f), Quaternion.Identity, new Vector3(0, 0.19f, 0), c);
                Box(into, new Vector3(0.71f, 0.06f, 0.43f), Quaternion.Identity, new Vector3(0, 0.34f, 0), Shade(c, 0.65f));
                for (var h = -1; h <= 1; h += 2)
                    Box(into, new Vector3(0.03f, 0.05f, 0.16f), Quaternion.Identity, new Vector3(h * 0.365f, 0.24f, 0), Shade(c, 0.5f));
                for (var k = 0; k < 3; k++)
                {
                    var at = new Vector3((k - 1) * 0.12f, 0.452f, 0.05f);
                    Box(into, new Vector3(0.07f, 0.07f, 0.16f), Up, at, new Vector3(0.85f, 0.66f, 0.3f), _cyl);
                    Box(into, new Vector3(0.06f, 0.06f, 0.07f), Up, at + new Vector3(0, 0.115f, 0), new Vector3(0.7f, 0.45f, 0.3f), _cone);
                }
                break;
            }
            case ResourceShape.Coil:
            {
                // Two coils of wire lying flat, the top one a little aside.
                for (var k = 0; k < 2; k++)
                    Box(into, new Vector3(0.7f, 0.7f, 0.22f), Up, new Vector3(k * 0.12f, 0.11f + k * 0.224f, k * 0.06f), Shade(c, k == 0 ? 0.88f : 1.08f), _tube);
                break;
            }
            case ResourceShape.Beam:
            {
                // I-beams: three side by side, two on them.
                for (var k = 0; k < 5; k++)
                {
                    var top = k >= 3;
                    var at = new Vector3(top ? 0.06f : 0, top ? 0.182f : 0.09f, top ? (k - 3.5f) * 0.16f : (k - 1) * 0.16f);
                    var col = Shade(c, R(0.88f, 1.06f));
                    Box(into, new Vector3(0.9f, 0.024f, 0.14f), Quaternion.Identity, at + new Vector3(0, 0.077f, 0), col);
                    Box(into, new Vector3(0.9f, 0.024f, 0.14f), Quaternion.Identity, at - new Vector3(0, 0.077f, 0), col);
                    Box(into, new Vector3(0.9f, 0.13f, 0.026f), Quaternion.Identity, at, Shade(col, 0.85f));
                }
                break;
            }
            case ResourceShape.Crystal:
            {
                // A cluster of points on a lump of rock.
                Box(into, new Vector3(0.6f, 0.2f, 0.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f), new Vector3(0, 0.08f, 0), new Vector3(0.35f, 0.33f, 0.38f), _bevel);
                for (var k = 0; k < 6; k++)
                {
                    var a = k * MathF.Tau / 6 + R(-0.3f, 0.3f);
                    var lean = k == 0 ? 0 : R(0.25f, 0.55f);
                    var h = k == 0 ? 0.6f : R(0.3f, 0.45f);
                    var w = k == 0 ? 0.17f : R(0.1f, 0.14f);
                    var turn = Quaternion.CreateFromAxisAngle(new Vector3(MathF.Sin(a), 0, -MathF.Cos(a)), lean); // (leaning out from the middle)
                    var foot = new Vector3(MathF.Cos(a) * (k == 0 ? 0 : 0.16f), 0.16f, MathF.Sin(a) * (k == 0 ? 0 : 0.14f));
                    Box(into, new Vector3(w, w, h), Quaternion.Concatenate(Up, turn), foot + Vector3.Transform(new Vector3(0, h * 0.5f, 0), turn), Shade(c, R(0.9f, 1.2f)), _point);
                }
                break;
            }
        }
    }
}
