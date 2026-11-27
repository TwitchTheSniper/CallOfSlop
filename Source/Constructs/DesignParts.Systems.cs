using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts, the fixed systems (DesignParts.cs has the map of the files; the plan's package G, Karl's step 5): each a design element
/// worked by the game's own code for that system, from the designer's own drawing and numbers, never a catalogue part.
/// <list type="bullet">
/// <item>Tow eye and tow hitch: a hitch (PartRole.Hitch) whose coupling point (<see cref="ConstructPartDef.HitchAt"/>) is the drawn eye's
/// loop or ball; F with the attach tool couples it as any hitch (ConstructEdits.Hitch, ConstructLinks.CanCouple).</item>
/// <item>Floodlights S, M and L: a lamp (the Floodlight part's) shining from the drawn lens along the designer's pitch and yaw, a spot of
/// its size's cone, its reach the drawn range capped at <see cref="ConstructLamps.MaxRange"/>, its colour the designer's beam. All of a
/// design's lamps are wired from one hidden latched switch at the helm (<c>lights</c>), which starts on.</item>
/// <item>Docking clamps S to Capital and the magnetic lock: the game's clamp or lock (a cargo logic part with a "dock" block), its catch box
/// (the part's own size) standing out of the skin over the drawn pad; F works it as any clamp (on: it holds what settles in its box).</item>
/// <item>Hull grapple and breaching cutter: the game's grapple and cutter (lamp parts with a "breach" block), gripping or cutting out of the
/// skin; worked by the helm's hook key (G) through a hidden Input Reader and a Toggle (each press grips or lets go).</item>
/// <item>Weapon mounts: the mount and its fitted weapon are two elements, split from the designer's own drawing
/// (<see cref="PieceWeapons.Split"/>); the weapon is the game's gun for it (<see cref="PieceWeapons.GamePart"/>: its damage, rate, rounds and
/// sound), its muzzle at the drawn barrel's tip and its bore along the drawn barrel, an ammo box (hidden, full as a spawned ship's are)
/// welded to it. Each Gunner seat gets a hidden Fire Director (the helm's when there is none) wired from the seat and into the guns
/// nearest it, so the seat's fire key fires them (ConstructWeapons.StepGuns). The torpedo sponson has no gun in the game yet: drawn,
/// inert, logged. Turrets (a joint under the weapon) are the next wave's (package H): their weapons fire fixed for now.</item>
/// </list>
/// The vectors an element carries for its function (Element.ThrustAt, Element.Exhaust: a point and a direction, moved into the part's frame
/// by IntoFrames) are a gun's muzzle and bore, a lamp's lens and beam, a hitch's coupling point and a clamp's pad face; Element.Force is a
/// lamp's reach and Element.Role a lamp's colour. A wire longer than <see cref="ConstructBuild.WireReach"/> (a ship's fire control and
/// lighting run the length of it) goes through hidden relays (OR gates) every <see cref="RelayStep"/>, so a player's blueprint of the
/// ship lays every wire again.
/// </summary>
public static partial class DesignParts
{
    public const float LogicKg = 5f; // a hidden director, switch, reader or toggle
    public const float AmmoKg = 15f; // a gun's hidden ammo box (the Ammo Box's)
    public const float RelayStep = 7f; // m between a long wire's hidden relays (under ConstructBuild.WireReach, 8)
    public const float SpotCone = 30f; // degrees: a floodlight S's half angle; an M's 0.8 of it, an L's 0.6 (a bigger reflector throws a tighter beam)

    private static readonly Dictionary<string, string> DockParts = new(StringComparer.Ordinal)
    {
        ["Docking clamp S"] = "Docking Clamp", ["Docking clamp M"] = "Docking Clamp Medium", ["Docking clamp L"] = "Docking Clamp Large",
        ["Docking clamp Capital"] = "Docking Clamp Capital", ["Magnetic lock"] = "Magnetic Lock",
    };

    private static readonly Dictionary<string, string> BreachParts = new(StringComparer.Ordinal) { ["Hull grapple"] = "Hull Grapple", ["Breaching cutter"] = "Breaching Cutter" };

