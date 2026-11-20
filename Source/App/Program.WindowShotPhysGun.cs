using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's physics gun check (pg01): "swing RATE SECS [DIST] [pitch]" (DIST: the hold distance, as the wheel sets it; pitch: the look turns up, not round: a joint held across its axis) turns the look at RATE degrees a second for SECS seconds
/// (as the mouse would), then holds it still for 1.5 s, and prints every few frames how far the held piece's grab point is
/// drawn from the crosshair's point (m) and at what angle off the look (degrees), on this peer. A host and a joined client
/// running the same swing should print the same lag (the client's piece is simulated by the server's spring:
/// GameSandboxPhysGunSim.cs). It ends with a summary line: the most and the mean lag while swinging, and the lag at rest.
/// </summary>
public static partial class Program
{
    private static long _wsSwingStart, _wsSwingLast;
    private static bool _wsSwingPitch;
    private static float _wsSwingRate, _wsSwingSecs, _wsSwingMax, _wsSwingSum, _wsSwingRest;
    private static int _wsSwingFrames, _wsSwingN, _wsWaitPeerFrames;
    private static bool _wsSwingRelHave; // (the piece's turn against the view, taken at the script's first swing: start with a still one, "swing 0 1")
    private static Quaternion _wsSwingRel0;
    private static float _wsSwingDrift, _wsSwingDriftRest;

    private static bool WsHeldKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "swing" or "waitpeer" or "beside" or "pitch" or "aimat" or "bodies" or "loose" or "framewatch";

