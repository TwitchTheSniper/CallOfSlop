using System.Numerics;

namespace CallOfSlop;

/// <summary>The frame's particles and post effects (docs/render/MATERIALS.md): the Video settings go to the renderers, new blasts become particles, the pool steps and draws, the post effects go over the world.</summary>
public static unsafe partial class Program
{
    private static double _blastSeen;

    /// <summary>The Video settings, once a frame; a client and the host alike.</summary>
    private static void ApplyVideoLevels()
    {
        LightGrid.SurfaceLevel = (float)_settings.Materials;
        Fx.Level = _settings.Particles;
        Fx.On = !_smokeNoFx;
        (PostFx.Level, PostFx.BloomOn, PostFx.HazeOn, PostFx.AoOn) = (_settings.PostEffects, _settings.Bloom, _settings.HeatHaze, _settings.AmbientOcclusion);
        Gfx.MarkGlow = PostFx.Bloom;
    }

    private static bool _smokeNoFx;

    /// <summary>Blasts newer than the last look: the game adds them on every peer (Game.Blasts), so this sees each once and needs nothing from the wire.</summary>
    private static void EmitBlasts(double now)
    {
        var newest = _blastSeen;
        foreach (var b in _game.Blasts)
            if (b.Born > _blastSeen)
            {
                Fx.Blast(b.Pos, b.Radius, b.Tint);
                PostFx.AddHaze(b.Pos + new Vector3(0, b.Radius * 0.4f, 0), b.Radius * 1.6f, 0.8f);
                newest = Math.Max(newest, b.Born);
            }
        _blastSeen = newest;
    }

    private static void DrawParticlesAndPost(double now, int w, int h)
    {
        using (Perf.Pass(PerfArea.Particles, PerfGpuPass.Particles))
        {
            FxGpu.Step((float)FrameDt, (float)(now % 4096.0));
            var l = Lighting.Current;
            var ambient = (l.AmbSky + l.AmbGround) * 0.5f + l.SunColor * MathF.Max(l.SunDir.Y, 0f) * 0.55f;
            FxGpu.Draw(Gfx, ambient);
        }
        using (Perf.Pass(PerfArea.PostFx, PerfGpuPass.PostFx))
            PostFx.Apply(Gfx, w, h, now);
    }
}
