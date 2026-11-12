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
    public const float TalkCamBack = 1.4f, TalkCamSide = 0.75f, TalkCamUp = 0.2f, TalkCamFrame = 0.35f;

    private static float _talkCamT; // 0: your own view, 1: the shot (before the ease)
    private static byte _talkCamOn = 255; // whose face the shot was last on
    private static Vector3 _talkCamEye, _talkCamFwd; // the last shot, held while easing out after they are gone

    /// <summary>How far into the shot the frame is (0..1, eased); above 0 the camera is the talk camera's.</summary>
    public static float TalkCamWeight => DeathCam.EaseInOut(_talkCamT);

    /// <summary>While the talk camera shows: the local player's own health bar is left off their head (PlayerRig.cs).</summary>
    public static bool TalkHidesBar(byte id) => _talkCamT > 0f && id == _game.LocalId;

    /// <summary>
    /// The shot for you at <paramref name="eye"/> talking to someone whose eye is at <paramref name="face"/>: where the
    /// camera wants to be (before walls) and where it looks.
    /// </summary>
    public static void TalkShot(Vector3 eye, Vector3 face, Vector3 up, out Vector3 cam, out Vector3 look)
    {
        var to = face - eye;
        var flat = to - up * Vector3.Dot(to, up);
        var ahead = flat.LengthSquared() > 1e-4f ? Vector3.Normalize(flat) : Vector3.Normalize(Vector3.Cross(up, Vector3.UnitX) is var f && f.LengthSquared() > 1e-6f ? f : Vector3.UnitZ);
        var right = Vector3.Normalize(Vector3.Cross(ahead, up));
        cam = eye - ahead * TalkCamBack - right * TalkCamSide + up * TalkCamUp;
        look = face + right * TalkCamFrame - up * 0.08f;
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
            TalkShot(head, them!.RenderEye, up, out var ideal, out var look);
            var cam = ThirdPerson.Pull(World.Query, head, ideal); // (a wall behind you pulls the camera in, as the shoulder camera's does)
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
