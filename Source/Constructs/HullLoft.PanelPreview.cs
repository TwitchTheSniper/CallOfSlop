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
        PanelOf(name) is { } s ? $"{PanelPresets[s.Kind].Label} {s.L} x {s.W} cm, {s.Material}" : name.Length <= 40 ? name : name[..38] + "..";

    public sealed class PanelPreview
    {
        /// <summary>The panel shown: its boxes (hidden ones first, then the facets), sockets, mass and bounds. Null until the first is whole.</summary>
        public ConstructPartDef? Shown { get; private set; }

        /// <summary>The newest key's base: its numbers are right at once (mass, sockets, bounds), its facets may still be coming.</summary>
        public ConstructPartDef? Base { get; private set; }

        public PanelShape ShownShape { get; private set; }
        public bool Building => _next < _total;
        public int Made { get; private set; } // keys this preview has made a base for (a probe's count)

        private ushort[] _shownShapes = [];
        private PanelShape _want;
        private bool _has;
        private int _next, _total;
        private readonly List<CPartBox> _facets = new(PanelMaxAlong * PanelMaxAcross);
        private readonly List<ushort> _shapes = new(PanelMaxAlong * PanelMaxAcross + 8);

        /// <summary>The key to show. A new one drops what was half made and starts again (one base a call at most: about 0.3 ms).</summary>
        public void Want(in PanelShape s)
        {
            if (_has && s == _want)
                return;
            DropPending();
            _want = s;
            _has = true;
            if (PanelRefusal(s) != null)
                return;
            var name = PanelKey(s);
            var outer = PartShapes.BeginCollect();
            try
            {
                Base = ConstructParts.ParseBody(PanelJson(name, s, 0, 0, false), name);
                var (nu, nv) = PanelGrid(s);
                _total = nu * nv;
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
                    var part = ConstructParts.ParseBody(PanelJson(name, _want, _next, to, false), name);
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
            ShownShape = _want;
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
