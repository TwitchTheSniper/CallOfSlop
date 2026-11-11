namespace CallOfSlop;

/// <summary>
/// The built-in screens of the production machines (docs/sandbox/CONSTRUCTS.md, "Machine screens"): one panel source, "machine", on a
/// plate the part kit's PartScreens places on each model. Four lines at most of 18 characters: what it is doing on the top line in the
/// state's colour, then its recipe or setting, then what it holds or lacks, and a bar: the job's progress, or how far along the output
/// or the piece is where a job is too short to watch (a batch of a Smelter is about a second).
/// <para>
/// All of it comes from the store and the part, which every peer has, so a client's screen says what the host's does. Filled like every
/// panel, only in range and in view, and laid out again only when the card changes; nothing here allocates.
/// </para>
/// </summary>
public static class MachineScreens
{
    private const int SendSteps = 10; // steps (10 Hz) between sends of a job's progress

    /// <summary>Server, at 10 Hz: a job's progress, sent about once a second, or at once when it stops.</summary>
    public static void SetProgress(ConstructWorld world, CStore st, int percent)
    {
        if (percent > 0 && ++st.ProgressAge < SendSteps)
            return;
        st.ProgressAge = 0;
        var v = (byte)Math.Clamp(percent, 0, 100);
        if (st.Progress == v)
            return;
        st.Progress = v;
        world.StoreDirty.Add(st.Handle);
    }

    public static void Fill(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        LogicDisplays.Ink(p, d, card);
        if (world.StoreOf(c, part) is not { } st)
        {
            card.State = PanelState.Off;
            return;
        }
        if (d.IsRefiner)
            Refiner(d, st, card);
        else if (d.IsAssembler)
            Assembler(p, d, st, card);
        else if (d.IsExtruder)
            Extruder(c, part, d, st, card);
        else if (d.IsRecycler)
            Recycler(world, c, part, d, st, card);
    }

    private static void Say(PanelCard card, PanelState state, string text)
    {
        card.State = state;
        card.Add(0, text);
    }

    private static void Amount(PanelCard card, int line, string label, int amount, string material)
    {
        card.Add(line, label);
        if (amount <= 0)
            card.Add(line, "empty");
        else
            card.Add(line, amount).Add(line, ' ').Add(line, material);
    }

    private static void Refiner(ConstructPartDef d, CStore st, PanelCard card)
    {
        var names = RecyclingTable.Current;
        var state = ConstructRefining.StateOf(RefiningTable.Current, d, st, out var r);
        if (st.Held)
            Say(card, PanelState.Idle, "Held off");
        else if (st.Busy)
            Say(card, PanelState.Working, "Refining");
        else
            switch (state)
            {
                case RefineState.Empty: Say(card, PanelState.Idle, "Needs input"); break;
                case RefineState.NoRecipe: Say(card, PanelState.Refused, "Wrong input"); break;
                case RefineState.TierLow: card.State = PanelState.Refused; card.Add(0, "Needs tier ").Add(0, r!.MinTier); break;
                case RefineState.Short: Say(card, PanelState.Wants, "Needs more"); break;
                case RefineState.OutputMixed: Say(card, PanelState.Wants, "Empty output"); break;
                case RefineState.OutputFull: Say(card, PanelState.Wants, "Output full"); break;
                default: Say(card, PanelState.Fault, "No power"); break;
            }
        if (state == RefineState.Short && !st.Busy && r!.Mix.Length == 0)
            card.Add(1, "Batch ").Add(1, r.UnitsIn).Add(1, ' ').Add(1, r.InputName);
        else if (r != null)
            card.Add(1, "Makes ").Add(1, r.OutputName);
        if (d.RefineMix)
        {
            Amount(card, 2, "In ", st.MixN > 0 ? st.MixAmount![0] : 0, st.MixN > 0 ? names.NameOf(st.MixMat![0]) : "");
            if (st.MixN > 1)
                card.Add(2, " +").Add(2, st.MixN - 1);
        }
        else
            Amount(card, 2, "In ", st.InAmount, names.NameOf(st.InMaterial));
        Amount(card, 3, "Out ", st.Amount, names.NameOf(st.Material));
        card.Bar = Math.Clamp(st.Amount / (float)Math.Max(1, d.BulkCapacity), 0, 1);
    }

