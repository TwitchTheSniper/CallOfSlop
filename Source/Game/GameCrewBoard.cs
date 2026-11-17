using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Boarding (docs/sandbox/design/bot-brain.md section 8, phase 5), server only: what a crew does when hostile players come aboard. The captain's
/// look (once a second while bodied) counts the boarders (a player riding the body or sitting in it), makes each one a foe (a deed), and weighs
/// the faction's boarding acts against its state: boarders against defenders, morale, hull, whether the helm is manned. Hold keeps the stations
/// (the pilot flies, the gunners fire outside); Repel stands the spares up from their couches to fight with their bodies' own bot brains; Flee runs
/// at full ahead; Call for help sends the same faction's crews within 3 km at the boarder; Abandon leaves the ship a derelict; Surrender leaves
/// every station, hands up (the ship stops: rule 1); Scuttle sends the nearest engineer (only an engineer: kill them first and it can't be done) to a magazine or fuel tank to arm a charge
/// (15 s), then counts down (90 s) and the point goes up as it would shot through. A player disarms it at the point with the attach tool's Fire
/// (5 s nearby), or destroys the point first (it burns then, no blast); kill the one arming it and it stops. A ship is taken once no crew aboard
/// is left who isn't dead or surrendered: it is the boarder's, its surrendered crew their prisoners (release, put off when landed, hire, or held).
/// Notices tell everyone aboard or within 200 m. Lock down and Dump weigh nothing yet (no locks, no cargo dump).
/// </summary>
public sealed partial class Game
{
    /// <summary>Within this of a scuttling ship (m) the countdown is heard; a disarm is worked from this close to the point (m).</summary>
    public const float ScuttleHeard = 200, DisarmReach = 4;

    /// <summary>A crewed body's helpers come from this far (m) when called.</summary>
    public const float HelpRange = 3000;

