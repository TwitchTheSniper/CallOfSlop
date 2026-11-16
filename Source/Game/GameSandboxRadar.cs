using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The local player's side of a craft's radar (<see cref="ConstructRadar"/>): what the scope shows and the lock they have
/// set. Contacts come from this PC's own copies of the bodies; only the lock is sent (<see cref="CActKind.RadarLock"/>).
/// </summary>
public sealed partial class Game
{
    /// <summary>The contacts the scope shows, nearest first (<see cref="RadarContactCount"/> of them).</summary>
    public readonly ushort[] RadarContacts = new ushort[ConstructRadar.MaxContacts];
    public int RadarContactCount;

    /// <summary>The local player's seated craft while it has a working radar, else null; its range.</summary>
    public Construct? RadarCraft;
    public float RadarRange;

    /// <summary>The contact the local player has locked (0: none), as sent to the server.</summary>
    public ushort RadarLock;

    /// <summary>Does the radar key lock on this craft (its radar is wired to a working Fire Director: <see cref="ConstructRadar.CanLock"/>)?</summary>
    public bool RadarCanLock;

    private ushort _radarBody;
    private int _radarFrames;

    /// <summary>Each frame, on every PC: the local player's radar, from the seat they are in.</summary>
    public void RadarUpdate(Player? me)
    {
        Construct? c = null;
        if (me != null && SandboxOn)
        {
            ref var h = ref SandboxHands.Hand(me.Id);
            if (h.Seated)
                c = Constructs.Get(h.SeatBody);
        }
        if (c == null)
        {
            _radarBody = 0;
            RadarLock = 0;
            RadarCraft = null;
            RadarRange = 0;
            RadarContactCount = 0;
            return;
        }
        RadarRange = ConstructRadar.Range(c);
        if (c.Id != _radarBody)
        {
            // A new seat starts with no lock, and clears the craft's (the craft's lock is the last one set).
            _radarBody = c.Id;
            RadarLock = 0;
            _radarFrames = 0;
            if (RadarRange > 0)
                SandboxSend(CActKind.RadarLock, body: c.Id, part: 0);
        }
        if (RadarRange <= 0)
        {
            RadarCraft = null;
            RadarContactCount = 0;
            return;
        }
        RadarCraft = c;
        RadarCanLock = ConstructRadar.CanLock(c);
        if (!RadarCanLock)
            RadarLock = 0;
        if (_radarFrames++ % 6 == 0) // (ten looks a second: a broadphase lookup each)
            RadarContactCount = ConstructRadar.Contacts(Constructs, c, RadarRange, RadarContacts);
        if (RadarLock != 0 && (Constructs.Get(RadarLock) is not { } t || !ConstructRadar.IsContact(c, t, RadarRange * ConstructRadar.HoldShare)))
            RadarLock = 0; // (off the radar: the server drops it too, at the next launch)
    }

    /// <summary>
    /// The radar key: the contact nearest the look (within <see cref="RadarPickCone"/> degrees) is locked; on the one
    /// locked already, the next one out from the look; past the last of them, none. True when something is locked now.
    /// </summary>
    public bool RadarPick(Vector3 eye, Vector3 look)
    {
        if (RadarCraft is not { } c || !RadarCanLock)
            return false;
        RadarLock = RadarNext(eye, look);
        SandboxSend(CActKind.RadarLock, body: c.Id, part: RadarLock);
        return RadarLock != 0;
    }

    /// <summary>What the radar key would lock from this look (0: it would clear the lock). The HUD shows it.</summary>
    public ushort RadarNext(Vector3 eye, Vector3 look)
    {
        var cos = MathF.Cos(RadarPickCone * MathF.PI / 180f);
        var lockDot = 2f;
        if (RadarLock != 0 && Constructs.Get(RadarLock) is { } held)
            lockDot = Dot(eye, look, held);
        ushort best = 0;
        var most = cos;
        for (var i = 0; i < RadarContactCount; i++)
        {
            if (RadarContacts[i] == RadarLock || Constructs.Get(RadarContacts[i]) is not { } b)
                continue;
            var d = Dot(eye, look, b);
            if (d > most && d < lockDot) // (with one locked: only those further from the look than it, so a press steps outwards)
            {
                most = d;
                best = b.Id;
            }
        }
        return best;
    }

    /// <summary>Degrees off the look a contact may be for the radar key to take it.</summary>
    public const float RadarPickCone = 30f;

    private static float Dot(Vector3 eye, Vector3 look, Construct b)
    {
        var to = b.WorldCom - eye;
        var len = to.Length();
        return len < 1e-3f ? 1f : Vector3.Dot(to / len, look);
    }
}
