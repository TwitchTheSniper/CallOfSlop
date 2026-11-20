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
        if (_game is not { SandboxOn: true } g || g.Local is not { } me)
            return;
        foreach (var c in g.Constructs.Bodies)
        {
            if (c.ModSockets.Length == 0 || Vector3.Distance(c.Pos, me.Eye) - c.Radius > CutFxRange)
                continue;
            for (var i = 0; i < c.ModSockets.Length; i++)
            {
                var s = c.ModSockets[i];
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
        ref var p = ref c.Parts[s.Plate];
        c.SocketAt(s.Plate, d.ModSocket, out var at, out _);
        var n = Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, Vector3.UnitY), c.Rot));
        var across = Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, Vector3.UnitX), c.Rot));
        var up = Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, Vector3.UnitZ), c.Rot));
        var top = c.ToWorld(at) + n * (s.Depth + 0.01f);
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
}
