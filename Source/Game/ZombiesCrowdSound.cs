using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The crowd's roar on this PC (docs/zombies/MASS-HORDE.md, "Audio"; <see cref="CrowdRoar"/>): every frame on a crowd
/// map the crowd's cells (the surface's counts: the host's own, a client's picture) feed the roar director with the
/// listener at the local player's ear. Off crowd maps the director never runs; when the crowd is gone the voices fade.
/// </summary>
public sealed partial class Game
{
    public readonly CrowdRoar Roar = new();

    private bool _roarWas;

    /// <summary>Client and host, each frame (from <see cref="UpdateZombiesFrame"/>).</summary>
    private void CrowdRoarFrame(float dt)
    {
        var on = Mode == GameMode.Zombies && ZRules.Crowd.Roar && CrowdSurfaceBind();
        if (!on)
        {
            if (_roarWas)
                Roar.Clear(Audio);
            _roarWas = false;
            return;
        }
        _roarWas = true;
        Roar.Full = ZRules.Crowd.RoarFull;
        var ear = Local is { } me ? me.Eye : Audio.ListenerPosition;
        Roar.Frame(Audio, CrowdGround, ear, dt);
    }

    /// <summary>Self-test: one frame of the roar director with the ear at <paramref name="ear"/>.</summary>
    public void SelfTestCrowdRoarFrame(Vector3 ear, float dt)
    {
        if (!CrowdSurfaceBind())
            return;
        Roar.Frame(Audio, CrowdGround, ear, dt);
    }
}
