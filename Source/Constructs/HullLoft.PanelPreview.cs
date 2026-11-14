using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A formed panel made for a picture, never for the world (the Panel Designer's preview: UiPanelDesigner.cs). The part is made from the key
/// by the same code as a real one (<see cref="PanelJson"/>: the same floats, so the picture is the panel), but it is NOT interned: it takes no
/// run-time part slot and no name in the table, so a slider dragged through a hundred values leaves nothing behind. Its base (boxes, sockets,
/// mass, bounds) is made at once when the key changes; its facets a few a frame inside a budget, into shapes this preview owns; the last
/// finished panel stays shown until the new one is whole. <see cref="Release"/> gives every shape back.
/// </summary>
public static partial class HullLoft
{
    /// <summary>"Hood 160 x 150 cm, steel" for a panel's key; a lofted part's or any other name as it is, cut short.</summary>
    public static string PanelLabel(string name) =>
        PanelOf(name) is { } s ? $"{PanelPresets[s.Kind].Label} {s.L} x {s.W} cm, {s.Material}" : CutLabel(name) ?? (name.Length <= 40 ? name : name[..38] + "..");

    /// <summary>"Door 110 x 120 cm, steel, 2 cuts" or "Cut plate 200 x 20 x 300 cm, steel, 1 cut" for a cut key; null for any other name.</summary>
    public static string? CutLabel(string name)
    {
        if (!IsCutName(name))
            return null;
        if (IsPaneName(name))
            return PaneLabel(name);
        var plate = name.StartsWith(CutPlatePrefix, StringComparison.Ordinal);
        var start = (plate ? CutPlatePrefix : CutPanelPrefix).Length;
        var bar = name.IndexOf(" | ", start, StringComparison.Ordinal);
        if (bar < 0)
            return null;
        var cuts = 1;
        for (var i = bar + 3; i < name.Length; i++)
            if (name[i] == ' ')
                cuts++;
        var blank = name[start..bar];
        var what = plate ? "Cut " + (blank.StartsWith("Extrusion ", StringComparison.Ordinal) ? blank[10..] : blank)
            : PanelOf(PanelPrefix + blank + ")") is { } b ? $"Cut {PanelPresets[b.Kind].Label.ToLowerInvariant()} {b.L} x {b.W} cm, {b.Material}" : "Cut panel";
        return $"{what}, {cuts} cut{(cuts == 1 ? "" : "s")}";
    }

    public sealed class PanelPreview
    {
        /// <summary>The panel shown: its boxes (hidden ones first, then the facets), sockets, mass and bounds. Null until the first is whole.</summary>
        public ConstructPartDef? Shown { get; private set; }

        /// <summary>The newest key's base: its numbers are right at once (mass, sockets, bounds), its facets may still be coming.</summary>
        public ConstructPartDef? Base { get; private set; }

        public PanelShape ShownShape { get; private set; }

        /// <summary>The shown panel's facets' outward normals, in the order its drawn boxes follow its solid ones (a picture shades by them, so the curve reads).</summary>
        public Vector3[] ShownNormals { get; private set; } = [];
        public bool Building => _next < _total;
        public int Made { get; private set; } // keys this preview has made a base for (a probe's count)

        private ushort[] _shownShapes = [];
        private PanelShape _want;
        private bool _has;
        private int _next, _total;
        private readonly List<CPartBox> _facets = new(PanelMaxAlong * PanelMaxAcross);
        private readonly List<ushort> _shapes = new(PanelMaxAlong * PanelMaxAcross + 8);

        private PanelShape _queued;
        private string? _wantCut, _queuedCut; // (a cut key over the shape, laser cutter phase 4; null: the panel whole)
        private CutPlan? _plan;
        private PanelShape _sheet; // (what is built: the cut plan's sheet, else the wanted shape)
        public string? ShownCut { get; private set; }
        private bool _hasQueued, _started;

        /// <summary>
        /// The key to show. While a panel is being made the newest key waits for it (the one before it is forgotten), so a slider dragged through a
        /// hundred values makes a panel every few frames, each whole, never one a frame: the picture follows the hand and nothing is thrown away half made.
        /// </summary>
        public void Want(in PanelShape s, string? cut = null)
        {
            if (_has && s == _want && cut == _wantCut)
            {
                _hasQueued = false;
                return;
            }
            if (Building)
            {
                (_queued, _queuedCut, _hasQueued) = (s, cut, true);
                return;
            }
            Start(s, cut);
        }

        private void Start(in PanelShape s, string? cut)
        {
            _hasQueued = false;
            _started = true;
            DropPending();
            _want = s;
            _wantCut = cut;
            _has = true;
            _plan = cut != null ? ReadCut(cut, out _) : null;
            if (_plan is { Plate: true })
                _plan = null; // (a cut plate isn't a panel sheet: shown uncut)
            _sheet = _plan?.Sheet ?? s; // (a cut key is built on its own sheet, never on another frame's shape)
            if (PanelRefusal(s) != null)
                return;
            var name = _plan != null ? cut! : PanelKey(s);
            var outer = PartShapes.BeginCollect();
            try
            {
                Base = ConstructParts.ParseBody(PanelJson(name, _sheet, 0, 0, true, _plan), name); // (with its slabs, as a deferred panel's first part: a part needs a drawn box; the facets replace them)
                if (_plan != null)
                    AddOpeningSockets(Base, _plan); // (a cut part's sockets in its openings, as CutFirst gives the real one: so the hashes agree)
                var (nu, nv) = PanelGrid(_sheet);
                _total = _plan?.Items ?? nu * nv;
                Made++;
            }
            catch (ConstructPartException e)
            {
                Console.WriteLine($"[panels] preview {name}: refused, {e.Message}");
                Base = null;
            }
            finally
            {
                _shapes.AddRange(PartShapes.EndCollect(outer));
            }
        }

        /// <summary>A frame's slices of the facets (6 each) inside <paramref name="budgetMs"/>; the finished panel replaces the one shown.</summary>
        public void Pump(double budgetMs = 1.5)
        {
            if (_started)
            {
                _started = false; // (this frame made the base: the facets begin next frame)
                if (budgetMs < 100)
                    return;
            }
            if (!Building && _hasQueued)
            {
                Start(_queued, _queuedCut);
                _started = false;
                return;
            }
            if (_next >= _total || Base == null)
                return;
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var name = Base.Name;
            while (_next < _total)
            {
                var to = Math.Min(_total, _next + PanelSlice);
                var outer = PartShapes.BeginCollect();
                try
                {
                    var part = ConstructParts.ParseBody(PanelJson(name, _sheet, _next, to, false, _plan), name);
                    for (var k = part.SolidBoxes; k < part.Boxes.Length; k++)
                        _facets.Add(part.Boxes[k]);
                }
                catch (ConstructPartException e)
                {
                    Console.WriteLine($"[panels] preview {name}: facets {_next} to {to} refused, {e.Message}");
                }
                finally
                {
                    _shapes.AddRange(PartShapes.EndCollect(outer));
                }
                _next = to;
                if (System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= budgetMs)
                    break;
            }
            if (_next < _total)
                return;
            // Whole: it becomes the panel shown, and the one before gives its shapes back.
            var d = Base;
            var boxes = new CPartBox[d.SolidBoxes + _facets.Count];
            Array.Copy(d.Boxes, boxes, d.SolidBoxes);
            _facets.CopyTo(boxes, d.SolidBoxes);
            d.Boxes = boxes;
            PartShapes.Release(_shownShapes);
            _shownShapes = [.. _shapes];
            _shapes.Clear();
            _facets.Clear();
            Shown = d;
            ShownShape = _sheet;
            ShownCut = _plan != null ? _wantCut : null;
            var (gu, gv) = PanelGrid(_sheet);
            var items = _plan?.Items ?? gu * gv;
            if (ShownNormals.Length != items)
                ShownNormals = new Vector3[items];
            for (int k = 0, it = 0; k < gu * gv && it < items; k++)
            {
                // (a cut panel draws each touched facet as its pieces, in the facets' order: each piece takes its facet's normal)
                var n = PanelNormal(_sheet, (PanelRowS(_sheet, k / gv, gu) + PanelRowS(_sheet, k / gv + 1, gu)) * 0.5f, (k % gv + 0.5f) * 2f / gv - 1);
                var count = _plan?.Pieces[k] is { } pieces ? pieces.Length : 1;
                for (var q = 0; q < count && it < items; q++)
                    ShownNormals[it++] = n;
            }
            _next = _total = 0;
        }

        private void DropPending()
        {
            if (_shapes.Count > 0)
            {
                PartShapes.Release([.. _shapes]);
                _shapes.Clear();
            }
            _facets.Clear();
            _next = _total = 0;
        }

        /// <summary>The panel closes: every shape goes back.</summary>
        public void Release()
        {
            DropPending();
            PartShapes.Release(_shownShapes);
            _shownShapes = [];
            Shown = Base = null;
            _has = false;
        }
    }
}
