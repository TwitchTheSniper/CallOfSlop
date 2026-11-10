using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--zrange-host|--zrange-client [--map NAME] [--port P] [--size N] [--dists 50,100,200,400]</c>: the far zombie crowd in the
/// real window, host and client (a measurement, not a test; it fails nothing). Start one host and one client process (each under
/// xvfb in the container, on a throwaway copy of the saves; <c>--lag</c> / <c>--loss</c> as ever). Both players stand in the
/// map's south-west corner looking north-east (51 degrees from +X); for each distance the host spawns a crowd group of N that far ahead, both peers
/// log the crowd zombies they draw (and the individuals they have), then each aims at the drawn zombie nearest the middle of
/// what it draws and fires its hand gun; the host logs the hits the server credited to each (crowd and individuals). Lines
/// start "[zrange]"; the host quits after the last distance, the client when the host goes.
/// </summary>
public static unsafe partial class Program
{
    private static int _zr; // 0 off, 1 host, 2 client
    private static int _zrSize = 400;
    private static float[] _zrDists = [50, 100, 200, 400];
    private static double _zrQuitAt = -1;
    private static double _zrStart = -1, _zrReady = -1, _zrPhaseAt = -1, _zrLastLog;
    private static int _zrPhase = -1;
    private static bool _zrPlaced, _zrPress, _zrZombies;
    private static int _zrShot;
    private static int _zrHits0Host, _zrHits0Client, _zrZ0Host, _zrZ0Client, _zrShots;
    private static long _zrDrawnSum, _zrIndSum, _zrSamples;
    private const double ZrPhaseSeconds = 16;

    private static readonly Vector3 ZrDir = new(MathF.Cos(51f * MathF.PI / 180f), 0, MathF.Sin(51f * MathF.PI / 180f)); // (Proving Ground: the ground stays within 0.8 m along it, past the track's banks)
    private static readonly float ZrYaw = MathF.Atan2(ZrDir.X, -ZrDir.Z);

    private static void StartZombieRange(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        _zr = args.Any(a => a.Equals("--zrange-client", StringComparison.OrdinalIgnoreCase)) ? 2 : 1;
        if (int.TryParse(Arg("--size"), out var n))
            _zrSize = Math.Clamp(n, 65, 500000);
        if (Arg("--dists") is { } ds)
            _zrDists = ds.Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        // --zombies: the Zombies mode's own crowd round instead (Mass Horde Test by default): no groups spawned, no shots, only
        // the log lines and two pictures (zrange_host_1.bmp ...) of what each peer draws from where it stands.
        _zrZombies = args.Any(a => a.Equals("--zombies", StringComparison.OrdinalIgnoreCase));
        _settings.Mode = _zrZombies ? GameMode.Zombies : GameMode.Sandbox;
        _settings.MapName = (Maps.Find(Arg("--map") ?? (_zrZombies ? "Mass Horde Test" : "Proving Ground")) ?? Maps.All[0]).Name;
        _settings.MapRandom = false;
        _settings.Bots = _settings.HostBots = 0;
        _settings.AutoStart = 0;
        if (ushort.TryParse(Arg("--port"), out var port))
            _settings.Port = port;
        _settings.JoinAddress = "127.0.0.1";
        Console.WriteLine($"[zrange] {(_zr == 1 ? "host" : "client")} on {_settings.MapName}, port {_settings.Port}, {_zrSize} zombies at {string.Join("/", _zrDists)} m");
        StartSession(_zr == 1 ? LobbyAction.Host : LobbyAction.Join);
    }

    private static Vector3 ZrCorner()
    {
        var m = World.Map;
        var h = MathF.Min(m.HalfX, m.HalfZ) - 30f;
        return new Vector3(-h, 0, -h);
    }

