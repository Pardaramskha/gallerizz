using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace Gallerizz
{
    internal sealed class SvgResult
    {
        public DrawingImage Image;
        public double Width;
        public double Height;
    }

    // Rendu d'un sous-ensemble solide de SVG 1.1 vers un Drawing WPF (vectoriel : net à tout zoom).
    // Couvert : formes, chemins complets, transformations, groupes, use/defs, dégradés, opacités,
    // pointillés, viewBox, <style> interne simple, texte basique, images incorporées, clip-path.
    // Hors périmètre : filtres, animations SMIL, scripts, CSS externe.
    internal sealed class SvgRenderer
    {
        private readonly Dictionary<string, XElement> _ids = new Dictionary<string, XElement>();
        private readonly List<CssRule> _rules = new List<CssRule>();
        private readonly string _baseDir;
        private int _useDepth;

        private SvgRenderer(string baseDir) { _baseDir = baseDir; }

        internal static SvgResult Render(string svgText, string baseDir)
        {
            var settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Ignore;
            settings.XmlResolver = null;
            XDocument doc;
            using (var reader = XmlReader.Create(new StringReader(svgText), settings))
                doc = XDocument.Load(reader);
            XElement root = doc.Root;
            if (root == null || root.Name.LocalName != "svg")
                throw new InvalidDataException("racine <svg> introuvable");

            var r = new SvgRenderer(baseDir);
            r.Index(root);

            // Dimensions intrinsèques : width/height, sinon viewBox, sinon 300×150.
            double[] vb = null;
            string vbAttr = Attr(root, "viewBox");
            if (vbAttr != null)
            {
                List<double> nums = SvgUtil.ParseNumberList(vbAttr);
                if (nums.Count == 4 && nums[2] > 0 && nums[3] > 0) vb = nums.ToArray();
            }
            double w = ParseSizeAttr(Attr(root, "width"));
            double h = ParseSizeAttr(Attr(root, "height"));
            if (double.IsNaN(w)) w = vb != null ? vb[2] : 300;
            if (double.IsNaN(h)) h = vb != null ? vb[3] : 150;

            var rootCtx = new Ctx(null, new Dictionary<string, string>());
            Drawing content = r.RenderChildren(root, r.BuildCtx(root, rootCtx));

            var group = new DrawingGroup();
            // Épingle les bornes du dessin sur la fenêtre de vue, et rogne ce qui dépasse.
            var pin = new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, w, h)));
            group.Children.Add(pin);
            if (content != null)
            {
                if (vb != null)
                {
                    var inner = new DrawingGroup();
                    inner.Transform = new MatrixTransform(ViewBoxTransform(vb, w, h, Attr(root, "preserveAspectRatio")));
                    inner.Children.Add(content);
                    group.Children.Add(inner);
                }
                else group.Children.Add(content);
            }
            group.ClipGeometry = new RectangleGeometry(new Rect(0, 0, w, h));
            group.Freeze();

            var img = new DrawingImage(group);
            img.Freeze();
            var result = new SvgResult();
            result.Image = img;
            result.Width = w;
            result.Height = h;
            return result;
        }

        private static double ParseSizeAttr(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Trim().EndsWith("%")) return double.NaN;
            double v = SvgUtil.ParseLength(s, double.NaN, double.NaN);
            return v > 0 ? v : double.NaN;
        }

        private static Matrix ViewBoxTransform(double[] vb, double w, double h, string par)
        {
            double sx = w / vb[2], sy = h / vb[3];
            bool none = par != null && par.Trim().StartsWith("none");
            if (!none)
            {
                bool slice = par != null && par.Contains("slice");
                double s = slice ? Math.Max(sx, sy) : Math.Min(sx, sy);
                sx = sy = s;
            }
            // Alignement : xMidYMid par défaut ; Min/Max gérés grossièrement via l'attribut.
            double ax = 0.5, ay = 0.5;
            if (par != null)
            {
                if (par.Contains("xMin")) ax = 0; else if (par.Contains("xMax")) ax = 1;
                if (par.Contains("YMin")) ay = 0; else if (par.Contains("YMax")) ay = 1;
            }
            double tx = (w - vb[2] * sx) * ax - vb[0] * sx;
            double ty = (h - vb[3] * sy) * ay - vb[1] * sy;
            return new Matrix(sx, 0, 0, sy, tx, ty);
        }

        private void Index(XElement el)
        {
            foreach (XElement e in el.DescendantsAndSelf())
            {
                string id = Attr(e, "id");
                if (id != null && !_ids.ContainsKey(id)) _ids[id] = e;
                if (e.Name.LocalName == "style") _rules.AddRange(SvgUtil.ParseCss(e.Value));
            }
        }

        private static string Attr(XElement el, string name)
        {
            XAttribute a = el.Attributes().FirstOrDefault(x => x.Name.LocalName == name);
            return a != null ? a.Value : null;
        }

        // ---- Contexte de style (cascade simplifiée : attributs < CSS interne < style inline) ----

        private sealed class Ctx
        {
            public readonly Ctx Parent;
            public readonly Dictionary<string, string> Props;
            public Ctx(Ctx parent, Dictionary<string, string> props) { Parent = parent; Props = props; }

            // Propriétés héritables : remonte la chaîne.
            public string Get(string name)
            {
                for (Ctx c = this; c != null; c = c.Parent)
                {
                    string v;
                    if (c.Props.TryGetValue(name, out v) && v != "inherit") return v;
                }
                return null;
            }

            // Propriétés non héritables (opacity, display...) : uniquement le niveau local.
            public string GetLocal(string name)
            {
                string v;
                return Props.TryGetValue(name, out v) ? v : null;
            }
        }

        private static readonly HashSet<string> KnownProps = new HashSet<string>
        {
            "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity",
            "stroke-linecap", "stroke-linejoin", "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset",
            "opacity", "color", "display", "visibility", "stop-color", "stop-opacity",
            "font-size", "font-family", "font-weight", "font-style", "text-anchor", "clip-path"
        };

        private Ctx BuildCtx(XElement el, Ctx parent)
        {
            var props = new Dictionary<string, string>();
            foreach (XAttribute a in el.Attributes())
            {
                string n = a.Name.LocalName.ToLowerInvariant();
                if (KnownProps.Contains(n)) props[n] = a.Value.Trim();
            }
            string id = Attr(el, "id");
            var classes = new HashSet<string>();
            string cls = Attr(el, "class");
            if (cls != null)
                foreach (string c in cls.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) classes.Add(c);
            foreach (CssRule rule in _rules.Where(x => x.Matches(el.Name.LocalName, classes, id))
                                           .OrderBy(x => x.Specificity).ThenBy(x => x.Order))
                foreach (var kv in rule.Props)
                    if (KnownProps.Contains(kv.Key)) props[kv.Key] = kv.Value;
            string style = Attr(el, "style");
            if (style != null)
                foreach (var kv in SvgUtil.ParseDeclarations(style))
                    if (KnownProps.Contains(kv.Key)) props[kv.Key] = kv.Value;
            return new Ctx(parent, props);
        }

        // ---- Parcours ----

        private Drawing RenderChildren(XElement el, Ctx ctx)
        {
            var group = new DrawingGroup();
            foreach (XElement child in el.Elements())
            {
                Drawing d = RenderElement(child, ctx);
                if (d != null) group.Children.Add(d);
            }
            return group.Children.Count > 0 ? group : null;
        }

        private Drawing RenderElement(XElement el, Ctx parentCtx)
        {
            string name = el.Name.LocalName;
            switch (name)
            {
                case "defs": case "style": case "title": case "desc": case "metadata":
                case "symbol": case "foreignObject": case "clipPath": case "mask":
                case "linearGradient": case "radialGradient": case "pattern": case "filter":
                case "marker": case "script":
                    return null;
            }

            Ctx ctx = BuildCtx(el, parentCtx);
            if (ctx.GetLocal("display") == "none") return null;
            if (ctx.Get("visibility") == "hidden" || ctx.Get("visibility") == "collapse") return null;

            Drawing body = null;
            switch (name)
            {
                case "svg":
                case "g":
                case "a":
                    body = RenderChildren(el, ctx);
                    break;
                case "switch":
                    foreach (XElement child in el.Elements())
                    {
                        if (child.Name.LocalName == "foreignObject") continue;
                        body = RenderElement(child, ctx);
                        if (body != null) break;
                    }
                    return Decorate(el, ctx, body);
                case "use":
                    body = RenderUse(el, ctx);
                    break;
                case "path":
                    body = MakeShape(SvgUtil.ParsePath(Attr(el, "d"), FillRuleOf(ctx)), ctx);
                    break;
                case "rect":
                    body = MakeShape(RectGeometry(el), ctx);
                    break;
                case "circle":
                    {
                        double cx = Len(el, "circle", "cx"), cy = Len(el, "circle", "cy"), rr = Len(el, "circle", "r");
                        if (rr > 0) body = MakeShape(new EllipseGeometry(new Point(cx, cy), rr, rr), ctx);
                    }
                    break;
                case "ellipse":
                    {
                        double cx = Len(el, "ellipse", "cx"), cy = Len(el, "ellipse", "cy");
                        double rx = Len(el, "ellipse", "rx"), ry = Len(el, "ellipse", "ry");
                        if (rx > 0 && ry > 0) body = MakeShape(new EllipseGeometry(new Point(cx, cy), rx, ry), ctx);
                    }
                    break;
                case "line":
                    {
                        var g = new LineGeometry(new Point(Len(el, "line", "x1"), Len(el, "line", "y1")),
                                                 new Point(Len(el, "line", "x2"), Len(el, "line", "y2")));
                        body = MakeShape(g, ctx, true);
                    }
                    break;
                case "polyline":
                case "polygon":
                    body = MakeShape(PolyGeometry(Attr(el, "points"), name == "polygon", FillRuleOf(ctx)), ctx);
                    break;
                case "text":
                    body = RenderText(el, ctx);
                    break;
                case "image":
                    body = RenderImage(el);
                    break;
                default:
                    return null;
            }
            return Decorate(el, ctx, body);
        }

        // Enveloppe transformation / opacité / clip autour d'un dessin.
        private Drawing Decorate(XElement el, Ctx ctx, Drawing body)
        {
            if (body == null) return null;
            Matrix m = SvgUtil.ParseTransform(Attr(el, "transform"));
            double opacity = Num(ctx.GetLocal("opacity"), 1.0);
            Geometry clip = ResolveClip(ctx.GetLocal("clip-path"));
            bool needGroup = !m.IsIdentity || opacity < 1.0 || clip != null;
            if (!needGroup) return body;
            var g = new DrawingGroup();
            g.Children.Add(body);
            if (!m.IsIdentity) g.Transform = new MatrixTransform(m);
            if (opacity < 1.0) g.Opacity = Math.Max(0, opacity);
            if (clip != null) g.ClipGeometry = clip;
            return g;
        }

        private Drawing RenderUse(XElement el, Ctx ctx)
        {
            if (_useDepth > 8) return null;
            string href = Href(el);
            if (href == null || !href.StartsWith("#")) return null;
            XElement target;
            if (!_ids.TryGetValue(href.Substring(1), out target)) return null;
            _useDepth++;
            try
            {
                Drawing d = target.Name.LocalName == "symbol" || target.Name.LocalName == "svg"
                    ? RenderChildren(target, BuildCtx(target, ctx))
                    : RenderElement(target, ctx);
                if (d == null) return null;
                double x = Len(el, "use", "x"), y = Len(el, "use", "y");
                if (x != 0 || y != 0)
                {
                    var g = new DrawingGroup();
                    g.Children.Add(d);
                    g.Transform = new TranslateTransform(x, y);
                    return g;
                }
                return d;
            }
            finally { _useDepth--; }
        }

        private static Geometry RectGeometry(XElement el)
        {
            double x = Len(el, "rect", "x"), y = Len(el, "rect", "y");
            double w = Len(el, "rect", "width"), h = Len(el, "rect", "height");
            if (w <= 0 || h <= 0) return null;
            double rx = Len(el, "rect", "rx", -1), ry = Len(el, "rect", "ry", -1);
            if (rx < 0 && ry >= 0) rx = ry;
            if (ry < 0 && rx >= 0) ry = rx;
            if (rx < 0) rx = 0;
            if (ry < 0) ry = 0;
            rx = Math.Min(rx, w / 2);
            ry = Math.Min(ry, h / 2);
            return new RectangleGeometry(new Rect(x, y, w, h), rx, ry);
        }

        private static Geometry PolyGeometry(string points, bool close, FillRule rule)
        {
            List<double> nums = SvgUtil.ParseNumberList(points);
            if (nums.Count < 4) return null;
            var pg = new PathGeometry();
            pg.FillRule = rule;
            var fig = new PathFigure();
            fig.StartPoint = new Point(nums[0], nums[1]);
            fig.IsFilled = true;
            fig.IsClosed = close;
            for (int i = 2; i + 1 < nums.Count; i += 2)
                fig.Segments.Add(new LineSegment(new Point(nums[i], nums[i + 1]), true));
            pg.Figures.Add(fig);
            return pg;
        }

        private static double Len(XElement el, string tag, string attr)
        {
            return Len(el, tag, attr, 0);
        }

        private static double Len(XElement el, string tag, string attr, double fallback)
        {
            return SvgUtil.ParseLength(Attr(el, attr), double.NaN, fallback);
        }

        private static double Num(string s, double fallback)
        {
            if (s == null) return fallback;
            s = s.Trim().TrimEnd('%');
            double v;
            if (!SvgUtil.TryParseDouble(s, out v)) return fallback;
            return v;
        }

        private static FillRule FillRuleOf(Ctx ctx)
        {
            return ctx.Get("fill-rule") == "evenodd" ? FillRule.EvenOdd : FillRule.Nonzero;
        }

        private Drawing MakeShape(Geometry geo, Ctx ctx)
        {
            return MakeShape(geo, ctx, false);
        }

        private Drawing MakeShape(Geometry geo, Ctx ctx, bool strokeOnly)
        {
            if (geo == null) return null;
            Brush fill = strokeOnly ? null : MakeBrush(ctx.Get("fill") ?? "black", ctx, Num(ctx.Get("fill-opacity"), 1.0));
            Pen pen = MakePen(ctx);
            if (fill == null && pen == null) return null;
            return new GeometryDrawing(fill, pen, geo);
        }

        private Pen MakePen(Ctx ctx)
        {
            string stroke = ctx.Get("stroke");
            if (stroke == null || stroke == "none") return null;
            Brush brush = MakeBrush(stroke, ctx, Num(ctx.Get("stroke-opacity"), 1.0));
            if (brush == null) return null;
            double width = SvgUtil.ParseLength(ctx.Get("stroke-width"), double.NaN, 1.0);
            if (width <= 0) return null;
            var pen = new Pen(brush, width);
            switch (ctx.Get("stroke-linecap"))
            {
                case "round": pen.StartLineCap = PenLineCap.Round; pen.EndLineCap = PenLineCap.Round; pen.DashCap = PenLineCap.Round; break;
                case "square": pen.StartLineCap = PenLineCap.Square; pen.EndLineCap = PenLineCap.Square; break;
                default: pen.DashCap = PenLineCap.Flat; break;
            }
            switch (ctx.Get("stroke-linejoin"))
            {
                case "round": pen.LineJoin = PenLineJoin.Round; break;
                case "bevel": pen.LineJoin = PenLineJoin.Bevel; break;
                default: pen.LineJoin = PenLineJoin.Miter; break;
            }
            pen.MiterLimit = Num(ctx.Get("stroke-miterlimit"), 4.0);
            string dash = ctx.Get("stroke-dasharray");
            if (dash != null && dash != "none")
            {
                List<double> nums = SvgUtil.ParseNumberList(dash);
                if (nums.Count > 0 && nums.Exists(v => v > 0))
                {
                    if (nums.Count % 2 == 1) nums.AddRange(new List<double>(nums));
                    // WPF exprime les tirets en multiples de l'épaisseur ; SVG en unités utilisateur.
                    var scaled = nums.Select(v => v / width).ToArray();
                    double offset = SvgUtil.ParseLength(ctx.Get("stroke-dashoffset"), double.NaN, 0) / width;
                    pen.DashStyle = new DashStyle(scaled, offset);
                }
            }
            return pen;
        }

        private Brush MakeBrush(string spec, Ctx ctx, double opacity)
        {
            if (spec == null) return null;
            spec = spec.Trim();
            if (spec == "none" || spec.Length == 0) return null;
            if (spec.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                int close = spec.IndexOf(')');
                if (close < 0) return null;
                string reference = spec.Substring(4, close - 4).Trim().Trim('\'', '"');
                Brush grad = null;
                if (reference.StartsWith("#"))
                {
                    XElement gel;
                    if (_ids.TryGetValue(reference.Substring(1), out gel)) grad = BuildGradient(gel);
                }
                if (grad == null)
                {
                    // Couleur de repli éventuelle après l'url().
                    string rest = spec.Substring(close + 1).Trim();
                    if (rest.Length > 0 && rest != "none") return MakeBrush(rest, ctx, opacity);
                    return null;
                }
                if (opacity < 1.0) grad.Opacity = opacity;
                return grad;
            }
            if (spec == "currentColor")
            {
                spec = ctx.Get("color") ?? "black";
            }
            Color c;
            if (!SvgUtil.TryParseColor(spec, out c)) return null;
            if (opacity < 1.0) c.A = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(c.A * opacity)));
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }

        // ---- Dégradés ----

        private Brush BuildGradient(XElement gel)
        {
            string kind = gel.Name.LocalName;
            if (kind != "linearGradient" && kind != "radialGradient") return null;

            // Chaîne d'héritage par href : attributs et stops manquants repris du dégradé référencé.
            var chain = new List<XElement>();
            XElement cur = gel;
            int guard = 0;
            while (cur != null && guard++ < 8)
            {
                chain.Add(cur);
                string href = Href(cur);
                XElement next = null;
                if (href != null && href.StartsWith("#")) _ids.TryGetValue(href.Substring(1), out next);
                cur = next;
            }
            Func<string, string> attr = name =>
            {
                foreach (XElement e in chain)
                {
                    string v = Attr(e, name);
                    if (v != null) return v;
                }
                return null;
            };
            List<XElement> stops = null;
            foreach (XElement e in chain)
            {
                var own = e.Elements().Where(x => x.Name.LocalName == "stop").ToList();
                if (own.Count > 0) { stops = own; break; }
            }
            if (stops == null || stops.Count == 0) return null;

            var stopCollection = new GradientStopCollection();
            foreach (XElement stop in stops)
            {
                Ctx sctx = BuildCtx(stop, null);
                double offset = Num(Attr(stop, "offset"), 0);
                if (Attr(stop, "offset") != null && Attr(stop, "offset").Trim().EndsWith("%")) offset /= 100.0;
                offset = Math.Max(0, Math.Min(1, offset));
                Color c;
                string sc = sctx.GetLocal("stop-color") ?? "black";
                if (sc == "currentColor" || !SvgUtil.TryParseColor(sc, out c)) c = Colors.Black;
                double so = Num(sctx.GetLocal("stop-opacity"), 1.0);
                if (so < 1.0) c.A = (byte)Math.Round(c.A * Math.Max(0, so));
                stopCollection.Add(new GradientStop(c, offset));
            }

            bool userSpace = attr("gradientUnits") == "userSpaceOnUse";
            GradientBrush brush;
            if (kind == "linearGradient")
            {
                double reference = userSpace ? double.NaN : 1.0;
                double x1 = ParseCoord(attr("x1"), 0, userSpace), y1 = ParseCoord(attr("y1"), 0, userSpace);
                double x2 = ParseCoord(attr("x2"), 1, userSpace), y2 = ParseCoord(attr("y2"), 0, userSpace);
                var lg = new LinearGradientBrush(stopCollection, new Point(x1, y1), new Point(x2, y2));
                brush = lg;
            }
            else
            {
                double cx = ParseCoord(attr("cx"), 0.5, userSpace), cy = ParseCoord(attr("cy"), 0.5, userSpace);
                double rr = ParseCoord(attr("r"), 0.5, userSpace);
                double fx = attr("fx") != null ? ParseCoord(attr("fx"), cx, userSpace) : cx;
                double fy = attr("fy") != null ? ParseCoord(attr("fy"), cy, userSpace) : cy;
                var rg = new RadialGradientBrush(stopCollection);
                rg.Center = new Point(cx, cy);
                rg.RadiusX = rr;
                rg.RadiusY = rr;
                rg.GradientOrigin = new Point(fx, fy);
                brush = rg;
            }
            brush.MappingMode = userSpace ? BrushMappingMode.Absolute : BrushMappingMode.RelativeToBoundingBox;
            switch (attr("spreadMethod"))
            {
                case "reflect": brush.SpreadMethod = GradientSpreadMethod.Reflect; break;
                case "repeat": brush.SpreadMethod = GradientSpreadMethod.Repeat; break;
                default: brush.SpreadMethod = GradientSpreadMethod.Pad; break;
            }
            string gt = attr("gradientTransform");
            if (gt != null) brush.Transform = new MatrixTransform(SvgUtil.ParseTransform(gt));
            brush.Freeze();
            return brush;
        }

        private static double ParseCoord(string s, double fallback, bool userSpace)
        {
            if (s == null) return fallback;
            s = s.Trim();
            if (s.EndsWith("%"))
            {
                double p;
                if (SvgUtil.TryParseDouble(s.Substring(0, s.Length - 1), out p))
                    return userSpace ? p : p / 100.0; // en objectBoundingBox, 50 % = 0,5
                return fallback;
            }
            double v;
            return SvgUtil.TryParseDouble(s, out v) ? v : fallback;
        }

        // ---- Texte, image, clip ----

        private Drawing RenderText(XElement el, Ctx ctx)
        {
            var group = new DrawingGroup();
            double x = Len(el, "text", "x"), y = Len(el, "text", "y");
            RenderTextRuns(el, ctx, group, ref x, ref y);
            return group.Children.Count > 0 ? group : null;
        }

        private void RenderTextRuns(XElement el, Ctx ctx, DrawingGroup group, ref double x, ref double y)
        {
            foreach (XNode node in el.Nodes())
            {
                var txt = node as XText;
                if (txt != null)
                {
                    string s = Regex.Replace(txt.Value, @"\s+", " ");
                    if (s.Trim().Length == 0) continue;
                    EmitRun(s.Trim(), ctx, group, ref x, ref y);
                    continue;
                }
                var child = node as XElement;
                if (child != null && child.Name.LocalName == "tspan")
                {
                    Ctx cctx = BuildCtx(child, ctx);
                    string ax = Attr(child, "x"), ay = Attr(child, "y");
                    if (ax != null) x = SvgUtil.ParseLength(ax, double.NaN, x);
                    if (ay != null) y = SvgUtil.ParseLength(ay, double.NaN, y);
                    RenderTextRuns(child, cctx, group, ref x, ref y);
                }
            }
        }

        private void EmitRun(string text, Ctx ctx, DrawingGroup group, ref double x, ref double y)
        {
            double size = SvgUtil.ParseLength(ctx.Get("font-size"), double.NaN, 16.0);
            if (size <= 0) return;
            var typeface = new Typeface(MapFontFamily(ctx.Get("font-family")), MapFontStyle(ctx.Get("font-style")),
                MapFontWeight(ctx.Get("font-weight")), FontStretches.Normal);
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, size, Brushes.Black, 1.0);
            double startX = x;
            string anchor = ctx.Get("text-anchor");
            if (anchor == "middle") startX -= ft.WidthIncludingTrailingWhitespace / 2;
            else if (anchor == "end") startX -= ft.WidthIncludingTrailingWhitespace;
            Geometry geo = ft.BuildGeometry(new Point(startX, y - ft.Baseline));
            Brush fill = MakeBrush(ctx.Get("fill") ?? "black", ctx, Num(ctx.Get("fill-opacity"), 1.0));
            Pen pen = MakePen(ctx);
            if (fill != null || pen != null)
                group.Children.Add(new GeometryDrawing(fill, pen, geo));
            if (anchor == null || anchor == "start") x += ft.WidthIncludingTrailingWhitespace;
        }

        private static FontFamily MapFontFamily(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return new FontFamily("Segoe UI");
            foreach (string raw in spec.Split(','))
            {
                string f = raw.Trim().Trim('\'', '"');
                if (f.Length == 0) continue;
                if (f.Equals("sans-serif", StringComparison.OrdinalIgnoreCase)) return new FontFamily("Segoe UI");
                if (f.Equals("serif", StringComparison.OrdinalIgnoreCase)) return new FontFamily("Times New Roman");
                if (f.Equals("monospace", StringComparison.OrdinalIgnoreCase)) return new FontFamily("Consolas");
                try { return new FontFamily(f); } catch { }
            }
            return new FontFamily("Segoe UI");
        }

        private static FontWeight MapFontWeight(string spec)
        {
            if (spec == null) return FontWeights.Normal;
            spec = spec.Trim();
            if (spec == "bold" || spec == "bolder") return FontWeights.Bold;
            double v;
            if (SvgUtil.TryParseDouble(spec, out v))
                return FontWeight.FromOpenTypeWeight(Math.Max(1, Math.Min(999, (int)v)));
            return FontWeights.Normal;
        }

        private static FontStyle MapFontStyle(string spec)
        {
            if (spec == "italic") return FontStyles.Italic;
            if (spec == "oblique") return FontStyles.Oblique;
            return FontStyles.Normal;
        }

        private Drawing RenderImage(XElement el)
        {
            string href = Href(el);
            if (href == null) return null;
            double x = Len(el, "image", "x"), y = Len(el, "image", "y");
            double w = Len(el, "image", "width"), h = Len(el, "image", "height");
            if (w <= 0 || h <= 0) return null;
            BitmapSource bmp = null;
            try
            {
                if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = href.IndexOf(',');
                    if (comma < 0 || href.IndexOf("base64", 0, comma, StringComparison.OrdinalIgnoreCase) < 0) return null;
                    byte[] bytes = Convert.FromBase64String(href.Substring(comma + 1).Trim());
                    var dec = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    bmp = dec.Frames[0];
                }
                else if (_baseDir != null && !href.Contains("://"))
                {
                    string path = Path.Combine(_baseDir, Uri.UnescapeDataString(href));
                    if (File.Exists(path))
                    {
                        var dec = BitmapDecoder.Create(new MemoryStream(File.ReadAllBytes(path)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        bmp = dec.Frames[0];
                    }
                }
            }
            catch { }
            if (bmp == null) return null;
            if (bmp.CanFreeze) bmp.Freeze();
            return new ImageDrawing(bmp, new Rect(x, y, w, h));
        }

        private Geometry ResolveClip(string spec)
        {
            if (spec == null || !spec.StartsWith("url(")) return null;
            int close = spec.IndexOf(')');
            if (close < 0) return null;
            string reference = spec.Substring(4, close - 4).Trim().Trim('\'', '"');
            if (!reference.StartsWith("#")) return null;
            XElement cp;
            if (!_ids.TryGetValue(reference.Substring(1), out cp) || cp.Name.LocalName != "clipPath") return null;
            Geometry combined = null;
            foreach (XElement child in cp.Elements())
            {
                Geometry g = null;
                switch (child.Name.LocalName)
                {
                    case "path": g = SvgUtil.ParsePath(Attr(child, "d"), FillRule.Nonzero); break;
                    case "rect": g = RectGeometry(child); break;
                    case "circle":
                        {
                            double cx = Len(child, "circle", "cx"), cy = Len(child, "circle", "cy"), rr = Len(child, "circle", "r");
                            if (rr > 0) g = new EllipseGeometry(new Point(cx, cy), rr, rr);
                        }
                        break;
                    case "ellipse":
                        {
                            double cx = Len(child, "ellipse", "cx"), cy = Len(child, "ellipse", "cy");
                            double rx = Len(child, "ellipse", "rx"), ry = Len(child, "ellipse", "ry");
                            if (rx > 0 && ry > 0) g = new EllipseGeometry(new Point(cx, cy), rx, ry);
                        }
                        break;
                    case "polygon": g = PolyGeometry(Attr(child, "points"), true, FillRule.Nonzero); break;
                }
                if (g == null) continue;
                Matrix m = SvgUtil.ParseTransform(Attr(child, "transform"));
                if (!m.IsIdentity) g.Transform = new MatrixTransform(m);
                combined = combined == null ? g : (Geometry)new CombinedGeometry(GeometryCombineMode.Union, combined, g);
            }
            return combined;
        }

        private static string Href(XElement el)
        {
            XAttribute a = el.Attributes().FirstOrDefault(x => x.Name.LocalName == "href");
            return a != null ? a.Value.Trim() : null;
        }
    }
}
