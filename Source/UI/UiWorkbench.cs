using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// What the workbench asks Program to start: a Workbench Range session with these built weapons (slots 3-4 used once
/// the slot upgrade is on or bought at the range's station), the built melee weapon (null = the standard strike) and
/// how many practice dummies stand down the range.
/// </summary>
public sealed record WorkbenchLaunch(WeaponDef Primary, WeaponDef Secondary, WeaponDef? Third = null, WeaponDef? Fourth = null, MeleeDef? Melee = null,
    bool ExtraSlots = false, int Dummies = 0);

/// <summary>
/// The Weapon Workbench (docs/zombies/WEAPON-BUILDER.md, "The workbench"). A new weapon starts with step 1, the grip
/// picker (cards with a 3D picture, receiver slots, fire style, carry weight and how many you own). Step 2 is the
/// assembly screen: your parts on the left (grouped, counted, searchable, with 3D icons; parts that fit nowhere on the
/// build are dimmed), the gun large in the middle (drag to turn it, wheel to zoom) with its sockets marked, and the build
/// as a clean linear list with live stat bars on the right. Drag a part onto the gun: the sockets it fits light up, it
/// previews snapped in place over one, and snaps in on release (a refused drop bounces back with the reason).
/// Right-click (or drag off) a part to take it off; right-click a receiver to copy its whole assembly onto a free
/// receiver slot, even without the parts: what you're missing shows red on the gun, in the list and on the saved builds,
/// and the build can't be saved or applied until you have them (in a run, missing parts can be crafted with scrap).
/// Double-click a part to put it on its best free socket; Auto-complete fills every empty needed socket; Ctrl+Z undoes.
/// The editing model is <see cref="BenchModel"/> (headless, self-tested); this file only draws it and feeds it input.
/// </summary>
public sealed partial class Ui
{
    /// <summary>The workbench screen is up (opened from the main menu, or at a Zombies workbench).</summary>
    public bool WorkbenchOpen;

    private BenchModel? _bm;

    /// <summary>Editing the melee slot (hilts, heads, mods) instead of a gun slot.</summary>
    private bool _wbMelee;

    /// <summary>Step 1: the grip picker is showing. <see cref="_wbPickKeep"/>: it moves the current build onto the new grip.</summary>
    private bool _wbPicking, _wbPickKeep;

    /// <summary>Sandbox: use a Zombies-like practice stock instead of every part.</summary>
    private bool _wbPractice;

    private BenchStock? _wbPracticeStock;
    private string _wbMessage = "";
    private bool _wbMessageBad;
    private double _wbMessageAt = -100;
    private ImInputString? _wbNameInput, _wbSearchInput;
    private string _wbSearch = "";
    private int _wbFilter = -1; // part category shown (-1 = all)
    private BenchCamera _wbCam = BenchCamera.Default;
    private BuildNode? _wbSel; // the part selected (list row or on the gun)
    private double _wbNow, _wbLastFrame;
    private Audio? _wbAudio;

    // Drag and drop.
    private PartDef? _dragPart;
    private BuildNode? _dragNode; // dragging a part off the gun (a move, or off to take it off)
    private bool _dragArmed, _dragging;
    private float2 _dragStart, _dragHome;
    private bool[] _dragValid = new bool[64];
    private int _dragValidCount;
    private int _hoverSpot = -1, _hoverRow = -1, _hoverRowPrev = -1;
    private bool _ctxShown, _escUsed;
    private Vector3 _camCenterGoal;
    private float _camRadiusGoal = 0.4f;
    private int _camGoalVersion = -1;
    private float2 _invMin, _invMax;
    private (int Version, int Spot, PartDef? Part, BuildNode? Node) _trialKey = (-1, -1, null, null);
    private AssembledWeapon? _trialAsm;
    private PartDef? _hoverPart; // an inventory part under the mouse: stat diffs for its best free socket
    private (int Version, PartDef? Part) _hoverTrialKey = (-1, null);
    private AssembledWeapon? _hoverTrialAsm;

    // Animations: a refused part bouncing back to the list, a fitted part sliding into its socket.
    private PartDef? _bouncePart;
    private float2 _bounceFrom, _bounceTo;
    private double _bounceAt = -10;
    private WeaponDef? _animRest, _animPart;
    private Vector3 _animDir;
    private double _animAt = -10;
    private int _animVersion = -1;

    // The 3D view this frame (picking and markers).
    private float2 _viewMin, _viewMax;
    private Matrix4x4 _viewPvm;
    private bool _viewHovered, _orbiting;
    private float2[] _spotScreen = new float2[64];
    private bool[] _spotOn = new bool[64];

    // The linear list this frame (rows are drop targets too).
    private int[] _rowSpot = new int[64];
    private string[] _rowLabel = new string[64], _rowName = new string[64];
    private int _rowVersion = -1;
    private float4[] _rowRect = new float4[64];
    private int _rowRectCount;
    private float4 _listClip;

    // The right-click menu.
    private BuildNode? _ctxNode;
    private bool _ctxOpen;

    // The inventory list, cached until the build, stock, filter or search changes.
    private readonly List<PartDef> _invParts = new();
    private readonly List<bool> _invFits = new();
    private readonly List<int> _invFree = new();
    private readonly List<PartCategory> _invCats = new();
    private (int Version, int Stock, int Filter, string Search, bool Melee) _invKey = (-1, 0, 0, "", false);

    // Missing-part badges on saved builds and presets.
    private readonly int[] _slotMissing = new int[BuildStore.SlotCount + 1];
    private readonly List<(StandardBuild Build, int Missing)> _presetBadges = new();
    private (int Stock, int Version, bool Melee) _badgeKey = (int.MinValue, -1, false);

    private static readonly float4 Yellow = new(1f, 0.85f, 0.3f, 1f);
    private static readonly ImColor8 Yellow8 = new(255, 216, 77, 255);
    private static readonly ImColor8 Cyan8 = new(90, 210, 255, 255);
    private static readonly PartCategory[] MeleeCategories = [PartCategory.Hilt, PartCategory.Head, PartCategory.MeleeMod];
    private static readonly string[] WbRowIds = Enumerable.Range(0, 512).Select(i => $"##wbrow{i}").ToArray();
    private static readonly string[] WbInvIds = Enumerable.Range(0, 512).Select(i => $"##wbinv{i}").ToArray();
    private static readonly string[] WbCountText = Enumerable.Range(0, 100).Select(i => $"x{i}").ToArray();

    private BenchModel Bm => _bm!;

    // ---- Opening and loading ----------------------------------------------------------------------------------

    public void OpenWorkbench()
    {
        BuildStore.EnsureLoaded();
        WorkbenchOpen = true;
        LoadWorkbenchSlot(BuildStore.Selected);
    }

    /// <summary>Smoke test: shows <paramref name="b"/> on the workbench's assembly screen (not saved).</summary>
    public void SmokeWorkbenchBuild(WeaponBuild b)
    {
        _wbMelee = b.IsMelee;
        StartEditing(b.Clone(), b.Name);
    }

    /// <summary>Smoke test: opens step 1 of a new weapon (the grip picker).</summary>
    public void SmokeWorkbenchPicker(bool melee)
    {
        _wbMelee = melee;
        StartEditing(null, "New Weapon");
    }

    /// <summary>
    /// Smoke test: a practice stock, a one-receiver build on a three-receiver grip, and its receiver assembly copied
    /// without the parts for it (the missing ones show red). Returns the missing-parts line.
    /// </summary>
    public string SmokeWorkbenchCopy(WeaponBuild b, BenchStock stock)
    {
        _wbMelee = false;
        _wbPractice = true;
        _wbPracticeStock = stock;
        StartEditing(b.Clone(), b.Name);
        if (Bm.Build.Receivers.FirstOrDefault() is { } r)
        {
            var why = Bm.CopyReceiver(r, out _);
            WbSay(why ?? $"Copied the {r.Part.Name} assembly. {Bm.Missing.Text()}", why != null);
        }
        return Bm.Missing.Text();
    }

    /// <summary>Switches the editor to the melee slot (or back to the selected gun slot).</summary>
    private void LoadWorkbenchMelee(bool melee)
    {
        if (BenchRun != null)
        {
            BenchLoad(melee ? 4 : 0); // a Zombies workbench: your own slots (UiWorkbenchRun.cs)
            return;
        }
        if (!melee)
        {
            LoadWorkbenchSlot(BuildStore.Selected);
            return;
        }
        _wbMelee = true;
        StartEditing(BuildStore.Melee?.Clone(), "Melee");
    }

    private void LoadWorkbenchSlot(int slot)
    {
        if (BenchRun != null)
        {
            BenchLoad(slot);
            return;
        }
        BuildStore.Selected = slot;
        _wbMelee = false;
        StartEditing(BuildStore.Slots[slot]?.Clone(), $"Build {slot + 1}");
    }

    /// <summary>Starts editing <paramref name="b"/> on the assembly screen, or (null / no grip) a new weapon at step 1.</summary>
    private void StartEditing(WeaponBuild? b, string name)
    {
        var build = b ?? new WeaponBuild { Name = name };
        _bm = new BenchModel(build, WbStock());
        _wbNameInput ??= new ImInputString(33);
        _wbSearchInput ??= new ImInputString(33);
        ImGuiSafe.SetInput(_wbNameInput, build.Name);
        _wbPicking = build.Root == null;
        _wbPickKeep = false;
        _wbSel = null;
        _wbFilter = -1;
        CancelDrag();
        ResetView();
    }

    /// <summary>The parts this workbench builds with: a run's (UiWorkbenchRun.cs), the sandbox's practice stock, or everything.</summary>
    private BenchStock WbStock()
    {
        if (BenchRun != null)
            return BenchRunStock();
        if (!_wbPractice)
            return BenchStock.Unlimited;
        return _wbPracticeStock ??= BenchStock.Practice(Parts.All);
    }

    private void WbSay(string text, bool bad = false)
    {
        _wbMessage = text;
        _wbMessageBad = bad;
        _wbMessageAt = _wbNow;
    }

    private void ResetView()
    {
        _wbCam = BenchCamera.Default;
        _wbCam.Frame(_bm?.Asm.PreviewDef?.BuiltMesh);
        (_camCenterGoal, _camRadiusGoal, _camGoalVersion) = (_wbCam.Center, _wbCam.Radius, _bm?.Version ?? -1);
    }

    /// <summary>The view keeps the whole gun framed as it grows or shrinks: it eases to the new bounds (orbit and zoom stay yours).</summary>
    private void FollowFraming(float dt)
    {
        if (_bm == null)
            return;
        if (_camGoalVersion != _bm.Version)
        {
            var goal = _wbCam;
            goal.Frame(_bm.Asm.PreviewDef?.BuiltMesh);
            (_camCenterGoal, _camRadiusGoal, _camGoalVersion) = (goal.Center, goal.Radius, _bm.Version);
        }
        var k = 1 - MathF.Exp(-dt * 7f);
        _wbCam.Center = Vector3.Lerp(_wbCam.Center, _camCenterGoal, k);
        _wbCam.Radius += (_camRadiusGoal - _wbCam.Radius) * k;
    }

