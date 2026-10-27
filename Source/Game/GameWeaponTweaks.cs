using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The weapon tweak window over the network (protocol 60): only the host changes values (the window is read-only on a
/// client); whenever the set changes the host sends the whole set to everyone (<see cref="Msg.WeaponTweaks"/>, at most
/// 10 times a second while a slider is dragged), and every joining client gets it right after its Hello, so everyone fires
/// the same weapons. A client drops the host's tweaks when it leaves (the weapons go back to its files).
/// </summary>
public sealed partial class Game
{
    private int _tweaksSent; // the WeaponTweaks.Version last sent to everyone (0: never tweaked, so nothing to send)
    private double _tweaksSentAt = -1e9;

    /// <summary>Client: the host's weapon tweak packets received (the self-test counts them).</summary>
    public int TweakPacketsReceived { get; private set; }

    /// <summary>Client: the host has sent weapon tweaks that change something (the scoreboard marks the match).</summary>
    public bool HostTweaked { get; private set; }

    /// <summary>The weapons in this match differ from the files as loaded (the host's tweaks, or our own when hosting).</summary>
    public bool WeaponsTweaked => IsServer ? _hostTweaked : HostTweaked;

    private bool _hostTweaked; // server: worked out when the set changes, not every frame

    private void WriteWeaponTweaks()
    {
        _w.Begin(Msg.WeaponTweaks);
        WeaponTweaks.Write(_w);
    }

    /// <summary>Server: one joining client gets the current set (also when empty, so a client's own tweaks never linger).</summary>
    private void SendWeaponTweaks(RakNetGUID to)
    {
        if (!IsServer || Net.IsOffline)
            return;
        WriteWeaponTweaks();
        Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server, every frame: sends the set to everyone when the window changed it (one int compare otherwise).</summary>
    private void TickWeaponTweaks(double now)
    {
        if (!IsServer || WeaponTweaks.Version == _tweaksSent || now - _tweaksSentAt < 0.1)
            return;
        _tweaksSent = WeaponTweaks.Version;
        _tweaksSentAt = now;
        _hostTweaked = WeaponTweaks.DiffersFromLoaded;
        if (Net.IsOffline)
            return;
        WriteWeaponTweaks();
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        Log($"weapon tweaks sent: {WeaponTweaks.MultCount} class multiplier(s), {WeaponTweaks.OverrideCount} override(s)");
    }

    private void ReadWeaponTweaks(ref PacketReader r)
    {
        WeaponTweaks.Read(ref r);
        TweakPacketsReceived++;
        HostTweaked = WeaponTweaks.Active;
    }

    /// <summary>Session over: a client puts its weapons back as its files have them.</summary>
    private void EndWeaponTweaks()
    {
        if (!IsServer && TweakPacketsReceived > 0)
            WeaponTweaks.ResetAll();
    }
}
