using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>Ragdolls on death and blast knockback (Source/Render/Ragdolls.cs, Source/Game/Knockback.cs): per frame.</summary>
public static unsafe partial class Program
{
    /// <summary>Every rendered frame, before the world is drawn: new bodies for the newly dead, the 60 Hz steps.</summary>
    private static void UpdateRagdolls(double now)
    {
        (Ragdolls.ZombieShirt, Ragdolls.ZombieSkin, Ragdolls.ZombiePants) = (ZShirt, ZSkin, ZPants);
        Gibs.Mode = _settings.Gore; // Settings > Video "Gore" (Source/Props/Gibs.cs)
        Ragdolls.Frame(_game, now, (float)FrameDt);
    }

    /// <summary>After the world: the bodies box by box when the instanced prop renderer couldn't draw them.</summary>
    private static void DrawRagdollsFallback()
    {
        if (!PropGfx.Instanced && Ragdolls.HasInstances)
            Ragdolls.DrawImmediate(Gfx);
    }
}