    /// <summary>
    /// The systems' own elements past their pieces: a hidden Fire Director per Gunner seat (else the helm's), wired from its seat into the
    /// guns nearest it; the lights' switch and the hook's reader and toggle at the helm, wired into the lamps and the grapples.
    /// </summary>
    private static IEnumerable<int> AddSystems(Steps st)
    {
        var els = st.Els;
        List<int> guns = [], lamps = [], breaches = [], gunners = [];
        var helm = -1;
        for (var i = 0; i < els.Count; i++)
            switch (els[i].Fn)
            {
                case "gun": guns.Add(i); break;
                case "lamp": lamps.Add(i); break;
                case "breach": breaches.Add(i); break;
                case "seat":
                    if (els[i].Role == "Gunner")
                        gunners.Add(i);
                    if (helm < 0 || els[i].Role == "Helm" && els[helm].Role != "Helm")
                        helm = i;
                    break;
            }
        if (guns.Count + lamps.Count + breaches.Count == 0)
            yield break;
        if (helm < 0)
        {
            Console.WriteLine($"[design] \"{st.D.Name}\" has {guns.Count} guns, {lamps.Count} floodlights and {breaches.Count} grapples or cutters but no seat: nothing works them");
            yield break;
        }
        var slot = 0;
        int Hidden(int seat, string id, string name, string fn)
        {
            // A small box in its seat's console, under the seat's hull (as the flight computer's: DesignParts.Pieces.cs), one beside the next.
            var s = els[seat];
            var at = new Vector3((s.BoxMin.X + s.BoxMax.X) * 0.5f + 0.2f * (++slot % 5 - 2), s.BoxMin.Y + 0.35f, (s.BoxMin.Z + s.BoxMax.Z) * 0.5f);
            els.Add(new Element { Id = id, Kind = "logic", Name = name, Fn = fn, Kg = LogicKg, BoxMin = at - new Vector3(0.06f), BoxMax = at + new Vector3(0.06f) });
            Weld(els, seat, els.Count - 1);
            return els.Count - 1;
        }
        var relays = 0;
        void Wire(int from, int to)
        {
            // A wire reaches WireReach (centre to centre) and a blueprint's wire segment as far: a longer run goes through hidden OR gates
            // (relays) along the line between the two, at most RelayStep apart, each passing the signal on a tick later.
            var (a, b) = (Mid(els[from]), Mid(els[to]));
            var n = Math.Max(0, (int)MathF.Ceiling(Vector3.Distance(a, b) / RelayStep) - 1);
            var prev = from;
            for (var k = 1; k <= n; k++)
            {
                var at = Vector3.Lerp(a, b, k / (float)(n + 1));
                els.Add(new Element { Id = $"relay{relays++}", Kind = "logic", Name = "relay", Fn = "relay", Kg = LogicKg * 0.2f, BoxMin = at - new Vector3(0.03f), BoxMax = at + new Vector3(0.03f) });
                Weld(els, prev, els.Count - 1);
                els[prev].Wires.Add(els.Count - 1);
                prev = els.Count - 1;
            }
            els[prev].Wires.Add(to);
        }
        if (guns.Count > 0)
        {
            // One director a Gunner seat, each gun wired from the nearest; the helm's when the design has no gunner.
            var seats = gunners.Count > 0 ? gunners : [helm];
            var dirs = new int[seats.Count];
            for (var k = 0; k < seats.Count; k++)
            {
                dirs[k] = Hidden(seats[k], $"fire{k}", "fire director", "director");
                els[seats[k]].Wires.Add(dirs[k]);
            }
            foreach (var g in guns)
            {
                var best = 0;
                for (var k = 1; k < seats.Count; k++)
                    if (Vector3.DistanceSquared(Mid(els[seats[k]]), Mid(els[g])) < Vector3.DistanceSquared(Mid(els[seats[best]]), Mid(els[g])))
                        best = k;
                Wire(dirs[best], g);
            }
        }
        if (lamps.Count > 0)
        {
            var sw = Hidden(helm, "lights", "lights switch", "switch");
            foreach (var l in lamps)
                Wire(sw, l);
        }
        if (breaches.Count > 0)
        {
            var reader = Hidden(helm, "hook", "hook key reader", "reader");
            var toggle = Hidden(helm, "hook.t", "hook toggle", "toggle");
            els[helm].Wires.Add(reader);
            els[reader].Wires.Add(toggle);
            foreach (var b in breaches)
                Wire(toggle, b);
        }
        Console.WriteLine($"[design] \"{st.D.Name}\"'s systems: {relays} relays; {guns.Count} guns on {(gunners.Count > 0 ? $"{gunners.Count} gunner seats' directors" : "the helm's director")}, {lamps.Count} floodlights on the helm's switch, {breaches.Count} grapples or cutters on the helm's hook key");
        yield return els.Count;
    }

    private static Vector3 Mid(Element e) => (e.BoxMin + e.BoxMax) * 0.5f;