    private static void ZombieRangeFrame(double now)
    {
        if (_zrStart < 0)
            _zrStart = now;
        if (_session is not { } g)
        {
            if (now - _zrStart > 240)
                ZrQuit("no session");
            return;
        }
        if (_zr == 1 && g.Phase == MatchPhase.Lobby && g.Players.Count >= 2 && (int)(now * 2) != (int)((now - FrameDt) * 2))
            g.LobbyStart(true);
        if (g.Phase == MatchPhase.Warmup)
        {
            if (_zr == 1)
                g.ForceStart();
            g.SetReady(true);
            g.RequestRespawn();
        }
        else if (g.Phase == MatchPhase.Playing && Ui.MenuOpen && g.Local is { Alive: true })
        {
            Ui.AutoDeploy(g, now);
            SetMouseCaptured(true);
        }
        else if (g.Phase == MatchPhase.Playing && g.Local is not { Alive: true } && !g.RespawnRequested)
            g.RequestRespawn();
        if (_zr == 2 && _zrReady > 0 && g.Phase != MatchPhase.Playing && now - _zrReady > 20)
        {
            ZrQuit(null); // (the host went)
            return;
        }
        if (g.Local is not { Alive: true } me || g.Phase != MatchPhase.Playing || Ui.MenuOpen)
        {
            if (now - _zrStart > 300)
                ZrQuit("never got into play");
            return;
        }
        me.ImmuneUntil = now + 600;
        if (_zrReady < 0)
            _zrReady = now;
        if (_zrZombies)
        {
            ZrZombiesFrame(g, me, now);
            return;
        }
        // Both stand in the corner (the client a little to the side), looking along the diagonal.
        var spot = ZrCorner() + (_zr == 2 ? new Vector3(2, 0, -2) : Vector3.Zero);
        spot.Y = World.Map.GroundHeight(spot.X, spot.Z) + 0.05f;
        if (_zr == 1)
            foreach (var p in g.Players.Values)
            {
                // (the host moves both players, the client's through the server)
                var ps = ZrCorner() + (p.Id == me.Id ? Vector3.Zero : new Vector3(2, 0, -2));
                ps.Y = World.Map.GroundHeight(ps.X, ps.Z) + 0.05f;
                if (p.Alive && Vector3.DistanceSquared(new Vector3(p.Position.X, 0, p.Position.Z), new Vector3(ps.X, 0, ps.Z)) > 4f)
                    p.Teleport(ps);
            }
        _zrPlaced = true;
        // The drawn crowd: its middle, and the drawn zombie nearest it (what a player would aim at).
        var inst = Crowd.Inst;
        var drawn = inst.Total;
        float sx = 0, sz = 0;
        foreach (var st in inst.Stripes)
            for (var l = 0; l < CrowdInstances.Lods; l++)
                for (var k = 0; k < st.Count[l]; k++)
                {
                    sx += st.Data[l][k].X;
                    sz += st.Data[l][k].Z;
                }
        var aliveInd = 0;
        float ix = 0, iz = 0;
        foreach (var z in g.Horde)
            if (z.Alive)
            {
                aliveInd++;
                ix += z.RenderPos.X;
                iz += z.RenderPos.Z;
            }
        Vector3? aim = null;
        var eye = _zr == 2 ? CrowdCam : me.RenderEye; // (the client: where its camera is)
        if (drawn > 0)
        {
            float cx = sx / drawn, cz = sz / drawn, best = float.MaxValue;
            foreach (var st in inst.Stripes)
                for (var l = 0; l < CrowdInstances.Lods; l++)
                    for (var k = 0; k < st.Count[l]; k++)
                    {
                        ref var o = ref st.Data[l][k];
                        var d = (o.X - cx) * (o.X - cx) + (o.Z - cz) * (o.Z - cz);
                        if (d < best)
                        {
                            best = d;
                            aim = new Vector3(o.X, (float)o.Y + inst.BaseY + 1.45f, o.Z); // (the chest: over a rise far off only the upper body shows)
                        }
                    }
        }
        else if (aliveInd > 0)
            aim = new Vector3(ix / aliveInd, 0, iz / aliveInd) with { Y = World.Map.GroundHeight(ix / aliveInd, iz / aliveInd) + 1.0f };
        var aimDist = aim is { } av ? Vector2.Distance(new Vector2(av.X, av.Z), new Vector2(eye.X, eye.Z)) : 0f;

        // The host's schedule: a group per distance, measured, then fired at.
        if (_zr == 1)
        {
            var have = g.Players.Values.Count(p => p.Alive);
            if (_zrPhase < 0)
            {
                if (have < 2 || now - _zrReady < 8)
                    return;
                ZrNextPhase(g, me, now);
            }
            else if (now - _zrPhaseAt > ZrPhaseSeconds)
            {
                if (_zrQuitAt < 0)
                    ZrPhaseSummary(g, me);
                if (_zrPhase + 1 >= _zrDists.Length)
                {
                    if (_zrQuitAt < 0)
                    {
                        g.SandboxClearSpawns(me); // (Clear all: the client's picture must empty too)
                        _zrQuitAt = now + 6;
                    }
                    else if (now > _zrQuitAt)
                    {
                        ZrQuit(null);
                        return;
                    }
                    _zrPhaseAt = now - ZrPhaseSeconds - 1; // (keeps coming back here)
                    return;
                }
                ZrNextPhase(g, me, now);
            }
        }
        var t = _zr == 1 ? now - _zrPhaseAt : now - _zrReady;
        var measuring = _zr == 2 || t is > 3 and < 8;
        if (measuring)
        {
            _zrDrawnSum += drawn;
            _zrIndSum += aliveInd;
            _zrSamples++;
        }
        if (now - _zrLastLog >= 1.0)
        {
            _zrLastLog = now;
            var picture = "";
            if (_zr == 2)
            {
                var p = g.CrowdSeen;
                float px = 0, pz = 0;
                for (var c = 0; c < p.Count.Length; c++)
                    if (p.Count[c] > 0)
                    {
                        px += (p.MinX + (c % p.Cols + 0.5f) * p.Cell) * p.Count[c];
                        pz += (p.MinZ + (c / p.Cols + 0.5f) * p.Cell) * p.Count[c];
                    }
                picture = p.Total > 0 ? $", picture {p.Total} round ({px / p.Total:0},{pz / p.Total:0})" : ", picture empty";
            }
            else
            {
                var c = g.Crowd;
                float px = 0, pz = 0;
                for (var i = 0; i < c.Count; i++)
                {
                    px += c.X[i];
                    pz += c.Z[i];
                }
                picture = $", server crowd {c.Count} particles{(c.Count > 0 ? $" round ({px / c.Count:0},{pz / c.Count:0})" : "")} + {c.MassTotal} mass, individuals {g.Horde.Count(z => z.Alive)}; client's picture off in {g.CrowdStale().Cells} cells by {g.CrowdStale().Zombies} of {g.CrowdStale().Total} (oldest {g.CrowdStale().OldestSends} sends), ZCrowd {(g.ZCrowdPacketsSent > 0 ? g.ZCrowdBytesSent / g.ZCrowdPacketsSent : 0)} B/send";
            }
            if (_zr == 1 && g.Crowd.MassTotal > 0)
            {
                // A perfect shot at a mass zombie: how far the shot reaches and whether the mass's own test meets it.
                var c = g.Crowd;
                var cell = Array.FindIndex(c.MassCount, m => m > 0);
                var tgt = c.MassFeet(cell, 0) + new Vector3(0, 1.45f, 0);
                var dd = Vector3.Normalize(tgt - eye);
                var ground = World.Query.Raycast(eye, dd, 1200f, QueryFilter.Shots);
                var far = g.CrowdShotFar(eye, dd, World.Range, World.Range, g.Active.Def, 0);
                var rd = far;
                var hitM = c.MassRayHit(eye, dd, ref rd, -1f, out var hc, out var hs, out _);
                Console.WriteLine($"[zrange] spread {g.CurrentSpread():0.0000} probe ray to mass cell {cell} at {Vector3.Distance(eye, tgt):0} m: ground at {ground:0} m, shot reaches {far:0} m, mass ray hit {hitM} ({hc}/{hs}) at {rd:0} m; stop at {eye + dd * far} ground there {World.Map.GroundHeight((eye + dd * far).X, (eye + dd * far).Z):0.00}; construct {g.Constructs.Raycast(eye + dd * 200f, dd, 1000f, out var chh)} {chh.Distance + 200f:0}");
            }
            Console.WriteLine($"[zrange] {(_zr == 1 ? "host" : "client")} t {now - _zrStart:0}: drawn crowd {drawn} ({Program.CrowdDrawnNear}/{Program.CrowdDrawnMid}/{Program.CrowdDrawnFar}, {Program.CrowdDrawnCulled} culled) "
                              + $"middle {(drawn > 0 ? $"({sx / drawn:0},{sz / drawn:0})" : "-")}, individuals {aliveInd}, aim {(aim is { } a2 ? $"({a2.X:0},{a2.Z:0}) {aimDist:0} m" : "none")}{picture}, shots {_zrShots}; eye ({eye.X:0},{eye.Y:0.0},{eye.Z:0}) yaw {g.LookYaw:0.00}, cam ({CrowdCam.X:0},{CrowdCam.Y:0.0},{CrowdCam.Z:0}) pitch {g.LookPitch:0.00}, cells {Crowd.Agents.CellsDrawn} drawn {Crowd.Agents.CellsCulled} out {Crowd.Agents.CellsFogged} fogged {Crowd.Agents.CellsHidden} hidden");
        }
        // Fire at what is drawn (4 s in on the host's schedule; the client whenever it draws a crowd).
        var w = g.Active;
        w.Ammo = Math.Max(w.Ammo, w.Def.Mag);
        if (aim is { } target && (_zr == 2 || t > 8))
        {
            var dir = target - eye;
            var len = dir.Length();
            g.LookYaw = MathF.Atan2(dir.X, -dir.Z);
            g.LookPitch = MathF.Asin(Math.Clamp(dir.Y / MathF.Max(len, 1e-3f), -1f, 1f));
            _zrPress = !_zrPress;
            var before = g.LastShotTime;
            g.UpdateTrigger(now, true, _zrPress);
            if (g.LastShotTime != before)
                _zrShots++;
        }
        else
        {
            g.LookYaw = ZrYaw;
            g.LookPitch = 0.02f;
        }
    }

