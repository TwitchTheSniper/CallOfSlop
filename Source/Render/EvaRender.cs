using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Manned Manoeuvring Unit on a suited player's back (docs/sandbox/EVA.md): the MMU part's own boxes, latched to the space
/// suit's attach point, and its thrusters' puffs while it fires. The immediate path (PlayerRender) and the scene graph
/// (PlayerRig's <see cref="PackNode"/>) emit the same boxes. Nothing allocates.
/// </summary>
public static class EvaRender
{
    /// <summary>The suit's attach point in the standing torso's frame (the space suit uniform's plate, Cosmetics/uniforms.json).</summary>
    private static readonly Vector3 Attach = new(0, -0.02f, 0.455f);

    private static ConstructPartDef? _mmu;
    private static bool _looked;

    /// <summary>The MMU part, or null when the part files have none.</summary>
    public static ConstructPartDef? MmuDef
    {
        get
        {
            if (!_looked)
            {
                _mmu = ConstructParts.Find("Manned Manoeuvring Unit");
                _looked = true;
            }
            return _mmu;
        }
    }

    /// <summary>Does this player wear an MMU now: on their back, alive, not seated (seated in its dock, the dock's own part is drawn).</summary>
    public static bool Wears(Player p) => (p.Eva & Eva.Mmu) != 0 && p.Alive && p.RenderSeat == SeatKind.None && MmuDef != null;

    /// <summary>Is a worn MMU flown now (vacuum, off a ladder): the wearer strapped in, knees bent, the boots on its pedestals (LegAnimator.PackLegs)? In air they walk, and the pedestals aren't drawn.</summary>
    public static bool Strapped(Player p) => Wears(p) && !p.RenderLadder && Eva.Vacuum(p.RenderPosition);

    /// <summary>The pack's frame (its latch at the suit's attach point, +z away from the back) in the upper body's frame, for the player's stance.</summary>
    public static Matrix4x4 OnBack(Player p)
    {
        var s = BodyModel.Stances[BodyModel.StanceIndex(p.RenderStance)];
        return Matrix4x4.CreateTranslation(Attach) * Matrix4x4.CreateScale(s.TorsoScale) * s.TorsoFrame;
    }

    /// <summary>The pack, and while it fires (<paramref name="burning"/>) a puff off each corner quad; <paramref name="m"/> is the pack frame -> the drawing space.</summary>
    public static void Emit<T>(ref T o, in Matrix4x4 m, bool burning, double time, bool strapped) where T : struct, IToolBoxOut
    {
        if (MmuDef is not { } d)
            return;
        foreach (ref readonly var b in d.Boxes.AsSpan())
            if (!b.Hidden)
                o.Box(b.Model(0, b.Center) * m, b.Color, 0, b.Shape, b.Glass);
        if (strapped)
            foreach (var (c, size, colour) in Pedestals)
                o.Box(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(c) * m, colour, 0);
        if (!burning)
            return;
        // Cold gas: short pale puffs off the quads, flickering one after another.
        for (var k = 0; k < 4; k++)
        {
            var on = 0.5f + 0.5f * MathF.Sin((float)(time * 31 + k * 1.7));
            if (on < 0.35f)
                continue;
            var x = (k & 1) == 0 ? -0.5f : 0.5f;
            var y = (k & 2) == 0 ? -0.5f : 0.46f;
            var size = 0.06f + 0.1f * on;
            var at = new Vector3(x + MathF.Sign(x) * size * 0.6f, y, 0.17f);
            o.Box(Matrix4x4.CreateScale(size, size * 0.7f, size * 0.7f) * Matrix4x4.CreateTranslation(at) * m, Puff, 0);
        }
    }

    private static readonly Vector3 Puff = new(0.86f, 0.88f, 0.9f);

    /// <summary>
    /// Where the wearer's soles are in the pack frame: the torso's middle stands 1.15 m over the feet (the latch 2 cm under
    /// it, 0.455 m behind the ankles), and strapped in the knees bend, the ankles at <see cref="LegAnimator.PackAnkle"/>
    /// (Karl, 2026-11-18: a barber's chair, more upright), so the soles sit that much higher and further forward.
    /// </summary>
    private static readonly float SoleY = -1.13f + LegAnimator.PackAnkle.Y - LegAnimator.AnkleHeight, FeetZ = -0.455f + LegAnimator.PackAnkle.Z; // (before Pedestals: initialised in order)