    private void CancelDrag()
    {
        _dragArmed = _dragging = false;
        _dragPart = null;
        _dragNode = null;
        _hoverSpot = -1;
        _orbiting = false;
    }

    /// <summary>The build moved onto another grip (or hilt): receivers go into the same-numbered slots, the rest by socket name where it fits.</summary>
    private static WeaponBuild Regrip(WeaponBuild b, PartDef grip)
    {
        var nb = new WeaponBuild(b.Name, grip); // the fire style comes with the grip
        if (b.Root != null)
            foreach (var (s, c) in b.Root.Children())
                if (grip.Sockets.FirstOrDefault(x => x.Name == s.Name && x.Type == s.Type) is { } ns)
                    nb.Root!.Attach(ns.Name, c.Clone());
        return nb;
    }

    private static float4 WbCol(Vector3 c) => new(c.X, c.Y, c.Z, 1f);

    private static ImColor8 WbC8(Vector3 c, float a = 1f) =>
        new((byte)Math.Clamp((int)(c.X * 255), 0, 255), (byte)Math.Clamp((int)(c.Y * 255), 0, 255), (byte)Math.Clamp((int)(c.Z * 255), 0, 255), (byte)Math.Clamp((int)(a * 255), 0, 255));

    private static ImColor8 WbFade(ImColor8 c, float a)
    {
        var v = c.AsByte4();
        return new ImColor8(v.X, v.Y, v.Z, (byte)Math.Clamp((int)(v.W * a), 0, 255));
    }

    private static string SocketLabel(PartSocket s) =>
        s.Name.Length == 0 ? s.Type : char.ToUpperInvariant(s.Name[0]) + s.Name[1..].Replace('_', ' ');

    private static Vector2 V2(float2 f) => new(f.X, f.Y);

    private static float2 F2(Vector2 v) => new(v.X, v.Y);

    private static bool In(float2 p, float2 min, float2 max) => p.X >= min.X && p.Y >= min.Y && p.X < max.X && p.Y < max.Y;

    // ---- The frame --------------------------------------------------------------------------------------------

