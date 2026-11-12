using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The talk camera (docs/story/STORY.md "Talking to people"): while the talk panel (Program.StoryTalk.cs) is open on a
/// person standing near you, the view eases from wherever it was (first person, the third-person shoulder camera) to an
/// over-the-shoulder shot: behind your left shoulder, looking at their face, so they sit left of the middle and the panel
/// on the right doesn't cover them. Closing the talk eases back to your own view, which has been worked out all along, so
/// it lands wherever you are now looking. Local only (nothing is sent), one sphere cast a frame while it shows, no
/// allocations. A talk joined from afar, a seat, a death or the opening's cameras skip or cut it.
/// </summary>
public static partial class Program
{
    /// <summary>Seconds the camera takes to ease into the shot and out of it.</summary>
    public const float TalkCamIn = 0.7f, TalkCamOut = 0.55f;

    /// <summary>The shot: metres behind your head, to its side, above it; and how far the aim sits to the right of their face (to put them left of the middle).</summary>
    public const float TalkCamBack = 1.4f, TalkCamSide = 0.85f, TalkCamUp = 0.08f, TalkCamFrame = 0.35f;

    private static float _talkCamT; // 0: your own view, 1: the shot (before the ease)
    private static byte _talkCamOn = 255; // whose face the shot was last on
    private static Vector3 _talkCamEye, _talkCamFwd; // the last shot, held while easing out after they are gone

    /// <summary>How far into the shot the frame is (0..1, eased); above 0 the camera is the talk camera's.</summary>
    public static float TalkCamWeight => DeathCam.EaseInOut(_talkCamT);

    /// <summary>
    /// While the talk camera shows: the local player's own health bar is left off their head (PlayerRig.cs). On a story ride
    /// (a cinematic, Program.Quarry.cs) nobody's is drawn.
    /// </summary>
    public static bool TalkHidesBar(byte id) => _talkCamT > 0f && id == _game.LocalId || _session is { QuarryRiding: true };

    /// <summary>
    /// The shots tried in turn, as (metres back, metres to the side: negative left): over the left shoulder, the right,
    /// then wider and more from the side as walls close in. The first whose view of their face is clear of walls and
    /// of your own head and shoulders is the one used; past them all, the camera sits just in front of your face.
    /// </summary>
    private static readonly (float Back, float Side)[] TalkShots =
    [
        (TalkCamBack, -TalkCamSide), (TalkCamBack, TalkCamSide),
        (0.9f, -1.15f), (0.9f, 1.15f),
        (0.35f, -1.3f), (0.35f, 1.3f),
    ];

    private static int _talkCamPick; // the shot in use (TalkShots), kept while it stays clear so the camera doesn't hop

    /// <summary>
    /// One of the shots for you at <paramref name="eye"/> talking to someone whose eye is at <paramref name="face"/>:
    /// where the camera wants to be (before walls) and where it looks (a little right of their face, so they sit left
    /// of the middle, away from the panel).
    /// </summary>
    public static void TalkShot(Vector3 eye, Vector3 face, Vector3 up, float back, float side, out Vector3 cam, out Vector3 look)
    {
        TalkAxes(eye, face, up, out var ahead, out var right);
        cam = eye - ahead * back + right * side + up * TalkCamUp;
        look = face + right * TalkCamFrame - up * 0.08f;
    }

    private static void TalkAxes(Vector3 eye, Vector3 face, Vector3 up, out Vector3 ahead, out Vector3 right)
    {
        var to = face - eye;
        var flat = to - up * Vector3.Dot(to, up);
        if (flat.LengthSquared() < 1e-4f)
            flat = MathF.Abs(up.X) < 0.9f ? Vector3.Cross(up, Vector3.UnitX) : Vector3.Cross(up, Vector3.UnitZ); // (straight above or below you: any way round)
        ahead = Vector3.Normalize(flat);
        right = Vector3.Normalize(Vector3.Cross(ahead, up));
    }