    bool ICrewHost.Board(Construct c, CrewShip s)
    {
        if (s.Prize)
            return true;
        if (s.Own)
            return false; // (a player's own crew: their faction aboard is welcome; a hostile boarder is a foe the guns can't see, phase 6 leaves it)
        var def = CrewFactions.Of(s.Faction);
        byte first = 255;
        var boarders = 0;
        foreach (var p in Players.Values)
        {
            if (!p.Alive || p.CrewSlot >= 0 || !Aboard(p, c.Id))
                continue;
            if (!BoardFoe(c, s, p))
                continue; // (only those the crew's rules call hostile: never the owner, the owner's faction or a hired crew's employer)
            boarders++;
            if (first == 255)
                first = p.Id;
            if (!s.Foe(p.Id))
            {
                s.MarkFoe(p.Id);
                Console.WriteLine($"[crew] construct {c.Id}: player {p.Id} boarded it{(def != null ? $" ({def.Name})" : "")}");
                if (def != null)
                    ((ICrewHost)this).Deed(s.Faction, p.Id, -10);
            }
        }
        if (s.ScStage > 0)
            ScuttleStep(c, s, def);
        if (boarders == 0)
        {
            if (s.Boarded)
            {
                s.Boarded = false;
                for (var i = 0; i < s.MemberCount; i++)
                    if (Crew.State[s.Members[i]] == CrewState.Fighting)
                        Crew.State[s.Members[i]] = CrewState.Free; // (back to their stations: the next look's fill)
                Console.WriteLine($"[crew] construct {c.Id}: no boarders left aboard");
            }
            return s.ScStage > 0;
        }
        s.Boarded = true;
        var defenders = 0;
        var fighters = 0;
        for (var i = 0; i < s.MemberCount; i++)
        {
            var st = Crew.State[s.Members[i]];
            if (st is CrewState.Dead or CrewState.Surrendered)
                continue;
            defenders++;
            if (Crew.Player[s.Members[i]] != 0)
                fighters++;
        }
        if (defenders == 0)
        {
            if (c.Owner == 255)
                CrewCapture(c, s, first); // (a player's ship is never taken by killing its crew: it stays its owner's)
            return true;
        }
        if (def == null || s.Act is BoardAct.Surrender or BoardAct.Abandon || s.Act == BoardAct.Scuttle && s.ScStage > 0)
            return true; // (a terminal act holds; a crew with no faction holds its stations)
        // The acts, weighed.
        var ratio = boarders / (float)Math.Max(1, defenders);
        s.Morale = Math.Clamp(s.Morale - 0.15f * ratio, 0, 1);
        var desperation = ratio * 0.5f + (1 - s.Morale) + (1 - s.Hull) * 0.5f;
        var helm = s.HelmOn(Crew);
        Span<float> score = stackalloc float[9];
        score[(int)BoardAct.Hold] = def.Board[(int)BoardAct.Hold] * (helm ? 1f : 0.2f);
        score[(int)BoardAct.Repel] = def.Board[(int)BoardAct.Repel] * Math.Clamp(1.2f / ratio, 0, 2) * (fighters > 0 ? 1f : 0.2f);
        score[(int)BoardAct.Flee] = def.Board[(int)BoardAct.Flee] * (helm && c.StabCount + c.WheelCount > 0 ? 0.8f : 0);
        score[(int)BoardAct.Abandon] = def.Board[(int)BoardAct.Abandon] * (s.Morale < 0.25f ? 1.5f : 0.05f);
        score[(int)BoardAct.Surrender] = def.Board[(int)BoardAct.Surrender] * (s.Morale < def.SurrenderMorale ? 2.5f : 0);
        score[(int)BoardAct.Scuttle] = def.Scuttle == ScuttleWhen.Never ? 0
            : def.Board[(int)BoardAct.Scuttle] * (def.Scuttle == ScuttleWhen.Always || desperation > def.ScuttleDesperation ? 2.2f : 0) * (ScuttlePoint(c, s, def) >= 0 && HasEngineer(s) ? 1 : 0);
        var best = BoardAct.Hold;
        for (var k = 0; k < score.Length; k++)
            if (score[k] > score[(int)best])
                best = (BoardAct)k;
        if (!s.CalledHelp && def.Board[(int)BoardAct.CallForHelp] > 0)
            CrewCallHelp(c, s, first);
        if (best != s.Act)
            Console.WriteLine($"[crew] construct {c.Id}: boarded by {boarders} ({defenders} crew left, morale {s.Morale * 100:0}%, desperation {desperation:0.00}): {best}");
        var was = s.Act;
        s.Act = best;
        switch (best)
        {
            case BoardAct.Repel:
                for (var i = 0; i < s.MemberCount; i++)
                {
                    var m = s.Members[i];
                    if (Crew.Player[m] == 0 || Crew.State[m] != CrewState.Station || s.StKind[Crew.Station[m]] != CrewStationKind.Rest
                        || !Players.TryGetValue((byte)(Crew.Player[m] - 1), out var bp))
                        continue;
                    Crew.Leave(m, CrewState.Fighting);
                    SandboxStand(bp, teleport: true); // (up from the couch: its bot brain fights, CrewBodyFights)
                }
                break;
            case BoardAct.Flee:
                s.Goal = CrewGoal.Flee;
                s.Contact = first;
                break;
            case BoardAct.Surrender:
                for (var i = 0; i < s.MemberCount; i++)
                    if (Crew.State[s.Members[i]] is not (CrewState.Dead or CrewState.Surrendered))
                        CrewSurrender(s.Members[i]);
                CrewNotice(c, $"The crew surrendered: hands up. Shooting them now costs standing with the {def.Name}.");
                break;
            case BoardAct.Abandon:
                CrewNotice(c, "The crew abandons ship: it is a derelict now.");
                Crew.Disband(s, Constructs, this, "abandoned ship");
                c.NpcFaction = 0;
                break;
            case BoardAct.Scuttle when was != BoardAct.Scuttle || s.ScStage == 0:
                ScuttleStart(c, s, def);
                break;
        }
        return true;
    }

    private bool HasEngineer(CrewShip s)
    {
        for (var i = 0; i < s.MemberCount; i++)
            if (Crew.Job[s.Members[i]] == CrewJob.Engineer && Crew.State[s.Members[i]] is not (CrewState.Dead or CrewState.Surrendered))
                return true;
        return false;
    }

    /// <summary>A player is aboard <paramref name="body"/>: riding its decks or sitting in it.</summary>
    private bool Aboard(Player p, ushort body) => p.Sim.RideBody == body || SandboxHands.Hand(p.Id).SeatBody == body;

