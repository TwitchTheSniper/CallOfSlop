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
        if (d.IsBakeOven)
            ConstructBakeOven.Screen(c, part, st, card);
        else if (d.IsRefiner)
            Refiner(c, part, d, st, card);
        else if (d.IsAssembler)
            Assembler(p, d, st, card, ConstructAssembling.FitOf(c, part));
        else if (d.IsExtruder)
        {
            if (d.IsLaserCutter)
                Cutter(c, part, st, card);
            else if (d.IsPanelFormer)
                Former(c, part, d, st, card);
            else
                Extruder(c, part, d, st, card);
        }
        else if (d.IsRecycler)
            Recycler(world, c, part, d, st, card);
        for (var l = 0; l < PanelCard.MaxLines; l++)
            card.Clip(l, card.Cols); // (nothing cut off without a mark: a long line ends in "..")
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

    private static void Refiner(Construct c, int part, ConstructPartDef d, CStore st, PanelCard card)
    {
        ref readonly var p = ref c.Parts[part];
        var names = RecyclingTable.Current;
        var state = ConstructRefining.StateOf(RefiningTable.Current, c, part, st, out var r);
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
        // The recipe line: the one set (the Recipe button under the lines picks it), else Auto and what the input makes.
        var set = (int)ConstructConfig.Get(p, d, CfgKey.Recipe, 0);
        if (set > 0 && set <= d.RefineChoices.Length)
            card.Add(1, "Recipe ").Add(1, d.RefineChoices[set - 1].OutputName);
        else if (state == RefineState.Short && !st.Busy && r!.Mix.Length == 0)
            card.Add(1, "Batch ").Add(1, r.UnitsIn).Add(1, ' ').Add(1, r.InputName);
        else if (r != null)
            card.Add(1, "Auto: ").Add(1, r.OutputName);
        else
            card.Add(1, "Recipe Auto");
        if (d.RefineMix)
        {
            Amount(card, 2, "In ", st.MixN > 0 ? st.MixAmount![0] : 0, st.MixN > 0 ? names.NameOf(st.MixMat![0]) : "");
            if (st.MixN > 1)
                card.Add(2, " +").Add(2, st.MixN - 1);
        }
        else
            Amount(card, 2, "In ", st.InAmount, names.NameOf(st.InMaterial));
        Amount(card, 3, "Out ", st.Amount, names.NameOf(st.Material));
        card.Bar = Math.Clamp(st.Amount / (float)Math.Max(1, ConstructStorage.Capacity(c, part)), 0, 1);
        if (d.BlockSquares && MachineBlocks.TryOf(c, part, out var asm) && asm.Core == part)
            Squares(c, asm, card, 4);
    }

    /// <summary>A squares Core's squares by size and its rate on one line ("3x3 2x2 4x 1x1 x24"), built with no string made.</summary>
    private static void Squares(Construct c, in CMachineAsm asm, PanelCard card, int line)
    {
        var list = MachineBlocks.Squares(c, asm);
        for (var n = MachineBlocks.MaxSquare; n >= 1; n--)
        {
            var k = 0;
            foreach (ref readonly var q in list)
                k += q.N == n ? 1 : 0;
            if (k == 0)
                continue;
            if (k > 1)
                card.Add(line, k).Add(line, "x ");
            card.Add(line, n).Add(line, 'x').Add(line, n).Add(line, ' ');
        }
        var r4 = Math.Max(4, (int)asm.R4);
        card.Add(line, 'x').Add(line, r4 / 4);
        if (r4 % 4 != 0)
            card.Add(line, r4 % 4 == 2 ? ".5" : r4 % 4 == 1 ? ".25" : ".75");
        if (asm.Broken > 0)
            card.Add(line, ", ").Add(line, asm.Broken).Add(line, " wrecked");
    }

    /// <summary>The first upgrade module in <paramref name="bits"/> (MachineBlocks.TierPieceNames), for a screen line.</summary>
    private static string FirstModule(byte bits)
    {
        for (var k = 2; k <= MachineBlocks.MaxTier; k++)
            if ((bits & 1 << k) != 0)
                return MachineBlocks.TierPieceNames[k];
        return "";
    }

    private static void Assembler(in CPart p, ConstructPartDef d, CStore st, PanelCard card, in AsmFit fit)
    {
        var r = ConstructAssembling.RecipeOf(AssemblingTable.Current, p, d);
        var state = ConstructAssembling.StateOf(p, d, st, r, fit);
        var names = RecyclingTable.Current;
        var line = 2;
        if (st.Held)
            Say(card, PanelState.Idle, "Held off");
        else if (st.Busy && st.Blocked == 0)
            Say(card, PanelState.Working, "Building");
        else
            switch (state)
            {
                case AsmState.NoRecipe: Say(card, PanelState.Wants, "No recipe set"); break; // (why it sits idle, plainly; line 1 says how to set one)
                case AsmState.NoOrders: Say(card, PanelState.Idle, "Waiting for orders"); break;
                case AsmState.TierLow: card.State = PanelState.Refused; card.Add(0, "Needs tier ").Add(0, r!.MinTier); break;
                case AsmState.NeedsModule: card.State = PanelState.Refused; card.Add(0, "Needs a module"); card.Add(2, FirstModule(fit.Missing(r!))); line = 3; break;
                case AsmState.NeedsTool: card.State = PanelState.Refused; card.Add(0, "Needs a tool"); card.Add(2, AsmFit.ToolBlockName(r!.ToolId)); line = 3; break;
                case AsmState.TooBig: card.State = PanelState.Refused; card.Add(0, "Too big"); card.Add(2, fit.W == 0 ? "Weld bed blocks" : "Bigger bed or masts"); line = 3; break;
                case AsmState.Pressed: card.State = PanelState.Refused; card.Add(0, "Pressed now"); card.Add(2, "Use a Press: ").Add(2, r!.PressedDie()); line = 3; break;
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
                case AsmState.Blocked:
                    Say(card, st.Blocked == (byte)AsmBlock.ConveyorSmall ? PanelState.Refused : st.Blocked == (byte)AsmBlock.ConveyorCut ? PanelState.Fault : PanelState.Wants, "Blocked");
                    card.Add(2, ConstructAssembling.BlockedWord(st));
                    line = 3;
                    break;
                default: Say(card, PanelState.Fault, "No power"); break;
            }
        if (r != null)
            card.Add(1, r.Name);
        else if (ConstructConfig.Get(p, d, CfgKey.Recipe, 0) < 0.5f)
            card.Add(1, state == AsmState.NoRecipe && !st.Held ? "Press Recipe below" : "No recipe set"); // (held off or not, an unset recipe is said)
        if (d.IsPanelFormer)
        {
            // A Panel Former works orders only: its stock, the preset its Queue button orders, then the queue (or how to give it one).
            Amount(card, 1, "Stock ", st.Amount, RecyclingTable.Current.NameOf(st.Material));
            card.Add(2, "Preset: ").Add(2, HullLoft.PanelPresets[Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.Recipe, 0), 0, HullLoft.PanelPresets.Length - 1)].Label);
            if (p.Orders != null)
                WorkOrders.FillQueue(p, card, 3);
            else
                card.Add(3, "No orders: Queue, or a bill");
            card.Bar = st.Busy ? st.Progress / 100f : -1;
            return;
        }
        if (p.Orders != null)
        {
            // Work orders (WorkOrders.cs): what it builds and what it lacks, then the queue: its totals and the next orders (the buttons under them).
            WorkOrders.FillQueue(p, card, line == 3 ? 3 : 2);
            card.Bar = -1;
            return;
        }
        card.Add(line, "Tray ").Add(line, st.Items);
        card.Bar = st.Busy ? st.Progress / 100f : 0;
    }

    /// <summary>A Laser Cutter's screen (ConstructLaserCutter.cs): its state, the offcut bin and its tenths, the blank on the bed, then the queue.</summary>
    private static void Cutter(Construct c, int part, CStore st, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        if (st.Held)
            Say(card, PanelState.Idle, "Held off");
        else
            switch ((ExtrudeStall)st.Refused)
            {
                case ExtrudeStall.NoPower: Say(card, PanelState.Fault, "No power"); break;
                case ExtrudeStall.NoMaterial: Say(card, PanelState.Wants, "Needs its blank"); break;
                case ExtrudeStall.BadShape: Say(card, PanelState.Refused, "Not a cut part"); break;
                case ExtrudeStall.OffcutFull: Say(card, PanelState.Wants, "Offcut bin full"); break;
                case ExtrudeStall.Blocked: Say(card, PanelState.Wants, "Blocked"); break;
                case ExtrudeStall.ConveyorSmall: Say(card, PanelState.Refused, "Conveyor too narrow"); break;
                case ExtrudeStall.ConveyorFull: Say(card, PanelState.Wants, "Conveyor full"); break;
                case ExtrudeStall.ConveyorCut: Say(card, PanelState.Fault, "Conveyor cut"); break;
                case ExtrudeStall.NoOrders: Say(card, PanelState.Idle, "Waiting for orders"); break;
                default: Say(card, st.Busy ? PanelState.Working : PanelState.Idle, st.Busy ? "Cutting" : "Waiting"); break;
            }
        Amount(card, 1, "Offcut ", st.Amount, RecyclingTable.Current.NameOf(st.Material));
        if (st.InAmount > 0)
            card.Add(1, " +0.").Add(1, st.InAmount);
        card.Add(2, st.OnBed ? "Blank on the bed" : "Bed empty");
        if (p.Orders != null)
            WorkOrders.FillQueue(p, card, 3);
        else
            card.Add(3, "No orders: a designer, a terminal or a bill");
        card.Bar = st.Busy ? st.Progress / 100f : -1;
    }

    /// <summary>
    /// A Panel Former's screen (ConstructPanelFormer.cs): its state, the blank its head order needs (or that it lies on the bed), the offcut
    /// bin, the preset its Queue orders, then the queue. The blank's line allocates only when the head order changes (the card's text is kept).
    /// </summary>
    private static void Former(Construct c, int part, ConstructPartDef d, CStore st, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        if (st.Held)
            Say(card, PanelState.Idle, "Held off");
        else
            switch ((ExtrudeStall)st.Refused)
            {
                case ExtrudeStall.NoPower: Say(card, PanelState.Fault, "No power"); break;
                case ExtrudeStall.NoMaterial: Say(card, PanelState.Wants, "Needs its blank"); break;
                case ExtrudeStall.BadShape: Say(card, PanelState.Refused, "Not a panel"); break;
                case ExtrudeStall.OffcutFull: Say(card, PanelState.Wants, "Offcut bin full"); break;
                case ExtrudeStall.Blocked: Say(card, PanelState.Wants, "Blocked"); break;
                case ExtrudeStall.ConveyorSmall: Say(card, PanelState.Refused, "Conveyor too narrow"); break;
                case ExtrudeStall.ConveyorFull: Say(card, PanelState.Wants, "Conveyor full"); break;
                case ExtrudeStall.ConveyorCut: Say(card, PanelState.Fault, "Conveyor cut"); break;
                case ExtrudeStall.NoOrders: Say(card, PanelState.Idle, "Waiting for orders"); break;
                case ExtrudeStall.OrderTooLong: Say(card, PanelState.Refused, d.BlockRole == BlockRole.Core ? "Bed can't take it" : "Too large"); break;
                default: Say(card, st.Busy ? PanelState.Working : PanelState.Idle, st.Busy ? "Forming" : "Waiting"); break;
            }
        if (st.OnBed)
            card.Add(1, "Blank on the bed");
        else if (WorkOrders.HeadOf(p, out var head) && head.Name != null && ConstructParts.Find(head.Name) is { } panel && ConstructPanelFormer.FlatOf(panel, out var f))
            card.Add(1, "Blank ").AddFixed(1, f.Length, 2).Add(1, " x ").AddFixed(1, f.Width, 2).Add(1, " m, ").Add(1, f.ThickCm).Add(1, " cm"); // (the flat size: up to 25% over takes it)
        else
            card.Add(1, "Bed empty");
        Amount(card, 2, "Offcut ", st.Amount, RecyclingTable.Current.NameOf(st.Material));
        var from = 3;
        if (d.CfgSlot[(int)CfgKey.Recipe] is >= 0 and var ps && d.Config[ps].Options.Length > 0)
        {
            card.Add(from, "Preset ").Add(from, d.Config[ps].Options[Math.Clamp((int)p.Cfg[ps], 0, d.Config[ps].Options.Length - 1)]); // (what its Queue orders; the Preset button picks it)
            from++;
        }
        if (p.Orders != null)
            WorkOrders.FillQueue(p, card, from);
        else
            card.Add(from, "No orders: Queue, or a bill");
        card.Bar = st.Busy ? st.Progress / 100f : -1;
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
                case ExtrudeStall.NoMaterial: Say(card, PanelState.Wants, d.IsPanelFormer ? "Needs sheet" : "Needs material"); break;
                case ExtrudeStall.BadMaterial: Say(card, PanelState.Refused, "Wrong material"); break;
                case ExtrudeStall.Mixed: Say(card, PanelState.Wants, "Other material"); break;
                case ExtrudeStall.BadShape: Say(card, PanelState.Refused, d.IsPanelFormer ? "Not a panel" : "Bad section"); break;
                case ExtrudeStall.AtCap: Say(card, PanelState.Wants, "At full size"); break;
                case ExtrudeStall.Blocked: Say(card, PanelState.Wants, "Blocked"); break;
                case ExtrudeStall.TooHeavy: Say(card, PanelState.Wants, "Path too heavy"); break;
                case ExtrudeStall.ChuteWaits: Say(card, PanelState.Wants, "Chute waits"); break;
                case ExtrudeStall.ConveyorSmall: Say(card, PanelState.Refused, "Conveyor too narrow"); break;
                case ExtrudeStall.ConveyorFull: Say(card, PanelState.Wants, "Conveyor full"); break;
                case ExtrudeStall.ConveyorCut: Say(card, PanelState.Fault, "Conveyor cut"); break;
                case ExtrudeStall.NoOrders: Say(card, PanelState.Idle, "Waiting for orders"); break;
                case ExtrudeStall.OrderMaterial: Say(card, PanelState.Wants, "Order needs other stock"); break;
                case ExtrudeStall.OrderTooLong: Say(card, PanelState.Refused, d.IsPanelFormer && d.BlockRole == BlockRole.Core ? "Bed can't take it" : "Order too long"); break;
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
            var from = 2;
            if (d.IsPanelFormer && d.CfgSlot[(int)CfgKey.Recipe] is >= 0 and var pss && d.Config[pss].Options.Length > 0)
            {
                card.Add(from, "Preset ").Add(from, d.Config[pss].Options[Math.Clamp((int)p.Cfg[pss], 0, d.Config[pss].Options.Length - 1)]); // (what its Queue orders; the Preset button picks it)
                from++;
            }
            if (ConstructExtruder.ProductOf(c, part) is var piece and >= 0 && c.Parts[piece].D is { IsExtrusion: true } od)
            {
                var l = from++;
                if (od.ExKey.Shape == ExShape.Fan)
                    card.Add(l, "Prop ").AddFixed(l, od.ExKey.H / 100f, 2).Add(l, " m");
                else if (od.ExKey.Shape == ExShape.Wing)
                    card.Add(l, "Span ").AddFixed(l, od.ExKey.L / 100f, 2).Add(l, " m");
                else if (wheel)
                    card.Add(l, "Wheel ").AddFixed(l, od.ExKey.W / 100f, 2).Add(l, " x ").AddFixed(l, od.ExKey.H / 100f, 2).Add(l, " m");
                else
                    card.Add(l, "Piece ").AddFixed(l, od.ExKey.L / 100f, 2).Add(l, " m");
            }
            WorkOrders.FillQueue(p, card, from); // (the queue straight under the stock when nothing is on the die)
            card.Bar = -1;
            return;
        }
        if (d.IsPanelFormer && d.CfgSlot[(int)CfgKey.Recipe] is >= 0 and var ps && d.Config[ps].Options.Length > 0)
            card.Add(1, "Preset ").Add(1, d.Config[ps].Options[Math.Clamp((int)p.Cfg[ps], 0, d.Config[ps].Options.Length - 1)]);
        else if (ConstructExtruder.FanSet(p, d))
            card.Add(1, (int)ConstructConfig.Get(p, d, CfgKey.FanKind, 0) is var fk && fk >= 0 && fk < ExtrusionTable.Current.FanKinds.Length ? ExtrusionTable.Current.FanKinds[fk].Label : "Fan"); // (what it makes: its Recipe button's pick)
        else if (ConstructExtruder.WingSet(p, d))
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
                if (on.ExKey.Shape == ExShape.Fan)
                    card.Add(3, "Across ").AddFixed(3, on.ExKey.H / 100f, 2).Add(3, " m");
                else if (on.ExKey.Shape == ExShape.Wing)
                    card.AddFixed(3, on.ExKey.W / 100f, 2).Add(3, " x ").AddFixed(3, on.ExKey.L / 100f, 2).Add(3, " m");
                else
                    card.AddFixed(3, on.ExKey.W / 100f, 2).Add(3, " x ").AddFixed(3, on.ExKey.H / 100f, 2).Add(3, " m");
                card.Bar = Math.Clamp(st.Amount / (float)Math.Max(1, ConstructStorage.Capacity(c, part)), 0, 1);
            }
            else
            {
                var most = ConstructExtruder.MaxLength(c, part);
                card.AddFixed(3, on.ExKey.L / 100f, 2).Add(3, " of ").AddFixed(3, most, 0).Add(3, " m");
                card.Bar = Math.Clamp(on.ExKey.L / 100f / MathF.Max(0.01f, most), 0, 1);
            }
        }
        else
            card.Bar = Math.Clamp(st.Amount / (float)Math.Max(1, ConstructStorage.Capacity(c, part)), 0, 1);
    }

    private static void Recycler(ConstructWorld world, Construct c, int part, ConstructPartDef d, CStore st, PanelCard card)
    {
        // The line's state word (as its lamp and a console say it), then the reason in plain words.
        var s = ProductionLines.Of(world, c, part, st, ConstructRender.Now);
        Say(card, ProductionLines.Panel(s), s == LineState.Running ? "Recycling" : ProductionLines.Word(s));
        card.Add(1, s switch
        {
            LineState.Running => "Breaking a wreck",
            LineState.Blocked => "Empty its output",
            LineState.Idle => "Run wire is off",
            LineState.NoPower => "Needs power",
            _ => "Drop wrecks in",
        });
        Amount(card, 2, "Out ", st.Amount, RecyclingTable.Current.NameOf(st.Material));
        card.Bar = st.Busy ? st.Progress / 100f : 0;
    }

    /// <summary>A gathering module's card: it has no screen, so this is what a monitor on its Data out shows (its state, why, its bin).</summary>
    public static void Gatherer(ConstructWorld world, Construct c, int part, ConstructPartDef d, PanelCard card)
    {
        if (world.StoreOf(c, part) is not { } st)
        {
            card.State = PanelState.Off;
            return;
        }
        var s = ProductionLines.Of(world, c, part, st, ConstructRender.Now);
        Say(card, ProductionLines.Panel(s), s == LineState.Running ? d.GatherSaw ? "Cutting" : "Drilling" : ProductionLines.Word(s));
        card.Add(1, s switch
        {
            LineState.Running => d.Name,
            LineState.Idle => "Run wire is off",
            LineState.NoPower => st.Drive is ShaftState.None or ShaftState.Turning ? "Needs power" : "Shaft not turning",
            _ => ConstructGatherer.StallWord(d, (GatherStall)st.Blocked),
        });
        Amount(card, 2, "Bin ", st.Amount, RecyclingTable.Current.NameOf(st.Material));
        card.Bar = Math.Clamp(st.Amount / (float)Math.Max(1, d.BulkCapacity), 0, 1);
        for (var l = 0; l < PanelCard.MaxLines; l++)
            card.Clip(l, card.Cols);
    }
}
