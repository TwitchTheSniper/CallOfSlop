using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Module sockets on the client (docs/sandbox/design/module-sockets.md 2.4 "Seen"): a socket's cutting ring at work. While a plate's
/// hidden <c>ModCut</c> setting says it burns (ModuleCut.cs), its head runs round the well's outline at the surface from where this
/// peer first saw it, throwing sparks, and the outline it has passed glows orange. Particles only (FrenzyParticles), on bodies within
/// <see cref="CutFxRange"/>; a body with no sockets costs one length test a frame. Allocates nothing.
/// </summary>
public static unsafe partial class Program
{
    private const float CutFxRange = 60f;
    private static readonly Random _cutFxDice = new(17);

    /// <summary>Once a frame.</summary>
    private static void UpdateModuleCutFx()
    {
        ConstructRender.SocketHeld = null;
        if (_game is not { SandboxOn: true } g || g.Local is not { } me)
            return;
        ref var hand = ref g.SandboxHands.Hand(me.Id);
        ConstructRender.SocketHeld = hand.Tool == ConstructTool.PhysicsGun && hand.GrabBody != 0 && hand.GrabSeg == 0 ? g.Constructs.Get(hand.GrabBody) : null; // (its room and fit shown at a socket near: ConstructRenderSockets.cs)
        foreach (var c in g.Constructs.Bodies)
        {
            if (c.ModSockets.Length == 0 || Vector3.Distance(c.Pos, me.Eye) - c.Radius > CutFxRange)
                continue;
            for (var i = 0; i < c.ModSockets.Length; i++)
            {
                var s = c.ModSockets[i];
                if (s.Plate < c.PartCount && ConstructConfig.Get(c.Parts[s.Plate], c.DefOf(s.Plate), CfgKey.ModWeld, 0) is var weld and not 0)
                {
                    WeldFx(c, s, (float)Math.Clamp(ModuleFitters.Seen(c, s.Plate) / MathF.Abs(weld), 0, 1), weld > 0);
                    continue;
                }
                if (s.Stand || s.Plate >= c.PartCount || !ModuleCut.Wants(s))
                    continue; // (a ring burns only while its well has hull in it: a stale setting draws nothing)
                var total = ConstructConfig.Get(c.Parts[s.Plate], c.DefOf(s.Plate), CfgKey.ModCut, 0);
                if (total <= 0)
                    continue;
                var f = (float)Math.Clamp(ModuleCut.Seen(c, s.Plate) / total, 0, 1);
                CutFx(c, s, f);
            }
        }
    }

    /// <summary>A point <paramref name="f"/> (0..1) of the way round a well's outline at the surface, in the world.</summary>
    private static Vector3 OutlineAt(Vector3 top, Vector3 across, Vector3 up, float hw, float hl, float f)
    {
        var per = 4 * (hw + hl);
        var d = (f - MathF.Floor(f)) * per;
        if (d < 2 * hw)
            return top + across * (-hw + d) - up * hl;
        d -= 2 * hw;
        if (d < 2 * hl)
            return top + across * hw + up * (-hl + d);
        d -= 2 * hl;
        if (d < 2 * hw)
            return top + across * (hw - d) + up * hl;
        d -= 2 * hw;
        return top - across * hw + up * (hl - d);
    }

    private static void CutFx(Construct c, in CModSocket s, float f)
    {
        var d = c.DefOf(s.Plate);
        ModuleSockets.PlateRest(c, s.Plate, out var pr, out var at, out _); // (the body's rest frame: a plate welded on freeform is in its weld's)
        var n = Vector3.Normalize(Vector3.Transform(Rot24.Apply(pr, Vector3.UnitY), c.Rot));
        var across = Vector3.Normalize(Vector3.Transform(Rot24.Apply(pr, Vector3.UnitX), c.Rot));
        var up = Vector3.Normalize(Vector3.Transform(Rot24.Apply(pr, Vector3.UnitZ), c.Rot));
        var top = c.ToWorld(at) + n * 0.01f; // (every dug style: the ring runs round its frame's face)
        float hw = s.W * 0.5f + ModuleCut.Margin * 0.5f, hl = s.L * 0.5f + ModuleCut.Margin * 0.5f;
        // The ring has heads at each corner: each runs a quarter of the outline.
        for (var h = 0; h < 4; h++)
        {
            var head = OutlineAt(top, across, up, hw, hl, (h + f) * 0.25f);
            for (var k = 0; k < 2; k++)
            {
                var v = n * (1.5f + (float)_cutFxDice.NextDouble() * 2f) + across * ((float)_cutFxDice.NextDouble() - 0.5f) * 3f + up * ((float)_cutFxDice.NextDouble() - 0.5f) * 3f;
                FrenzyParticles.Spark(head, v, new Vector3(1f, 0.85f, 0.5f), 0.012f, 0.35f, 9f);
            }
            FrenzyParticles.Spark(head, Vector3.Zero, new Vector3(1f, 1f, 0.9f), 0.05f, 0.08f, 0f, 0f, 0f);
            // The kerf behind it: glowing orange where it has passed (cooling as it goes).
            var back = (float)_cutFxDice.NextDouble() * f;
            FrenzyParticles.Spark(OutlineAt(top, across, up, hw, hl, (h + back) * 0.25f), Vector3.Zero, new Vector3(1f, 0.45f, 0.1f) * (0.5f + 0.5f * back / MathF.Max(f, 0.01f)), 0.03f, 0.3f, 0f, 0f, 0f);
        }
    }

