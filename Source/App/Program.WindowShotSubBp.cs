namespace CallOfSlop;

/// <summary>
/// The window tool's <c>subbp</c> verb (docs/sandbox/design/sub-blueprints.md): every Building Station of the first one's body at
/// once. <c>subbp tree</c> prints each station's job, its sub-job header and its uses' rows as this peer has them; <c>subbp stock</c>
/// (host) puts what every building station lacks into its hold; <c>subbp bodies</c> lists bodies with sub-blueprint uses.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>
    /// The window tool's <c>bpui</c> verb: the Blueprint Desk's chain through its own calls. <c>bpui stamp NAME</c> (the Blueprints tab's
    /// Stamp: the ghost comes up; then <c>key Fire</c> places it), <c>bpui selectall [PART]</c> (every part of the body under PART, or of the
    /// biggest body with uses, into the selector's selection; then <c>key SelectSave</c> opens the save window), <c>bpui rows</c> (its Uses
    /// list), <c>bpui save NAME [replace] [lock|live] [newversion|newblueprint NAME2]</c> (its Save, as the window's buttons set), <c>bpui
    /// list</c> (the Blueprints tab's entries, uses and broken check), <c>bpui lock NAME USE on|off</c>, <c>bpui pack NAME</c>.
    /// </summary>
    private static void WsBpUi(Game g, string arg)
    {
        var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = words.Length > 0 ? words[0] : "";
        switch (verb)
        {
            case "stamp" when words.Length > 1:
                Ui.ToolStamp(g, string.Join(' ', words.Skip(1)));
                WsSay($"bpui: stamp {string.Join(' ', words.Skip(1))}: {(Selector.Mode == SelectController.Placing.Stamp ? "placing" : "loading")}");
                break;
            case "selectall":
            {
                Construct? body = null;
                if (words.Length > 1)
                    body = WsFind(g, string.Join(' ', words.Skip(1))).C;
                else
                    foreach (var b in g.Constructs.Bodies)
                        if (b.Uses is { Count: > 0 } && b.HeldBy == 0 && (body == null || b.PartCount > body.PartCount))
                            body = b;
                if (body == null)
                {
                    WsSay("bpui: selectall: no body");
                    break;
                }
                Selector.Selection.Clear();
                for (var i = 0; i < body.PartCount; i++)
                    Selector.Selection.Add(body, i);
                WsSay($"bpui: selectall: {Selector.Selection.Count} parts of body {body.Id}");
                break;
            }
            case "rows":
                WsSay($"bpui: save window {(Ui.BlueprintSaveOpen ? "open" : "closed")}{(Ui.ToolSaveWhy.Length > 0 ? ": " + Ui.ToolSaveWhy : "")}; the selector's frames: {string.Join("; ", UseFrameRender.Labels.Select(l => l.Text))}");
                foreach (var line in Ui.ToolSaveRows())
                    WsSay("bpui: use: " + line);
                break;
            case "save" when words.Length > 1:
            {
                var replace = words.Contains("replace");
                bool? lockAll = words.Contains("lock") ? true : words.Contains("live") ? false : null;
                UseChoice? choice = words.Contains("newversion") ? UseChoice.NewVersion : words.Contains("newblueprint") ? UseChoice.NewBlueprint : null;
                var at = Array.IndexOf(words, "newblueprint");
                WsSay($"bpui: save {words[1]}: {Ui.ToolSave(words[1], replace, lockAll, choice, at > 0 && at + 1 < words.Length ? words[at + 1] : "")}");
                break;
            }
            case "list":
                foreach (var e in PlayerBlueprints.List())
                {
                    var broken = e.Uses.Count > 0 ? SubBlueprints.BrokenWhy(File.ReadAllText(e.File), e.Name + ".json") : "";
                    WsSay($"bpui: {e.Name} ({e.Parts} steps{(e.Version > 0 ? $", v{e.Version}" : "")}){(e.UsesLine.Length > 0 ? " " + e.UsesLine : "")}{(broken.Length > 0 ? " BROKEN: " + broken : "")}");
                }
                break;
            case "lock" when words.Length > 3:
                Ui.ToolSetLock(words[1], int.TryParse(words[2], out var u) ? u : 0, words[3] == "on");
                WsSay("bpui: " + Ui.ToolSaid);
                break;
            case "pack" when words.Length > 1:
                Ui.ToolPack(words[1]);
                WsSay("bpui: " + Ui.ToolSaid);
                break;
            default:
                WsSay("bpui: stamp NAME, selectall [PART], rows, save NAME [replace] [lock|live] [newversion|newblueprint NAME2], list, lock NAME USE on|off, pack NAME");
                break;
        }
    }

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
            case "arms": // every lifter on the stations' body: its shoulder and reach
                for (var t = 0; t < sc.PartCount; t++)
                    if (ConstructLifters.DefOf(sc, t) is { } ld)
                    {
                        ConstructLifters.Frame(sc, t, ld, out _, out _, out var shoulder);
                        WsSay($"subbp: lifter {t} ({sc.DefOf(t).Name}): shoulder {shoulder:0.00}, reach {ld.ReachMin:0.0}-{ld.ReachMax:0.0} m, payload {ld.Payload:0} kg: {ConstructLifters.Look(g.Constructs, sc, t)}");
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
                    ConstructStation.Bay(sc, t, out var bfloor, out _, out var bsize);
                    WsSay($"subbp: station {t} (Sub-jobs {(ConstructStationTree.SubJobsOn(sc, t) ? "Auto" : "Off")}, bay floor {bfloor:0.00} size {bsize:0.0}): {ConstructStation.StateText(j)}{(j is { Note.Length: > 0 } ? " - " + j.Note : "")}");
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