    private void CrewSurrender(int m)
    {
        if (Crew.Player[m] != 0 && Players.TryGetValue((byte)(Crew.Player[m] - 1), out var bp))
            SandboxStand(bp, teleport: true);
        Crew.Leave(m, CrewState.Surrendered);
    }

    /// <summary>The same faction's awake crews within <see cref="HelpRange"/> turn on the boarder.</summary>
    private void CrewCallHelp(Construct c, CrewShip s, byte boarder)
    {
        s.CalledHelp = true;
        var n = 0;
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { Tier: not CrewTier.Asleep } o && o != s && o.Faction == s.Faction && !o.Prize && Constructs.Get(o.Body) is { } oc
                && Vector3.DistanceSquared(oc.WorldCom, c.WorldCom) < HelpRange * HelpRange)
            {
                o.MarkFoe(boarder);
                n++;
            }
        if (n > 0)
            Console.WriteLine($"[crew] construct {c.Id}: its crew called for help: {n} crews of the {CrewFactions.Of(s.Faction)?.Name} answer");
    }

    /// <summary>Taken: the ship is the boarder's, its surrendered crew their prisoners, the faction thinks the worse of them.</summary>
    /// <summary>
    /// Whether <paramref name="p"/> aboard counts as a boarder: someone the crew's faction rules call hostile (<see cref="Fights"/>: a foe or
    /// standing below 0, or the stabilizer's setting with no faction), and never the ship's owner or anyone in the owner's faction.
    /// </summary>
    private bool BoardFoe(Construct c, CrewShip s, Player p)
    {
        if (c.Owner != 255 && (p.Id == c.Owner || Factions.Shares(c.Owner, p.Id)))
            return false;
        return Fights(c, s, p);
    }

    private void CrewCapture(Construct c, CrewShip s, byte by)
    {
        var def = CrewFactions.Of(s.Faction);
        s.Prize = true;
        s.Act = BoardAct.Surrender;
        s.ScStage = 0;
        s.SeatsDirty = true; // (clients' look line: the charge)
        s.ScPart = -1;
        c.NpcFaction = 0;
        if (by != 255 && ConstructPlace.LimitRefusal(Constructs, by) is null)
        {
            c.Owner = by;
            Constructs.StructureDirty.Add(c.Id);
        }
        var prisoners = 0;
        for (var i = 0; i < s.MemberCount; i++)
            if (Crew.State[s.Members[i]] == CrewState.Surrendered)
                prisoners++;
        if (def != null && by != 255)
            ((ICrewHost)this).Deed(s.Faction, by, -20);
        CrewBounty(by, prisoners); // (credits: GameCrewHire.cs)
        s.RosterDirty = true;
        Console.WriteLine($"[crew] construct {c.Id}: taken by player {by}{(c.Owner == by ? "" : " (who can't own another: it stays nobody's)")}, {prisoners} prisoners");
        CrewNotice(c, prisoners > 0
            ? $"You took the ship. Its {prisoners} crew are your prisoners: release them, put them off once landed, hire them or hold them (Crew window)."
            : "You took the ship.");
    }

    /// <summary>A line to everyone aboard or within <see cref="ScuttleHeard"/> m.</summary>
    private void CrewNotice(Construct c, string text)
    {
        var at = c.WorldCom;
        foreach (var p in Players.Values)
            if (p.CrewSlot < 0 && p.Bot == null && (Aboard(p, c.Id) || Vector3.DistanceSquared(p.Position, at) < ScuttleHeard * ScuttleHeard))
                SandboxHands.Notices.Add((p.Id, text));
    }

    // ---- Scuttling --------------------------------------------------------------------------------------------------------------------

    /// <summary>The part a crew would scuttle at: the faction's first kind of point that has a part still whole (a magazine holding rounds, a fuel tank), not the one just disarmed. -1: none.</summary>
    private static int ScuttlePoint(Construct c, CrewShip s, NpcFactionDef def)
    {
        foreach (var kind in def.ScuttlePoints)
            for (var i = 0; i < c.PartCount; i++)
            {
                ref var p = ref c.Parts[i];
                if (p.Health <= 0 || p.Broken || p.Tag == s.ScTried && s.ScTried != 0)
                    continue;
                var d = p.D;
                var fits = kind switch
                {
                    "magazine" => d.Role == PartRole.AmmoBox && p.Rounds > 0 || d.AmmoStore,
                    "fuel" => d.Role == PartRole.FuelTank || d.IsTankVolume,
                    "reactor" => d.Role == PartRole.Battery && d.BatteryCapacity > 50000,
                    _ => false,
                };
                if (fits)
                    return i;
            }
        return -1;
    }

    private static string PointName(Construct c, int part) => part >= 0 && part < c.PartCount ? c.DefOf(part).Name : "?";

    private void ScuttleStart(Construct c, CrewShip s, NpcFactionDef def)
    {
        var point = ScuttlePoint(c, s, def);
        if (point < 0)
            return;
        // Who goes: the nearest living engineer, else anyone who isn't surrendered.
        var at = ConstructRender.PartCentre(c, point);
        int who = -1;
        var bestD = float.MaxValue;
        for (var i = 0; i < s.MemberCount; i++)
            {
                var m = s.Members[i];
                if (Crew.State[m] is CrewState.Dead or CrewState.Surrendered || Crew.Job[m] != CrewJob.Engineer)
                    continue; // (only an engineer scuttles: kill them first and it can't be done, the user's answer 7)
                var from = Crew.Station[m] >= 0 && Crew.Station[m] < s.StCount ? ConstructRender.PartCentre(c, s.StPart[Crew.Station[m]]) : c.WorldCom;
                var d = Vector3.Distance(from, at);
                if (d < bestD)
                    (who, bestD) = (m, d);
            }
        if (who < 0)
            return;
        if (Crew.Player[who] != 0 && Players.TryGetValue((byte)(Crew.Player[who] - 1), out var bp))
            SandboxStand(bp, teleport: true);
        Crew.Leave(who, CrewState.Scuttling);
        s.ScPart = (short)point;
        s.ScTag = c.Parts[point].Tag;
        s.ScMember = who;
        s.ScStage = 1;
        s.SeatsDirty = true; // (clients' look line: the charge)
        s.ScAt = Crew.Tick + Math.Max(World.TickRate, (long)(bestD * 1.4f / CrewWorld.WalkSpeed * World.TickRate));
        s.ScDisarmBy = 255;
        Console.WriteLine($"[crew] construct {c.Id}: crew member {Crew.Uid[who]} ({Crew.Job[who]}) goes to scuttle it at the {PointName(c, point)} ({bestD:0} m)");
    }

    /// <summary>The scuttle's stages, once a captain's look: the walk, the arming (the one arming must live), the countdown, the blast.</summary>
    private void ScuttleStep(Construct c, CrewShip s, NpcFactionDef? def)
    {
        CrewWorld.ScuttleFollow(c, s); // (a rebuild or split since the last look: the charge stays on its own part)
        int point = s.ScPart;
        if (point < 0 || point >= c.PartCount || c.Parts[point].Health <= 0)
        {
            Console.WriteLine($"[crew] construct {c.Id}: the scuttle point is gone: no charge");
            if (s.ScStage == 3)
                CrewNotice(c, "The scuttle charge's point was destroyed: it burns, it won't blow.");
            ScuttleEnd(s, keepTrying: true);
            return;
        }
        var tick = Crew.Tick;
        if (s.ScStage is 1 or 2 && (s.ScMember < 0 || Crew.State[s.ScMember] != CrewState.Scuttling))
        {
            Console.WriteLine($"[crew] construct {c.Id}: the one {(s.ScStage == 1 ? "going to scuttle" : "arming the charge")} is down: no charge");
            if (s.ScStage == 2)
                CrewNotice(c, "The scuttle charge was never armed: its engineer is down.");
            ScuttleEnd(s, keepTrying: true);
            return;
        }
        if (s.ScDisarmBy != 255)
        {
            // A disarm under way: its player must stay alive and by the point.
            if (!Players.TryGetValue(s.ScDisarmBy, out var dp) || !dp.Alive || Vector3.Distance(dp.Eye, ConstructRender.PartCentre(c, point)) > DisarmReach + 1)
            {
                Console.WriteLine($"[crew] construct {c.Id}: player {s.ScDisarmBy}'s disarm stopped (left the charge)");
                CrewNotice(c, "The disarm stopped: stay at the charge.");
                s.ScDisarmBy = 255;
            }
            else if (tick >= s.ScDisarmAt)
            {
                Console.WriteLine($"[crew] construct {c.Id}: player {s.ScDisarmBy} disarmed the scuttle charge at the {PointName(c, point)}");
                CrewNotice(c, $"Scuttle charge disarmed: {PointName(c, point)}.");
                s.ScTried = c.Parts[point].Tag;
                ScuttleEnd(s, keepTrying: true);
                return;
            }
        }
        if (tick < s.ScAt)
        {
            if (s.ScStage == 3 && tick >= s.ScSaidAt)
            {
                var left = (s.ScAt - tick) / World.TickRate;
                CrewNotice(c, $"Scuttle charge: {left} s ({PointName(c, point)}). Disarm it there with the attach tool.");
                s.ScSaidAt = tick + (left > 30 ? 30 : 10) * World.TickRate;
            }
            return;
        }
        switch (s.ScStage)
        {
            case 1:
                s.ScStage = 2;
                s.SeatsDirty = true; // (clients' look line: the charge)
                s.ScAt = tick + (long)((def?.ScuttleArm ?? 15) * World.TickRate);
                Console.WriteLine($"[crew] construct {c.Id}: arming a scuttle charge at the {PointName(c, point)}");
                CrewNotice(c, $"Scuttle charge arming: {PointName(c, point)}. Kill the one arming it, or get there.");
                break;
            case 2:
                s.ScStage = 3;
                s.SeatsDirty = true; // (clients' look line: the charge)
                s.ScAt = tick + (long)((def?.ScuttleCountdown ?? 90) * World.TickRate);
                s.ScSaidAt = tick;
                Crew.State[s.ScMember] = CrewState.Free; // (armed: they go back)
                Console.WriteLine($"[crew] construct {c.Id}: the scuttle charge at the {PointName(c, point)} is armed: {def?.ScuttleCountdown ?? 90:0} s");
                break;
            case 3:
                Console.WriteLine($"[crew] construct {c.Id}: the scuttle charge at the {PointName(c, point)} went off");
                CrewNotice(c, "The scuttle charge went off.");
                ScuttleBlow(c, point);
                ScuttleEnd(s, keepTrying: false);
                break;
        }
    }

    private void ScuttleEnd(CrewShip s, bool keepTrying)
    {
        if (s.ScMember >= 0 && Crew.State[s.ScMember] == CrewState.Scuttling)
            Crew.State[s.ScMember] = CrewState.Free;
        s.ScStage = 0;
        s.SeatsDirty = true; // (clients' look line: the charge)
        s.ScPart = -1;
        s.ScMember = -1;
        s.ScDisarmBy = 255;
        if (keepTrying)
            s.Act = BoardAct.Hold; // (the next look weighs again: another point, if there is one)
    }

    /// <summary>The point goes up as it would shot through: a cook-off or a fuel blast, else a blast of its own.</summary>
    private void ScuttleBlow(Construct c, int part)
    {
        var at = ConstructRender.PartCentre(c, part);
        if (ConstructWorld.MayCookOff(c.DefOf(part)))
            Constructs.DamagePart(c, part, c.Parts[part].Health + 1e5f, out _, raw: true);
        else if (Constructs.CookOffCount < Constructs.CookOffs.Length)
            Constructs.CookOffs[Constructs.CookOffCount++] = new CCookOff { At = at, Radius = 8, Damage = 900, Ticks = 1, Body = c.Id };
    }

    /// <summary>
    /// Server: the attach tool's Fire (a Mend act) at a scuttle point with a charge on it: the disarm starts (5 s, nearby). Null: not a charge's
    /// point (the act goes on as a mend); "" when it started; else why not.
    /// </summary>
    private string? CrewDisarm(Player p, Construct c, int part)
    {
        if (Crew.ShipOf(c.Id) is not { ScStage: >= 2 } s || s.ScPart != part)
            return null;
        if (Vector3.Distance(p.Eye, ConstructRender.PartCentre(c, part)) > DisarmReach)
            return "get right up to the charge";
        if (s.ScDisarmBy == p.Id)
            return "";
        s.ScDisarmBy = p.Id;
        s.ScDisarmAt = Crew.Tick + (long)((CrewFactions.Of(s.Faction)?.ScuttleDisarm ?? 5) * World.TickRate);
        SandboxHands.Notices.Add((p.Id, "Disarming: stay at the charge."));
        return "";
    }

    /// <summary>A crew body repelling boarders fights by its own bot brain (Game.cs's tick).</summary>
    private bool CrewBodyFights(Player p) => p.CrewSlot >= 0 && p.CrewSlot < Crew.High && Crew.State[p.CrewSlot] == CrewState.Fighting;

    // ---- Prisoners ----------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server: what <paramref name="p"/> does with a prisoner (a surrendered crew member on a ship they own): "release" (they go; a little standing
    /// back), "drop" (put off: the ship must be landed or still), "hold" (kept aboard). Hiring is GameCrewHire.cs's. Null: done; else why not.
    /// </summary>
    public string? CrewPrisoner(Player p, ushort body, uint uid, string op)
    {
        if (Constructs.Get(body) is not { } c || Crew.ShipOf(body) is not { } s)
            return "no crew aboard that";
        if (c.Owner != p.Id && !Factions.Shares(c.Owner, p.Id))
            return "not your ship";
        var m = CrewByUid(s, uid);
        if (m < 0 || Crew.State[m] != CrewState.Surrendered)
            return "no such prisoner";
        var faction = Crew.Fac[m];
        s.RosterDirty = true;
        switch (op)
        {
            case "release":
                Crew.Release(m, this);
                ((ICrewHost)this).Deed(faction, p.Id, 5);
                Console.WriteLine($"[crew] construct {body}: player {p.Id} released prisoner {uid}");
                return null;
            case "drop":
                if (c.Vel.Length() > 1)
                    return "land or stop the ship first";
                Crew.Release(m, this);
                ((ICrewHost)this).Deed(faction, p.Id, 3);
                Console.WriteLine($"[crew] construct {body}: player {p.Id} put prisoner {uid} off");
                return null;
            case "hold":
                return null; // (already held)
            default:
                return $"no prisoner order \"{op}\"";
        }
    }

    private int CrewByUid(CrewShip s, uint uid)
    {
        for (var i = 0; i < s.MemberCount; i++)
            if (Crew.Uid[s.Members[i]] == uid)
                return s.Members[i];
        return -1;
    }

    // ---- Tools (allocate) -------------------------------------------------------------------------------------------------------------

    public string CrewSetFaction(string name)
    {
        var f = CrewFactions.Find(name);
        if (f == 0)
            return $"no faction {name}";
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { } s && Constructs.Get(s.Body) is { } c)
            {
                CrewWorld.SetFaction(c, s, f);
                for (var k = 0; k < s.MemberCount; k++)
                    Crew.Fac[s.Members[k]] = f;
                s.SeatsDirty = true;
                return $"construct {c.Id}: {name}, morale {s.Morale * 100:0}%";
            }
        return "no crewed body";
    }

    public string CrewDisarmFirst(Player me)
    {
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { ScStage: >= 2 } s && Constructs.Get(s.Body) is { } c)
            {
                var at = ConstructRender.PartCentre(c, s.ScPart);
                if (SandboxHands.Hand(me.Id).Seated)
                    SandboxStand(me, teleport: true);
                me.Teleport(at + GravityFrames.UpAt(at) * 0.3f);
                return $"{CrewDisarm(me, c, s.ScPart) ?? "not a charge"} (at {Vector3.Distance(me.Eye, at):0.0} m)";
            }
        return "no scuttle charge armed or arming";
    }

    public string CrewPrisonerFirst(Player me, string op)
    {
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { } s)
                for (var k = 0; k < s.MemberCount; k++)
                    if (Crew.State[s.Members[k]] == CrewState.Surrendered)
                        return (op == "hire" ? CrewHire(me, s.Body, Crew.Uid[s.Members[k]]) : CrewPrisoner(me, s.Body, Crew.Uid[s.Members[k]], op)) ?? $"done: {Crew.Describe(s)}";
        return "no prisoner";
    }

}