    /// <summary>
    /// Is the view from <paramref name="cam"/> to <paramref name="face"/> clear: of your own head and shoulders (spheres
    /// round <paramref name="eye"/>, the head's wide enough for a hat's brim) and of the world?
    /// </summary>
    public static bool TalkShotClear(IWorldQuery q, Vector3 cam, Vector3 face, Vector3 eye, Vector3 up)
    {
        var head = eye + up * 0.05f; // (the head is a 0.45 m box round the eye, BodyModel.Stances; a hat's brim reaches further)
        var chest = eye - up * 0.45f; // (shoulders 0.8 m wide, and arms held up with a tool or gun)
        if (SegmentDistance(cam, face, head) < 0.5f || SegmentDistance(cam, face, chest) < 0.45f) // (their whole face clear, not only its middle)
            return false;
        var d = face - cam;
        var len = d.Length();
        if (!(len > 0.5f) || !float.IsFinite(len))
            return false;
        var reach = len - 0.35f; // (stop short of them: their own body isn't the wall)
        return q.Raycast(cam, d / len, reach, QueryFilter.All) >= reach - 0.01f;
    }

    private static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector3.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-6f), 0f, 1f);
        return Vector3.Distance(a + ab * t, p);
    }

    /// <summary>The camera and its look for this frame: the shot in use if it is still clear, else the first clear one, else in front of your face.</summary>
    private static void TalkPick(Vector3 head, Vector3 face, Vector3 up, bool fresh, out Vector3 cam, out Vector3 look)
    {
        var q = World.Query;
        if (fresh)
            _talkCamPick = 0;
        for (var n = 0; n <= TalkShots.Length; n++)
        {
            var i = n == 0 ? _talkCamPick : n - 1; // (the one in use first)
            if (n > 0 && i == _talkCamPick || i >= TalkShots.Length)
                continue;
            TalkShot(head, face, up, TalkShots[i].Back, TalkShots[i].Side, out var ideal, out look);
            cam = ThirdPerson.Pull(q, head, ideal); // (a wall behind you pulls the camera in, as the shoulder camera's does)
            if (TalkShotClear(q, cam, face, head, up))
            {
                _talkCamPick = i;
                return;
            }
        }
        TalkAxes(head, face, up, out var ahead, out _);
        cam = head + ahead * 0.3f + up * 0.04f; // (no room anywhere: just in front of your own face)
        look = face;
        _talkCamPick = TalkShots.Length;
    }

    /// <summary>
    /// In the frame, after your own view is worked out: eases it towards the talk shot. True while any of the shot shows
    /// (no first-person gun, your body drawn, no render origin on the seat) once the camera has left your head.
    /// </summary>
    private static bool TalkCamera(Player? local, ref Vector3 eye, ref Vector3 fwd, ref float fov)
    {
        var dt = (float)FrameDt;
        Player? them = null;
        var want = _talkTo != 255 && !_talkRemote && local is { Alive: true, RenderSeat: SeatKind.None } && _game.SandboxOn && !_game.OpeningActive && !_game.QuarryRiding
                   && _game.Players.TryGetValue(_talkTo, out them) && them.Alive;
        if (!want && local is not { Alive: true })
            _talkCamT = 0; // (a death cuts it: the death camera takes over)
        _talkCamT = want ? MathF.Min(1f, _talkCamT + dt / TalkCamIn) : MathF.Max(0f, _talkCamT - dt / TalkCamOut);
        if (_talkCamT <= 0f || local == null)
        {
            _talkCamOn = 255;
            return false;
        }
        if (want)
        {
            var head = local.RenderEye;
            var up = GravityFrames.UpAt(head);
            TalkPick(head, them!.RenderEye, up, _talkCamOn != them.Id, out var cam, out var look);
            var dir = look - cam;
            var len = dir.Length();
            var f = len > 1e-3f && float.IsFinite(len) ? dir / len : fwd;
            if (_talkCamOn != them.Id)
                (_talkCamEye, _talkCamFwd) = (cam, f); // (a new face: the shot starts there)
            else
            {
                var k = 1f - MathF.Exp(-dt * 8f); // (then follows them as they turn and settle, never jumping)
                _talkCamEye = Vector3.Lerp(_talkCamEye, cam, k);
                _talkCamFwd = Vector3.Normalize(Vector3.Lerp(_talkCamFwd, f, k));
            }
            _talkCamOn = them.Id;
        }
        var w = TalkCamWeight;
        var startEye = eye;
        eye = Vector3.Lerp(eye, _talkCamEye, w);
        var mixed = Vector3.Lerp(fwd, _talkCamFwd, w);
        fwd = mixed.LengthSquared() > 1e-6f ? Vector3.Normalize(mixed) : _talkCamFwd;
        fov = float.Lerp(fov, BaseFov, w); // (aiming down the sights or a sprint's wider view: back to the plain view)
        return Vector3.DistanceSquared(startEye, eye) > 0.3f * 0.3f; // (inside your own head until then: the first-person view stays)
    }
}