    private static void Assembler(in CPart p, ConstructPartDef d, CStore st, PanelCard card)
    {
        var r = ConstructAssembling.RecipeOf(AssemblingTable.Current, p, d);
        var state = ConstructAssembling.StateOf(d, st, r);
        var names = RecyclingTable.Current;
        var line = 2;
        if (st.Held)
            Say(card, PanelState.Idle, "Held off");
        else if (st.Busy && st.Blocked == 0)
            Say(card, PanelState.Working, "Building");
        else
            switch (state)
            {
                case AsmState.NoRecipe: Say(card, PanelState.Idle, "No recipe"); card.Add(2, "Configure it"); line = 3; break;
                case AsmState.TierLow: card.State = PanelState.Refused; card.Add(0, "Needs tier ").Add(0, r!.MinTier); break;
                case AsmState.Untaught: card.State = PanelState.Refused; card.Add(0, "Not taught"); card.Add(2, "A faction teaches it (F2)"); line = 3; break;
                case AsmState.Short:
                    Say(card, PanelState.Wants, "Needs input");
                    if (ConstructAssembling.MissingOf(st, r!, out var m, out var missing))
                    {
                        if (missing != null)
                            card.Add(2, r!.CountOf(missing.Id)).Add(2, ' ').Add(2, missing.Name);
                        else
                            card.Add(2, r!.UnitsOf(m)).Add(2, ' ').Add(2, names.NameOf(m));
                        line = 3;
                    }
                    break;
                case AsmState.OutputFull: Say(card, PanelState.Wants, "Tray full"); break;
                case AsmState.Blocked: Say(card, PanelState.Wants, st.Blocked == (byte)AsmBlock.WorldFull ? "World is full" : "Output blocked"); break;
                default: Say(card, PanelState.Fault, "No power"); break;
            }
        if (r != null)
            card.Add(1, r.Name);
        card.Add(line, "Tray ").Add(line, st.Items);
        card.Bar = st.Busy ? st.Progress / 100f : 0;
    }