    /// <summary>This file's kinds: the weapon mounts, the floodlights, the tow points, the clamps and locks, the grapple and the cutter, and their functions.</summary>
    private static void RegisterSystems(KindTable k)
    {
        foreach (var m in PieceWeapons.Mounts)
            k.Piece(m.Name, Mount);
        foreach (var f in DesignFittings.Floods)
            k.Piece(f.Name, Flood);
        k.Piece("Tow eye", Tow);
        k.Piece("Tow hitch", Tow);
        foreach (var name in DockParts.Keys)
            k.Piece(name, Dock);
        foreach (var name in BreachParts.Keys)
            k.Piece(name, Breach);
        k.Fn("gun", GunStart, GunFinish);
        k.Fn("ammo", _ => Bare("Ammo Box"));
        k.Fn("lamp", e => Bare(DesignFittings.FloodOf(e.Name)!.Part), LampFinish);
        k.Fn("hitch", e => Bare(e.Name == "Tow hitch" ? "Tow Hitch" : "Tow Eye"), (e, d) => d.HitchAt = e.ThrustAt);
        k.Fn("dock", e => Bare(DockParts[e.Name]), DockFinish);
        k.Fn("breach", e => Bare(BreachParts[e.Name]));
        k.Fn("director", _ => Bare("Fire Director"));
        k.Fn("switch", _ => SwitchStart());
        k.Fn("reader", _ => ReaderStart());
        k.Fn("toggle", _ => Bare("Toggle"));
        k.Fn("relay", _ => Bare("OR Gate"));
    }

    /// <summary>A game part's copy as an element's start: its role, logic, settings and numbers, nothing it draws or draws power for.</summary>
    private static ConstructPartDef Bare(string part)
    {
        var d = ConstructParts.Need(part).ShallowCopy();
        d.Panels = [];
        d.PowerDraw = 0; // (a design carries no power plant yet, as its drives are unfed)
        return d;
    }

    // ---- Weapon mounts ------------------------------------------------------------------------------------------------------

    /// <summary>A mount with a weapon on it becomes two elements, the mount and the weapon (with its ammo box), split from the designer's own drawing.</summary>
    private static void Mount(in PieceAt a)
    {
        var (p, e, els) = (a.P, a.E, a.Els);
        if (p.Fit.Length == 0)
            return; // (an empty mount: structure)
        if (PieceWeapons.GamePart(p.Fit) is not { } part)
        {
            Console.WriteLine($"[design] the {p.Fit} on {p.Kind} {e.Id} has no gun in the game yet: drawn, inert");
            return;
        }
        if (ConstructParts.Find(part) is null)
        {
            Console.WriteLine($"[design] REFUSED the {p.Fit} on {p.Kind} {e.Id}: its game part \"{part}\" isn't loaded; drawn, inert");
            return;
        }
        var (mount, weapon) = PieceWeapons.Split(p, a.W);
        if (weapon is not { Tris: > 0 })
            return;
        Reshape(e, mount);
        var g = new Element { Id = e.Id + ".gun", Kind = "piece", Name = p.Fit, Fn = "gun" };
        Reshape(g, weapon);
        // The bore along the drawn barrel (the weapon's +Z, towards the bow on the skin), the muzzle at its tip: the middle of the
        // corners within 2 cm of the furthest along it (a twin mount's two barrels' mouths: the middle between them).
        var bore = Vector3.Normalize(a.Z);
        var far = float.MinValue;
        for (var i = 0; i + 2 < weapon.P.Count; i += 3)
            far = MathF.Max(far, Vector3.Dot(new Vector3((float)weapon.P[i], (float)weapon.P[i + 1], (float)weapon.P[i + 2]), bore));
        var (sum, n) = (Vector3.Zero, 0);
        for (var i = 0; i + 2 < weapon.P.Count; i += 3)
            if (new Vector3((float)weapon.P[i], (float)weapon.P[i + 1], (float)weapon.P[i + 2]) is var v && Vector3.Dot(v, bore) > far - 0.02f)
                (sum, n) = (sum + v, n + 1);
        g.ThrustAt = sum / Math.Max(1, n);
        g.Exhaust = bore;
        g.Rot = Facing(-bore, a.Y); // (a gun fires along its part's -z: ConstructWeapons.BuildHeavy)
        els.Add(g);
        var gi = els.Count - 1;
        Weld(els, a.At, gi);
        // Its rounds: a hidden ammo box in the mount's trunk under the skin, welded to the gun (a box bolted to a gun feeds it).
        var box = a.O - a.Y * (0.15f * a.S);
        els.Add(new Element { Id = g.Id + ".ammo", Kind = "ammo", Name = "ammo box", Fn = "ammo", Kg = AmmoKg, BoxMin = box - new Vector3(0.1f), BoxMax = box + new Vector3(0.1f) });
        Weld(els, gi, els.Count - 1);
    }