    /// <summary>Draws the workbench. Returns a launch request when Test fire is clicked.</summary>
    public WorkbenchLaunch? DrawWorkbench(Audio audio, double now)
    {
        if (_bm == null)
            OpenWorkbench();
        _wbAudio = audio;
        _wbNow = now;
        var dt = (float)Math.Clamp(now - _wbLastFrame, 0, 0.1);
        _wbLastFrame = now;
        _wbCam.Nudge = MathF.Max(0, _wbCam.Nudge - dt);
        FollowFraming(dt);
        var stock = WbStock();
        if (!ReferenceEquals(stock, Bm.Stock))
            Bm.SetStock(stock);
        WorkbenchLaunch? launch = null;
        _loadoutDrawn = true; // keeps the 3D preview alive (see EndFrame)
        Preview = null;
        _hoverPart = null;
        _hoverRowPrev = _hoverRow;
        _hoverRow = -1;
        _rowRectCount = 0;
        _escUsed = false;
        var vp = ImGui.GetMainViewport();
        var size = new float2(MathF.Min(1600 * _scale, vp.WorkSize.X - 30), MathF.Min(900 * _scale, vp.WorkSize.Y - 30));
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X / 2, vp.WorkPos.Y + vp.WorkSize.Y / 2), ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoTitleBar;
        if (ImGui.Begin("##workbench"u8, flags))
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            if (_wbPicking)
                ImGui.GetWindowDrawList().AddRectFilled(winPos, winPos + winSize, new ImColor8(9, 10, 13, 246), 6 * _scale); // no 3D view behind the picker
            DrawWbHeader();
            var avail = ImGui.GetContentRegionAvail();
            var bodyH = avail.Y - 44 * _scale;
            if (_wbPicking)
                DrawGripPicker(avail.X, bodyH, winPos, winSize);
            else
                launch = DrawAssembly(avail.X, bodyH, winPos, winSize);
            DrawWbFooter();
            DrawWbContextMenu();
        }
        ImGui.End();
        if (!WorkbenchOpen)
            return null;
        UpdateDrag();
        DrawDragOverlay();
        if (!_escUsed && !_ctxShown)
            WbShortcuts();
        return launch;
    }

    private void DrawWbHeader()
    {
        ImGui.PushFont(default, 22);
        ImGui.TextColored(Orange, _wbMelee ? "MELEE WORKBENCH"u8 : "WEAPON WORKBENCH"u8);
        ImGui.PopFont();
        ImGui.SameLine();
        var step1 = _wbPicking;
        ImGui.TextColored(step1 ? Orange : Dim, step1 ? "   [1] PICK A GRIP"u8 : "   1 pick a grip"u8);
        ImGui.SameLine();
        ImGui.TextColored(!step1 ? Orange : Dim, !step1 ? "  >  [2] ASSEMBLE"u8 : "  >  2 assemble"u8);
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, BenchRun != null ? "      Zombies run: only the parts you own" : _wbPractice ? "      Sandbox: practice parts (Zombies rules)" : "      Sandbox: every part unlocked");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - 230 * _scale);
        if (ImGui.RadioButton("GUNS"u8, !_wbMelee) && _wbMelee)
            LoadWorkbenchMelee(false);
        ImGui.SameLine();
        if (ImGui.RadioButton("MELEE SLOT"u8, _wbMelee) && !_wbMelee)
            LoadWorkbenchMelee(true);
        ImGui.Separator();
    }

    private void DrawWbFooter()
    {
        if (ImGui.Button(BenchRun != null ? "CLOSE"u8 : "BACK"u8, new float2(150 * _scale, 32 * _scale)))
        {
            WorkbenchOpen = false;
            BenchRun = null;
            CancelDrag();
        }
        ImGui.SameLine();
        var age = _wbNow - _wbMessageAt;
        if (_wbMessage.Length > 0 && age < 8)
        {
            var a = (float)Math.Clamp(1.4 - age / 6, 0.35, 1);
            var c = _wbMessageBad ? Red : Green;
            ImGuiSafe.TextColored(new float4(c.X, c.Y, c.Z, a), _wbMessage);
        }
        else
            ImGuiSafe.TextColored(Dim, _wbPicking
                ? "Click a grip to start. The grip sets how many receivers the weapon holds and how they fire."
                : "Drag parts onto the gun - double-click: best free socket - right-click a part: remove / copy receiver - Ctrl+Z undo, Ctrl+Y redo - A auto-complete - Del remove - N new - G grip - R view");
    }

    // ---- Step 1: the grip picker -------------------------------------------------------------------------------

    private void DrawGripPicker(float width, float height, float2 winPos, float2 winSize)
    {
        var grips = Parts.All.Where(p => _wbMelee ? p.Hilt != null : p.Frame != null);
        ImGui.PushFont(default, 18);
        ImGui.TextColored(Orange, _wbPickKeep ? (_wbMelee ? "CHANGE THE HILT"u8 : "CHANGE THE GRIP"u8) : (_wbMelee ? "STEP 1: PICK A HILT FOR THE NEW MELEE WEAPON"u8 : "STEP 1: PICK A GRIP FOR THE NEW WEAPON"u8));
        ImGui.PopFont();
        ImGuiSafe.TextColored(Dim, _wbPickKeep
            ? "Your parts move onto the new grip: receivers into the same-numbered slots, the rest where the new grip has the socket."
            : _wbMelee ? "The hilt is the handle: it sets the base damage, swing and reach. Heads and mods go on it next."
            : "The grip is the root of the weapon: it sets how many receivers it holds, how they fire and how much it carries. First-tier grips hold one receiver; multi-receiver frames come later in a run.");
        if (Bm.Build.Root != null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Back to the build"u8))
                _wbPicking = false;
        }
        if (!ImGui.BeginChild("##wbgrips"u8, new float2(width, height - 50 * _scale), ImGuiChildFlags.None))
        {
            ImGui.EndChild();
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var cardW = 250 * _scale;
        var cardH = 232 * _scale;
        var gap = 12 * _scale;
        var cols = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + gap) / (cardW + gap)));
        var i = 0;
        foreach (var g in grips)
        {
            if (i % cols != 0)
                ImGui.SameLine(0, gap);
            var p0 = ImGui.GetCursorScreenPos();
            var clicked = ImGui.InvisibleButton(WbInvIds[i & 511], new float2(cardW, cardH));
            var hot = ImGui.IsItemHovered();
            var p1 = p0 + new float2(cardW, cardH);
            var owned = Bm.Stock.Owned(g);
            var have = owned > 0;
            var rc = PartDef.RarityColor(g.Rarity);
            dl.AddRectFilled(p0, p1, hot ? Hover8 : Card8, 6 * _scale);
            dl.AddRectFilled(p0, new float2(p1.X, p0.Y + 4 * _scale), WbC8(rc, have ? 1f : 0.4f), 6 * _scale);
            if (hot)
                dl.AddRect(p0, p1, Orange8, 6 * _scale, ImDrawFlags.None, 2f);
            var imgMin = p0 + new float2(8 * _scale, 10 * _scale);
            var imgMax = new float2(p1.X - 8 * _scale, p0.Y + cardH * 0.5f);
            DrawPartIcon(dl, g, imgMin, imgMax, !have);
            var x = p0.X + 10 * _scale;
            var y = imgMax.Y + 6 * _scale;
            DlText(dl, new float2(x, y), WbC8(rc, have ? 1f : 0.55f), g.Name);
            DlText(dl, new float2(p1.X - 10 * _scale - ImGui.CalcTextSize(PartDef.RarityName(g.Rarity)).X, y), WbC8(rc, 0.8f), PartDef.RarityName(g.Rarity));
            y += 22 * _scale;
            if (g.Frame is { } f)
            {
                var cap = g.ReceiverCapacity;
                ImGui.PushFont(default, 20);
                DlText(dl, new float2(x, y), cap == 1 ? White8 : Orange8, cap == 1 ? "1 RECEIVER SLOT" : $"{cap} RECEIVER SLOTS");
                ImGui.PopFont();
                y += 26 * _scale;
                DlText(dl, new float2(x, y), Dim8, $"{(cap > 1 ? f.Style == FireStyle.Volley ? "Volley (all at once)" : "Rotate (taking turns)" : "Single receiver")}, {f.Hands}-handed");
                y += 18 * _scale;
                DlText(dl, new float2(x, y), Dim8, $"Carries {f.Carry:0} kg{(f.Ads ? "" : ", hip fire only")}{(g.Sockets.Any(s => s.Type == "hopper") ? ", shared feed" : "")}");
            }
            else if (g.Hilt is { } h)
            {
                ImGui.PushFont(default, 20);
                DlText(dl, new float2(x, y), White8, $"{h.Damage:0} DAMAGE");
                ImGui.PopFont();
                y += 26 * _scale;
                DlText(dl, new float2(x, y), Dim8, $"{h.Swing:0.00} s swing, {h.Reach:0.0} m reach, cleave {h.Cleave}");
                y += 18 * _scale;
                DlText(dl, new float2(x, y), Dim8, $"{h.Hands}-handed{(h.Stun > 0 ? $", stun {h.Stun:0.0} s" : "")}");
            }
            var ownTxt = Bm.Stock.IsUnlimited ? "sandbox" : have ? $"{owned} owned" : "none owned";
            DlText(dl, new float2(x, p1.Y - 22 * _scale), have ? Green8 : Red8, ownTxt);
            if (hot)
                ImGuiSafe.SetTooltip($"{g.Name} - {PartDef.RarityName(g.Rarity)}\n{g.Flavor}\n{PartSummary(g)}"
                                     + (have ? "" : "\nYou don't own one: you can plan with it, but it shows as missing until you do."));
            if (clicked)
                PickGrip(g);
            i++;
        }
        ImGui.EndChild();
    }

    private void PickGrip(PartDef grip)
    {
        var old = Bm.Build;
        WeaponBuild nb;
        if (_wbPickKeep && old.Root != null)
            nb = Regrip(old, grip);
        else
            nb = new WeaponBuild(WeaponBuild.CleanName(_wbNameInput?.ToString() ?? old.Name), grip);
        Bm.SetBuild(nb, keepHistory: old.Root != null);
        _wbPicking = false;
        _wbPickKeep = false;
        _wbSel = null;
        ResetView();
        _wbCam.Nudge = 0.3f;
        _wbAudio?.Effect(Sfx.DeployDevice, 0.5f);
        var missing = Bm.Stock.Free(grip, Bm.Build) < 0;
        WbSay(grip.Hilt != null ? $"Melee weapon on the {grip.Name}: drag a head onto it."
            : $"New weapon on the {grip.Name} ({grip.ReceiverCapacity} receiver slot{(grip.ReceiverCapacity == 1 ? "" : "s")}): drag a receiver onto it."
              + (missing ? " (You don't own this grip: it's missing.)" : ""), missing);
    }

    // ---- Step 2: the assembly screen ---------------------------------------------------------------------------

    private WorkbenchLaunch? DrawAssembly(float width, float height, float2 winPos, float2 winSize)
    {
        var leftW = width * 0.22f;
        var rightW = width * 0.3f;
        var midW = width - leftW - rightW - 16 * _scale;
        _invMin = ImGui.GetCursorScreenPos();
        _invMax = _invMin + new float2(leftW, height);
        if (ImGui.BeginChild("##wbinv"u8, new float2(leftW, height), ImGuiChildFlags.Borders))
            DrawInventory(leftW);
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##wbmid"u8, new float2(midW, height), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            DrawCentre(midW, height, winPos, winSize);
        ImGui.EndChild();
        ImGui.SameLine();
        WorkbenchLaunch? launch = null;
        if (ImGui.BeginChild("##wbright"u8, new float2(rightW, height), ImGuiChildFlags.Borders))
        {
            DrawLinear(height * 0.4f);
            DrawStatBars();
            launch = BenchRun != null ? DrawBenchRunSide(Bm.Build) : DrawWorkbenchSide(Bm.Build); // Zombies: your slots, parts and scrap (UiWorkbenchRun.cs)
        }
        ImGui.EndChild();
        return launch;
    }

    // ---- Left: your parts --------------------------------------------------------------------------------------

    private void RefreshInventory()
    {
        var key = (Bm.Version, Bm.Stock.Fingerprint, _wbFilter, _wbSearch, _wbMelee);
        if (key == _invKey)
            return;
        _invKey = key;
        _invParts.Clear();
        _invFits.Clear();
        _invFree.Clear();
        _invCats.Clear();
        foreach (var p in Bm.Stock.Parts(Parts.All))
        {
            if (p.IsRoot || p.IsMeleePart != _wbMelee)
                continue;
            if (!_invCats.Contains(p.Category))
                _invCats.Add(p.Category);
            if (_wbFilter >= 0 && (int)p.Category != _wbFilter)
                continue;
            if (_wbSearch.Length > 0 && !p.Name.Contains(_wbSearch, StringComparison.OrdinalIgnoreCase)
                && !PartDef.CategoryName(p.Category).Contains(_wbSearch, StringComparison.OrdinalIgnoreCase))
                continue;
            var free = Bm.Stock.Free(p, Bm.Build);
            _invParts.Add(p);
            _invFree.Add(free);
            _invFits.Add(free > 0 && Bm.FitsAnywhere(p));
        }
    }

    private void DrawInventory(float width)
    {
        ImGui.TextColored(Orange, BenchRun != null ? "YOUR PARTS"u8 : _wbPractice ? "PRACTICE PARTS"u8 : "PARTS"u8);
        ImGui.SetNextItemWidth(-1);
        try
        {
            if (ImGui.InputTextWithHint("##wbsearch"u8, "Search parts..."u8, _wbSearchInput!))
                _wbSearch = _wbSearchInput!.ToString().Trim();
        }
        catch (Exception ex)
        {
            ImGuiSafe.FieldFailed("part search", ex);
        }
        RefreshInventory();
        // Category chips.
        if (WbChip("All", _wbFilter < 0))
            _wbFilter = -1;
        var x = ImGui.GetItemRectMax().X;
        foreach (var c in _invCats)
        {
            var label = PartDef.CategoryName(c);
            var w = ImGui.CalcTextSize(label).X + 14 * _scale;
            if (x + w + 8 * _scale < ImGui.GetWindowPos().X + width - 8 * _scale)
                ImGui.SameLine();
            if (WbChip(label, _wbFilter == (int)c))
                _wbFilter = _wbFilter == (int)c ? -1 : (int)c;
            x = ImGui.GetItemRectMax().X;
        }
        ImGui.Separator();
        if (!ImGui.BeginChild("##wbinvlist"u8, new float2(0, 0), ImGuiChildFlags.None))
        {
            ImGui.EndChild();
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var rowH = 46 * _scale;
        var rowW = ImGui.GetContentRegionAvail().X;
        if (_invParts.Count == 0)
            ImGui.TextColored(Dim, BenchRun != null ? "No loose parts that fit this list. Zombies drop parts (rarer ones in later rounds); salvage piles give scrap to craft with."u8 : "Nothing matches."u8);
        PartCategory? group = null;
        for (var i = 0; i < _invParts.Count; i++)
        {
            var p = _invParts[i];
            if (p.Category != group)
            {
                group = p.Category;
                ImGui.Spacing();
                ImGuiSafe.TextColored(Dim, PartDef.CategoryName(p.Category).ToUpperInvariant());
            }
            var p0 = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton(WbInvIds[i & 511], new float2(rowW, rowH));
            var hot = ImGui.IsItemHovered();
            var p1 = p0 + new float2(rowW, rowH - 2 * _scale);
            var fits = _invFits[i];
            var free = _invFree[i];
            var dragged = _dragging && _dragNode == null && _dragPart == p;
            dl.AddRectFilled(p0, p1, dragged ? new ImColor8(40, 60, 44, 240) : hot ? Hover8 : Card8, 4 * _scale);
            DrawPartIcon(dl, p, p0 + new float2(3 * _scale, 3 * _scale), new float2(p0.X + 64 * _scale, p1.Y - 3 * _scale), !fits);
            var rc = PartDef.RarityColor(p.Rarity);
            DlText(dl, new float2(p0.X + 70 * _scale, p0.Y + 4 * _scale), fits ? WbC8(rc) : WbC8(rc * 0.55f + new Vector3(0.2f)), p.Name);
            DlText(dl, new float2(p0.X + 70 * _scale, p0.Y + 23 * _scale), fits ? Dim8 : new ImColor8(105, 106, 114, 255),
                p.Bores.Length > 0 ? $"{PartDef.RarityName(p.Rarity)} - {p.Bores[0]}{(p.Bores.Length > 1 ? "+" : "")}" : PartDef.RarityName(p.Rarity));
            if (!Bm.Stock.IsUnlimited)
            {
                var txt = free >= 0 && free < WbCountText.Length ? WbCountText[free] : $"x{free}";
                var tw = ImGui.CalcTextSize(txt).X;
                var b0 = new float2(p1.X - tw - 12 * _scale, p0.Y + 6 * _scale);
                dl.AddRectFilled(b0, b0 + new float2(tw + 8 * _scale, 18 * _scale), free > 0 ? new ImColor8(36, 70, 44, 255) : new ImColor8(80, 30, 28, 255), 3 * _scale);
                DlText(dl, b0 + new float2(4 * _scale, 1 * _scale), free > 0 ? Green8 : Red8, txt);
            }
            if (hot && !_dragging)
            {
                _hoverPart = p;
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                var tip = $"{p.Name} - {PartDef.RarityName(p.Rarity)} {PartDef.CategoryName(p.Category)}\n{p.Flavor}\n{PartSummary(p)}";
                if (!Bm.Stock.IsUnlimited)
                    tip += $"\nYou have {Bm.Stock.Owned(p)} ({Math.Max(0, free)} free).";
                tip += !fits ? $"\nDoesn't fit this build: {(free <= 0 ? "none left to fit." : Bm.WhyNowhere(p))}" : "\nDrag it onto the gun, or double-click to fit it.";
                ImGuiSafe.SetTooltip(tip);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _dragArmed = true;
                    _dragPart = p;
                    _dragNode = null;
                    _dragStart = ImGui.GetMousePos();
                    _dragHome = new float2(p0.X + 32 * _scale, (p0.Y + p1.Y) / 2);
                }
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _dragArmed = false;
                    var why = Bm.AutoPlace(p, out var placed);
                    if (why == null && placed != null)
                        Snapped(placed, $"Fitted the {p.Name} ({SocketLabel(placed.ParentSocket!)} of the {placed.Parent!.Part.Name}).");
                    else
                        Refused(p, ImGui.GetMousePos(), _dragHome, why ?? "It doesn't fit.");
                }
            }
        }
        ImGui.EndChild();
    }

    private bool WbChip(string label, bool on)
    {
        if (on)
            ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.42f, 0.27f, 0.1f, 1f));
        var clicked = ImGui.SmallButton(label);
        if (on)
            ImGui.PopStyleColor();
        return clicked;
    }

    /// <summary>A part's cached 3D picture in a box (a tile with its category letter until the picture is ready).</summary>
    private void DrawPartIcon(ImDrawListPtr dl, PartDef p, float2 min, float2 max, bool dim)
    {
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        if (w < 4 || h < 4)
            return;
        var tex = PartThumbs.Get(p);
        if (tex != 0)
        {
            // Keep the picture's aspect: crop the middle (GL rows start at the bottom, so v runs 1 -> 0).
            var texAspect = PartThumbs.Width / (float)PartThumbs.Height;
            var aspect = w / h;
            float u = 1, v = 1;
            if (aspect > texAspect)
                v = texAspect / aspect;
            else
                u = aspect / texAspect;
            dl.AddImage(new ImTextureRef { _TexID = new ImTextureID((nint)tex) }, min, max, new float2(0.5f - u / 2, 0.5f + v / 2), new float2(0.5f + u / 2, 0.5f - v / 2),
                dim ? new ImColor8(120, 120, 128, 150) : new ImColor8(255, 255, 255, 255));
            return;
        }
        var rc = PartDef.RarityColor(p.Rarity);
        dl.AddRectFilled(min, max, WbC8(rc * 0.25f, dim ? 0.5f : 1f), 3 * _scale);
        var letter = PartDef.CategoryName(p.Category)[..1];
        var ts = ImGui.CalcTextSize(letter);
        DlText(dl, new float2((min.X + max.X - ts.X) / 2, (min.Y + max.Y - ts.Y) / 2), WbC8(rc, dim ? 0.5f : 1f), letter);
    }

    // ---- Centre: the gun --------------------------------------------------------------------------------------

    private void DrawCentre(float width, float height, float2 winPos, float2 winSize)
    {
        var b = Bm.Build;
        // Toolbar.
        if (ImGui.Button(_wbMelee ? "New melee weapon"u8 : "New weapon"u8))
        {
            _wbPicking = true;
            _wbPickKeep = false;
        }
        ImGui.SameLine();
        if (ImGui.Button(_wbMelee ? "Change hilt"u8 : "Change grip"u8))
        {
            _wbPicking = true;
            _wbPickKeep = true;
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!Bm.CanUndo);
        if (ImGui.Button("Undo"u8))
            WbUndo(false);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!Bm.CanRedo);
        if (ImGui.Button("Redo"u8))
            WbUndo(true);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Auto-complete"u8))
            WbAutoComplete();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Fills every empty socket the build needs with parts you have (the rarest that fits first). Key: A"u8);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Max(80 * _scale, ImGui.GetContentRegionAvail().X));
        try
        {
            if (ImGui.InputTextWithHint("##wbname"u8, "Weapon name"u8, _wbNameInput!))
                b.Name = WeaponBuild.CleanName(_wbNameInput!.ToString());
        }
        catch (Exception ex)
        {
            ImGuiSafe.FieldFailed("build name", ex);
        }

        // The view.
        var origin = ImGui.GetCursorScreenPos();
        var stripH = 58 * _scale;
        var viewSize = new float2(width, MathF.Max(120 * _scale, height - (origin.Y - ImGui.GetWindowPos().Y) - stripH));
        ImGui.InvisibleButton("##wbview"u8, viewSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        _viewMin = origin;
        _viewMax = origin + viewSize;
        var mouse = ImGui.GetMousePos();
        _viewHovered = In(mouse, _viewMin, _viewMax) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        _wbCam.Matrices(viewSize.X / MathF.Max(1, viewSize.Y), out var pose, out var view, out var proj);
        _viewPvm = pose * view * proj;
        ProjectSpots();
        _hoverSpot = -1;
        if (_viewHovered && Ray(mouse, out var ro, out var rd))
        {
            var tol = (_dragging ? 22 : 12) * _scale * 2 * MathF.Tan(BenchCamera.Fov / 2) / MathF.Max(1, viewSize.Y); // generous while dropping, tight for picking (so a click near the gun still turns it)
            if (_dragging)
                _hoverSpot = BenchModel.PickRay(Bm.Spots, _dragValid.AsSpan(0, Math.Min(_dragValid.Length, Bm.Spots.Count)), ro, rd, tol);
            else
                _hoverSpot = BenchModel.PickRay(Bm.Spots, ReadOnlySpan<bool>.Empty, ro, rd, tol);
        }

        // What the view shows: the build, or the dragged part snapped into the socket under the mouse.
        var def = Bm.Asm.PreviewDef;
        if (_dragging && _hoverSpot >= 0 && TrialFor(_hoverSpot) is { PreviewDef: { } td })
            def = td;
        if (def != null)
        {
            WeaponDef? rest = null, part = null;
            var offset = Vector3.Zero;
            var t = (float)(_wbNow - _animAt) / 0.2f;
            if (t < 1 && _animVersion == Bm.Version && def == Bm.Asm.PreviewDef && _animRest != null)
            {
                rest = _animRest;
                part = _animPart;
                var e = 1 - t;
                offset = _animDir * (0.16f * e * e);
            }
            Preview = new GunsmithPreview(def, winPos, winPos + winSize, _viewMin, _viewMax, _wbCam, rest, part, offset);
        }

        // Mouse in the view (not dragging a part): pick, drag a part off, orbit, zoom.
        var io = ImGui.GetIO();
        if (_viewHovered && !_dragging && !_dragArmed)
        {
            var spot = _hoverSpot >= 0 ? Bm.Spots[_hoverSpot] : default;
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (_hoverSpot >= 0 && spot.Child != null)
                {
                    _dragArmed = true;
                    _dragNode = spot.Child;
                    _dragPart = spot.Child.Part;
                    _dragStart = mouse;
                    _dragHome = _spotScreen[_hoverSpot];
                }
                else
                    _orbiting = true;
            }
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && _hoverSpot >= 0 && spot.Child != null)
            {
                _ctxNode = spot.Child;
                _ctxOpen = true;
            }
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && _hoverSpot < 0)
                ResetView();
            if (io.MouseWheel != 0)
                _wbCam.Zoom = Math.Clamp(_wbCam.Zoom * MathF.Pow(1.12f, io.MouseWheel), 0.5f, 4f);
        }
        if (_orbiting)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var d = io.MouseDelta;
                _wbCam.Yaw += d.X * 0.008f;
                _wbCam.Pitch = Math.Clamp(_wbCam.Pitch + d.Y * 0.008f, -1.3f, 1.3f);
            }
            else
                _orbiting = false;
        }

        DrawMarkers();
        // A hint in the corner, and what's under the mouse.
        var dl = ImGui.GetWindowDrawList();
        if (b.Root != null)
        {
            var grip = b.Root.Part;
            DlText(dl, _viewMin + new float2(10 * _scale, 8 * _scale), WbC8(PartDef.RarityColor(grip.Rarity)), grip.Name);
            DlText(dl, _viewMin + new float2(10 * _scale, 26 * _scale), Dim8, b.IsMelee ? "melee weapon"
                : $"{b.Receivers.Count}/{grip.ReceiverCapacity} receiver slot{(grip.ReceiverCapacity == 1 ? "" : "s")} - fires {b.Style.ToString().ToLowerInvariant()}");
        }
        DlText(dl, new float2(_viewMin.X + 10 * _scale, _viewMax.Y - 22 * _scale), new ImColor8(120, 122, 132, 255), "drag: turn - wheel: zoom - double-click: reset");
        if (!_dragging && _hoverSpot >= 0 && Bm.Spots[_hoverSpot] is var hs)
        {
            var label = hs.Child != null ? $"{hs.Child.Part.Name}{(hs.Missing ? "  (MISSING)" : "")}" : $"{SocketLabel(hs.Socket)} of the {hs.Parent.Part.Name}{(hs.Needed ? " (needed)" : " (empty)")}";
            DlText(dl, _spotScreen[_hoverSpot] + new float2(14 * _scale, -8 * _scale), hs.Missing ? Red8 : White8, label);
            if (hs.Child != null)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        // Under the view: what's missing, what's needed.
        ImGui.SetCursorScreenPos(new float2(_viewMin.X, _viewMax.Y + 4 * _scale));
        DrawMissingStrip();
    }

    private bool Ray(float2 mouse, out Vector3 origin, out Vector3 dir) =>
        BenchCamera.Ray(V2(mouse), _viewPvm, V2(_viewMin), V2(_viewMax - _viewMin), out origin, out dir);

    private void ProjectSpots()
    {
        var n = Bm.Spots.Count;
        if (_spotScreen.Length < n)
        {
            _spotScreen = new float2[n * 2];
            _spotOn = new bool[n * 2];
        }
        var vmin = V2(_viewMin);
        var vsize = V2(_viewMax - _viewMin);
        for (var i = 0; i < n; i++)
        {
            _spotOn[i] = BenchCamera.Project(Bm.Spots[i].Pos, _viewPvm, vmin, vsize, out var sp, out _);
            _spotScreen[i] = F2(sp);
        }
    }

    /// <summary>The socket markers: where a dragged part fits (green; cyan = swap), what's needed (yellow), missing (red), selected (orange).</summary>
    private void DrawMarkers()
    {
        var dl = ImGui.GetWindowDrawList();
        var pulse = 0.5f + 0.5f * MathF.Sin((float)_wbNow * 6f);
        var rowSpot = _hoverRowPrev >= 0 && _hoverRowPrev < Bm.Rows.Count ? RowSpot(_hoverRowPrev) : -1; // the list is drawn after the view: last frame's row
        for (var i = 0; i < Bm.Spots.Count; i++)
        {
            if (!_spotOn[i] || !In(_spotScreen[i], _viewMin, _viewMax))
                continue;
            var s = Bm.Spots[i];
            var c = _spotScreen[i];
            var hot = i == _hoverSpot;
            if (_dragging)
            {
                if (i >= _dragValid.Length || !_dragValid[i])
                    continue;
                var swap = s.Child != null;
                var col = swap ? Cyan8 : Green8;
                var r = (hot ? 12f : 7f + pulse * 2f) * _scale;
                if (!swap)
                    dl.AddCircleFilled(c, r * 0.55f, WbFade(col, hot ? 0.95f : 0.55f), 16);
                dl.AddCircle(c, r, col, 20, (hot ? 3f : 2f) * _scale);
                if (hot)
                    DlText(dl, c + new float2(15 * _scale, -9 * _scale), col, swap ? $"swap: {SocketLabel(s.Socket)} ({s.Child!.Part.Name})" : $"{SocketLabel(s.Socket)} of the {s.Parent.Part.Name}");
                continue;
            }
            if (s.Missing)
            {
                dl.AddCircleFilled(c, 5f * _scale, Red8, 12);
                dl.AddCircle(c, (8f + pulse * 2f) * _scale, WbFade(Red8, 0.8f), 16, 2f * _scale);
            }
            else if (s.Needed)
                dl.AddCircle(c, (6f + pulse * 3f) * _scale, Yellow8, 16, 2f * _scale);
            else if (s.Child != null && (s.Child == _wbSel || i == rowSpot || hot))
                dl.AddCircleFilled(c, 3f * _scale, White8, 10);
            if (s.Child != null && s.Child == _wbSel)
                dl.AddCircle(c, 10f * _scale, Orange8, 20, 2.5f * _scale);
            if (i == rowSpot)
                dl.AddCircle(c, 13f * _scale, WbFade(Orange8, 0.7f), 20, 2f * _scale);
            if (hot)
                dl.AddCircle(c, 9f * _scale, White8, 20, 1.5f * _scale);
        }
    }

    private void DrawMissingStrip()
    {
        var m = Bm.Missing;
        if (m.Total > 0)
        {
            ImGuiSafe.TextColored(Red, m.Text());
            ImGui.SameLine();
            ImGui.TextColored(Dim, "(red on the gun and in the list)"u8);
        }
        var problems = Bm.Asm.Problems;
        if (problems.Count == 0 && m.Total == 0 && Bm.Build.Root != null)
            ImGui.TextColored(Green, Bm.Build.IsMelee ? "Ready to swing."u8 : "Ready to fire."u8);
        else if (problems.Count > 0)
        {
            var p = problems[0];
            ImGuiSafe.TextColored(p.Incomplete ? Yellow : Red, (p.Incomplete ? "Needs: " : "Problem: ") + p.Message + (problems.Count > 1 ? $"  (+{problems.Count - 1} more)" : ""));
        }
    }

    // ---- Right: the build as a list, stats, saving ------------------------------------------------------------

    private int RowSpot(int row)
    {
        RefreshRows();
        return row < _rowSpot.Length ? _rowSpot[row] : -1;
    }

    private void RefreshRows()
    {
        if (_rowVersion == Bm.Version)
            return;
        _rowVersion = Bm.Version;
        var n = Bm.Rows.Count;
        if (_rowSpot.Length < n)
        {
            _rowSpot = new int[n * 2];
            _rowLabel = new string[n * 2];
            _rowName = new string[n * 2];
            _rowRect = new float4[n * 2];
        }
        for (var i = 0; i < n; i++)
        {
            var r = Bm.Rows[i];
            _rowSpot[i] = r.Socket != null ? Bm.SpotOf(r.ParentPath, r.Socket.Name) : -1;
            _rowLabel[i] = r.Socket == null ? (r.Node!.Part.Hilt != null ? "Hilt" : "Grip") : SocketLabel(r.Socket);
            _rowName[i] = r.Kind == BenchRowKind.Part ? r.Node!.Part.Name
                : r.Socket!.Type == "receiver" ? "needs a receiver" : $"needs {(r.Socket.Type == "ammo" ? "ammo" : r.Socket.Type)}";
        }
    }

    private void DrawLinear(float height)
    {
        ImGui.TextColored(Orange, "THE BUILD"u8);
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"  {Bm.Build.PartCount} parts, {Bm.Build.Weight:0.0} kg");
        if (!ImGui.BeginChild("##wblinear"u8, new float2(0, height), ImGuiChildFlags.Borders))
        {
            ImGui.EndChild();
            return;
        }
        RefreshRows();
        var dl = ImGui.GetWindowDrawList();
        var wp = ImGui.GetWindowPos();
        var ws = ImGui.GetWindowSize();
        _listClip = new float4(wp.X, wp.Y, wp.X + ws.X, wp.Y + ws.Y);
        var rowH = 26 * _scale;
        var w = ImGui.GetContentRegionAvail().X;
        var mouse = ImGui.GetMousePos();
        for (var i = 0; i < Bm.Rows.Count && i < WbRowIds.Length; i++)
        {
            var r = Bm.Rows[i];
            var p0 = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton(WbRowIds[i], new float2(w, rowH), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            var hot = In(mouse, p0, p0 + new float2(w, rowH)) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
            var rclicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
            _rowRect[i] = new float4(p0.X, p0.Y, p0.X + w, p0.Y + rowH);
            _rowRectCount = i + 1;
            if (hot)
                _hoverRow = i;
            var spot = _rowSpot[i];
            var dropOk = _dragging && spot >= 0 && spot < _dragValid.Length && _dragValid[spot];
            var indent = r.Depth * 16 * _scale;
            var selected = r.Node != null && r.Node == _wbSel;
            var bg = dropOk && hot ? new ImColor8(40, 80, 50, 245) : dropOk ? new ImColor8(30, 52, 36, 235) : selected ? new ImColor8(60, 44, 24, 245) : hot ? Hover8 : new ImColor8(0, 0, 0, 0);
            dl.AddRectFilled(p0, p0 + new float2(w, rowH - 2 * _scale), bg, 3 * _scale);
            // Tree guides: a short elbow from the parent's column.
            for (var d = 1; d <= r.Depth; d++)
            {
                var gx = p0.X + (d - 0.5f) * 16 * _scale;
                dl.AddLine(new float2(gx, p0.Y), new float2(gx, p0.Y + (d == r.Depth ? rowH * 0.5f : rowH)), new ImColor8(70, 74, 84, 255), 1f);
            }
            if (r.Depth > 0)
                dl.AddLine(new float2(p0.X + (r.Depth - 0.5f) * 16 * _scale, p0.Y + rowH * 0.5f), new float2(p0.X + indent + 2 * _scale, p0.Y + rowH * 0.5f), new ImColor8(70, 74, 84, 255), 1f);
            var x = p0.X + indent + 4 * _scale;
            if (r.Kind == BenchRowKind.Part)
            {
                var part = r.Node!.Part;
                DrawPartIcon(dl, part, new float2(x, p0.Y + 2 * _scale), new float2(x + 32 * _scale, p0.Y + rowH - 4 * _scale), false);
                x += 38 * _scale;
                DlText(dl, new float2(x, p0.Y + 4 * _scale), Dim8, _rowLabel[i]);
                var lw = MathF.Max(ImGui.CalcTextSize(_rowLabel[i]).X + 10 * _scale, 86 * _scale);
                DlText(dl, new float2(x + lw, p0.Y + 4 * _scale), r.Missing ? Red8 : WbC8(PartDef.RarityColor(part.Rarity)), _rowName[i]);
                if (r.Missing)
                {
                    const string tag = "MISSING";
                    var tw = ImGui.CalcTextSize(tag).X;
                    var t0 = new float2(p0.X + w - tw - 12 * _scale, p0.Y + 3 * _scale);
                    dl.AddRectFilled(t0, t0 + new float2(tw + 8 * _scale, 18 * _scale), new ImColor8(110, 28, 24, 255), 3 * _scale);
                    DlText(dl, t0 + new float2(4 * _scale, 1 * _scale), White8, tag);
                }
            }
            else
            {
                dl.AddCircle(new float2(x + 8 * _scale, p0.Y + rowH * 0.5f), 6 * _scale, Yellow8, 12, 1.5f * _scale);
                DlText(dl, new float2(x + 4.5f * _scale, p0.Y + 4 * _scale), Yellow8, "+");
                x += 22 * _scale;
                DlText(dl, new float2(x, p0.Y + 4 * _scale), Yellow8, $"{_rowLabel[i]}: {_rowName[i]}");
            }
            if (hot && !_dragging)
            {
                if (r.Kind == BenchRowKind.Part)
                    ImGuiSafe.SetTooltip($"{r.Node!.Part.Name}{(r.Missing ? " - MISSING: you don't have this part" : "")}\n{PartSummary(r.Node.Part)}\nRight-click: options. Drag it off to take it off.");
                else
                    ImGuiSafe.SetTooltip($"Empty: the build needs {(r.Socket!.Type == "ammo" ? "ammo" : "a " + r.Socket.Type)} here. Drag one from your parts onto it (or onto the yellow ring on the gun).");
            }
            if (clicked && !_dragging)
            {
                if (r.Node != null)
                {
                    _wbSel = r.Node;
                    if (r.Parent != null)
                    {
                        _dragArmed = true; // rows can be dragged off too
                        _dragNode = r.Node;
                        _dragPart = r.Node.Part;
                        _dragStart = mouse;
                        _dragHome = p0 + new float2(20 * _scale, rowH / 2);
                    }
                }
                else if (r.Socket != null)
                {
                    _wbFilter = (int)CategoryFor(r.Socket);
                    WbSay($"Showing {PartDef.CategoryName(CategoryFor(r.Socket)).ToLowerInvariant()} parts: drag one onto the {SocketLabel(r.Socket).ToLowerInvariant()} socket.");
                }
            }
            if (rclicked && r.Node != null)
            {
                _ctxNode = r.Node;
                _ctxOpen = true;
            }
        }
        ImGui.EndChild();
    }

    private static PartCategory CategoryFor(PartSocket s) => s.Accepts.Length > 0 ? s.Accepts[0] : s.Type switch
    {
        "receiver" => PartCategory.Receiver,
        "barrel" => PartCategory.Barrel,
        "feed" => PartCategory.Feed,
        "ammo" => PartCategory.Ammo,
        "stock" => PartCategory.Stock,
        "foregrip" => PartCategory.Grip,
        "optic" => PartCategory.Optic,
        "muzzle" => PartCategory.Muzzle,
        "upgrade" => PartCategory.Upgrade,
        "amp" => PartCategory.Multiplier,
        "hopper" => PartCategory.Feed,
        "head" => PartCategory.Head,
        "mod" => PartCategory.MeleeMod,
        _ => PartCategory.Utility,
    };

    /// <summary>The build with the dragged part (or part being moved) in spot <paramref name="spot"/>, cached per hovered spot.</summary>
    private AssembledWeapon? TrialFor(int spot)
    {
        var key = (Bm.Version, spot, _dragPart, _dragNode);
        if (key != _trialKey)
        {
            _trialKey = key;
            _trialAsm = _dragPart != null ? Bm.TrialPlace(spot, _dragPart, _dragNode) : null;
        }
        return _trialAsm;
    }

    /// <summary>What the stat bars compare with: the dragged part in the socket under the mouse, or a hovered part in its best free socket.</summary>
    private AssembledWeapon? StatTrial()
    {
        if (_dragging)
            return _hoverSpot >= 0 ? TrialFor(_hoverSpot) : HoverRowTrial();
        if (_hoverPart == null)
            return null;
        var key = (Bm.Version, _hoverPart);
        if (key != _hoverTrialKey)
        {
            _hoverTrialKey = key;
            var spot = Bm.BestFreeSpot(_hoverPart);
            _hoverTrialAsm = spot >= 0 ? Bm.TrialPlace(spot, _hoverPart) : null;
        }
        return _hoverTrialAsm;
    }

    private AssembledWeapon? HoverRowTrial()
    {
        if (_hoverRow < 0 || RowSpot(_hoverRow) is var s && (s < 0 || s >= _dragValid.Length || !_dragValid[s]))
            return null;
        return TrialFor(RowSpot(_hoverRow));
    }

    private void DrawStatBars()
    {
        ImGui.Spacing();
        var trial = StatTrial();
        var b = Bm.Build;
        if (b.IsMelee)
        {
            var cur = Bm.Asm.Melee;
            var tm = trial?.Melee;
            ImGui.TextColored(Orange, "MELEE STATS"u8);
            if (cur == null)
            {
                ImGui.TextColored(Dim, "Fit a head to see its numbers."u8);
                return;
            }
            WbBar("Damage", cur.Damage, tm?.Damage, MathF.Sqrt(cur.Damage / 150f), tm != null ? MathF.Sqrt(tm.Damage / 150f) : null, "0", true);
            WbBar("Swing", cur.Swing, tm?.Swing, 1 - Math.Clamp(cur.Swing / 1.8f, 0, 1), tm != null ? 1 - Math.Clamp(tm.Swing / 1.8f, 0, 1) : null, "0.00 s", false);
            WbBar("DPS", cur.Dps, tm?.Dps, MathF.Sqrt(cur.Dps / 250f), tm != null ? MathF.Sqrt(tm.Dps / 250f) : null, "0", true);
            WbBar("Reach", cur.Reach, tm?.Reach, cur.Reach / 3.2f, tm != null ? tm.Reach / 3.2f : null, "0.0 m", true);
            WbBar("Cleave", cur.Cleave, tm?.Cleave, cur.Cleave / 5f, tm != null ? tm.Cleave / 5f : null, "0", true);
            WbBar("Stun", cur.Stun, tm?.Stun, cur.Stun / 3f, tm != null ? tm.Stun / 3f : null, "0.0 s", true);
            WbBar("Weight", b.Weight, trial?.Build.Weight, b.Weight / BuildRules.MaxMeleeWeight, trial != null ? trial.Build.Weight / BuildRules.MaxMeleeWeight : null, "0.0 kg", false);
            return;
        }
        var d = Bm.Asm.Def;
        var t = trial?.Def;
        ImGui.TextColored(Orange, "STATS"u8);
        if (d != null)
        {
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, $"  {d.ClassName} - {d.ModeName} - {b.Receivers.Count} receiver(s), {d.BarrelCount} barrel(s)");
            WbBar("Damage", d.ShotDamage, t?.ShotDamage, MathF.Sqrt(d.ShotDamage / 400f), t != null ? MathF.Sqrt(t.ShotDamage / 400f) : null, "0", true);
            WbBar("Fire rate", d.Rpm, t?.Rpm, StatBars.FireRate(d), t != null ? StatBars.FireRate(t) : null, "0 rpm", true);
            WbBar("Recoil", Recoil.EffectiveKickDeg(d), t != null ? Recoil.EffectiveKickDeg(t) : null, 1 - StatBars.Control(d), t != null ? 1 - StatBars.Control(t) : null, "0.00", false);
            WbBar("Magazine", d.Mag, t?.Mag, StatBars.Magazine(d), t != null ? StatBars.Magazine(t) : null, "0", true);
            WbBar("DPS", d.Dps, t?.Dps, MathF.Sqrt(d.Dps / 3000f), t != null ? MathF.Sqrt(t.Dps / 3000f) : null, "0", true);
            WbBar("Reload", d.Reload, t?.Reload, 1 - StatBars.Reload(d), t != null ? 1 - StatBars.Reload(t) : null, "0.0 s", false);
            WbBar("Mobility", d.Mobility, t?.Mobility, StatBars.Mobility(d), t != null ? StatBars.Mobility(t) : null, "0.00", true);
            if (d.SpoolTime > 0)
                ImGuiSafe.TextColored(Dim, $"Spins up over {d.SpoolTime:0.0} s from {d.SpoolStart * 100:0}% fire rate.");
            if (d.NoAds)
                ImGui.TextColored(Yellow, "Hip fire only."u8);
            if (Game.IsCart(d))
                ImGuiSafe.TextColored(Yellow, $"A real cart: push it, {Keybinds.NameOf(_hudSettings, GameAction.Interact)} parks it, {Keybinds.NameOf(_hudSettings, GameAction.Interact)} at its bar mans its gun.");
            if (d.BuildFlags.Length > 0)
                ImGuiSafe.TextColored(Dim, "Flags: " + string.Join(", ", d.BuildFlags));
        }
        else
            ImGui.TextColored(Dim, "Finish the build to see its numbers (the list shows what it needs)."u8);
        var cap = b.CarryWeight * 2;
        WbBar("Weight", b.Weight, trial?.Build.Weight, cap > 0 ? b.Weight / cap : 0, trial != null && cap > 0 ? trial.Build.Weight / (trial.Build.CarryWeight * 2) : null, "0.0 kg", false);
        if (b.Frame != null)
            ImGuiSafe.TextColored(Dim, $"Carries {b.CarryWeight:0.#} kg before it slows you (at most {cap:0.#} kg). {b.ScrapValue} scrap.");
    }

    /// <summary>A stat bar with the change a trial would make: green where it gets better, red where worse.</summary>
    private void WbBar(string label, float now, float? then, float fillNow, float? fillThen, string fmt, bool higherIsBetter)
    {
        var dl = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        var h = 20 * _scale;
        ImGui.Dummy(new float2(w, h));
        var labelW = 78 * _scale;
        var valueW = 118 * _scale;
        DlText(dl, new float2(p0.X, p0.Y + 2 * _scale), Dim8, label);
        var b0 = new float2(p0.X + labelW, p0.Y + 6 * _scale);
        var bw = MathF.Max(20 * _scale, w - labelW - valueW);
        var b1 = b0 + new float2(bw, 8 * _scale);
        dl.AddRectFilled(b0, b1, new ImColor8(40, 43, 52, 255), 3 * _scale);
        var a = Math.Clamp(fillNow, 0, 1);
        if (fillThen is { } ft)
        {
            var c = Math.Clamp(ft, 0, 1);
            var lo = MathF.Min(a, c);
            var hi = MathF.Max(a, c);
            dl.AddRectFilled(b0, new float2(b0.X + bw * lo, b1.Y), new ImColor8(200, 202, 210, 255), 3 * _scale);
            var better = higherIsBetter ? then > now : then < now;
            if (hi > lo)
                dl.AddRectFilled(new float2(b0.X + bw * lo, b0.Y), new float2(b0.X + bw * hi, b1.Y), better ? Green8 : Red8, 0);
        }
        else
            dl.AddRectFilled(b0, new float2(b0.X + bw * a, b1.Y), new ImColor8(200, 202, 210, 255), 3 * _scale);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var txt = now.ToString(fmt, inv);
        DlText(dl, new float2(b1.X + 8 * _scale, p0.Y + 2 * _scale), White8, txt);
        if (then is { } tv && MathF.Abs(tv - now) > MathF.Max(1e-3f, MathF.Abs(now) * 0.005f))
        {
            var better = higherIsBetter ? tv > now : tv < now;
            var ch = StatBars.Change(MathF.Max(now, 1e-3f), tv);
            DlText(dl, new float2(b1.X + 8 * _scale + ImGui.CalcTextSize(txt).X + 6 * _scale, p0.Y + 2 * _scale), better ? Green8 : Red8, ch.Length > 0 ? ch : tv.ToString(fmt, inv));
        }
    }

    // ---- The right-click menu ---------------------------------------------------------------------------------

    private void DrawWbContextMenu()
    {
        if (_ctxOpen)
        {
            ImGui.OpenPopup("##wbctx"u8);
            _ctxOpen = false;
        }
        _ctxShown = ImGui.BeginPopup("##wbctx"u8);
        if (!_ctxShown)
            return;
        var n = _ctxNode;
        if (n == null || !Bm.Build.Nodes.Contains(n))
        {
            ImGui.TextColored(Dim, "That part isn't on the build any more."u8);
            ImGui.EndPopup();
            return;
        }
        ImGuiSafe.TextColored(WbCol(PartDef.RarityColor(n.Part.Rarity)), n.Part.Name);
        ImGui.Separator();
        if (n.Part.Category == PartCategory.Receiver && n.Parent == Bm.Build.Root)
        {
            var target = Bm.CopyTarget(n);
            ImGui.BeginDisabled(target == null);
            if (ImGui.Selectable(target != null ? $"Copy receiver assembly (to {SocketLabel(target).ToLowerInvariant()})" : "Copy receiver assembly (no free receiver slot)"))
                WbCopy(n);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGuiSafe.SetTooltip(target != null ? "Duplicates this receiver and everything hanging off it onto a free receiver slot. Parts you don't have show red until you get them." : $"The {Bm.Build.Root!.Part.Name} has every receiver slot full.");
        }
        if (n != Bm.Build.Root && ImGui.Selectable("Take it off (back to your parts)"u8))
            WbRemove(n);
        if (n == Bm.Build.Root && ImGui.Selectable(_wbMelee ? "Change hilt..."u8 : "Change grip..."u8))
        {
            _wbPicking = true;
            _wbPickKeep = true;
        }
        if (ImGui.Selectable("Select"u8))
            _wbSel = n;
        ImGui.EndPopup();
    }

    // ---- Edits, feedback ----------------------------------------------------------------------------------------

    private void WbUndo(bool redo)
    {
        if (redo ? Bm.Redo() : Bm.Undo())
        {
            ImGuiSafe.SetInput(_wbNameInput!, Bm.Build.Name);
            _wbSel = null;
            WbSay(redo ? "Redone." : "Undone.");
            _wbAudio?.Effect(Sfx.HitTick, 0.3f);
        }
    }

    private void WbAutoComplete()
    {
        var n = Bm.AutoComplete(Bm.Stock.Parts(Parts.All));
        if (n > 0)
        {
            _wbCam.Nudge = 0.3f;
            _wbAudio?.Effect(Sfx.DeployDevice, 0.55f);
        }
        WbSay(n > 0 ? $"Auto-complete fitted {n} part{(n == 1 ? "" : "s")}." + (Bm.Asm.Problems.Count > 0 ? " Still needs: " + Bm.Asm.Problems[0].Message : "")
            : Bm.Asm.Problems.Any(p => p.Incomplete) ? "Nothing you have fits the empty sockets." : "Nothing to fill: every needed socket has a part.", n == 0 && Bm.Asm.Problems.Any(p => p.Incomplete));
    }

    private void WbRemove(BuildNode n)
    {
        var name = n.Part.Name;
        if (Bm.Remove(n) is { } why)
        {
            WbSay(why, true);
            return;
        }
        if (_wbSel == n)
            _wbSel = null;
        _wbAudio?.Effect(Sfx.HitTick, 0.45f);
        WbSay($"Took off the {name} (and what was on it): back to your parts.");
    }

    private void WbCopy(BuildNode receiver)
    {
        if (Bm.CopyReceiver(receiver, out var target) is { } why)
        {
            WbSay(why, true);
            _wbAudio?.Effect(Sfx.DryFire, 0.5f);
            return;
        }
        _wbCam.Nudge = 0.35f;
        _wbAudio?.Effect(Sfx.DeployDevice, 0.6f);
        var copy = Bm.Build.Root!.Child(target!.Name);
        if (copy != null)
            StartSlide(copy);
        WbSay(Bm.Missing.Total > 0
            ? $"Copied the {receiver.Part.Name} assembly to {SocketLabel(target).ToLowerInvariant()}. {Bm.Missing.Text()}: get them to finish it."
            : $"Copied the {receiver.Part.Name} assembly to {SocketLabel(target).ToLowerInvariant()}.", Bm.Missing.Total > 0);
    }

    /// <summary>A part went on: click, a little camera nudge, and it slides into its socket.</summary>
    private void Snapped(BuildNode placed, string message)
    {
        _wbAudio?.Effect(Sfx.DeployDevice, 0.55f);
        _wbCam.Nudge = 0.25f;
        StartSlide(placed);
        _wbSel = placed;
        WbSay(message);
    }

    private void StartSlide(BuildNode node)
    {
        _animRest = _animPart = null;
        if (Bm.Asm is not { Model: { } model, PreviewDef: { BuiltMesh: { } mesh } def })
            return;
        var (from, to) = BuildModel.NodeRange(model, node);
        if (to <= from || to > mesh.Parts.Length)
            return;
        _animRest = def.Clone();
        _animRest.BuiltMesh = mesh.Without(from, to);
        _animPart = def.Clone();
        _animPart.BuiltMesh = mesh.Slice(from, to);
        if (node.Part.Category == PartCategory.Optic)
            _animRest.Optic = OpticKind.Iron;
        else
            _animPart.Optic = OpticKind.Iron;
        var at = model.Nodes.TryGetValue(node, out var sn) ? sn.WorldPosition : _wbCam.Center;
        var dir = at - _wbCam.Center;
        dir.Y = MathF.Abs(dir.Y) + 0.05f; // in from above and outwards
        _animDir = dir.LengthSquared() > 1e-6f ? Vector3.Normalize(dir) : Vector3.UnitY;
        _animAt = _wbNow;
        _animVersion = Bm.Version;
    }

    private void Refused(PartDef part, float2 from, float2 home, string why)
    {
        _bouncePart = part;
        _bounceFrom = from;
        _bounceTo = home;
        _bounceAt = _wbNow;
        _wbAudio?.Effect(Sfx.DryFire, 0.5f);
        WbSay(why, true);
    }

    // ---- Dragging ---------------------------------------------------------------------------------------------

    private void UpdateDrag()
    {
        var mouse = ImGui.GetMousePos();
        if (_dragArmed && !_dragging)
        {
            var d = mouse - _dragStart;
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && d.X * d.X + d.Y * d.Y > 36 * _scale * _scale && _dragPart != null)
            {
                _dragging = true;
                if (_dragValid.Length < Bm.Spots.Count)
                    _dragValid = new bool[Bm.Spots.Count * 2];
                Array.Clear(_dragValid);
                _dragValidCount = Bm.ValidSpots(_dragPart, _dragValid, _dragNode);
                _wbAudio?.Effect(Sfx.HitTick, 0.25f);
                if (_dragValidCount == 0 && _dragNode == null)
                    WbSay($"The {_dragPart.Name} fits nowhere on this build: {(Bm.Stock.Free(_dragPart, Bm.Build) <= 0 ? "you have none left." : Bm.WhyNowhere(_dragPart))}", true);
            }
            else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                if (_dragNode != null)
                    _wbSel = _dragNode; // a click on a part: select it
                CancelDrag();
            }
        }
        if (_dragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            Drop(mouse);
        if ((_dragging || _orbiting) && ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            WbSay("Cancelled.");
            CancelDrag();
            _escUsed = true; // the same Esc doesn't also close the workbench
        }
    }

    private void Drop(float2 mouse)
    {
        var part = _dragPart!;
        var node = _dragNode;
        var target = _viewHovered ? _hoverSpot : -1;
        if (target < 0 && _hoverRow >= 0 && In(mouse, new float2(_listClip.X, _listClip.Y), new float2(_listClip.Z, _listClip.W)))
        {
            var s = RowSpot(_hoverRow);
            if (s >= 0 && s < _dragValid.Length && _dragValid[s])
                target = s;
        }
        var home = _dragHome;
        CancelDrag();
        if (target >= 0)
        {
            if (node != null)
            {
                var why = Bm.Move(node, target);
                if (why == null)
                    Snapped(node, $"Moved the {part.Name} to the {SocketLabel(node.ParentSocket!).ToLowerInvariant()} of the {node.Parent!.Part.Name}.");
                else
                    Refused(part, mouse, home, why);
                return;
            }
            var spot = Bm.Spots[target];
            var replaced = spot.Child?.Part.Name;
            var err = Bm.Place(target, part, out var placed);
            if (err == null && placed != null)
                Snapped(placed, replaced != null ? $"Swapped the {replaced} for the {part.Name} (its parts moved over where they fit)." : $"Fitted the {part.Name}.");
            else
                Refused(part, mouse, home, err ?? "It doesn't fit there.");
            return;
        }
        if (node != null)
        {
            // Dragged off the gun onto your parts (or out of the view and the list): it comes off.
            var inList = In(mouse, new float2(_listClip.X, _listClip.Y), new float2(_listClip.Z, _listClip.W));
            if (In(mouse, _invMin, _invMax) || (!In(mouse, _viewMin, _viewMax) && !inList))
                WbRemove(node);
            return;
        }
        // Over a socket it doesn't fit: say why. Elsewhere: drop it on a glowing socket.
        string reason;
        var any = -1;
        if (_viewHovered && Ray(mouse, out var ro, out var rd))
            any = BenchModel.PickRay(Bm.Spots, ReadOnlySpan<bool>.Empty, ro, rd, 30 * _scale * 2 * MathF.Tan(BenchCamera.Fov / 2) / MathF.Max(1, _viewMax.Y - _viewMin.Y));
        if (any >= 0)
            reason = Bm.WhyNot(any, part) ?? BuildRules.SocketWhy(Bm.Spots[any].Parent, Bm.Spots[any].Socket, part) ?? "It doesn't fit there.";
        else if (_dragValidCount == 0)
            reason = Bm.Stock.Free(part, Bm.Build) <= 0 ? $"You have no {part.Name} left." : Bm.WhyNowhere(part);
        else
            reason = $"Drop the {part.Name} on one of the glowing sockets.";
        Refused(part, mouse, home, reason);
    }

    private void DrawDragOverlay()
    {
        var fg = ImGui.GetForegroundDrawList();
        var bt = (float)(_wbNow - _bounceAt) / 0.3f;
        if (_bouncePart != null && bt < 1)
        {
            var e = 1 - (1 - bt) * (1 - bt);
            var at = _bounceFrom + (_bounceTo - _bounceFrom) * e;
            var s = new float2(56 * _scale, 40 * _scale);
            DrawPartIcon(fg, _bouncePart, at - s * 0.5f, at + s * 0.5f, false);
            fg.AddRect(at - s * 0.5f, at + s * 0.5f, WbFade(Red8, 1 - bt), 4 * _scale, ImDrawFlags.None, 2f);
        }
        if (!_dragging || _dragPart == null)
            return;
        var m = ImGui.GetMousePos();
        var size = new float2(78 * _scale, 54 * _scale);
        var p0 = m + new float2(12 * _scale, 6 * _scale);
        var ok = _hoverSpot >= 0 || HoverRowTrial() != null;
        fg.AddRectFilled(p0, p0 + size, new ImColor8(20, 22, 28, 230), 5 * _scale);
        DrawPartIcon(fg, _dragPart, p0 + new float2(3, 3), p0 + size - new float2(3, 3), false);
        fg.AddRect(p0, p0 + size, ok ? Green8 : _viewHovered ? WbFade(Red8, 0.8f) : Orange8, 5 * _scale, ImDrawFlags.None, 2f);
        var label = _dragNode != null && !_viewHovered && _hoverRow < 0 ? $"{_dragPart.Name}: release to take it off" : _dragPart.Name;
        fg.AddText(ImGui.GetFont(), ImGui.GetFontSize(), p0 + new float2(0, size.Y + 4 * _scale), ok ? Green8 : White8, label);
        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
    }

    private void WbShortcuts()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput || _dragging || _bm == null)
            return;
        var ctrl = io.KeyCtrl;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            if (_wbPicking && Bm.Build.Root != null)
                _wbPicking = false;
            else
            {
                WorkbenchOpen = false;
                BenchRun = null;
            }
            return;
        }
        if (_wbPicking)
            return;
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.Z, false))
            WbUndo(io.KeyShift);
        else if (ctrl && ImGui.IsKeyPressed(ImGuiKey.Y, false))
            WbUndo(true);
        else if (ctrl)
            return;
        else if ((ImGui.IsKeyPressed(ImGuiKey.Delete, false) || ImGui.IsKeyPressed(ImGuiKey.Backspace, false)) && _wbSel != null && Bm.Build.Nodes.Contains(_wbSel))
            WbRemove(_wbSel);
        else if (ImGui.IsKeyPressed(ImGuiKey.A, false))
            WbAutoComplete();
        else if (ImGui.IsKeyPressed(ImGuiKey.C, false) && _wbSel is { Part.Category: PartCategory.Receiver } r && Bm.Build.Nodes.Contains(r))
            WbCopy(r);
        else if (ImGui.IsKeyPressed(ImGuiKey.N, false))
            (_wbPicking, _wbPickKeep) = (true, false);
        else if (ImGui.IsKeyPressed(ImGuiKey.G, false))
            (_wbPicking, _wbPickKeep) = (true, true);
        else if (ImGui.IsKeyPressed(ImGuiKey.R, false))
            ResetView();
    }

    // ---- Part text -----------------------------------------------------------------------------------------------

    /// <summary>One line on what a part is and does (bores, weight, stat changes).</summary>
    private static string PartSummary(PartDef p)
    {
        var bits = new List<string>();
        if (p.Frame is { } f)
            bits.Add($"Holds {p.ReceiverCapacity} receiver{(p.ReceiverCapacity == 1 ? "" : "s")}" + (p.ReceiverCapacity > 1 ? $", always fires {f.Style.ToString().ToLowerInvariant()} ({(f.Style == FireStyle.Volley ? "all at once" : "taking turns")})" : "")
                     + $", carries {f.Carry:0} kg{(f.Ads ? "" : ", hip fire only")}" + (p.Sockets.Any(s => s.Type == "hopper") ? ", takes a shared feed" : ""));
        if (p.Shared is { } sh)
            bits.Add($"shared feed: feeds up to {sh.Feeds} receivers with no feed of their own (pooled)");
        if (p.CarryBonus > 0)
            bits.Add($"weight relief: the grip carries {p.CarryBonus:0.#} kg more");
        if (p.Hilt is { } hl)
            bits.Add($"melee hilt: {hl.Damage:0} damage, {hl.Swing:0.00} s swing, {hl.Reach:0.0} m reach, cleave {hl.Cleave}{(hl.Stun > 0 ? $", stun {hl.Stun:0.0} s" : "")}, {hl.Hands}-handed");
        if (p.Melee is { } me)
        {
            var mb = new List<string>();
            if (me.Damage != 0) mb.Add($"damage {(me.Damage > 0 ? "+" : "")}{me.Damage:0}");
            if (me.DamageMult != 1) mb.Add($"damage x{me.DamageMult:0.##}");
            if (me.SwingMult != 1) mb.Add($"swing time x{me.SwingMult:0.##}");
            if (me.Reach != 0) mb.Add($"reach {(me.Reach > 0 ? "+" : "")}{me.Reach:0.##} m");
            if (me.Cleave != 0) mb.Add($"cleave {(me.Cleave > 0 ? "+" : "")}{me.Cleave}");
            if (me.Stun != 0) mb.Add($"stun +{me.Stun:0.0} s");
            bits.Add(string.Join(", ", mb));
        }
        if (p.Receiver is { } r)
            bits.Add($"{r.Action.ToString().ToLowerInvariant()} action, {r.Bore} rounds, {(r.RpmPerBarrel > 0 ? $"{r.RpmPerBarrel} rpm per barrel" : $"{r.Rpm} rpm")}");
        if (p.Split is { } s)
            bits.Add($"{p.Sockets.Count(x => x.Type == "barrel")} outputs at {SplitInfo.OutputMult(p) * 100:0}% damage each (the same total){(s.SpreadMult != 1 ? $", spread x{s.SpreadMult:0.##}" : "")}");
        if (p.Bores.Length > 0)
            bits.Add("for " + string.Join(", ", p.Bores) + " rounds");
        if (p.RequiresAction.Length > 0)
            bits.Add("needs " + string.Join("/", p.RequiresAction.Select(a => a.ToString().ToLowerInvariant())) + " action");
        bits.Add($"{p.Weight:0.0#} kg, {p.Scrap} scrap");
        foreach (var e in p.Effects)
            bits.Add(e.Op switch
            {
                "set" => $"{WeaponFile.Key(e.Field)} {e.Value}",
                "mul" => $"{WeaponFile.Key(e.Field)} x{e.Value}",
                "grow" => $"{WeaponFile.Key(e.Field)} x{e.Value}",
                _ => $"{WeaponFile.Key(e.Field)} {((float)e.Value >= 0 ? "+" : "")}{e.Value}",
            });
        if (p.Flags.Length > 0)
            bits.Add("flags: " + string.Join(", ", p.Flags));
        return string.Join("; ", bits);
    }

    // ---- Sandbox side panel: saving, presets, toggles ---------------------------------------------------------

    /// <summary>Missing-part counts of the saved slots and presets against the current stock (cached).</summary>
    private void RefreshBadges()
    {
        var key = (Bm.Stock.Fingerprint, Bm.Version, _wbMelee);
        if (key == _badgeKey)
            return;
        _badgeKey = key;
        for (var i = 0; i < BuildStore.SlotCount; i++)
            _slotMissing[i] = BuildStore.Slots[i] is { } sb ? MissingReport.Of(sb, Bm.Stock).Total : 0;
        _slotMissing[BuildStore.SlotCount] = BuildStore.Melee is { } mb ? MissingReport.Of(mb, Bm.Stock).Total : 0;
        _presetBadges.Clear();
        foreach (var sb in StandardBuilds.All)
        {
            var b = sb.Build();
            if (b.IsMelee == _wbMelee)
                _presetBadges.Add((sb, MissingReport.Of(b, Bm.Stock).Total));
        }
    }

    /// <summary>A small red "N missing" badge after the last item (nothing when 0).</summary>
    private void MissingBadge(int n)
    {
        if (n <= 0)
            return;
        ImGui.SameLine();
        ImGuiSafe.TextColored(Red, $"[{n} missing]");
    }

    private WorkbenchLaunch? DrawWorkbenchSide(WeaponBuild b)
    {
        WorkbenchLaunch? launch = null;
        RefreshBadges();
        ImGui.Separator();
        var ready = Bm.CanFinalize;
        if (_wbMelee)
        {
            ImGui.TextColored(Orange, "MELEE SLOT"u8);
            ImGuiSafe.TextColored(Dim, $"Saved: {BuildStore.Melee?.Name ?? "(the standard knife / butt strike)"}");
            MissingBadge(_slotMissing[BuildStore.SlotCount]);
            ImGui.BeginDisabled(!ready);
            if (ImGui.Button("Save melee"u8))
            {
                BuildStore.Melee = b.Clone();
                BuildStore.Save();
                _badgeKey = default;
                WbSay($"Saved '{b.Name}' to the melee slot.");
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGuiSafe.SetTooltip(Bm.FinalizeTip());
            ImGui.SameLine();
            if (ImGui.Button("Revert"u8))
                LoadWorkbenchMelee(true);
            ImGui.SameLine();
            if (ImGui.Button("Use the standard strike"u8))
            {
                BuildStore.Melee = null;
                BuildStore.Save();
                WbSay("The melee key does the standard knife / butt strike again.");
            }
        }
        else
        {
            ImGui.TextColored(Orange, "SAVED BUILDS"u8);
            for (var i = 0; i < BuildStore.SlotCount; i++)
            {
                var s = BuildStore.Slots[i];
                var label = $"{i + 1}. {(s?.Name ?? "(empty: new weapon)")}{(i >= BuildStore.FreeSlots ? "  (slot upgrade)" : "")}##slot{i}";
                if (ImGui.Selectable(label, BuildStore.Selected == i))
                    LoadWorkbenchSlot(i);
                if (ImGui.IsItemHovered())
                    ImGuiSafe.SetTooltip((_slotMissing[i] > 0 ? $"{_slotMissing[i]} part(s) missing against your parts.\n" : "")
                                         + (i == BuildStore.Second ? "Your second weapon in the range." : i >= BuildStore.FreeSlots ? "Slots 3-4: used once the weapon-slot upgrade is on (toggle below, or the station in the range)." : "Click to edit this build."));
                MissingBadge(_slotMissing[i]);
            }
            ImGui.BeginDisabled(!ready);
            if (ImGui.Button($"SAVE TO SLOT {BuildStore.Selected + 1}"))
            {
                BuildStore.Slots[BuildStore.Selected] = b.Clone();
                BuildStore.Save();
                _badgeKey = default;
                WbSay($"Saved '{b.Name}' to slot {BuildStore.Selected + 1}.");
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGuiSafe.SetTooltip(Bm.FinalizeTip());
            ImGui.SameLine();
            if (ImGui.Button("Revert"u8))
                LoadWorkbenchSlot(BuildStore.Selected);
            ImGuiSafe.TextColored(Dim, $"Second weapon in the range: slot {BuildStore.Second + 1}");
            ImGui.SameLine();
            if (ImGui.SmallButton("next"u8))
            {
                BuildStore.Second = (BuildStore.Second + 1) % BuildStore.SlotCount;
                BuildStore.Save();
            }
        }
        DrawWorkbenchPresets();
        ImGui.Separator();

        // ---- Sandbox toggles ----
        if (ImGui.Checkbox("Practice parts (Zombies rules)"u8, ref _wbPractice))
        {
            _wbPracticeStock = null;
            Bm.SetStock(WbStock());
            WbSay(_wbPractice ? "Practice parts: two of every common part, one of every uncommon. Missing parts show red." : "Every part unlocked again.");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Builds with what an early Zombies run might own, so you can try missing parts and copying assemblies without a run."u8);
        if (ImGui.Checkbox("Weapon slots 3-4 unlocked"u8, ref BuildStore.ExtraSlotsUnlocked))
            BuildStore.Save();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("In Zombies the slot upgrade is bought on the map, once per run. Here it's a toggle (or use the station by the range's spawn)."u8);
        ImGuiSafe.TextColored(Dim, $"Practice dummies: {BuildStore.Dummies}");
        ImGui.SameLine();
        if (ImGui.SmallButton("-##dum"u8) && BuildStore.Dummies > 0)
        {
            BuildStore.Dummies--;
            BuildStore.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("+##dum"u8) && BuildStore.Dummies < 8)
        {
            BuildStore.Dummies++;
            BuildStore.Save();
        }
        ImGui.Spacing();
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button("TEST FIRE"u8, new float2(-1, 38 * _scale)) && ready)
        {
            launch = WorkbenchLaunchFor(b, out var why); // assembles every slot once, on the click
            if (launch == null)
                WbSay(why, true);
            else
            {
                if (_wbMelee)
                    BuildStore.Melee = b.Clone();
                else
                    BuildStore.Slots[BuildStore.Selected] = b.Clone();
                BuildStore.Save();
            }
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGuiSafe.SetTooltip(ready ? $"Opens the Workbench Range with your builds ({Keybinds.NameOf(_hudSettings, GameAction.Interact)}: park / push a cart, buy the slot upgrade)." : Bm.FinalizeTip());
        return launch;
    }

    /// <summary>The standard complete builds (the mystery box's and the wall's), as presets for the editor, with what you'd be missing.</summary>
    private void DrawWorkbenchPresets()
    {
        RefreshBadges();
        if (_presetBadges.Count == 0)
            return;
        if (!ImGui.CollapsingHeader("Standard builds (box / wall presets)"u8, ImGuiTreeNodeFlags.None))
            return;
        foreach (var (sb, missing) in _presetBadges)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, WbCol(PartDef.RarityColor(sb.Tier)));
            var clicked = ImGui.Selectable($"{sb.Name}  [{PartDef.RarityName(sb.Tier)}, {string.Join("/", sb.Sources)}]##preset{sb.Name}");
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetTooltip(sb.Flavor + (missing > 0 ? $"\nYou'd be missing {missing} part(s): load it as a plan, the missing ones show red." : ""));
            MissingBadge(missing);
            if (!clicked)
                continue;
            var b = sb.Build();
            Bm.SetBuild(b, keepHistory: Bm.Build.Root != null);
            ImGuiSafe.SetInput(_wbNameInput!, b.Name);
            _wbPicking = false;
            _wbSel = null;
            ResetView();
            WbSay($"Loaded the standard build '{sb.Name}'{(Bm.Missing.Total > 0 ? $". {Bm.Missing.Text()}" : " (save it to keep it).")}", Bm.Missing.Total > 0);
        }
    }

    /// <summary>What Test fire would start (every slot's build that's complete, the melee slot, the toggles), or null with the reason.</summary>
    private WorkbenchLaunch? WorkbenchLaunchFor(WeaponBuild editing, out string why)
    {
        why = "";
        WeaponDef? Gun(int slot) =>
            !_wbMelee && slot == BuildStore.Selected ? Bm.Asm.Def : BuildStore.Slots[slot] is { IsMelee: false } sb ? WeaponBuilder.Assemble(sb).Def : null;
        var primary = Gun(BuildStore.Selected);
        if (primary == null && _wbMelee)
            primary = Enumerable.Range(0, BuildStore.SlotCount).Select(Gun).FirstOrDefault(d => d != null);
        if (primary == null)
        {
            why = _wbMelee ? "Save a complete gun to a slot first: the range needs a gun as well as the melee weapon." : "Finish the build first (it needs everything listed).";
            return null;
        }
        var second = BuildStore.Second != BuildStore.Selected ? Gun(BuildStore.Second) : null;
        var rest = Enumerable.Range(0, BuildStore.SlotCount).Where(i => i != BuildStore.Selected && i != BuildStore.Second).Select(Gun).Where(d => d != null).ToList();
        var melee = _wbMelee ? Bm.Asm.Melee : BuildStore.Melee is { } m ? WeaponBuilder.Assemble(m).Melee : null;
        if (_wbMelee && melee == null)
        {
            why = "Finish the melee weapon first (it needs a head).";
            return null;
        }
        return new WorkbenchLaunch(primary, second ?? primary, rest.ElementAtOrDefault(0), rest.ElementAtOrDefault(1), melee, BuildStore.ExtraSlotsUnlocked, BuildStore.Dummies);
    }
}