    private static bool WsHeldVerb(Game g, string verb, string arg)
    {
        if (verb == "waitpeer")
        {
            // "waitpeer": the host's script waits (up to 120 s) until another player has joined
            if (g.Players.Count < 2 && _wsWaitPeerFrames++ < 120 * 60)
            {
                _wsQueue.EnqueueFront("waitpeer");
                _wsWait = 30;
            }
            else
                _wsWaitPeerFrames = 0;
            return true;
        }
        if (verb == "aimat" && g.Local is { } eyeOf)
        {
            // "aimat N|PART": the look turned to construct N's centre of mass (or the body holding PART)
            if (!ushort.TryParse(arg, out var id))
                id = WsFind(g, arg.Trim()).C?.Id ?? 0; // (or a part's name: "aimat Chest@new")
            if (g.Constructs.Get(id) is not { } body)
            {
                WsFail($"aimat: no construct {arg}");
                return true;
            }
            var eyeAt = g.RiderDrawnEye(eyeOf); // (on a moving deck the eye as the deck is drawn: a client draws bodies behind its own predicted eye)
            WsLook(g, eyeAt, body.WorldCom);
            WsSay(FormattableString.Invariant($"aimat: construct {id} at {body.WorldCom:0.0}, {Vector3.Distance(eyeAt, body.WorldCom):0.0} m"));
            _wsWait = 3;
            return true;
        }
        if (verb == "bodies" && g.Local is { } nearOf)
        {
            // "bodies [M]": the bodies within M m (default 30) of this player: id, parts, first part, distance, held or carried, asleep, speed and centre
            var m = float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bm) ? bm : 30f;
            foreach (var b in g.Constructs.Bodies)
                if (b.PartCount > 0 && Vector3.Distance(b.WorldCom, nearOf.Position) < m + b.Radius)
                    WsSay(FormattableString.Invariant($"bodies: {b.Id} {b.DefOf(0).Name} x{b.PartCount}, {Vector3.Distance(b.WorldCom, nearOf.Position):0.0} m, {b.Mass:0} kg{(b.CarriedBy != 0 ? $", carried by {b.CarriedBy}" : "")}{(b.Anchored ? ", anchored" : "")}{(b.SpaceShip != 0 ? ", a ship" : "")}, {(b.Asleep ? "asleep" : "awake")}, {b.Vel.Length():0.000} m/s, centre {b.WorldCom.X:0.000},{b.WorldCom.Y:0.000},{b.WorldCom.Z:0.000}"));
            return true;
        }
        if (verb == "loose" && g.IsServer && g.Local is { } placer)
        {
            // "loose PART DIST": host, a new loose body of one PART DIST m along the look (aboard a moving ship it takes the
            // ship's motion as every new body does: ConstructRide.SpawnIntoMotion), turned as the deck stands under the player
            var cut = arg.LastIndexOf(' ');
            var name = cut > 0 ? arg[..cut].Trim() : arg.Trim();
            var d = cut > 0 && float.TryParse(arg[(cut + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dd) ? dd : 2.5f;
            if (ConstructParts.Find(name) is not { } def)
            {
                WsFail($"loose: no part \"{name}\"");
                return true;
            }
            var deck = placer.Sim.RideUp != Vector3.Zero && g.Constructs.Get(placer.Sim.RideBody) is { } rb ? rb.Rot : ConstructGrab.TangentQ(placer.Position);
            var at = placer.Eye + g.LookDir(g.LookYaw, g.LookPitch) * d;
            var made = ConstructBuild.PlaceNew(g.Constructs, def, new FreePlacement { Pos = at, Rot = deck });
            WsSay(FormattableString.Invariant($"loose: {(made == null ? "not placed" : $"body {made.Id}, {made.Mass:0} kg, at {at:0.0}")}"));
            _wsWait = 10;
            return true;
        }
        if (verb == "framewatch")
        {
            // "framewatch SECS": every frame for SECS s, each loose body within 400 m in a moving construct's frame (the host's
            // Frame, a client's NetFrame) drawn against its frame as drawn: the largest jump a frame, its drift and its speed
            // through the frame, then a line each (local-frame-physics.md: on screen nothing should drift from the deck)
            _wsFwSecs = float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fs) ? fs : 3f;
            _wsFwStart = Stopwatch.GetTimestamp();
            _wsFw.Clear();
            _wsWait = Math.Max(_wsWait, 2);
            return true;
        }
        if (verb == "pitch")
        {
            // "pitch DEG": the look's pitch alone (the yaw stays where it is)
            g.LookPitch = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) * MathF.PI / 180f;
            _wsWait = 3;
            return true;
        }
        if (verb == "beside" && g.Local is { } self)
        {
            // "beside M": host, every other player set down M m to the host's right, facing as the host faces; a client
            // waits (up to 60 s) until it stands there (by straight distance: on a round world's side too), then takes the host's look
            var right = Vector3.Normalize(Vector3.Cross(g.LookDir(self.Yaw, 0), GravityFrames.UpAt(self.Position)));
            var m = float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bm) ? bm : 3f;
            if (g.IsServer)
            {
                foreach (var other in g.Players.Values)
                    if (other != self)
                    {
                        other.Teleport(self.Position + right * m);
                        WsSay(FormattableString.Invariant($"beside: player {other.Id} to {self.Position + right * m:0.0}, yaw {self.Yaw * 180 / MathF.PI:0.0}"));
                    }
                _wsWait = 30;
                return true;
            }
            var host = g.Players.Values.FirstOrDefault(q => q != self && q.Alive);
            if (host == null || MathF.Abs(Vector3.Distance(host.Position, self.Position) - m) > 1f)
            {
                if (_wsWaitPeerFrames++ < 60 * 2)
                {
                    _wsQueue.EnqueueFront($"beside {arg}");
                    _wsWait = 30;
                    return true;
                }
                WsFail(FormattableString.Invariant($"beside: the host never set this player beside it (host {(host == null ? "unseen" : $"{Vector3.Distance(host.Position, self.Position):0.0} m away")})"));
                return true;
            }
            _wsWaitPeerFrames = 0;
            g.LookYaw = host.Yaw;
            WsSay(FormattableString.Invariant($"beside: {Vector3.Distance(host.Position, self.Position):0.0} m from the host, yaw {host.Yaw * 180 / MathF.PI:0.0}"));
            _wsWait = 3;
            return true;
        }
        if (verb != "swing")
            return false;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var bits = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (bits.Length < 2 || !float.TryParse(bits[0], ci, out _wsSwingRate) || !float.TryParse(bits[1], ci, out _wsSwingSecs))
        {
            WsFail("swing RATE SECS [DIST] [pitch]");
            return true;
        }
        if (bits.Length > 2 && float.TryParse(bits[2], ci, out var dist))
            _pgDist = dist; // (as the wheel sets it)
        _wsSwingPitch = bits.Length > 3 && bits[3].Equals("pitch", StringComparison.OrdinalIgnoreCase);
        _wsSwingStart = _wsSwingLast = Stopwatch.GetTimestamp();
        _wsSwingMax = _wsSwingSum = _wsSwingRest = 0;
        _wsSwingFrames = _wsSwingN = 0;
        _wsSwingDrift = _wsSwingDriftRest = 0;
        _wsWait = Math.Max(_wsWait, 2);
        return true;
    }

    private static long _wsFwStart;
    private static float _wsFwSecs;
    private static readonly Dictionary<ushort, (Vector3 First, Vector3 Last, float Jump, int Frames, ushort Frame)> _wsFw = new();

    /// <summary>Once a frame: a running framewatch reads the loose bodies against their frames as both are drawn.</summary>
    private static void WsFrameWatch(Game g, Player me)
    {
        if (_wsFwStart == 0)
            return;
        foreach (var b in g.Constructs.Bodies)
        {
            var fid = g.IsServer ? b.Frame : b.NetFrame;
            if (fid == 0 || g.Constructs.Get(fid) is not { } f || Vector3.Distance(b.WorldCom, me.Position) > 400f)
                continue;
            // (the frame's drawn turn as a turn, not from the differences of points kilometres out: their floats' grain
            // turned the axes by a milliradian, 2-3 cm at 24 m)
            var fp = g.DrawnPoint(f, f.Pos);
            var fq = g.IsServer && fp != f.Pos ? Quaternion.Normalize(Quaternion.Slerp(f.PrevRot, f.Rot, g.TickAlpha)) : f.Rot;
            var local = Vector3.Transform(g.DrawnPoint(b, b.WorldCom) - fp, Quaternion.Conjugate(fq));
            if (_wsFw.TryGetValue(b.Id, out var e) && e.Frame == fid)
                _wsFw[b.Id] = (e.First, local, MathF.Max(e.Jump, Vector3.Distance(local, e.Last)), e.Frames + 1, fid);
            else
                _wsFw[b.Id] = (local, local, 0, 1, fid);
        }
        _wsWait = Math.Max(_wsWait, 2);
        if (Stopwatch.GetElapsedTime(_wsFwStart).TotalSeconds < _wsFwSecs)
            return;
        foreach (var (id, e) in _wsFw)
            if (g.Constructs.Get(id) is { } b)
                WsSay(FormattableString.Invariant($"framewatch: {(g.IsServer ? "host" : "client")} body {id} ({b.DefOf(0).Name}) in {e.Frame}: {e.Frames} frames, jump {e.Jump * 1000:0.0} mm a frame most, drift {Vector3.Distance(e.First, e.Last):0.000} m, at {e.Last:0.00}, {(b.Asleep ? "asleep" : "awake")}, speed through it {(g.IsServer ? b.FrameVel.Length() : b.NetFrameVel.Length()):0.00} m/s; frame at {g.Constructs.Get(e.Frame)?.Vel.Length() ?? 0:0} m/s"));
        if (_wsFw.Count == 0)
            WsSay("framewatch: no loose body in a frame near");
        _wsFwStart = 0;
        _wsWait = 0;
    }

    /// <summary>Once a frame: a running swing turns the look and reads the held piece's lag.</summary>
    private static void WsHeldFrame()
    {
        if (_session is { } fg && fg.Local is { } fme)
            WsFrameWatch(fg, fme);
        if (_wsSwingStart == 0 || _session is not { } g || g.Local is not { } me)
            return;
        var now = Stopwatch.GetTimestamp();
        var t = (float)Stopwatch.GetElapsedTime(_wsSwingStart, now).TotalSeconds;
        var dt = (float)Stopwatch.GetElapsedTime(_wsSwingLast, now).TotalSeconds;
        _wsSwingLast = now;
        if (t < _wsSwingSecs && _wsSwingPitch)
            g.LookPitch = Math.Clamp(g.LookPitch + _wsSwingRate * MathF.PI / 180f * dt, -1.5f, 1.5f);
        else if (t < _wsSwingSecs)
            g.LookYaw += _wsSwingRate * MathF.PI / 180f * dt;
        _wsWait = Math.Max(_wsWait, 2);
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (h.GrabBody != 0 && g.Constructs.Get(h.GrabBody) is { } c)
        {
            var look = g.LookDir(g.LookYaw, g.LookPitch);
            var eye = g.RiderDrawnEye(me); // (on a deck, the eye as the deck is drawn)
            var at = c.ToWorld(h.GrabLocal);
            if (h.GrabSeg != 0 && h.GrabSeg <= c.JointCount)
            {
                c.WorldFrame(h.GrabSeg, out var sq, out var st); // (a held joint: the point is in its segment's frame)
                at = st + Vector3.Transform(h.GrabLocal, sq);
            }
            at = g.DrawnPoint(c, at); // (the host draws between its last two ticks: the point where it is drawn, as the eye is)
            var lag = Vector3.Distance(at, eye + look * _pgDist);
            // The piece's turn against the view's own yaw frame (built from the look and the up the player stands to, not
            // from the code's frame): with no turn key it should stay put as the look sweeps, in any gravity frame.
            var up = h.RideBody != 0 ? Vector3.Transform(Vector3.UnitY, h.RideTurn) : GravityFrames.UpAt(eye);
            var flat = look - up * Vector3.Dot(look, up);
            var drift = 0f;
            if (flat.LengthSquared() > 1e-6f && h.GrabSeg == 0)
            {
                flat = Vector3.Normalize(flat);
                var rx = Vector3.Cross(flat, up);
                var viewQ = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(rx.X, rx.Y, rx.Z, 0, up.X, up.Y, up.Z, 0, -flat.X, -flat.Y, -flat.Z, 0, 0, 0, 0, 1)));
                var rel = Quaternion.Normalize(Quaternion.Conjugate(viewQ) * c.Rot);
                if (!_wsSwingRelHave)
                    (_wsSwingRel0, _wsSwingRelHave) = (rel, true);
                drift = 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(rel, _wsSwingRel0)), 0f, 1f)) * 180f / MathF.PI;
                if (t < _wsSwingSecs)
                    _wsSwingDrift = MathF.Max(_wsSwingDrift, drift);
                else
                    _wsSwingDriftRest = drift;
            }
            var off = at - eye;
            var deg = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(off), look), -1f, 1f)) * 180f / MathF.PI;
            if (t < _wsSwingSecs)
            {
                _wsSwingMax = MathF.Max(_wsSwingMax, lag);
                _wsSwingSum += lag;
                _wsSwingN++;
            }
            else
                _wsSwingRest = lag;
            if (_wsSwingFrames++ % 6 == 0)
                WsSay(FormattableString.Invariant($"swing: t {t:0.00} s, lag {lag:0.000} m, {deg:0.0} deg off the look, sim {g.SandboxHeldSimulated || g.SandboxJointSimulated}, seg {h.GrabSeg}, lead {g.SandboxHeldLead:0.000} m, turn off the view {drift:0.0} deg, spin {c.AngVel.Length() * 180f / MathF.PI:0} deg/s, eye off {Vector3.Distance(g.SandboxHeldEye, eye):0.000} (tick eye {Vector3.Distance(g.SandboxHeldEyeEarly, eye):0.000}) m, dist {_pgDist:0.0}, mass {c.Mass:0} kg"));
        }
        if (t >= _wsSwingSecs + 1.5f)
        {
            WsSay(FormattableString.Invariant($"swing done: {(g.IsServer ? "host" : "client")}, swinging max {_wsSwingMax:0.000} m, mean {(_wsSwingN > 0 ? _wsSwingSum / _wsSwingN : 0):0.000} m over {_wsSwingN} frames; at rest {_wsSwingRest:0.000} m; turn off the view at most {_wsSwingDrift:0.0} deg swinging, {_wsSwingDriftRest:0.0} deg at rest"));
            _wsSwingStart = 0;
            _wsWait = 0;
        }
    }
}
