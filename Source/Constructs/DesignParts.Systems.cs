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
/// inert, logged.</item>
/// <item>Turrets (package H): a turret ring's weapon rides two joints, a bearing (a turntable) and a trunnion (a motor hinge), so the
/// director's seat lays it on what its sitter looks at (ConstructWeapons.Aim: AimJoints); a director's wire through relays still counts
/// as the director's (ConstructWeapons.RelayedDirector). The renderer draws a joint's segment at its live pose (ConstructRenderDesign).</item>
/// <item>Hangars (package H): a hangar bay is the game's Hangar Bay dock of its size, its catch box the designer's bay volume (held to the
/// part's side), its frame the designer's boxes, its door the drawn leaf (an event door: F); a lift platform is a one-span lift (a
/// piston) and a ramp deck a motor hinge, each with the designer's deck as its leaf, worked by F as a lift's car (DesignParts.Risers.cs).</item>
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
                els.Add(new Element { Id = $"relay{relays++}", Kind = "logic", Name = "relay", Fn = "relay", Kg = LogicKg * 0.2f, BoxMin = at - new Vector3(0.03f), BoxMax = at + new Vector3(0.03f), NoCollider = true }); // (on the straight line between the two, through rooms and doorways: nothing a person meets)
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
        k.Fn("bay", e => Bare(BayPart(e)), BayFinish);
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
        // On a turret ring: its bearing (a turntable) and its trunnion (a motor hinge riding it) first, the weapon on the trunnion, so the
        // Gunner seat's director lays it (ConstructWeapons: AimJoints); else fixed on its mount.
        var (under, seg) = PieceWeapons.MountOf(p.Kind) is { Ring: true } ring ? Turret(a, ring, g, bore) : (a.At, (byte)0);
        g.Seg = seg;
        els.Add(g);
        var gi = els.Count - 1;
        Weld(els, under, gi);
        // Its rounds: a hidden ammo box in the mount's trunk under the skin, welded to the gun (a box bolted to a gun feeds it); on a turret,
        // inside the weapon's house, turning with it (no feed crosses a turret's joints in a design yet).
        var box = seg == 0 ? a.O - a.Y * (0.15f * a.S) : (g.BoxMin + g.BoxMax) * 0.5f;
        els.Add(new Element { Id = g.Id + ".ammo", Kind = "ammo", Name = "ammo box", Fn = "ammo", Kg = AmmoKg, Seg = seg, BoxMin = box - new Vector3(0.1f), BoxMax = box + new Vector3(0.1f), NoCollider = seg != 0 });
        Weld(els, gi, els.Count - 1);
    }

    // ---- Turrets ------------------------------------------------------------------------------------------------------------

    public const float TurretElevMin = -10f, TurretElevMax = 80f; // degrees a turret's trunnion lays its weapon below and above its ring
    public const float BearingKgPerSize = 40f; // a turret's bearing and its trunnion, each, at Size 1 (squared with the size)
    public const float TurretSideways = 0.25f; // a ring whose axis's climb is under this (over 75 degrees off the vertical) turns about its own way, not the ship's up

    /// <summary>
    /// A turret ring's two joints, added before its weapon (a joint's parts come after it: BuildSteps): its bearing, a turntable about the
    /// ring's axis through the ring's top, and its trunnion, a motor hinge across the bore through the weapon's middle, riding the bearing.
    /// Both take the game's Turret Ring of the mount's size for their speed and strength. A joint turns about one of the ship's six ways (its
    /// part's turn is a Rot24), so a ring on a curved skin turns about the way nearest its axis (logged past 15 degrees). Returns the element
    /// the weapon is welded to and the weapon's segment: the mount and 0 when the body has no joints left (the weapon fixed, logged).
    /// </summary>
    private static (int Under, byte Seg) Turret(in PieceAt a, PieceWeapons.Mount ring, Element g, Vector3 bore)
    {
        var els = a.Els;
        var joints = 0;
        foreach (var x in els)
            if (x.Joint != null)
                joints++;
        if (joints + 2 > Construct.MaxJoints)
        {
            Console.WriteLine($"[design] the {a.P.Fit} on {a.P.Kind} {a.E.Id} is fixed: the body's {Construct.MaxJoints} joints are all used");
            return (a.At, 0);
        }
        var y = Vector3.Normalize(a.Y);
        // Its bearing upright (up or down the ship) unless the ring stands within TurretSideways of the vertical: a gunner's aim reads a
        // joint about a level axis as an elevation (ConstructWeapons.Aim), so a shoulder's ring tilted well off the vertical still turns
        // about the ship's up, and only a ring on a near-vertical side turns about its own (nearest) way.
        var up = MathF.Abs(y.Y) >= TurretSideways ? (byte)(y.Y > 0 ? 2 : 3) : NearestDir(y);
        var tilt = MathF.Acos(Math.Clamp(Vector3.Dot(ConstructParts.DirVector(up), y), -1, 1)) * 180 / MathF.PI;
        if (tilt > 15)
            Console.WriteLine($"[design] the turret {a.E.Id} stands {tilt:0} degrees off the ship's axes: it turns about {ConstructParts.DirVector(up)}");
        var part = ConstructParts.Find("Turret Ring " + ring.Name[^1]) ?? ConstructParts.Find("Turret Ring S");
        var (speed, strength) = part != null ? (part.MotorSpeed, part.MotorStrength) : (MathF.PI / 3, 12000f);
        // (a bigger ring turns a heavier house: at least three times the weapon's weight a metre off its pivot, so it never sags)
        strength = MathF.Max(strength * a.S * a.S * a.S, g.Kg * 9.81f * 3f * MathF.Max(1f, (g.BoxMax - g.BoxMin).Length() * 0.5f));
        var top = a.O + y * ((float)ring.Top * a.S);
        var kg = BearingKgPerSize * a.S * a.S;
        var bearing = new Element
        {
            Id = a.E.Id + ".ring", Kind = "turret", Name = "Turret bearing", Kg = kg, BoxMin = top - y * 0.06f - new Vector3(0.05f), BoxMax = top - y * 0.06f + new Vector3(0.05f),
            NoCollider = true, // (inside the ring's own hull)
            Joint = new JointSpec { Type = JointType.Turntable, Axis = up, Pivot = top, Speed = speed, Strength = strength, Limited = false, Min = float.NegativeInfinity, Max = float.PositiveInfinity }, // (turning for ever, as a Turret Ring's)
        };
        els.Add(bearing);
        var bi = els.Count - 1;
        Weld(els, a.At, bi);
        // The trunnion through the weapon's middle, across its bore and the bearing's axis (so the two lay it both ways), turned so that a
        // positive angle raises the bore off the ring.
        var mid = (g.BoxMin + g.BoxMax) * 0.5f;
        var axisUp = ConstructParts.DirVector(up);
        var acrossWay = Vector3.Cross(axisUp, bore);
        var across = NearestDir(acrossWay.LengthSquared() > 0.1f ? acrossWay : Vector3.Cross(y, bore));
        if (Vector3.Dot(Vector3.Cross(ConstructParts.DirVector(across), bore), axisUp) < 0)
            across ^= 1;
        var trunnion = new Element
        {
            Id = a.E.Id + ".elev", Kind = "turret", Name = "Turret trunnion", Kg = kg, Seg = (byte)(joints + 1), BoxMin = mid - new Vector3(0.05f), BoxMax = mid + new Vector3(0.05f), NoCollider = true,
            Joint = new JointSpec
            {
                Type = JointType.Motor, Axis = across, Pivot = mid, Speed = speed, Strength = strength, Limited = true,
                Min = TurretElevMin * MathF.PI / 180, Max = TurretElevMax * MathF.PI / 180,
            },
        };
        els.Add(trunnion);
        Weld(els, bi, els.Count - 1);
        return (els.Count - 1, (byte)(joints + 2));
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

    // ---- Hangars (DesignHangar: the designer's bays, lift platforms and ramp decks) --------------------------------------------

    public const float BayDoorSecondsPerM = 0.25f; // a bay door's slide or swing, a second for each 4 m of its leaf (2 s at least)
    public const float BayDoorMaxTravel = 60f; // m a bay door slides at most (a Capital door's 48 m and its gap)

    /// <summary>
    /// A hangar piece's own elements, from its copy's mesh <paramref name="m"/> (the ship's frame, drawn shut: DesignHangar.Draw), called
    /// with its door leaves (DesignParts.Doors.cs, PieceDoors). Its frame collides as the designer's own boxes (the bay's posts, edges, back
    /// wall and deck, a well's rings), never one hull over its mouth. A bay is the game's Hangar Bay of its size (a cargo dock that holds many
    /// craft and throws them out along its mouth when it lets go), its catch box the bay's volume, and its door the drawn leaf (an event door).
    /// A lift platform is a one-span lift (DesignParts.Risers.cs: a piston, its deck on it; F on the deck lowers or raises it), a ramp deck a
    /// motor hinge at its aft edge with its slab on it, lowered by F to the designer's angle.
    /// </summary>
    private static void HangarLeaves(in PieceAt a, DesignMesh m)
    {
        var (p, e, els) = (a.P, a.E, a.Els);
        FrameBoxes(e, m);
        var leaf = m.Leaves?.Find(l => l.Leaf == 0) ?? default;
        if (DesignHangar.Of(p) is { } b)
        {
            Bay(a, b);
            if (leaf.To <= leaf.From)
                return;
            var door = Leaf($"{e.Id}.d0", $"{p.Kind} door", m, leaf.From, leaf.To, a.X, a.Y, a.Z);
            var (dx, dz) = DesignHangar.DoorExtent(p, a.W);
            var width = (float)Math.Max(dx, dz) * (float)p.Hole;
            var seconds = MathF.Max(HatchSeconds, width * BayDoorSecondsPerM);
            if (p.BayDoorType == "Sliding")
            {
                Slide(door, dz >= dx ? a.Z : a.X, seconds); // (along its long side, as the page draws it run aside)
                var along = ConstructParts.DirVector(door.Door!.Axis);
                door.Door.Travel = MathF.Min(MathF.Abs(Vector3.Dot(door.BoxMax - door.BoxMin, along)) + LeafGap, BayDoorMaxTravel);
            }
            else if (Upright(a) == 0)
                Swing(door, m, leaf.From, leaf.To, a.X * (a.X.Y >= 0 ? -1 : 1), a.Z, a.Y, seconds); // (a side bay's drawbridge, on its lower edge)
            else
                Swing(door, m, leaf.From, leaf.To, -a.Z, a.X, a.Y, seconds); // (on its aft edge, out of the skin: a quarter turn, the page's 70 degrees)
            els.Add(door);
            Weld(els, a.At, els.Count - 1);
            return;
        }
        if (leaf.To <= leaf.From || !DesignHangar.IsDeck(p.Kind))
            return;
        var joints = 0;
        foreach (var x in els)
            if (x.Joint != null)
                joints++;
        if (joints + 1 > Construct.MaxJoints)
        {
            Console.WriteLine($"[design] the {p.Kind} {e.Id} doesn't move: the body's {Construct.MaxJoints} joints are all used (drawn up, solid)");
            return;
        }
        // The deck (or slab) as a box-only element, from the designer's leaf; it rides the joint ("lift{id}.j0" and ".car": a lift's F works it).
        var id = "lift" + e.Id.Replace('.', '_');
        var car = Leaf($"{id}.car", p.Kind, m, leaf.From, leaf.To, a.X, a.Y, a.Z);
        (car.Fn, car.Door, car.Kind, car.Seg) = ("", null, "lift", (byte)(joints + 1));
        car.Kg = MathF.Max(car.Kg, (float)(p.W * p.L * p.Hole * p.Hole) * DeckKgPerM2);
        JointSpec j;
        Vector3 at;
        if (p.Kind == "Lift platform")
        {
            at = a.O - a.Y * (float)DesignHangar.LiftDeck; // (in the well, over the deck's middle)
            j = new JointSpec { Type = JointType.Piston, Axis = NearestDir(a.Y), Min = 0, Max = (float)p.Travel, Speed = LiftSpeed, Strength = LiftStrength, Pivot = at };
        }
        else
        {
            // Its hinge along its aft edge, turned so that a positive angle lowers its far end out of the skin.
            at = a.O - a.Z * (float)(p.L * p.Hole / 2);
            var axis = NearestDir(a.X);
            if (Vector3.Dot(Vector3.Cross(ConstructParts.DirVector(axis), a.Z), a.Y) < 0)
                axis ^= 1;
            j = new JointSpec { Type = JointType.Motor, Axis = axis, Min = 0, Max = (float)DesignHangar.RampAngle(p), Speed = LiftSpeed / MathF.Max(1f, (float)(p.L * p.Hole) * 0.5f), Strength = LiftStrength * (float)(p.L * p.Hole), Pivot = at };
        }
        var ram = new Element
        {
            Id = $"{id}.j0", Kind = "lift", Name = p.Kind == "Lift platform" ? "Lift platform ram" : "Ramp deck hinge", Kg = 20f, Joint = j, NoCollider = true,
            BoxMin = at - new Vector3(0.05f), BoxMax = at + new Vector3(0.05f),
        };
        els.Add(ram);
        Weld(els, a.At, els.Count - 1);
        els.Add(car);
        Weld(els, els.Count - 2, els.Count - 1);
    }

    /// <summary>
    /// A boarding ramp (DesignRamp, Karl 2026-11-27), drawn stowed: its frame, well and sills collide as boxes; its ramp (leaf 0) rides a
    /// motor hinge on its fore edge at the room's floor ("liftramp{id}.j0", ".car"), and its extension (leaf 1) a piston on the ramp
    /// (".j1", ".ext") that runs out aft along it. The two are a <see cref="JointChain"/>: F (<see cref="LiftUse"/>) opens both, the ramp
    /// swings down first, then the extension runs out; shut, the extension comes in first. Each moves as any joint, so the pose, the
    /// wire and the save carry it, and its colliders ride its segment. <paramref name="floor"/>: its room's floor in from the skin (m).
    /// </summary>
    private static void RampLeaves(in PieceAt a, DesignMesh m, double floor)
    {
        var (p, e, els) = (a.P, a.E, a.Els);
        FrameBoxes(e, m);
        (int From, int To) Range(int leaf) => m.Leaves?.Find(l => l.Leaf == leaf) is { } l ? (l.From, l.To) : (0, 0);
        var (f0, t0) = Range(0);
        var (f1, t1) = Range(1);
        if (t0 <= f0 || t1 <= f1)
        {
            Console.WriteLine($"[design] the {p.Kind} {e.Id} draws no ramp or no extension: it has none");
            return;
        }
        var id = "liftramp" + e.Id.Replace('.', '_');
        var car = Leaf($"{id}.car", p.Kind, m, f0, t0, a.X, a.Y, a.Z);
        var ext = Leaf($"{id}.ext", $"{p.Kind} extension", m, f1, t1, a.X, a.Y, a.Z);
        (car.Fn, car.Door, car.Kind) = ("", null, "lift");
        (ext.Fn, ext.Door, ext.Kind) = ("", null, "lift");
        var joints = 0;
        foreach (var x in els)
            if (x.Joint != null)
                joints++;
        var run = (float)DesignRamp.ExtendOf(p);
        var need = run > 0.01f ? 2 : 1;
        if (joints + need > Construct.MaxJoints)
        {
            // (stowed and solid: the ramp is drawn, but doesn't lower)
            Console.WriteLine($"[design] the {p.Kind} {e.Id} doesn't lower: the body's {Construct.MaxJoints} joints are all used (stowed, solid)");
            els.Add(car);
            Weld(els, a.At, els.Count - 1);
            els.Add(ext);
            Weld(els, a.At, els.Count - 1);
            return;
        }
        // The hinge, turned so that a positive angle swings the ramp's far end (aft of it) out of the skin.
        var w = a.W;
        var hinge = V(DesignRamp.Hinge(p, w, floor));
        var axis = NearestDir(a.X);
        if (Vector3.Dot(Vector3.Cross(ConstructParts.DirVector(axis), -a.Z), a.Y) < 0)
            axis ^= 1;
        var angle = (float)DesignRamp.Angle(p, floor);
        var secs = (float)DesignRamp.Seconds;
        var length = (float)(3.2 * p.RampL / DesignRamp.DefLength * p.RampW / DesignRamp.DefWidth) + run; // (the plate's weight and reach: as before at the default hatch)
        var ram = new Element
        {
            Id = $"{id}.j0", Kind = "lift", Name = "Boarding ramp hinge", Kg = 20f, NoCollider = true, BoxMin = hinge - new Vector3(0.05f), BoxMax = hinge + new Vector3(0.05f),
            Joint = new JointSpec { Type = JointType.Motor, Axis = axis, Min = 0, Max = angle, Speed = angle / secs, Strength = LiftStrength * length, Pivot = hinge, Chain = need == 2 ? JointChain.Leads : JointChain.None },
        };
        els.Add(ram);
        Weld(els, a.At, els.Count - 1);
        car.Seg = (byte)(joints + 1);
        els.Add(car);
        Weld(els, els.Count - 2, els.Count - 1);
        var carAt = els.Count - 1;
        if (need == 1)
        {
            ext.Seg = car.Seg; // (no run: it stays nested under the ramp)
            els.Add(ext);
            Weld(els, carAt, els.Count - 1);
            return;
        }
        var mid = (ext.BoxMin + ext.BoxMax) * 0.5f;
        var slide = new Element
        {
            Id = $"{id}.j1", Kind = "lift", Name = "Boarding ramp extension ram", Kg = 15f, NoCollider = true, Seg = car.Seg, BoxMin = mid - new Vector3(0.05f), BoxMax = mid + new Vector3(0.05f),
            Joint = new JointSpec { Type = JointType.Piston, Axis = NearestDir(-a.Z), Min = 0, Max = run, Speed = MathF.Max(run / secs, 0.2f), Strength = LiftStrength, Pivot = mid, Chain = JointChain.Follows },
        };
        els.Add(slide);
        Weld(els, carAt, els.Count - 1);
        ext.Seg = (byte)(joints + 2);
        els.Add(ext);
        Weld(els, els.Count - 2, els.Count - 1);
    }

    /// <summary>Which of a place's axes is nearest the ship's up (0 X, 1 Y, 2 Z), as DesignHangar reads a bay's.</summary>
    private static int Upright(in PieceAt a)
    {
        float ax = MathF.Abs(a.X.Y), ay = MathF.Abs(a.Y.Y), az = MathF.Abs(a.Z.Y);
        return ay >= ax && ay >= az ? 1 : ax >= az ? 0 : 2;
    }

    /// <summary>
    /// A hangar piece's frame (its mesh past its leaves) as solid boxes, one each of the designer's boxes (each 12 triangles: DesignMesh.Box
    /// and DesignHangar's slabs), in place of the one hull its corners would give (which would fill the bay or the well).
    /// </summary>
    private static void FrameBoxes(Element e, DesignMesh m)
    {
        var boxes = new List<ElementBox>();
        for (var t = 0; t + 12 <= m.Tris; t += 12)
        {
            if (InLeaf(m, t))
                continue;
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (var i = t * 9; i < (t + 12) * 9; i += 3)
            {
                var v = new Vector3((float)m.P[i], (float)m.P[i + 1], (float)m.P[i + 2]);
                (lo, hi) = (Vector3.Min(lo, v), Vector3.Max(hi, v));
            }
            boxes.Add(new ElementBox(lo, hi));
        }
        if (m.Tris % 12 != 0)
            Console.WriteLine($"[design] {e.Name} {e.Id}: its drawing isn't all boxes ({m.Tris} triangles): its frame collides as {boxes.Count} boxes");
        if (boxes.Count == 0)
            return;
        e.Boxes = boxes;
        e.Hulls = [];
        (e.Points, e.Runs) = (null, null);
        (e.BoxMin, e.BoxMax) = (boxes[0].Min, boxes[0].Max);
    }

    /// <summary>A bay's piece is the game's bay of its size: its part's -z out of its mouth (where it throws craft), its catch box the bay's volume.</summary>
    private static void Bay(in PieceAt a, DesignHangar.BaySize b)
    {
        var e = a.E;
        e.Fn = "bay";
        var y = Vector3.Normalize(a.Y);
        e.Rot = Facing(-y, MathF.Abs(y.Y) > 0.7f ? a.Z : Vector3.UnitY); // (ConstructCarrier.Front: a dock throws along its part's -z)
        // The volume from the mouth's skin in to the back wall, across and along as the page draws it (DesignHangar.Extent), as a box in
        // the ship's frame round those corners (its size kept in Exhaust, a direction turned as the part is: BayFinish reads it back).
        var (ex, ey, ez) = DesignHangar.Extent(b, a.W);
        var (x, z) = (Vector3.Normalize(a.X), Vector3.Normalize(a.Z));
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var k = 0; k < 8; k++)
        {
            var c = a.O + x * (float)(((k & 1) == 0 ? -0.5 : 0.5) * ex) - y * (float)((k & 2) == 0 ? 0 : ey) + z * (float)(((k & 4) == 0 ? -0.5 : 0.5) * ez);
            (lo, hi) = (Vector3.Min(lo, c), Vector3.Max(hi, c));
        }
        e.ThrustAt = (lo + hi) * 0.5f;
        e.Exhaust = hi - lo;
    }

    /// <summary>The game's bay a design's bay is: the smallest Hangar Bay whose dock side holds its volume (Bay keeps it in Exhaust), so a
    /// bay made bigger in the designer catches and holds bigger craft; past the Large's side, the Large (BayFinish holds it to that).</summary>
    private static string BayPart(Element e)
    {
        var side = MathF.Max(MathF.Abs(e.Exhaust.X), MathF.Max(MathF.Abs(e.Exhaust.Y), MathF.Abs(e.Exhaust.Z)));
        foreach (var b in DesignHangar.Bays)
            if (ConstructParts.Find(b.Part) is { } d && ConstructCargo.SideLimit(d) >= side - 0.05f)
                return b.Part;
        return DesignHangar.Bays[^1].Part;
    }

    private static void BayFinish(Element e, ConstructPartDef d)
    {
        var size = Vector3.Abs(e.Exhaust);
        var limit = ConstructCargo.SideLimit(d);
        var want = size;
        size = Vector3.Min(size, new Vector3(limit));
        if (Vector3.Distance(want, size) > 0.05f)
            Console.WriteLine($"[design] the {e.Name} {e.Id}'s catch box is {want.X:0.#} x {want.Y:0.#} x {want.Z:0.#} m, past its part's {limit:0} m side: held to {size.X:0.#} x {size.Y:0.#} x {size.Z:0.#}");
        d.Config = [.. d.Config.Select(x => x.Id switch
        {
            CfgKey.SizeX => x.Copy(dflt: Math.Clamp(size.X, x.Min, x.Max)),
            CfgKey.SizeY => x.Copy(dflt: Math.Clamp(size.Y, x.Min, x.Max)),
            CfgKey.SizeZ => x.Copy(dflt: Math.Clamp(size.Z, x.Min, x.Max)),
            CfgKey.AtX => x.Copy(dflt: Math.Clamp(e.ThrustAt.X, x.Min, x.Max)),
            CfgKey.AtY => x.Copy(dflt: Math.Clamp(e.ThrustAt.Y, x.Min, x.Max)),
            CfgKey.AtZ => x.Copy(dflt: Math.Clamp(e.ThrustAt.Z, x.Min, x.Max)),
            _ => x,
        })];
    }
}