    private static void Extruder(Construct c, int part, ConstructPartDef d, CStore st, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var wheel = d.IsWheelFormer;
        if (st.Held)
            Say(card, PanelState.Idle, "Held off");
        else
            switch ((ExtrudeStall)st.Refused)
            {
                case ExtrudeStall.NoPower: Say(card, PanelState.Fault, "No power"); break;
                case ExtrudeStall.NoMaterial: Say(card, PanelState.Wants, "Needs material"); break;
                case ExtrudeStall.BadMaterial: Say(card, PanelState.Refused, "Wrong material"); break;
                case ExtrudeStall.Mixed: Say(card, PanelState.Wants, "Other material"); break;
                case ExtrudeStall.BadShape: Say(card, PanelState.Refused, "Bad section"); break;
                case ExtrudeStall.AtCap: Say(card, PanelState.Wants, "At full size"); break;
                case ExtrudeStall.Blocked: Say(card, PanelState.Wants, "Blocked"); break;
                case ExtrudeStall.TooHeavy: Say(card, PanelState.Wants, "Path too heavy"); break;
                case ExtrudeStall.ChuteWaits: Say(card, PanelState.Wants, "Chute waits"); break;
                case ExtrudeStall.ConveyorSmall: Say(card, PanelState.Refused, "Conveyor too narrow"); break;
                case ExtrudeStall.ConveyorFull: Say(card, PanelState.Wants, "Conveyor full"); break;
                case ExtrudeStall.ConveyorCut: Say(card, PanelState.Fault, "Conveyor cut"); break;
                case ExtrudeStall.NoOrders: Say(card, PanelState.Idle, "Waiting for orders"); break;
                case ExtrudeStall.OrderMaterial: Say(card, PanelState.Wants, "Order needs other stock"); break;
                default:
                    if (st.Busy)
                        Say(card, PanelState.Working, wheel ? "Forming" : "Extruding");
                    else
                        Say(card, PanelState.Idle, "Waiting");
                    break;
            }
        if (p.Orders != null)
        {
            // Work orders (WorkOrders.cs): stock, the piece so far, then the queue: its totals and the next three (the buttons under them).
            Amount(card, 1, "Stock ", st.Amount, RecyclingTable.Current.NameOf(st.Material));
            if (ConstructExtruder.ProductOf(c, part) is var piece and >= 0 && c.Parts[piece].D is { IsExtrusion: true } od)
            {
                if (wheel)
                    card.Add(2, "Wheel ").AddFixed(2, od.ExKey.W / 100f, 2).Add(2, " x ").AddFixed(2, od.ExKey.H / 100f, 2).Add(2, " m");
                else
                    card.Add(2, "Piece ").AddFixed(2, od.ExKey.L / 100f, 2).Add(2, " m");
            }
            WorkOrders.FillQueue(p, card, 3);
            card.Bar = -1;
            return;
        }
        if (d.IsFanFormer)
            card.Add(1, "Fan");
        else if (d.IsAirframeFormer)
            card.Add(1, Extrusion.AirframeOf((ushort)ConstructConfig.Get(p, d, CfgKey.WingKind, 0))?.Label ?? "Wing");
        else if (wheel)
            card.Add(1, ConstructExtruder.ByDepth(p, d) ? "Depth mode" : "Width mode");
        else if (d.CfgSlot[(int)CfgKey.Profile] is >= 0 and var slot && d.Config[slot].Options.Length > 0)
            card.Add(1, d.Config[slot].Options[Math.Clamp((int)p.Cfg[slot], 0, d.Config[slot].Options.Length - 1)]);
        Amount(card, 2, "Stock ", st.Amount, RecyclingTable.Current.NameOf(st.Material));
        var product = ConstructExtruder.ProductOf(c, part);
        if (product >= 0 && c.Parts[product].D is { IsExtrusion: true } on)
        {
            if (wheel)
            {
                card.AddFixed(3, on.ExKey.W / 100f, 2).Add(3, " x ").AddFixed(3, on.ExKey.H / 100f, 2).Add(3, " m");
                card.Bar = Math.Clamp(st.Amount / (float)Math.Max(1, d.BulkCapacity), 0, 1);
            }
            else
            {
                var most = MathF.Min(d.ExtMaxLength, Extrusion.MaxLengthCm / 100f);
                card.AddFixed(3, on.ExKey.L / 100f, 2).Add(3, " of ").AddFixed(3, most, 0).Add(3, " m");
                card.Bar = Math.Clamp(on.ExKey.L / 100f / MathF.Max(0.01f, most), 0, 1);
            }
        }
        else
            card.Bar = Math.Clamp(st.Amount / (float)Math.Max(1, d.BulkCapacity), 0, 1);
    }

    private static void Recycler(ConstructWorld world, Construct c, int part, ConstructPartDef d, CStore st, PanelCard card)
    {
        var powered = ConstructRecycler.Powered(world, c, part);
        if (st.Held)
            Say(card, PanelState.Idle, "Held off");
        else if (st.Amount >= d.BulkCapacity)
            Say(card, PanelState.Wants, "Output full");
        else if (!powered)
            Say(card, PanelState.Fault, "No power");
        else if (st.Busy)
            Say(card, PanelState.Working, "Recycling");
        else
            Say(card, PanelState.Idle, "Waiting");
        card.Add(1, st.Busy ? "Breaking a wreck" : "Drop wrecks in");
        Amount(card, 2, "Out ", st.Amount, RecyclingTable.Current.NameOf(st.Material));
        card.Bar = st.Busy ? st.Progress / 100f : 0;
    }
}