    /// <summary>An element's look, area, mass and collider made again from <paramref name="m"/> (the ship's frame).</summary>
    private static void Reshape(Element e, DesignMesh m)
    {
        e.Mesh = new LookBuilder();
        for (var t = 0; t < m.Tris; t++)
            e.Mesh.AddTri(m, t, Vector3.One, new Vector3(float.NaN));
        e.Area = e.Mesh.Area;
        e.Kg = e.Area * PieceKgPerM2;
        e.Hulls = [];
        HullOf(e, m.P);
    }

    private static ConstructPartDef GunStart(Element e)
    {
        var d = Bare(PieceWeapons.GamePart(e.Name)!);
        if (d.GunEnergy > 0 && d.GunMag > 0)
            d.GunEnergy = 0; // (an energy gun with rounds, the railgun: a design has no capacitors yet, so it fires on its rounds alone)
        return d;
    }

    private static void GunFinish(Element e, ConstructPartDef d) => d.GunMuzzle = e.ThrustAt;

    // ---- Floodlights, tow points, clamps, grapples ------------------------------------------------------------------------

    private static void Flood(in PieceAt a)
    {
        if (DesignFittings.Beam(a.P, a.W) is not { } b)
            return;
        var e = a.E;
        e.Fn = "lamp";
        e.ThrustAt = V(b.At);
        e.Exhaust = Vector3.Normalize(V(b.Dir));
        e.Force = (float)Math.Min(b.Range, ConstructLamps.MaxRange);
        e.Role = a.P.Beam;
    }

    private static void LampFinish(Element e, ConstructPartDef d)
    {
        var colour = int.TryParse(e.Role.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : 0xfff4d0;
        d.Config = [.. d.Config.Select(x => x.Id == CfgKey.Colour ? x.Copy(dflt: colour) : x)];
        var size = Array.FindIndex(DesignFittings.Floods, f => f.Name == e.Name);
        d.LightCone = SpotCone * (size switch { 1 => 0.8f, 2 => 0.6f, _ => 1f });
        d.LightDir = Vector3.Normalize(e.Exhaust);
        d.LightAt = e.ThrustAt;
        d.LightRange = Math.Clamp(e.Force, ConstructLamps.Range, ConstructLamps.MaxRange);
    }

    private static void Tow(in PieceAt a)
    {
        a.E.Fn = "hitch";
        a.E.ThrustAt = a.O + a.Y * (float)(DesignFittings.TowHeight(a.P.Kind) * a.S); // (the eye's loop or the ball, out of the skin)
    }

    private static void Dock(in PieceAt a)
    {
        var e = a.E;
        e.Fn = "dock";
        e.Rot = Facing(a.Z, a.Y); // (its part's +y out of the skin: the catch box stands there)
        var top = 0f;
        for (var i = 0; i + 2 < (e.Mesh?.Pos.Count ?? 0); i += 3)
            top = MathF.Max(top, Vector3.Dot(new Vector3(e.Mesh!.Pos[i], e.Mesh.Pos[i + 1], e.Mesh.Pos[i + 2]) - a.O, Vector3.Normalize(a.Y)));
        e.ThrustAt = a.O + Vector3.Normalize(a.Y) * top; // (the pad's face, the middle of the ring)
    }

    private static void DockFinish(Element e, ConstructPartDef d)
    {
        // The catch box the part's own size, standing on the pad's face (its part frame: +y out of the skin).
        var size = d.Config.FirstOrDefault(x => x.Id == CfgKey.SizeY)?.Default ?? ConstructCargo.DefaultSize.Y;
        var at = e.ThrustAt + new Vector3(0, size * 0.5f, 0);
        d.Config = [.. d.Config.Select(x => x.Id switch
        {
            CfgKey.AtX => x.Copy(dflt: Math.Clamp(at.X, x.Min, x.Max)),
            CfgKey.AtY => x.Copy(dflt: Math.Clamp(at.Y, x.Min, x.Max)),
            CfgKey.AtZ => x.Copy(dflt: Math.Clamp(at.Z, x.Min, x.Max)),
            _ => x,
        })];
    }

    private static void Breach(in PieceAt a)
    {
        a.E.Fn = "breach";
        a.E.Rot = Facing(a.Z, -a.Y); // (a breach part grips or cuts out of its face, its part's -y: out of the skin)
    }

    private static ConstructPartDef SwitchStart()
    {
        var d = Bare("Switch");
        d.Config = [.. d.Config.Select(x => x.Id == CfgKey.StartOn ? x.Copy(dflt: 1) : x)]; // (the lights on from the spawn)
        return d;
    }

    private static ConstructPartDef ReaderStart()
    {
        var d = Bare("Input Reader");
        d.Settings = [ConstructParts.KeyHook]; // (the seat's hook key, G: a grapple's own key)
        d.DefaultSetting = 0;
        return d;
    }
}
