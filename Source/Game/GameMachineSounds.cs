using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Machine sounds in the game (Karl, 2026-11-17; SynthMachine.cs makes them): reactors hum by load (MachineSounds.cs), a
/// robot shell starts up when someone wakes in it and powers down when they leave it standing, and the Shell Control
/// Chair's link is heard at the chair by everyone near it. The chair's own player hears the helmet and the link at their
/// ear, with the helmet's picture (<see cref="ShellLinkView"/> calls <see cref="LinkCue"/>). All worked out on each
/// peer from what it already has (the shell flag every peer hears; the plant share is a byte in the pose, protocol 335).
/// Fixed arrays by player id: nothing allocates.
/// </summary>
public sealed partial class Game
{
    private readonly byte[] _mcShell = new byte[256]; // 0 not seen yet, 1 in their body, 2 in a shell
    private readonly Vector3[] _mcAt = new Vector3[256]; // where each was last frame
    private readonly ushort[] _mcChair = new ushort[256], _mcSeat = new ushort[256];
    private double _mcLast;
    private readonly uint[] _mcSeen = new uint[256]; // the frame each id was last looked at
    private uint _mcFrame = 1;

    /// <summary>The link's sounds at the chair, as heard by others, are this much of the wearer's own.</summary>
    private const float ChairHeardGain = 0.55f;

    /// <summary>Every Sandbox frame, host and clients (Program.Sandbox.cs): the hums and the shells' start-ups.</summary>
    public void MachineFrameSounds(double now)
    {
        var dt = (float)Math.Clamp(now - _mcLast, 0, 0.25);
        _mcLast = now;
        if (!Audio.Enabled)
        {
            _mcFrame++; // (nothing heard: everyone starts unseen when sound comes back)
            return;
        }
        CallOfSlop.MachineSounds.Frame(Constructs, Audio, Audio.ListenerPosition, dt);
        _mcFrame++;
        foreach (var p in Players.Values)
        {
            var id = p.Id;
            if (_mcSeen[id] != _mcFrame - 1)
                _mcShell[id] = 0; // (not here last frame: a joiner, perhaps on a leaver's id, starts unseen)
            _mcSeen[id] = _mcFrame;
            var state = p.InShell ? (byte)2 : (byte)1;
            var was = _mcShell[id];
            if (was != 0 && was != state && p.Alive)
            {
                if (state == 2)
                {
                    Audio.Effect3D(Sfx.ShellBoot, p.Position, 0.85f);
                    if (id != LocalId && ChairAt(p.ShellChair, p.ShellSeat) is { } chair)
                        Audio.Effect3D(Sfx.ShellLinkWake, chair, ChairHeardGain);
                }
                else
                {
                    Audio.Effect3D(Sfx.ShellPowerDown, _mcAt[id], 0.8f); // (the shell stands where they were: or it fell, destroyed)
                    if (id != LocalId && ChairAt(_mcChair[id], _mcSeat[id]) is { } chair)
                        Audio.Effect3D(Sfx.ShellUnlink, chair, ChairHeardGain);
                }
            }
            _mcShell[id] = state;
            _mcAt[id] = p.Position;
            (_mcChair[id], _mcSeat[id]) = (p.ShellChair, p.ShellSeat);
        }
    }

    /// <summary>A shell chair's seat in the world, or null when its construct isn't here.</summary>
    private Vector3? ChairAt(ushort chair, ushort seat) =>
        Constructs.Get(chair) is { } c && seat < c.PartCount ? ConstructRender.PartCentre(c, seat) : null;

    /// <summary>From <see cref="ShellLinkView"/>: one of the link-up's sounds at this PC's player's ear.</summary>
    private void LinkCue(Sfx sfx)
    {
        if (Audio.Enabled)
            Audio.Effect(sfx, 0.6f);
    }

    /// <summary>
    /// Server: a Robot Shell Dock's clamps closing on a shell (<paramref name="clamp"/>) or letting it go, at
    /// <paramref name="at"/> (the dock), heard here and sent to every client (<see cref="Msg.MachineSound"/>).
    /// </summary>
    public void DockSound(bool clamp, Vector3 at) => MachineSoundAll(clamp ? Sfx.DockClamp : Sfx.DockRelease, at);

    /// <summary>Server: a machine's one-shot here and on every client (only the machine sounds: the reader refuses others).</summary>
    private void MachineSoundAll(Sfx sfx, Vector3 at)
    {
        if (Audio.Enabled)
            Audio.Effect3D(sfx, at, 0.9f);
        if (Net.IsOffline || !IsServer)
            return;
        var w = SbW;
        w.Begin(Msg.MachineSound).U8((byte)sfx).F32(at.X).F32(at.Y).F32(at.Z);
        Net.Broadcast(w.Span, PacketReliability.Unreliable);
    }

    /// <summary>Client: a <see cref="Msg.MachineSound"/> (after its id).</summary>
    private void ReadMachineSound(ref PacketReader r)
    {
        var sfx = (Sfx)r.U8();
        var at = r.Vec();
        if (sfx is >= Sfx.ShellHelmetDown and <= Sfx.DockRelease && float.IsFinite(at.X + at.Y + at.Z) && Audio.Enabled)
            Audio.Effect3D(sfx, at, 0.9f);
    }
}
