using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Where each action sits on the gun (the user, 2026-10-23: "Mix where the actions sit on the gun"): trigger groups
    /// and sears under the receiver in front of the grip, the pump slide and ramrod at the front under the barrel, bolts,
    /// levers, cranks, motors and drives on the side. A new action has to be added here.
    /// </summary>
    private static readonly Dictionary<string, string> ActionPlacements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Semi-Auto Trigger"] = "under", ["Full-Auto Sear"] = "under", ["Carbine Sear"] = "under", ["Blowback SMG Sear"] = "under",
        ["Buzzsaw Sear"] = "under", ["Heavy Auto Sear"] = "under", ["Riot Trigger"] = "under", ["Marksman Trigger"] = "under",
        ["Three-Round Burst Group"] = "under", ["Ratchet Burst Group"] = "under", ["Binary Trigger"] = "under",
        ["Hyperburst Group"] = "under", ["Hair Trigger"] = "under", ["Overdrive Sear"] = "under", ["Panic Button"] = "under",
        ["Lob Trigger"] = "under", ["Revolver Hammer"] = "under",
        ["Thunderclap Hammer"] = "under", ["Avalanche Burst Group"] = "under", ["Perpetual Sear"] = "under",
        ["Pump Slide"] = "front", ["Muzzleloader Ramrod"] = "front",
        ["Turn Bolt"] = "side", ["Straight-Pull Bolt"] = "side", ["Lever Action"] = "side", ["Hand-Crank Rotary"] = "side",
        ["Electric Rotary Drive"] = "side", ["Chain Drive Motor"] = "side", ["Autocannon Drive"] = "side",
        ["Bicycle Chain Drive"] = "side", ["Break Action"] = "side",
    };

    /// <summary>
    /// Action placement (docs/zombies/WEAPON-BUILDER.md, "Where actions sit"): every action sits where the table above
    /// says; every receiver's action socket has an "under" and a "front" point and the model puts each action there
    /// (only the model: the build text, stats and sockets are the same); an action under the receiver or at its front
    /// doesn't cut into the grip's handle, a foregrip hanging under the gun, or any magazine that leaves it room in front
    /// of the handle (on the Rifle Grip and the Service Pistol Grip, whose handle all the standard grips share).
    /// Floating boxes for every receiver x action pair: "Workbench actions".
    /// </summary>
    private static void CheckActionPlacement()
    {
        PartDef P(string name) => Parts.Find(name) ?? throw new InvalidOperationException($"action placement test needs the part '{name}'");
        var receivers = Parts.InCategory(PartCategory.Receiver).ToList();
        var actions = Parts.InCategory(PartCategory.Action).ToList();
        var counts = new Dictionary<string, int>();
        foreach (var a in actions)
        {
            var place = a.Placement ?? "side";
            counts[place] = counts.GetValueOrDefault(place) + 1;
            if (!ActionPlacements.TryGetValue(a.Name, out var want))
                Fail($"action placement: the {a.Name} isn't in the placement table (say where it sits: under, front or side)");
            else if (want != place)
                Fail($"action placement: the {a.Name} sits {place}, should sit {want}");
        }
        Line("placements: " + string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")));
        foreach (var name in ActionPlacements.Keys)
            if (Parts.Find(name) == null)
                Fail($"action placement: the table names '{name}', which isn't a part any more");

        // ---- Every receiver has both points, and the model puts the action there ------------------------------------
        var grip = P("Rifle Grip");
        var gripSocket = grip.Sockets.First(s => s.Name == "receiver_1");
        foreach (var r in receivers)
        {
            if (r.Sockets.FirstOrDefault(s => s.Type == "action") is not { } sock)
                continue; // "Workbench actions" fails it
            foreach (var point in new[] { "under", "front" })
                if (!sock.Points.ContainsKey(point))
                    Fail($"action placement: the {r.Name}'s action socket has no \"{point}\" point");
            foreach (var a in actions)
            {
                var b = new WeaponBuild("Placement", grip);
                b.Root!.Attach("receiver_1", r).Attach("action", a);
                var m = BuildModel.Build(b, Vector3.One);
                var want = m.Root.LocalPosition + gripSocket.Pos + sock.PosFor(a);
                var got = m.Root.Find("action_group")?.WorldPosition ?? new Vector3(float.NaN);
                if (!(Vector3.Distance(got, want) < 1e-4f))
                    Fail($"action placement: the {a.Name} on the {r.Name} is drawn at {FmtPos(got)}, its {a.Placement ?? "side"} point is {FmtPos(want)}");
            }
        }

        // ---- Clearance: handle, foregrips under the gun, magazines ---------------------------------------------------
        var feeds = Parts.InCategory(PartCategory.Feed).Where(f => f.Mount == "feed").ToList();
        var foregrips = Parts.All.Where(p => p.Category == PartCategory.Grip && p.Mount == "foregrip"
            && p.Boxes.All(x => x.Pos.Y + x.Size.Y * 0.5f <= 0.001f)).ToList(); // hang below their socket (a carry handle rises through the barrel)
        var placed = actions.Where(a => a.Placement != null).ToList();
        int pairs = 0, exempt = 0;
        foreach (var g in new[] { grip, P("Service Pistol Grip") })
        {
            var rs = g.Sockets.First(s => s.Type == "receiver").Name;
            var fs = g.Sockets.FirstOrDefault(s => s.Type == "foregrip")?.Name;
            ModelBox[] Owned(WeaponBuild b, BuildNode n)
            {
                var m = BuildModel.Build(b, Vector3.One);
                var (from, to) = BuildModel.NodeRange(m, n);
                return m.Mesh.Matrices[from..to].Select(ModelBox.Of).ToArray();
            }
            // The handle: the grip's own boxes round its hand point (which the model moves to the grip anchor).
            var bare = new WeaponBuild("Placement", g);
            var bm = BuildModel.Build(bare, Vector3.One);
            var handle = bm.Mesh.Matrices.Take(bm.Mesh.RootParts).Select(ModelBox.Of).Where(x => x.Contains(BuildModel.GripAnchor)).ToArray();
            if (handle.Length == 0)
            {
                Fail($"action placement: no box of the {g.Name} holds its hand point");
                continue;
            }
            var handleFront = handle.Min(x => x.MinZ);
            var under = new List<ModelBox>();
            if (fs != null)
                foreach (var f in foregrips)
                {
                    var b = new WeaponBuild("Placement", g);
                    under.AddRange(Owned(b, b.Root!.Attach(fs, f)));
                }
            foreach (var r in receivers)
            {
                var mags = new List<(PartDef Feed, ModelBox[] Boxes)>();
                foreach (var f in feeds.Where(f => f.Bores.Length == 0 || f.Bores.Contains(r.Receiver!.Bore)))
                {
                    var b = new WeaponBuild("Placement", g);
                    mags.Add((f, Owned(b, b.Root!.Attach(rs, r).Attach("feed", f))));
                }
                foreach (var a in placed)
                {
                    var b = new WeaponBuild("Placement", g);
                    var ab = Owned(b, b.Root!.Attach(rs, r).Attach("action", a));
                    pairs++;
                    bool Cuts(IEnumerable<ModelBox> other) => ab.Any(x => other.Any(y => x.Touches(y, -0.001f)));
                    var hits = new List<string>();
                    if (Cuts(handle))
                        hits.Add($"the {g.Name}'s handle");
                    if (Cuts(under))
                        hits.Add("a foregrip");
                    var length = ab.Max(x => x.MaxZ) - ab.Min(x => x.MinZ);
                    foreach (var (f, fb) in mags)
                    {
                        if (!Cuts(fb))
                            continue;
                        if (handleFront - fb.Max(x => x.MaxZ) < length + 0.001f)
                            exempt++; // it reaches back to the handle: no room left for a trigger group (drums on pistols)
                        else
                            hits.Add($"the {f.Name}");
                    }
                    if (hits.Count > 0)
                        Fail($"action placement: the {a.Name} on the {r.Name} ({g.Name}) cuts into {string.Join(", ", hits)}");
                }
            }
        }
        Line($"clearance: {pairs} grip x receiver x placed action checks against handle, {foregrips.Count} foregrips and every magazine that fits; "
             + $"{exempt} overlaps with feeds that reach back to the handle (no room for a trigger group in front of it)");
    }
}
