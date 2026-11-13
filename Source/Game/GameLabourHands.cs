using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Story people at work among the convict labourers (docs/story/LABOUR.md "Hands"; Karl, 2026-11-13: you can speak to the
/// convicts who are doing things, and a few of them know of a break-out). A labour post (Story/Posts/PenalColony.json,
/// <c>"at": "labour", "gang": NAME</c>) puts a person on a Work step into one of the gangs, in its last inmate's place.
/// While they stand there working, they are drawn as that gang's labourer, shovelling or huddled like the rest
/// (Render/LabourRender.cs), and their own body isn't; the gang's last figure stands aside for them. The moment they do
/// anything else (a player talks to them, they react, the shift ends) the body is shown and the gang is one short. The
/// host works it out with the bodies, four times a second, and sends <see cref="Msg.LabourHands"/> only when it changes
/// (and to each joiner). Nothing allocates.
/// </summary>
public sealed partial class Game
{
    public const int LabourHandsMax = 8;

    /// <summary>Kinds of gang a person can work in: those that stay put (a walking gang has no place to stand in).</summary>
    public static bool LabourHandKind(LabourKind k) =>
        k is LabourKind.Shovel or LabourKind.Pick or LabourKind.Hammer or LabourKind.Huddle or LabourKind.Sit or LabourKind.Stand;

    /// <summary>Both sides: who works in a gang now (their player id) and in which (an index into <see cref="LabourScenes"/>).</summary>
    public readonly byte[] LabourHandPlayer = new byte[LabourHandsMax], LabourHandScene = new byte[LabourHandsMax];
    public int LabourHandCount { get; private set; }

    private readonly short[] _lbGang = new short[LabourScenesMax]; // server: each scene's gang in the file
    private readonly byte[] _lhPlayer = new byte[LabourHandsMax], _lhScene = new byte[LabourHandsMax];
    private PacketWriter? _lhW;

    /// <summary>Both sides: the gang a player works in now (an index into <see cref="LabourScenes"/>), or -1.</summary>
    public int LabourHandOf(byte player)
    {
        for (var i = 0; i < LabourHandCount; i++)
            if (LabourHandPlayer[i] == player)
                return LabourHandScene[i] < LabourCount ? LabourHandScene[i] : -1;
        return -1;
    }

    /// <summary>Server: the scene of the gang of that name, or -1 (its place isn't built yet: the quarry before the first duty).</summary>
    private int LabourSceneOf(string gang)
    {
        var script = LabourScript.Current.Scenes;
        for (var i = 0; i < LabourCount; i++)
            if (_lbGang[i] >= 0 && _lbGang[i] < script.Length && script[_lbGang[i]].Name.Equals(gang, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>
    /// Server: where a person stands to work in a gang (its last inmate's place, as Render/LabourRender.cs lays them out,
    /// without that figure's own small turn) and what they face.
    /// </summary>
    private bool LabourPostAt(string gang, out Vector3 stand, out Vector3 face)
    {
        stand = face = default;
        if (!LabourOn || LabourSceneOf(gang) is not (var k and >= 0))
            return false;
        ref readonly var s = ref LabourScenes[k];
        var up = StandInPath.Up(s.P0);
        var fwd = StandInPath.Along(s.Facing, s.P0);
        var right = Vector3.Normalize(Vector3.Cross(fwd, up));
        var gap = MathF.Max(s.Gap, 0.5f);
        var n = Math.Max((int)s.Inmates, 1);
        var i = n - 1;
        switch (s.Kind)
        {
            case LabourKind.Huddle:
            case LabourKind.Hammer:
            {
                var a = MathF.Tau * (i + 0.5f) / n;
                stand = s.P0 + (right * MathF.Sin(a) - fwd * MathF.Cos(a)) * gap;
                face = s.P0;
                return true;
            }
            default:
                stand = s.P0 + right * (i * gap);
                face = stand + fwd * 3f;
                return true;
        }
    }

    /// <summary>Server, with the bodies (four times a second): who stands working in a gang now; sent when it changed.</summary>
    private void LabourHandsTick(double now)
    {
        var posts = StoryPostsData.Current.Posts;
        var n = 0;
        if (Story is { } s && LabourCount > 0)
            for (var i = 0; i < _bodyCount && n < LabourHandsMax; i++)
            {
                ref var b = ref _bodies[i];
                if (b.Post <= 0 || b.Post > posts.Length || posts[b.Post - 1].At != StoryPostPlace.Labour || b.Fight != 0 || b.Leaving || b.Goal != b.PostGoal)
                    continue;
                if (!Players.TryGetValue(b.Player, out var p) || !p.Alive || _talkWith[p.Id] != 0 || now < _faceUntil[p.Id])
                    continue; // (talking, or just spoken to someone: stopped work to face them)
                if (s.Agents.Of(b.Entity) is not { Busy: true } a || a.DoingFrom != b.PostFor || s.Rules.People.Actions[a.Doing.Action].Verb != _vWork)
                    continue;
                if (Flat2(b.PostGoal - p.Position) > StoryPostNear * StoryPostNear || LabourSceneOf(posts[b.Post - 1].Gang) is not (var k and >= 0))
                    continue;
                _lhPlayer[n] = p.Id;
                _lhScene[n] = (byte)k;
                n++;
            }
        var same = n == LabourHandCount;
        for (var i = 0; same && i < n; i++)
            same = _lhPlayer[i] == LabourHandPlayer[i] && _lhScene[i] == LabourHandScene[i];
        if (same)
            return;
        Array.Copy(_lhPlayer, LabourHandPlayer, n);
        Array.Copy(_lhScene, LabourHandScene, n);
        LabourHandCount = n;
        Console.WriteLine($"[labour] {n} story people working in the gangs");
        LabourHandsSend(null);
    }

    /// <summary>Server: who works in a gang, to everyone or one joiner: u8 n, each (u8 player, u8 scene).</summary>
    private void LabourHandsSend(RakNetGUID? to)
    {
        if (Net.IsOffline || !IsServer)
            return;
        var w = _lhW ??= new PacketWriter(64);
        w.Begin(Msg.LabourHands).U8((byte)LabourHandCount);
        for (var i = 0; i < LabourHandCount; i++)
            w.U8(LabourHandPlayer[i]).U8(LabourHandScene[i]);
        if (to is { } g)
            Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
        else
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.LabourHands"/> (after its id).</summary>
    private void ReadLabourHands(ref PacketReader r)
    {
        if (!r.Has(1))
            return;
        var n = Math.Min((int)r.U8(), LabourHandsMax);
        var count = 0;
        for (var i = 0; i < n && r.Has(2); i++)
        {
            LabourHandPlayer[count] = r.U8();
            LabourHandScene[count] = r.U8();
            count++;
        }
        LabourHandCount = count;
    }
}
