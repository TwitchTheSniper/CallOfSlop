namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The workbench list names every row right (developer report 2026-10-27: after opening one Hellwheel build and then
    /// another, rows read "Barrel 2 / Standard Barrel" while their pictures and colours were a muzzle and an upgrade). The
    /// list caches its labels and names per model version, and every newly opened build's model started at version 1, so
    /// the second build drew the first one's text. Checks: versions are unique across models; opening the reported pair
    /// of builds one after the other, then every receiver with every socket filled (each a fresh model), the label and
    /// name the list draws for each row are its own socket (the label names the socket's kind) and its own part.
    /// </summary>
    private static void CheckWorkbenchNames()
    {
        var a = new BenchModel(BuildOf("Rifle Grip"), BenchStock.Unlimited);
        var b = new BenchModel(BuildOf("Pistol Grip"), BenchStock.Unlimited);
        if (a.Version == b.Version)
            Fail($"workbench names: two newly opened builds share the model version {a.Version} (the list's cache would keep the first one's rows)");

        var ui = new Ui();
        var checkedRows = 0;
        var opened = 0;
        void Open(WeaponBuild build, string what)
        {
            ui.WorkbenchOpenForTest(build);
            opened++;
            var m = ui.WorkbenchModelForTest!;
            var n = m.Rows.Count;
            for (var i = 0; i < n; i++)
            {
                var r = m.Rows[i];
                var (rows, label, name) = ui.WorkbenchRowTextForTest(i);
                var wantName = BenchModel.RowName(r);
                var wantLabel = BenchModel.RowLabel(r);
                if (rows != n || label != wantLabel || name != wantName)
                {
                    Fail($"workbench names: {what}, row {i} reads \"{label}: {name}\" but holds \"{wantLabel}: {wantName}\"");
                    return;
                }
                if (r.Kind == BenchRowKind.Part && r.Node!.Part.Name != name)
                    Fail($"workbench names: {what}, row {i} names {name} but its part is {r.Node.Part.Name}");
                if (r.Socket != null && !label.ToLowerInvariant().Replace(' ', '_').StartsWith(r.Socket.Type, StringComparison.Ordinal))
                    Fail($"workbench names: {what}, row {i} is labelled \"{label}\" for a {r.Socket.Type} socket");
                if (r.Kind == BenchRowKind.Part && r.Socket != null && r.Node!.ParentSocket != r.Socket)
                    Fail($"workbench names: {what}, row {i} ({label}) shows a part from the {r.Node.ParentSocket?.Name} socket");
                checkedRows++;
            }
        }

        // The reported pair: a six-barrel Hellwheel, then a one-barrel Hellwheel with a muzzle, an upgrade, a foregrip and a stock.
        Open(BuildOf("Rifle Grip{receiver_1:Hellwheel Rotary Receiver{barrel_1:Standard Barrel,barrel_2:Standard Barrel,barrel_3:Standard Barrel,barrel_4:Standard Barrel,barrel_5:Standard Barrel,barrel_6:Standard Barrel,feed:Backpack Hopper,ammo:Armour Piercing,action:Electric Rotary Drive},foregrip:Chaingun Handle}"), "six-barrel Hellwheel");
        Open(BuildOf("Rifle Grip{receiver_1:Hellwheel Rotary Receiver{barrel_1:Standard Barrel{muzzle:Flash Hider},feed:Backpack Hopper,ammo:Armour Piercing,action:Electric Rotary Drive,upgrade_rear:Overclock Core},foregrip:Chaingun Handle,stock:Gyro Stock}"), "Hellwheel opened after the six-barrel one");

        // Every receiver, every socket filled with the first part (by name) that fits it, each opened after the last.
        var grips = Parts.All.Where(p => p.Frame != null && !p.IsMeleePart).OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        var fillers = Parts.All.Where(p => !p.IsRoot && p.Kind == BuildKind.Gun).OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        var receivers = 0;
        foreach (var rp in Parts.InCategory(PartCategory.Receiver).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            WeaponBuild? build = null;
            foreach (var g in grips)
            {
                var root = new BuildNode(g);
                if (g.Sockets.FirstOrDefault(s => s.Type == "receiver" && BuildRules.SocketWhy(root, s, rp) == null) is { } rs)
                {
                    build = new WeaponBuild(rp.Name, g);
                    var recv = build.Root!.Attach(rs.Name, rp);
                    foreach (var s in rp.Sockets)
                        if (fillers.FirstOrDefault(p => BuildRules.SocketWhy(recv, s, p) == null) is { } fit)
                            recv.Attach(s.Name, fit);
                    break;
                }
            }
            if (build == null)
                continue;
            receivers++;
            Open(build, $"{rp.Name} with every socket filled");
        }
        Line($"workbench names: {opened} builds opened in turn ({receivers} receivers, every socket filled), {checkedRows} rows checked");
        if (receivers < 10)
            Fail($"workbench names: only {receivers} receivers could be opened on a grip");
    }
}
