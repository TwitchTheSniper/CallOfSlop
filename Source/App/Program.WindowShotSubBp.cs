namespace CallOfSlop;

/// <summary>
/// The window tool's <c>subbp</c> verb (docs/sandbox/design/sub-blueprints.md): every Building Station of the first one's body at
/// once. <c>subbp tree</c> prints each station's job, its sub-job header and its uses' rows as this peer has them; <c>subbp stock</c>
/// (host) puts what every building station lacks into its hold; <c>subbp bodies</c> lists bodies with sub-blueprint uses.
/// </summary>
public static unsafe partial class Program
{
    private static void WsSubBp(Game g, string arg)
    {
        var (sc, _) = WsFind(g, "Building Station");
        if (sc == null)
        {
            WsFail("subbp: no Building Station");
            return;
        }
        switch (arg.Trim())
        {
            case "stock" when g.IsServer:
                for (var t = 0; t < sc.PartCount; t++)
                    if (sc.DefOf(t).IsBuildStation && sc.Parts[t].Station is { State: StationState.Building })
                        WsSay($"subbp: station {t}: stock: {StationProbe.Stock(g.Constructs, sc, t)}");
                break;
            case "cut" when g.IsServer: // host: the last part off every finished sub-job's body (a delivery shot up on its way)
            case "carry" when g.IsServer: // host, a server shortcut: every finished sub-job's body set down beside its main station's bay (a crane's or a player's carry)
                for (var t = 0; t < sc.PartCount; t++)
                    if (sc.Parts[t].Station is { Role: 1, State: StationState.Done } sj && g.Constructs.Get(sj.Body) is { } done)
                    {
                        if (arg.Trim() == "cut")
                        {
                            var doomed = new bool[done.PartCount];
                            doomed[^1] = true;
                            g.Constructs.RemoveParts(done, doomed);
                            WsSay($"subbp: station {t}: its body {done.Id} lost a part: {done.PartCount} left");
                            continue;
                        }
                        ConstructStation.Bay(sc, sj.RootPart, out var floor, out var rot, out var size);
                        done.HeldBy = 0;
                        done.Pos = done.PrevPos = done.NetPos = done.NetFromPos = floor + System.Numerics.Vector3.Transform(new System.Numerics.Vector3(size.X * 0.5f + 1.5f, 0.3f, 0), rot);
                        done.Wake();
                        g.Constructs.StructureDirty.Add(done.Id);
                        WsSay($"subbp: station {t}: its body {done.Id} set down beside station {sj.RootPart}'s bay");
                    }
                break;
            case "plan": // each body with uses as the save window plans it: which uses stay uses
                foreach (var b in g.Constructs.Bodies)
                    if (b.Uses is { Count: > 0 } && b.PartCount > 1)
                    {
                        var clip = ConstructClip.Of(b, "plan", g.Constructs);
                        var plan = ClipUses.Plan(clip);
                        foreach (var row in plan?.Rows ?? [])
                            WsSay($"subbp: body {b.Id} plan: {row.Name} (tag {row.Tag}, {row.Parts} parts): {(row.Kept ? "kept as a use" : row.Why)}");
                    }
                break;
            case "bodies":
                foreach (var b in g.Constructs.Bodies)
                    if (b.Uses is { Count: > 0 } uses)
                        WsSay($"subbp: body {b.Id}: {b.PartCount} parts, held by {b.HeldBy}, frozen {b.Frozen}, uses: {string.Join(", ", uses.Select(u => $"{u.Name} v{u.Version}{(u.Parent >= 0 ? $" in {u.Parent}" : "")}"))}; part 0: {SubBlueprints.LookOf(b, 0)}");
                break;
            default:
                for (var t = 0; t < sc.PartCount; t++)
                {
                    if (!sc.DefOf(t).IsBuildStation)
                        continue;
                    var j = sc.Parts[t].Station;
                    WsSay($"subbp: station {t} (Sub-jobs {(ConstructStationTree.SubJobsOn(sc, t) ? "Auto" : "Off")}): {ConstructStation.StateText(j)}{(j is { Note.Length: > 0 } ? " - " + j.Note : "")}");
                    if (j is { Role: 1 })
                        WsSay($"subbp:   header: {j.Header}");
                    if (j != null)
                        foreach (var line in ConstructStationTree.Lines(sc, j))
                            WsSay("subbp:   " + line);
                }
                break;
        }
    }
}