    /// <summary>
    /// A Module Fitter's torch (module-sockets.md 7.4): one head round the plug's outline at the plate's face, <paramref name="f"/> of the
    /// way. Welding: a white point, sparks, and the bead behind it cooling from white to orange to dark red. Cutting the seam: a white
    /// kerf and a heavier spray outward.
    /// </summary>
    private static void WeldFx(Construct c, in CModSocket s, float f, bool weld)
    {
        var d = c.DefOf(s.Plate);
        ModuleSockets.PlateRest(c, s.Plate, out var pr, out var at, out _); // (the body's rest frame: a plate welded on freeform is in its weld's)
        var n = Vector3.Normalize(Vector3.Transform(Rot24.Apply(pr, Vector3.UnitY), c.Rot));
        var across = Vector3.Normalize(Vector3.Transform(Rot24.Apply(pr, Vector3.UnitX), c.Rot));
        var up = Vector3.Normalize(Vector3.Transform(Rot24.Apply(pr, Vector3.UnitZ), c.Rot));
        var face = c.ToWorld(at) + n * 0.02f;
        float hw = s.W * 0.5f, hl = s.L * 0.5f;
        var head = OutlineAt(face, across, up, hw, hl, f);
        for (var k = 0; k < (weld ? 2 : 4); k++)
        {
            var v = n * (1f + (float)_cutFxDice.NextDouble() * 2f) + across * ((float)_cutFxDice.NextDouble() - 0.5f) * 3f + up * ((float)_cutFxDice.NextDouble() - 0.5f) * 3f;
            FrenzyParticles.Spark(head, v, new Vector3(1f, 0.85f, 0.55f), 0.012f, 0.35f, 9f);
        }
        FrenzyParticles.Spark(head, Vector3.Zero, weld ? new Vector3(0.85f, 0.9f, 1f) : new Vector3(1f, 1f, 0.9f), 0.06f, 0.08f, 0f, 0f, 0f);
        // The bead (or the kerf) behind the head: hot near it, cooling with distance (a welded bead ends dark: 10 s of cooling).
        for (var k = 0; k < 3; k++)
        {
            var back = (float)_cutFxDice.NextDouble() * f;
            var heat = f > 0.001f ? back / f : 1f;
            var colour = weld ? Vector3.Lerp(new Vector3(0.35f, 0.06f, 0.02f), Vector3.Lerp(new Vector3(1f, 0.5f, 0.12f), new Vector3(1f, 0.95f, 0.8f), heat * heat), heat) : new Vector3(1f, 0.55f, 0.15f) * (0.4f + 0.6f * heat);
            FrenzyParticles.Spark(OutlineAt(face, across, up, hw, hl, back), Vector3.Zero, colour, 0.025f, 0.3f, 0f, 0f, 0f);
        }
    }

    /// <summary>Which of a plate's frame controls the crosshair is on (1 eject, 2 lock; 0 none), and the point in the body's frame.</summary>
    private static byte ModControlAt(in ConstructHit hit, out Vector3 bodyLocal)
    {
        bodyLocal = default;
        if (hit.Body == null || (uint)hit.Part >= (uint)hit.Body.PartCount || hit.SlotPlus1 != 0 || hit.RoutePlus1 != 0)
            return 0;
        var d = hit.Body.DefOf(hit.Part);
        if (d.ModPart != ModPart.Socket || d.ModStand)
            return 0;
        Freeform.PartWorld(hit.Body, hit.Part, out var pos, out var rot);
        bodyLocal = hit.Body.ToLocal(hit.Point);
        return ModuleSockets.ControlNear(d, Vector3.Transform(hit.Point - pos, Quaternion.Conjugate(rot)));
    }
}