    private static void ZrNextPhase(Game g, Player me, double now)
    {
        _zrPhase++;
        _zrPhaseAt = now;
        _zrDrawnSum = _zrIndSum = _zrSamples = 0;
        _zrShots = 0;
        g.SandboxClearSpawns(me);
        var client = g.Players.Values.FirstOrDefault(p => p.Id != me.Id);
        _zrHits0Host = g.CrowdHitsBy[me.Id];
        _zrZ0Host = g.ZHitsBy[me.Id];
        _zrHits0Client = client != null ? g.CrowdHitsBy[client.Id] : 0;
        _zrZ0Client = client != null ? g.ZHitsBy[client.Id] : 0;
        var d = _zrDists[_zrPhase];
        var at = ZrCorner() + ZrDir * d;
        at.Y = World.Map.GroundHeight(at.X, at.Z);
        var home = me.Position;
        if (d > 140)
            me.Teleport(new Vector3(at.X, World.Map.GroundHeight(at.X, at.Z) + 0.05f, at.Z) - ZrDir * 120f); // (a spawn is refused past SpawnReach; the next frame puts the host back)
        var ask = g.SandboxSpawnRequest(me, SbSpawnKind.Zombies, BotDifficulty.Normal, SbLoadKind.Any, 0, 0, _zrSize, at);
        me.Teleport(home);
        var gd = g.Active.Def;
        Console.WriteLine($"[zrange] gun {gd.Name}: arc speed {PowderCharge.HandSpeed(gd):0} m/s, range {gd.RangeM} m (min mult {gd.MinDamageMult}), reach {gd.Reach}, built {gd.BuildText != null}");
        Console.WriteLine($"[zrange] host phase {_zrPhase}: {_zrSize} zombies at {d} m ({at.X:0},{at.Z:0}): {ask ?? "ok"}");
    }