    /// <summary>
    /// The foot pedestals (Karl, 2026-11-18: a worn MMU is a vehicle, the feet rest on it, no walking): from the frame's foot
    /// a strut down each side behind the legs, a bar across under the heels, a plate under each boot with a toe loop. Drawn
    /// only when worn (a docked pack's would stand through its dock's floor); each piece a few mm off the next one's faces.
    /// </summary>
    private static readonly (Vector3 Centre, Vector3 Size, Vector3 Colour)[] Pedestals = MakePedestals();

    private static (Vector3, Vector3, Vector3)[] MakePedestals()
    {
        var white = new Vector3(0.894f, 0.894f, 0.863f);
        var grey = new Vector3(0.655f, 0.671f, 0.671f);
        var dark = new Vector3(0.165f, 0.173f, 0.184f);
        var hazard = new Vector3(0.847f, 0.651f, 0.157f);
        static (Vector3, Vector3, Vector3) B(Vector3 a, Vector3 b, Vector3 c) => ((a + b) / 2, b - a, c);
        var list = new List<(Vector3, Vector3, Vector3)>();
        float plateTop = SoleY - 0.003f, plateBot = SoleY - 0.035f;
        float heelZ = FeetZ + 0.13f, toeZ = FeetZ - 0.17f;
        foreach (var s in new[] { -1f, 1f })
        {
            float xi = 0.075f, xo = 0.265f;
            Vector3 L(float x0, float y0, float z0, float x1, float y1, float z1) => s > 0 ? new(x0, y0, z0) : new(-x1, y0, z0);
            Vector3 H(float x0, float y0, float z0, float x1, float y1, float z1) => s > 0 ? new(x1, y1, z1) : new(-x0, y1, z1);
            // The strut: down from under the frame's corner (its foot at y -0.55) behind the calf.
            list.Add(B(L(0.235f, plateBot - 0.004f, 0.045f, 0.285f, -0.556f, 0.1f), H(0.235f, plateBot - 0.004f, 0.045f, 0.285f, -0.556f, 0.1f), white));
            // The arm forward from the strut's foot to the plate, under it.
            list.Add(B(L(0.24f, plateBot - 0.03f, heelZ, 0.28f, plateBot - 0.002f, 0.04f), H(0.24f, plateBot - 0.03f, heelZ, 0.28f, plateBot - 0.002f, 0.04f), grey));
            // The plate under the boot, its tread, the toe loop and the heel cup.
            list.Add(B(L(xi, plateBot, toeZ, xo, plateTop, heelZ), H(xi, plateBot, toeZ, xo, plateTop, heelZ), grey));
            list.Add(B(L(xi + 0.01f, plateTop, toeZ + 0.03f, xo - 0.01f, plateTop + 0.004f, heelZ - 0.03f), H(xi + 0.01f, plateTop, toeZ + 0.03f, xo - 0.01f, plateTop + 0.004f, heelZ - 0.03f), dark));
            list.Add(B(L(xi - 0.004f, plateTop, toeZ + 0.02f, xo + 0.004f, SoleY + 0.07f, toeZ + 0.06f), H(xi - 0.004f, plateTop, toeZ + 0.02f, xo + 0.004f, SoleY + 0.07f, toeZ + 0.06f), hazard));
            list.Add(B(L(xi + 0.005f, plateTop, heelZ - 0.025f, xo - 0.005f, SoleY + 0.05f, heelZ + 0.003f), H(xi + 0.005f, plateTop, heelZ - 0.025f, xo - 0.005f, SoleY + 0.05f, heelZ + 0.003f), dark));
        }
        // The bar across between the struts' feet, behind the heels.
        list.Add(B(new(-0.233f, plateBot - 0.028f, 0.05f), new(0.233f, plateBot + 0.01f, 0.095f), white));
        return [.. list];
    }
}
