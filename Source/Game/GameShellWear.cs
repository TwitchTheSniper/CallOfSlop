using System.Numerics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>Wear not yet a whole step, per work (a worn shell's, server only).</summary>
[InlineArray(ShellWear.Works)]
public struct ShellWearAcc
{
    private float _e;
}

/// <summary>
/// Robot shell wear in the game (Karl, 2026-11-17; the numbers are Constructs/ShellWear.json, the rules Source/Constructs/ShellWear.cs;
/// docs/sandbox/constructs/joints-and-logic.md "Robot shell wear"). Mild: in Survival a shell's eight works wear while someone wears it (legs while it walks,
/// arms while a tool is out, the rest all the time) and hits wear its plating and one other work. A work below its worn mark shows:
///  - a leg actuator limps (the leg drags, 80 % pace); failed, the leg is lost and the shell hops on the other (50 %);
///  - an arm actuator shakes: the attach tool's fitting takes longer (up to 1.35x; failed, the arm hangs: 1.6x, both 2.2x);
///  - the power cell is sluggish (down to 80 % pace, flat 65 %) and browns out now and then: the view dims with a falling whine;
///  - the processor glitches now and then: the view tears for a moment with a short digital chirp;
///  - the optics show static and scan lines, faint at first; failed, near blind;
///  - the plating lets hits through harder (up to 1.4x).
/// Every glitch is short and well spaced: a hint that something wants seeing to, never a nag. Fixing it, on a standing shell: the
/// attach tool's Fire fits a spare part from the inventory (the worst work first: it is new again) or, with no spare, services a worn
/// work back to 70 % for a little metal; a failed work needs its part. An NPC crew's engineer on a ship (a hired crew's too) services
/// the shells standing aboard and anyone wearing one aboard, free (the crew's own spares, as with its drives). Server state is the
/// wearer's run (<see cref="ShellRun.Acc"/>) and <see cref="Player.ShellWear"/>, which every peer hears with <see cref="Msg.Shell"/>
/// when a work's condition moves a 5 % step; a standing shell keeps it in its part's hidden settings.
/// </summary>
public sealed partial class Game
{
    private const int SwStep = 15; // ticks between wear looks (4 a second)

    private readonly Random _swRng = new(7193);

    // ---- Server: wear --------------------------------------------------------------------------------------------------

    /// <summary>Server, each tick (after <see cref="ShellTick"/>): the worn shells wear, their wearers' fitting speed follows the arms.</summary>
    private void ShellWearTick()
    {
        if (ServerTick % SwStep != 0)
            return;
        var t = ShellWear.Current;
        var on = SandboxSurvivalOption && t.Problem.Length == 0;
        foreach (var p in Players.Values)
        {
            ref var run = ref _sh[p.Id];
            if (!run.On)
            {
                SandboxHands.ShellFitSlow[p.Id] = 1;
                continue;
            }
            SandboxHands.ShellFitSlow[p.Id] = ShellFitShare(p.ShellWear);
            if (!on || !p.Alive)
                continue;
            var secs = SwStep / (float)World.TickRate;
            var v = p.Sim.Velocity;
            var walk = p.Sim.Grounded ? Math.Clamp(MathF.Sqrt(v.X * v.X + v.Z * v.Z) / World.WalkSpeed, 0f, 1.5f) : 0f;
            var tool = SandboxHands.Hand(p.Id).Tool;
            var arms = tool is ConstructTool.None or ConstructTool.Hands ? 0.3f : 1f;
            for (var k = 0; k < ShellWear.Works; k++)
            {
                var w = (ShellWork)k;
                var share = w is ShellWork.LegL or ShellWork.LegR ? walk : w is ShellWork.ArmL or ShellWork.ArmR ? arms : 1f;
                if (share > 0)
                    run.Acc[k] += share * secs * 255f / (t.HoursOf(w) * 3600f);
            }
            ShellWearApply(p, ref run, t);
        }
        ShellStandScan();
    }

    /// <summary>Server: the whole steps of wear built up go onto the works; a work that turns worn or fails is said, and peers hear a 5 % step.</summary>
    private void ShellWearApply(Player p, ref ShellRun run, ShellWear t)
    {
        var send = false;
        for (var k = 0; k < ShellWear.Works; k++)
        {
            if (run.Acc[k] < 1)
                continue;
            var whole = (int)run.Acc[k];
            run.Acc[k] -= whole;
            var was = p.ShellWear[k];
            if (was >= ShellWear.Failed)
                continue;
            var now = (byte)Math.Min(ShellWear.Failed, was + whole);
            p.ShellWear[k] = now;
            if (ShellWear.Pct(now) / 5 != ShellWear.Pct(run.Sent[k]) / 5 || now >= ShellWear.Failed)
                send = true;
            var w = (ShellWork)k;
            if (now >= ShellWear.Failed)
                ShellWearSay(p, ref run, w, $"Your shell's {ShellWear.NameOf(w)} has failed ({t.Symptom(w, now)}): leave the shell and fit a new {ShellWear.SpareName(w)} with the attach tool, or have a crew engineer see to it.", true);
            else if (t.Worn(now) && !t.Worn(was))
                ShellWearSay(p, ref run, w, $"Your shell's {ShellWear.NameOf(w)} is worn ({t.Symptom(w, now)}). A service with the attach tool sees to it, on the standing shell.", false);
        }
        if (!send)
            return;
        run.Sent = p.ShellWear;
        ShellBroadcast(p);
    }

    private void ShellWearSay(Player p, ref ShellRun run, ShellWork w, string text, bool always)
    {
        var now = SandboxNow;
        if (!always && now - run.FaultAt < 60)
            return; // (one note a minute at most: wear is a hint, not a nag)
        run.FaultAt = now;
        SandboxHands.Notices.Add((p.Id, text));
        Log($"[shellwear] {p.Name}: {ShellWear.NameOf(w)} at {ShellWear.Pct(p.ShellWear[(int)w])} %");
    }

    /// <summary>The attach tool's fitting time for a shell with these arms: shaky arms take longer, a hanging one longer still.</summary>
    public static float ShellFitShare(in ShellCond c)
    {
        var t = ShellWear.Current;
        var l = c[(int)ShellWork.ArmL];
        var r = c[(int)ShellWork.ArmR];
        var lost = (l >= ShellWear.Failed ? 1 : 0) + (r >= ShellWear.Failed ? 1 : 0);
        return lost switch { 2 => 2.2f, 1 => 1.6f, _ => 1f + 0.35f * MathF.Max(t.Fault(l), t.Fault(r)) };
    }

    /// <summary>Both sides: how fast a player in a shell moves, as a share (1: not in a shell, or nothing wrong; GameShellWear.cs).</summary>
    public static float ShellMoveMult(Player? p) => p is { InShell: true } ? ShellWear.Current.MoveShare(p.ShellWear) * ShellFrames.Worn(p).Speed : 1f; // (and its frame's pace: Constructs/Shells.json)

    /// <summary>
    /// Server, from <see cref="DamagePlayer"/>: a hit on someone in a shell, by the part it landed on (Karl, 2026-11-18). An arm or a leg:
    /// that actuator takes it (failed: the arm hangs, the leg is lost) and only <see cref="ShellWear.LimbHealth"/> of it reaches the
    /// shell's health. The body: health (worn plating lets it through harder), and it wears the plating and the power cell. The head:
    /// health, and by chance (<see cref="ShellWear.HeadChance"/>) the processor or the optics. In Creative as well as Survival (it is
    /// combat, not wear). The damage to take.
    /// </summary>
    private int ShellHit(Player victim, int amount, BloodPart part)
    {
        ref var run = ref _sh[victim.Id];
        if (!run.On)
            return amount;
        if (victim.ShellFrame != 0 && amount > 0)
            amount = Math.Max(1, (int)MathF.Round(amount * ShellFrames.Worn(victim).Damage)); // (its frame's plating: the Mining Shell's x0.7)
        var t = ShellWear.Current;
        if (t.Problem.Length > 0)
            return amount;
        var limb = part switch
        {
            BloodPart.LegL => ShellWork.LegL,
            BloodPart.LegR => ShellWork.LegR,
            BloodPart.ArmL => ShellWork.ArmL,
            BloodPart.ArmR => ShellWork.ArmR,
            _ => (ShellWork?)null,
        };
        int hurt;
        if (limb is { } l)
        {
            run.Acc[(int)l] += amount * t.HitLimb * 2.55f;
            hurt = (int)MathF.Round(amount * t.LimbHealth);
        }
        else
        {
            hurt = Math.Max(1, (int)MathF.Round(amount * t.HitShare(victim.ShellWear)));
            run.Acc[(int)ShellWork.Plating] += amount * t.HitPlating * 2.55f;
            if (part == BloodPart.Head)
            {
                if (_swRng.NextSingle() < t.HeadChance)
                    run.Acc[(int)(_swRng.Next(2) == 0 ? ShellWork.Cpu : ShellWork.Optics)] += amount * t.HitHead * 2.55f;
            }
            else
                run.Acc[(int)ShellWork.Power] += amount * t.HitOther * 2.55f;
        }
        ShellWearApply(victim, ref run, t);
        return hurt;
    }

    // ---- Server: the attach tool on a standing shell ----------------------------------------------------------------------

    private static readonly ConstructPartDef?[] _spares = new ConstructPartDef?[ShellWear.Works];

    /// <summary>The spare part for a work (the part kit's ShellParts.cs; null: none loaded).</summary>
    public static ConstructPartDef? ShellSpare(ShellWork w) => _spares[(int)w] ??= ConstructParts.Find(ShellWear.SpareName(w));

    /// <summary>What the attach tool's Fire would do to a standing shell with this wear, for this player.</summary>
    private enum ShellFix : byte { Nothing, Fit, Service, NeedsPart, Repair }

    /// <summary>
    /// Both sides: what Fire does to a standing shell. Damage first (shells don't heal by themselves: a repair patches it back to full);
    /// then the most worn work past the service mark: a spare in hand or in the inventory fits a new one; else a worn (not failed)
    /// work is serviced; a failed one with no spare is said.
    /// </summary>
    private ShellFix ShellFixOf(byte player, in ShellCond c, int health, out ShellWork work)
    {
        var t = ShellWear.Current;
        var mark = ShellWear.WearOf(t.ServiceToPct);
        work = ShellWork.Plating;
        if (health < World.MaxHealth)
            return ShellFix.Repair;
        work = ShellWork.LegL;
        ShellWork? service = null, needs = null;
        Span<int> order = stackalloc int[ShellWear.Works];
        for (var k = 0; k < order.Length; k++)
            order[k] = k;
        for (var a = 0; a < order.Length; a++) // (most worn first)
            for (var b = a + 1; b < order.Length; b++)
                if (c[order[b]] > c[order[a]])
                    (order[a], order[b]) = (order[b], order[a]);
        foreach (var k in order)
        {
            if (c[k] <= mark)
                break;
            var w = (ShellWork)k;
            if (ShellHasSpare(player, w))
            {
                work = w;
                return ShellFix.Fit;
            }
            if (c[k] < ShellWear.Failed)
                service ??= w;
            else
                needs ??= w;
        }
        if (service is { } s)
        {
            work = s;
            return ShellFix.Service;
        }
        if (needs is { } n)
        {
            work = n;
            return ShellFix.NeedsPart;
        }
        return ShellFix.Nothing;
    }

    /// <summary>Is this part one of the shell spares?</summary>
    public static bool IsShellSpare(ushort part)
    {
        for (var k = 0; k < ShellWear.Works; k++)
            if (ShellSpare((ShellWork)k) is { } d && d.Id == part)
                return true;
        return false;
    }

    private bool ShellHasSpare(byte player, ShellWork w)
    {
        if (ShellSpare(w) is not { } d)
            return false;
        if (SandboxHands.InfiniteParts || SandboxHands.Hand(player).Held == d.Id)
            return true;
        return SandboxInv.Of(player).Count(d.Id) > 0;
    }

    /// <summary>Server: the attach tool's Fire at a standing Robot Shell (from <see cref="BreakdownMendAct"/>): null when it worked, else why not.</summary>
    private string? ShellServiceAct(Player p, Construct c, int part)
    {
        ref var sp = ref c.Parts[part];
        var d = sp.D;
        ShellCond wear = default;
        ShellWear.Read(sp, d, ref wear);
        var t = ShellWear.Current;
        var health = ShellWear.HealthOf(sp, d);
        switch (ShellFixOf(p.Id, wear, health, out var w))
        {
            case ShellFix.Repair:
                if (SandboxHands.MendPay(Constructs, p.Id, p.Eye, t.ServiceMaterial, t.ServiceCost) is { } owes)
                    return owes;
                ShellWear.PutHealth(ref sp, d, World.MaxHealth);
                ShellWearStore(c, part, wear);
                SandboxHands.Done.Add($"player {p.Id}: repaired the robot shell on construct {c.Id} ({health} to {World.MaxHealth})");
                SandboxHands.Notices.Add((p.Id, "Robot shell repaired: its damage is patched."));
                return null;
            case ShellFix.Fit:
            {
                var spare = ShellSpare(w)!;
                ref var h = ref SandboxHands.Hand(p.Id);
                if (!SandboxHands.InfiniteParts)
                {
                    if (h.Held == spare.Id)
                        h.Held = ConstructParts.None;
                    else if (!SandboxInv.Of(p.Id).Take(spare.Id))
                        return $"no {spare.Name} in your inventory";
                }
                wear[(int)w] = 0;
                ShellWearStore(c, part, wear);
                SandboxHands.Done.Add($"player {p.Id}: fitted a new {ShellWear.NameOf(w)} to the robot shell on construct {c.Id}");
                SandboxHands.Notices.Add((p.Id, $"New {ShellWear.NameOf(w)} fitted: good as new."));
                return null;
            }
            case ShellFix.Service:
                if (SandboxHands.MendPay(Constructs, p.Id, p.Eye, t.ServiceMaterial, t.ServiceCost) is { } why)
                    return why;
                wear[(int)w] = ShellWear.WearOf(t.ServiceToPct);
                ShellWearStore(c, part, wear);
                SandboxHands.Done.Add($"player {p.Id}: serviced the {ShellWear.NameOf(w)} of the robot shell on construct {c.Id}");
                SandboxHands.Notices.Add((p.Id, $"{char.ToUpperInvariant(ShellWear.NameOf(w)[0])}{ShellWear.NameOf(w)[1..]} serviced ({ShellWear.Pct(wear[(int)w])} %). Only a new {ShellWear.SpareName(w)} makes it like new."));
                return null;
            case ShellFix.NeedsPart:
                return $"its {ShellWear.NameOf(w)} has failed: fit a new {ShellWear.SpareName(w)} (none in your inventory)";
            default:
                return "the shell is in good order";
        }
    }

    /// <summary>
    /// Server: the first time a player wakes in a shell in Survival, a small kit of spare works goes into their inventory (two leg
    /// actuators, one of each other), so the first worn part can be put right before the production system makes more.
    /// </summary>
    private bool ShellSparesKit(Player p)
    {
        if (!SandboxSurvivalOption || SandboxHands.InfiniteParts)
            return false;
        var inv = SandboxInv.Of(p.Id);
        var given = false;
        for (var k = 0; k < ShellWear.Works; k++)
            if (k != (int)ShellWork.LegR && k != (int)ShellWork.ArmR && ShellSpare((ShellWork)k) is { } d)
                given |= inv.Add(d.Id, k == (int)ShellWork.LegL ? 2 : 1);
        return given;
    }

    /// <summary>Server: a standing shell's wear written back, and its body sent.</summary>
    private void ShellWearStore(Construct c, int part, in ShellCond wear)
    {
        ShellWear.Write(ref c.Parts[part], c.Parts[part].D, wear);
        c.Dirty = true;
        Constructs.StructureDirty.Add(c.Id);
    }

    // ---- Server: crew engineers ----------------------------------------------------------------------------------------

    /// <summary>Standing shells, found by a slow scan (every 2 s, only while crews are about): what an engineer may look at. Fixed: nothing allocates.</summary>
    private readonly ushort[] _shStand = new ushort[64];
    private int _shStandN;

    private void ShellStandScan()
    {
        if (ServerTick % 120 != 0)
            return;
        _shStandN = 0;
        if (Crew.ShipHigh == 0)
            return;
        foreach (var d in Constructs.Bodies)
            if (d.PartCount == 1 && d.Parts[0].D.ShellBody && !d.Parts[0].Broken && _shStandN < _shStand.Length)
                _shStand[_shStandN++] = d.Id;
    }

    /// <summary>An engineer's errand to a shell: the ship it is on, the standing shell (or the wearer, player id + 1), when it is done.</summary>
    private struct EngShellJob
    {
        public ushort Ship; // 0: a free slot
        public ushort Shell;
        public byte Wearer;
        public long Done;
        public bool Walks;
    }

    private readonly EngShellJob[] _engShell = new EngShellJob[16];

    /// <summary>Seconds an engineer spends going over a shell, after the walk.</summary>
    public const float EngShellService = 15;

    private int EngShellSlot(ushort ship)
    {
        for (var k = 0; k < _engShell.Length; k++)
            if (_engShell[k].Ship == ship)
                return k;
        return -1;
    }

    /// <summary>
    /// Server, from <see cref="CrewEngineer"/>: an engineer at a shell. True: they are at it (nothing else this look). When done, every
    /// worn or failed work on it is serviced back to the mark, free.
    /// </summary>
    private bool CrewShellBusy(Construct c, CrewShip s, int eng)
    {
        var slot = EngShellSlot(c.Id);
        if (slot < 0)
            return false;
        ref var j = ref _engShell[slot];
        if (Crew.Tick < j.Done || !Crew.ErrandDone(eng) && Crew.Tick < j.Done + ErrandWait)
            return true;
        var t = ShellWear.Current;
        var mark = ShellWear.WearOf(t.ServiceToPct);
        if (j.Wearer != 0 && Players.TryGetValue((byte)(j.Wearer - 1), out var p) && p.InShell && _sh[p.Id].On && p.Sim.RideBody == c.Id)
        {
            for (var k = 0; k < ShellWear.Works; k++)
                p.ShellWear[k] = Math.Min(p.ShellWear[k], mark);
            p.Health = World.MaxHealth; // (its damage patched too: shells don't heal by themselves)
            _sh[p.Id].Sent = p.ShellWear;
            ShellBroadcast(p);
            SandboxHands.Notices.Add((p.Id, "The crew's engineer went over your shell: everything serviced."));
            Console.WriteLine($"[crew] construct {c.Id}: its engineer serviced {p.Name}'s robot shell");
        }
        else if (j.Shell != 0 && Constructs.Get(j.Shell) is { PartCount: 1 } d && d.Parts[0].D.ShellBody)
        {
            ShellCond wear = default;
            ShellWear.Read(d.Parts[0], d.Parts[0].D, ref wear);
            for (var k = 0; k < ShellWear.Works; k++)
                wear[k] = Math.Min(wear[k], mark);
            ShellWear.PutHealth(ref d.Parts[0], d.Parts[0].D, World.MaxHealth);
            ShellWearStore(d, 0, wear);
            Console.WriteLine($"[crew] construct {c.Id}: its engineer serviced the robot shell {d.Id}");
        }
        j = default;
        CrewErrandHome(c, s, eng);
        return true;
    }

    /// <summary>Server, from <see cref="CrewEngineer"/> with nothing else to do: a shell aboard that wants a service, walked to.</summary>
    private void CrewShellStart(Construct c, CrewShip s, int eng)
    {
        var t = ShellWear.Current;
        if (t.Problem.Length > 0 || EngShellSlot(0) is var slot && slot < 0)
            return;
        var reach = c.Radius + 2f;
        var at = c.WorldCom;
        Vector3? where = null;
        ushort shell = 0;
        byte wearer = 0;
        foreach (var p in Players.Values)
            if (p.InShell && p.Alive && p.Sim.RideBody == c.Id && (t.NeedsService(p.ShellWear) || p.Health < World.MaxHealth) && (p.Id == c.Owner || Factions.Shares(c.Owner, p.Id) || s.Own))
            {
                where = p.Position;
                wearer = (byte)(p.Id + 1);
                break;
            }
        if (where == null)
            for (var k = 0; k < _shStandN; k++)
            {
                if (Constructs.Get(_shStand[k]) is not { PartCount: 1 } d || !d.Parts[0].D.ShellBody || Vector3.DistanceSquared(d.WorldCom, at) > reach * reach)
                    continue;
                ShellCond wear = default;
                ShellWear.Read(d.Parts[0], d.Parts[0].D, ref wear);
                if (!t.NeedsService(wear) && ShellWear.HealthOf(d.Parts[0], d.Parts[0].D) >= World.MaxHealth)
                    continue;
                where = d.ToWorld(Vector3.Zero);
                shell = d.Id;
                break;
            }
        if (where is not { } w)
            return;
        var st = CrewStationOf(s, CrewStationKind.Works);
        var walk = st >= 0 ? Vector3.Distance(ConstructRender.PartCentre(c, s.StPart[st]), w) * 1.4f / CrewWorld.WalkSpeed : 0;
        ref var j = ref _engShell[slot];
        j.Ship = c.Id;
        j.Shell = shell;
        j.Wearer = wearer;
        j.Walks = Crew.Errand(c, s, eng, c.ToLocal(w), this);
        j.Done = Crew.Tick + (long)((walk + EngShellService) * World.TickRate);
        Console.WriteLine($"[crew] construct {c.Id}: its engineer {(j.Walks ? "walks" : "goes")} to a robot shell ({walk + EngShellService:0} s)");
    }

    // ---- Both sides: the look line on a standing shell ------------------------------------------------------------------

    private long _swLookKey = -1;
    private string? _swLookText;

    /// <summary>The attach tool's look line at a standing Robot Shell (null: not one). Made again only when what it says changes.</summary>
    public string? ShellWearLook(Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount || !c.DefOf(part).ShellBody || c.PartCount != 1)
            return null;
        ShellCond wear = default;
        ShellWear.Read(c.Parts[part], c.Parts[part].D, ref wear);
        var health = ShellWear.HealthOf(c.Parts[part], c.Parts[part].D);
        var fix = ShellFixOf(LocalId, wear, health, out var w);
        long key = (long)c.Id << 40 ^ (long)fix << 36 ^ (long)w << 32 ^ SandboxInv.Of(LocalId).Version;
        key = key * 31 + health;
        for (var k = 0; k < ShellWear.Works; k++)
            key = key * 31 + wear[k];
        if (key == _swLookKey)
            return _swLookText;
        _swLookKey = key;
        var t = ShellWear.Current;
        var faults = new System.Text.StringBuilder();
        for (var k = 0; k < ShellWear.Works; k++)
            if (wear[k] > ShellWear.WearOf(t.ServiceToPct))
                faults.Append(faults.Length > 0 ? ", " : "").Append(ShellWear.ShortOf((ShellWork)k)).Append(' ').Append(ShellWear.Pct(wear[k])).Append('%');
        var worst = ShellWear.Worst(wear, out var most);
        if (health < World.MaxHealth)
            faults.Insert(0, faults.Length > 0 ? $"health {health * 100 / World.MaxHealth}%, " : $"health {health * 100 / World.MaxHealth}%");
        var state = faults.Length > 0 ? faults.ToString() : $"in good order ({ShellWear.Pct(most)}% at worst)";
        var cost = t.ServiceCost > 0 ? $"{t.ServiceCost} {t.ServiceMaterial}" : "free";
        _swLookText = fix switch
        {
            ShellFix.Repair => $"Robot Shell: {state} - {SandboxKeys.Fire}: repair the damage ({cost})",
            ShellFix.Fit => $"Robot Shell: {state} - {SandboxKeys.Fire}: fit a new {ShellWear.NameOf(w)}",
            ShellFix.Service => $"Robot Shell: {state} - {SandboxKeys.Fire}: service the {ShellWear.NameOf(w)} ({cost})",
            ShellFix.NeedsPart => $"Robot Shell: {state} - the {ShellWear.NameOf(w)} needs a new {ShellWear.SpareName(w)}",
            _ => most > 0 ? $"Robot Shell: {state}" : null,
        };
        _ = worst;
        return _swLookText;
    }

    // ---- Both sides: the HUD and what it looks and sounds like ----------------------------------------------------------

    /// <summary>What this PC's player's shell does to the view this frame (Ui/UiShellWear.cs draws it).</summary>
    public struct ShellView
    {
        public float Tear; // the processor's glitch: 0 none, 1 at its worst
        public float Static; // the optics' static and scan lines, 0..1
        public float Dim; // the power cell's brown-out, 0..1
        public int Seed; // changes every few frames: where the specks and tears fall
    }

    private double _swNextGlitch, _swGlitchTo, _swNextBrown, _swBrownAt = -10, _swNextCrackle;
    private float _swGlitchK;
    private readonly double[] _swGrindAt = new double[256];

    /// <summary>
    /// Every frame, host and clients: this PC's player's shell faults as they see and hear them, and the grinding of any shell's worn leg
    /// nearby. Spaced well apart and short. False: nothing to draw.
    /// </summary>
    public bool ShellWearFrame(double now, out ShellView view)
    {
        view = default;
        if (!SandboxOn)
            return false;
        var t = ShellWear.Current;
        foreach (var p in Players.Values)
        {
            if (!p.InShell || !p.Alive)
                continue;
            var leg = MathF.Max(t.Fault(p.ShellWear[(int)ShellWork.LegL]), t.Fault(p.ShellWear[(int)ShellWork.LegR]));
            var v = p.RenderVelocity;
            if (leg <= 0 || v.X * v.X + v.Z * v.Z < 1 || !p.RenderGrounded || now < _swGrindAt[p.Id])
                continue;
            _swGrindAt[p.Id] = now + 1.3 + 1.4 * _swRng.NextDouble(); // (now and then, not every step)
            Audio.Effect3DSmall(Sfx.ShellServoGrind, p.RenderPosition + new Vector3(0, 0.5f, 0), 0.25f + 0.3f * leg, 25f);
        }
        if (Local is not { InShell: true, Alive: true } me)
        {
            _swNextGlitch = _swNextBrown = _swNextCrackle = 0;
            return false;
        }
        var cpu = t.Fault(me.ShellWear[(int)ShellWork.Cpu]);
        var optics = t.Fault(me.ShellWear[(int)ShellWork.Optics]);
        var power = t.Fault(me.ShellWear[(int)ShellWork.Power]);
        var any = false;
        // The processor: a short tear and a chirp, every 50 s worn, every 8 s failed.
        if (cpu > 0)
        {
            if (_swNextGlitch <= 0)
                _swNextGlitch = now + 5 + 10 * _swRng.NextDouble();
            if (now >= _swNextGlitch)
            {
                _swGlitchTo = now + 0.12 + 0.25 * cpu * _swRng.NextDouble();
                _swGlitchK = 0.4f + 0.6f * cpu;
                _swNextGlitch = now + float.Lerp(50, 8, cpu) * (0.7 + 0.6 * _swRng.NextDouble());
                Audio.Effect(Sfx.ShellGlitch, 0.18f + 0.17f * cpu);
            }
            if (now < _swGlitchTo)
            {
                view.Tear = _swGlitchK;
                any = true;
            }
        }
        else
            _swNextGlitch = 0;
        // The optics: static all the time, faint until near failure, and a soft crackle now and then.
        if (optics > 0)
        {
            view.Static = 0.15f + 0.85f * optics * optics;
            any = true;
            if (_swNextCrackle <= 0)
                _swNextCrackle = now + 8 + 8 * _swRng.NextDouble();
            if (now >= _swNextCrackle)
            {
                _swNextCrackle = now + float.Lerp(40, 12, optics) * (0.7 + 0.6 * _swRng.NextDouble());
                Audio.Effect(Sfx.ShellStatic, 0.12f + 0.15f * optics);
            }
        }
        else
            _swNextCrackle = 0;
        // The power cell: a brown-out, the view dimming for a moment with a falling whine.
        if (power > 0)
        {
            if (_swNextBrown <= 0)
                _swNextBrown = now + 10 + 15 * _swRng.NextDouble();
            if (now >= _swNextBrown)
            {
                _swBrownAt = now;
                _swNextBrown = now + float.Lerp(70, 18, power) * (0.7 + 0.6 * _swRng.NextDouble());
                Audio.Effect(Sfx.ShellBrownout, 0.2f + 0.15f * power);
            }
            var since = (float)(now - _swBrownAt);
            if (since < 0.9f)
            {
                view.Dim = (0.25f + 0.3f * power) * (since < 0.15f ? since / 0.15f : 1 - (since - 0.15f) / 0.75f);
                any = true;
            }
        }
        else
            _swNextBrown = 0;
        view.Seed = (int)(now * 24);
        return any;
    }

    /// <summary>A tool (the window tool's <c>shellwear</c>): sets a work's condition on the local player's shell, or on the nearest standing one.</summary>
    public string ShellWearSet(Player me, string work, float pct)
    {
        if (!IsServer)
            return "host only";
        var k = Array.FindIndex(Enum.GetNames<ShellWork>(), n => n.Equals(work, StringComparison.OrdinalIgnoreCase));
        if (k < 0 && work != "all")
            return $"no work \"{work}\" (LegL, LegR, ArmL, ArmR, Power, Cpu, Optics, Plating or all)";
        var wear = ShellWear.WearOf(pct);
        if (_sh[me.Id].On)
        {
            for (var i = 0; i < ShellWear.Works; i++)
                if (k < 0 || i == k)
                    me.ShellWear[i] = wear;
            _sh[me.Id].Sent = me.ShellWear;
            ShellBroadcast(me);
            return $"worn shell: {work} at {pct:0} %";
        }
        Construct? best = null;
        var near = float.MaxValue;
        foreach (var d in Constructs.Bodies)
            if (d.PartCount == 1 && d.Parts[0].D.ShellBody && Vector3.DistanceSquared(d.WorldCom, me.Position) is var f && f < near)
                (best, near) = (d, f);
        if (best == null)
            return "no robot shell standing";
        ShellCond c = default;
        ShellWear.Read(best.Parts[0], best.Parts[0].D, ref c);
        for (var i = 0; i < ShellWear.Works; i++)
            if (k < 0 || i == k)
                c[i] = wear;
        ShellWearStore(best, 0, c);
        return $"standing shell {best.Id}: {work} at {pct:0} %";
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>Each player's fitting time share from a robot shell's arms (Game/GameShellWear.cs; 1: no shell, or sound arms).</summary>
    public readonly float[] ShellFitSlow = Ones();

    private static float[] Ones()
    {
        var a = new float[256];
        Array.Fill(a, 1f);
        return a;
    }
}