    private static void ZrPhaseSummary(Game g, Player me)
    {
        var client = g.Players.Values.FirstOrDefault(p => p.Id != me.Id);
        var d = _zrDists[_zrPhase];
        Console.WriteLine($"[zrange] RESULT {d} m: host drew {(_zrSamples > 0 ? _zrDrawnSum / (double)_zrSamples : 0):0} crowd + {(_zrSamples > 0 ? _zrIndSum / (double)_zrSamples : 0):0} individuals (mean, 3-8 s); "
                          + $"server hits by host: crowd {g.CrowdHitsBy[me.Id] - _zrHits0Host}, individuals {g.ZHitsBy[me.Id] - _zrZ0Host} ({_zrShots} shots); "
                          + $"by client: crowd {(client != null ? g.CrowdHitsBy[client.Id] - _zrHits0Client : 0)}, individuals {(client != null ? g.ZHitsBy[client.Id] - _zrZ0Client : 0)}");
    }

    /// <summary>--zombies: log what each peer has and draws once a second, a picture at 15 s and 30 s, the host quits at 36 s.</summary>
    private static void ZrZombiesFrame(Game g, Player me, double now)
    {
        var t = now - _zrReady;
        // Look at the middle of the crowd as this peer knows it (the host's counts, the client's picture).
        var ccx = 0.0;
        var ccz = 0.0;
        long cn = 0;
        if (_zr == 1)
        {
            var cr = g.Crowd;
            for (var i = 0; i < cr.Count; i++)
            {
                ccx += cr.X[i];
                ccz += cr.Z[i];
                cn++;
            }
            for (var c = 0; c < cr.MassCount.Length; c++)
                if (cr.MassCount[c] > 0)
                {
                    var f = cr.MassFeet(c, 0);
                    ccx += f.X * cr.MassCount[c];
                    ccz += f.Z * cr.MassCount[c];
                    cn += cr.MassCount[c];
                }
        }
        else
        {
            var pc = g.CrowdSeen;
            for (var c = 0; c < pc.Count.Length; c++)
                if (pc.Count[c] > 0)
                {
                    ccx += (pc.MinX + (c % pc.Cols + 0.5f) * pc.Cell) * pc.Count[c];
                    ccz += (pc.MinZ + (c / pc.Cols + 0.5f) * pc.Cell) * pc.Count[c];
                    cn += pc.Count[c];
                }
        }
        if (cn > 0)
        {
            var dx = (float)(ccx / cn) - me.RenderEye.X;
            var dz = (float)(ccz / cn) - me.RenderEye.Z;
            g.LookYaw = MathF.Atan2(dx, -dz);
            var gy = World.Map.GroundHeight((float)(ccx / cn), (float)(ccz / cn));
            g.LookPitch = MathF.Atan2(gy - me.RenderEye.Y, MathF.Sqrt(dx * dx + dz * dz));
        }
        if (now - _zrLastLog >= 1.0)
        {
            _zrLastLog = now;
            var c = g.Crowd;
            var p = g.CrowdSeen;
            Console.WriteLine($"[zrange] {(_zr == 1 ? "host" : "client")} t {t:0}: phase {g.Phase}, round {g.ZRound}, crowd on {g.CrowdOn}, server particles {c.Count} mass {c.MassTotal}, picture {p.Total}, "
                              + $"individuals {g.Horde.Count(z => z.Alive)}; drawn {Crowd.Inst.Total} ({Program.CrowdDrawnNear}/{Program.CrowdDrawnMid}/{Program.CrowdDrawnFar}, {Program.CrowdDrawnCulled} culled), "
                              + $"agents {Crowd.Agents.Count}, cells {Crowd.Agents.CellsDrawn} drawn {Crowd.Agents.CellsCulled} out {Crowd.Agents.CellsFogged} fogged {Crowd.Agents.CellsHidden} hidden; eye ({me.RenderEye.X:0},{me.RenderEye.Y:0.0},{me.RenderEye.Z:0})");
        }
        if (_zrShot == 0 && t > 15 || _zrShot == 1 && t > 30)
        {
            _zrShot++;
            _shotFull = true;
            SmokeShot($"zrange_{(_zr == 1 ? "host" : "client")}_{_zrShot}");
        }
        if (_zr == 1 && t > 36)
            ZrQuit(null);
    }

    private static void ZrQuit(string? why)
    {
        Console.WriteLine(why != null ? $"[zrange] FAILED: {why}" : "[zrange] DONE");
        _quit = true;
    }
}
